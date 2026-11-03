// Feature 153 browser verification of the mobile workspace (/m) in a production build.
//
// It starts an in-memory mock API (scripts/mobile-workspace/mock-api.mjs) and drives a local Chrome or Edge at
// phone sizes. The app must be a production build that points at the mock API and runs in the same Worker runtime
// as the Docker image, for example:
//
//   VITE_FINANCIAL_COPILOT_API_BASE_URL=http://127.0.0.1:55153 npm run build
//   npx wrangler dev --config dist/server/wrangler.json --ip 127.0.0.1 --port 5197 --local
//   node scripts/mobile-workspace.browser.mjs            (APP_URL defaults to http://127.0.0.1:5197)
//
// It is deliberately not named *.test.mjs, so Vitest does not collect it.
import { existsSync } from "node:fs";
import { mkdir } from "node:fs/promises";
import { createRequire } from "node:module";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright-core";
import { createMockApi } from "./mobile-workspace/mock-api.mjs";

const require = createRequire(import.meta.url);
const frontendRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const APP_URL = process.env.APP_URL ?? "http://127.0.0.1:5197";
const MOCK_PORT = Number(process.env.MOCK_API_PORT ?? 55153);
const SCREENSHOT_DIR =
  process.env.SCREENSHOT_DIR ?? path.join(frontendRoot, "dist", "mobile-evidence");
const executablePath = [
  process.env.CHROME_PATH,
  "C:/Program Files/Google/Chrome/Application/chrome.exe",
  "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
  "/usr/bin/google-chrome",
  "/usr/bin/chromium",
].find((candidate) => candidate && existsSync(candidate));
if (!executablePath) throw new Error("A local Chrome, Chromium, or Edge is required.");

const IPHONE_UA =
  "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1";

const results = [];
async function check(name, run) {
  try {
    const detail = await run();
    results.push({ name, pass: true, detail: detail ?? "" });
  } catch (error) {
    results.push({
      name,
      pass: false,
      detail: error instanceof Error ? error.message : String(error),
    });
  }
}
function assert(condition, message) {
  if (!condition) throw new Error(message);
}

function fakeAccessToken() {
  const encode = (value) => Buffer.from(JSON.stringify(value)).toString("base64url");
  return [
    encode({ alg: "none", typ: "JWT" }),
    encode({
      sub: "fc3b4b7b-61cf-4295-aa53-5273e4c0d5a0",
      "financial_copilot:tenant_id": "39fd0553-95fb-4882-ae72-d96e67320611",
      email: "tester@example.com",
      exp: Math.floor(Date.now() / 1000) + 3600,
    }),
    "signature",
  ].join(".");
}

async function newPhoneContext(browser, { width = 360, theme = "dark" } = {}) {
  const context = await browser.newContext({
    viewport: { width, height: 800 },
    deviceScaleFactor: 2,
    isMobile: true,
    hasTouch: true,
    userAgent: IPHONE_UA,
    locale: "fa-IR",
  });
  const token = fakeAccessToken();
  await context.addInitScript(
    ([accessToken, chosenTheme]) => {
      sessionStorage.setItem("financial_copilot_access_token", accessToken);
      localStorage.setItem("financial-copilot-theme", chosenTheme);
    },
    [token, theme],
  );
  return context;
}

async function open(page, route, readySelector = "nav[aria-label='ناوبری اصلی'], h1") {
  await page.goto(APP_URL + route, { waitUntil: "networkidle" });
  await page.waitForSelector(readySelector, { timeout: 15_000 });
  await page.waitForLoadState("networkidle");
}

async function horizontalOverflow(page) {
  return page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  );
}

async function runAxe(page) {
  await page.addScriptTag({ path: require.resolve("axe-core/axe.min.js") });
  return page.evaluate(async () => {
    const result = await window.axe.run(document, {
      runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"] },
    });
    const unresolvedContrast =
      result.incomplete.find((item) => item.id === "color-contrast")?.nodes.length ?? 0;
    if (unresolvedContrast > 0)
      window.__axeUnresolvedContrast = (window.__axeUnresolvedContrast ?? 0) + unresolvedContrast;
    return result.violations
      .filter((violation) => violation.impact === "serious" || violation.impact === "critical")
      .map(
        (violation) =>
          `${violation.id} (${violation.nodes.length}): ${violation.nodes[0]?.target.join(" ")}`,
      );
  });
}

async function smallText(page) {
  return page.evaluate(() => {
    const offenders = [];
    for (const element of document.querySelectorAll("body *")) {
      if (!(element instanceof HTMLElement) || element.closest(".sr-only")) continue;
      const ownText = [...element.childNodes].some(
        (node) => node.nodeType === 3 && node.textContent.trim(),
      );
      if (!ownText || element.offsetParent === null) continue;
      const size = parseFloat(getComputedStyle(element).fontSize);
      if (size < 12)
        offenders.push(
          `${element.tagName.toLowerCase()} ${size}px «${element.textContent.trim().slice(0, 20)}»`,
        );
    }
    return offenders;
  });
}

async function smallTargets(page) {
  return page.evaluate(() => {
    const offenders = [];
    for (const element of document.querySelectorAll(
      "header a, header button, nav a, form button",
    )) {
      const box = element.getBoundingClientRect();
      if (box.width === 0) continue;
      if (box.width < 44 || box.height < 44) {
        offenders.push(
          `${element.getAttribute("aria-label") ?? element.textContent?.trim()} ${Math.round(box.width)}×${Math.round(box.height)}`,
        );
      }
    }
    return offenders;
  });
}

async function main() {
  await mkdir(SCREENSHOT_DIR, { recursive: true });
  const mock = createMockApi({ loginToken: fakeAccessToken() });
  await new Promise((resolve) => mock.server.listen(MOCK_PORT, "127.0.0.1", resolve));
  const browser = await chromium.launch({ executablePath, headless: true });
  const threadRoute = `/m/c/${mock.richConversationId}`;
  const screens = [
    "/m",
    "/m/history",
    "/m/market",
    "/m/watchlist",
    "/m/account",
    "/m/account/profile",
    threadRoute,
  ];

  try {
    // AC-03: no horizontal page scroll at 320, 360 and 390 px, including the rich answer blocks (T15).
    for (const width of [320, 360, 390]) {
      const context = await newPhoneContext(browser, { width });
      const page = await context.newPage();
      for (const route of screens) {
        await check(`AC-03 no horizontal scroll ${route} @${width}px`, async () => {
          await open(page, route);
          const overflow = await horizontalOverflow(page);
          assert(overflow <= 1, `page is ${overflow}px wider than the viewport`);
        });
        if (width === 360) {
          const name = route.replaceAll("/", "_").replace(/^_/, "") || "root";
          await page.screenshot({
            path: path.join(SCREENSHOT_DIR, `${name}-360.png`),
            fullPage: false,
          });
        }
      }
      await context.close();
    }

    const context = await newPhoneContext(browser);
    const page = await context.newPage();

    await check("AC-01 four tabs, aria-current, ≥ 44 px targets", async () => {
      await open(page, "/m/market");
      const tabs = await page.$$eval("nav[aria-label='ناوبری اصلی'] a", (links) =>
        links.map((link) => ({
          text: link.textContent.trim(),
          current: link.getAttribute("aria-current"),
          width: link.getBoundingClientRect().width,
          height: link.getBoundingClientRect().height,
        })),
      );
      assert(
        tabs.map((tab) => tab.text).join(",") === "گفتگو,تاریخچه,بازار,دیده‌بان",
        JSON.stringify(tabs),
      );
      assert(
        tabs
          .filter((tab) => tab.current === "page")
          .map((tab) => tab.text)
          .join() === "بازار",
        "active tab",
      );
      assert(
        tabs.every((tab) => tab.width >= 44 && tab.height >= 44),
        "tab target below 44 px",
      );
    });

    await check("AC-02 «گفتگوی جدید» on every screen, including a conversation", async () => {
      for (const route of ["/m", "/m/history", "/m/market", "/m/watchlist", threadRoute]) {
        await open(page, route);
        assert(
          await page.getByRole("link", { name: "گفتگوی جدید" }).count(),
          `missing on ${route}`,
        );
      }
    });

    await check(
      "Head: /m overrides viewport and adds noindex; manifest and apple tags present",
      async () => {
        await open(page, "/m");
        const viewport = await page.getAttribute("meta[name=viewport]", "content");
        assert(
          viewport.includes("viewport-fit=cover") &&
            viewport.includes("interactive-widget=resizes-content"),
          viewport,
        );
        assert(
          !viewport.includes("user-scalable=no") && !viewport.includes("maximum-scale"),
          "zoom disabled",
        );
        assert((await page.getAttribute("meta[name=robots]", "content")) === "noindex", "robots");
        assert(await page.$("link[rel=manifest][href='/manifest.webmanifest']"), "manifest link");
        assert(await page.$("link[rel=apple-touch-icon]"), "apple-touch-icon");
        assert((await page.$$("meta[name=viewport]")).length === 1, "duplicate viewport meta");
      },
    );

    await check(
      "AC-06a first-message failure: indeterminate card, history link, no retry, draft restored",
      async () => {
        await open(page, "/m");
        const composer = page.getByLabel("پرسش خود را بنویسید");
        const question = "شبیه‌سازی خطا برای کگهر";
        await composer.fill(question);
        await page.getByRole("button", { name: "ارسال" }).click();
        const alert = page.getByRole("alert");
        await alert.waitFor({ timeout: 15_000 });
        const text = await alert.textContent();
        assert(text.includes("وضعیت این درخواست مشخص نیست"), text);
        assert((await alert.getByRole("button").count()) === 0, "the card offers a button");
        assert((await alert.getByRole("link", { name: "تاریخچه" }).count()) === 1, "history link");
        assert((await composer.inputValue()) === question, "draft not restored to the composer");
      },
    );

    let newThreadUrl = "";
    await check("AC-04 a first question opens /m/c/{id} with the answer", async () => {
      await open(page, "/m");
      await page.getByLabel("پرسش خود را بنویسید").fill("گزارش فصلی فملی");
      await page.getByRole("button", { name: "ارسال" }).click();
      await page.getByText("در حال تحلیل...").first().waitFor({ timeout: 5_000 });
      await page.waitForURL(/\/m\/c\/[0-9a-f-]{36}$/, { timeout: 15_000 });
      await page
        .getByText("پاسخ آزمایشی به «گزارش فصلی فملی»")
        .first()
        .waitFor({ timeout: 15_000 });
      newThreadUrl = page.url();
    });

    await check(
      "AC-06b follow-up lost after the server persisted it: the refetch shows the answer, no error",
      async () => {
        assert(newThreadUrl, "no conversation from the previous check");
        await page.getByLabel("پرسش خود را بنویسید").fill("ثبت و قطع اتصال");
        await page.getByRole("button", { name: "ارسال" }).click();
        await page
          .getByText("پاسخ آزمایشی به «ثبت و قطع اتصال»")
          .first()
          .waitFor({ timeout: 15_000 });
        await page.waitForTimeout(500);
        assert((await page.getByRole("alert").count()) === 0, "an error card is still shown");
      },
    );

    await check("AC-10/11/13 history: limit 100, normalized search, 100-item note", async () => {
      await open(page, "/m/history");
      await page.getByText("— فقط ۱۰۰ گفتگوی اخیر نمایش داده می‌شود —").waitFor();
      await page.getByLabel("جستجو در عنوان گفتگوها").fill("كگهر");
      await page
        .getByRole("link", { name: /روند فروش ماهانه کگهر/ })
        .first()
        .waitFor();
      await page.getByLabel("جستجو در عنوان گفتگوها").fill("عبارتی که وجود ندارد");
      await page.getByText("نتیجه‌ای یافت نشد").waitFor();
    });

    await check(
      "AC-08 an unknown conversation shows «این گفتگو پیدا نشد» with history and new-chat links",
      async () => {
        await page.goto(`${APP_URL}/m/c/00000000-0000-4000-8000-000000000000`, {
          waitUntil: "networkidle",
        });
        await page.getByText("این گفتگو پیدا نشد").waitFor({ timeout: 15_000 });
        assert(await page.getByRole("link", { name: "تاریخچه" }).count(), "history link");
        assert(
          (await page.getByRole("link", { name: "گفتگوی جدید" }).count()) >= 1,
          "new chat link",
        );
      },
    );

    await check("AC-12 delete needs confirmation and removes the row", async () => {
      await open(page, "/m/history");
      const target = page.getByRole("link", { name: /گفتگوی شماره 7(?!\d)/ });
      await target.waitFor();
      await page
        .getByRole("button", { name: "گزینه‌های گفتگوی گفتگوی شماره 7", exact: true })
        .click();
      await page.getByRole("button", { name: "حذف گفتگو" }).click();
      await page.getByText("این گفتگو برای همیشه حذف می‌شود.").waitFor();
      await page.getByRole("button", { name: "حذف", exact: true }).click();
      await target.waitFor({ state: "detached", timeout: 10_000 });
    });

    await check("AC-14 market shows all six indices with direction glyphs", async () => {
      await open(page, "/m/market");
      const cards = await page.$$eval("main ul.grid > li", (items) =>
        items.map((item) => item.textContent),
      );
      assert(cards.length === 6, `${cards.length} index cards`);
      assert(
        cards.every((text) => /[▲▼]/.test(text)),
        "glyph missing",
      );
    });

    await check(
      "AC-15/16 watchlist rows with names; symbol sheet offers prompts and unfollow",
      async () => {
        await open(page, "/m/watchlist");
        await page.getByText("پالایش نفت اصفهان").waitFor();
        await page.getByText("داده قدیمی").first().waitFor();
        await page.getByRole("button", { name: /شپنا/ }).first().click();
        await page.getByText("گزارش فصلی شپنا").waitFor();
        await page.getByRole("button", { name: "حذف از دیده‌بان" }).waitFor();
      },
    );

    await check(
      "AC-17 add symbol: debounced search, follow with toast, API 400 shown inline",
      async () => {
        await open(page, "/m/watchlist");
        await page.getByRole("button", { name: "افزودن", exact: true }).click();
        const search = page.getByLabel("جستجوی نماد یا نام شرکت");
        await search.fill("کگل");
        await page.getByRole("button", { name: /کگل/ }).click();
        await page
          .getByText("سقف نمادهای قابل دنبال کردن در اشتراک شما پر شده است.")
          .waitFor({ timeout: 10_000 });
        await search.fill("کگهر");
        await page.getByRole("button", { name: /سنگ آهن گهر زمین/ }).click();
        await page.getByText("کگهر به دیده‌بان اضافه شد").waitFor({ timeout: 10_000 });
      },
    );

    await check(
      "AC-19 account: credits, text-only purchase guidance, no tahlilapp.com link",
      async () => {
        await open(page, "/m/account");
        await page.getByText("۱,۲۰۰ از ۲,۶۰۰").waitFor();
        await page
          .getByText("برای خرید یا تمدید اعتبار، از طریق حساب کاربری خود در تحلیل‌اپ اقدام کنید.")
          .waitFor();
        assert(
          (await page.$$("a[href*='tahlilapp.com']")).length === 0,
          "external purchase link rendered",
        );
        await page.getByRole("button", { name: "نمایش نسخه دسکتاپ" }).waitFor();
        await page.getByRole("button", { name: "خروج از حساب" }).waitFor();
      },
    );

    await check(
      "T21 offline: banner, send disabled with hint, draft kept across offline → online",
      async () => {
        await open(page, "/m");
        const composer = page.getByLabel("پرسش خود را بنویسید");
        await composer.fill("پیش‌نویس آفلاین");
        await context.setOffline(true);
        await page.getByText("اتصال اینترنت برقرار نیست").first().waitFor();
        assert(
          await page.getByRole("button", { name: "ارسال" }).isDisabled(),
          "send enabled offline",
        );
        await page.getByText("برای ارسال پرسش به اینترنت نیاز است").waitFor();
        await context.setOffline(false);
        await page.waitForTimeout(300);
        assert((await composer.inputValue()) === "پیش‌نویس آفلاین", "draft lost");
        assert(
          await page.getByRole("button", { name: "ارسال" }).isEnabled(),
          "send still disabled",
        );
      },
    );

    await check(
      "AC-23 desktop switch lands on the desktop path and is not redirected back after reload",
      async () => {
        await open(page, "/m/account");
        await page.getByRole("button", { name: "نمایش نسخه دسکتاپ" }).click();
        await page.waitForURL(/\/profile$/);
        await page.reload({ waitUntil: "networkidle" });
        assert(page.url().endsWith("/profile"), page.url());
        const cookies = await context.cookies();
        assert(
          cookies.some(
            (cookie) => cookie.name === "sapio_experience" && cookie.value === "desktop",
          ),
          "cookie",
        );
        await context.clearCookies();
      },
    );

    await check("Flag off (default): an iPhone opening /chat is not redirected", async () => {
      await page.goto(APP_URL + "/chat", { waitUntil: "networkidle" });
      assert(new URL(page.url()).pathname === "/chat", page.url());
    });
    await context.close();

    // AC-24: a signed-out deep link goes through login and returns to the same conversation.
    const signedOut = await browser.newContext({
      viewport: { width: 360, height: 800 },
      isMobile: true,
      hasTouch: true,
    });
    const signedOutPage = await signedOut.newPage();
    await check(
      "AC-24 signed-out /m/c/{id} → /auth?redirect=… → back to the conversation after login",
      async () => {
        await signedOutPage.goto(APP_URL + threadRoute, { waitUntil: "networkidle" });
        await signedOutPage.waitForURL(/\/auth\?/, { timeout: 15_000 });
        const redirect = new URL(signedOutPage.url()).searchParams.get("redirect");
        assert(redirect === threadRoute, `redirect=${redirect}`);
        await signedOutPage.getByLabel("ایمیل").fill("tester@example.com");
        await signedOutPage.getByLabel("رمز عبور").fill("password");
        await signedOutPage.getByRole("button", { name: "ورود" }).click();
        await signedOutPage.waitForURL((url) => url.pathname === threadRoute, { timeout: 15_000 });
      },
    );
    await signedOut.close();

    // AC-30 / AC-31 / design §1: axe (serious + critical), text ≥ 12 px, 44 px targets, landmarks, in both themes.
    for (const theme of ["dark", "light"]) {
      const themed = await newPhoneContext(browser, { theme });
      const themedPage = await themed.newPage();
      for (const route of screens) {
        await check(`AC-30 axe ${theme} ${route}`, async () => {
          await open(themedPage, route);
          const violations = await runAxe(themedPage);
          assert(violations.length === 0, violations.join("; "));
          const unresolved = await themedPage.evaluate(() => window.__axeUnresolvedContrast ?? 0);
          return unresolved
            ? `${unresolved} contrast checks axe could not resolve (review manually)`
            : "";
        });
        if (theme === "dark") {
          await check(`Design §1 text ≥ 12 px and 44 px targets ${route}`, async () => {
            const small = await smallText(themedPage);
            const targets = await smallTargets(themedPage);
            assert(
              small.length === 0 && targets.length === 0,
              [...small, ...targets].slice(0, 6).join("; "),
            );
          });
          await check(`AC-31 landmarks ${route}`, async () => {
            assert((await themedPage.$$("main")).length === 1, "exactly one main");
            assert((await themedPage.$$("header")).length >= 1, "header");
            const unnamed = await themedPage.$$eval("button, a", (elements) =>
              elements
                .filter((element) => element.getBoundingClientRect().width > 0)
                .filter(
                  (element) => !(element.getAttribute("aria-label") || element.textContent.trim()),
                )
                .map((element) => element.outerHTML.slice(0, 80)),
            );
            assert(unnamed.length === 0, unnamed.join("; "));
          });
        }
      }
      await check(
        `AC-30 axe ${theme} with the symbol sheet and delete confirmation open`,
        async () => {
          await open(themedPage, "/m/watchlist");
          await themedPage.getByRole("button", { name: /شپنا/ }).first().click();
          await themedPage.getByRole("button", { name: "حذف از دیده‌بان" }).waitFor();
          await themedPage.waitForTimeout(400);
          const sheetViolations = await runAxe(themedPage);
          await open(themedPage, "/m/history");
          await themedPage
            .getByRole("button", { name: /گزینه‌های گفتگوی/ })
            .first()
            .click();
          await themedPage.getByRole("button", { name: "حذف گفتگو" }).click();
          await themedPage.getByRole("button", { name: "حذف", exact: true }).waitFor();
          await themedPage.waitForTimeout(400);
          const confirmViolations = await runAxe(themedPage);
          const all = [...sheetViolations, ...confirmViolations];
          assert(all.length === 0, all.join("; "));
        },
      );
      await themed.close();
    }

    // Desktop: the narrow-viewport suggestion appears below 1024 px, and resizing never changes the URL (T16).
    const desktop = await browser.newContext({ viewport: { width: 1440, height: 900 } });
    await desktop.addInitScript(
      (accessToken) => sessionStorage.setItem("financial_copilot_access_token", accessToken),
      fakeAccessToken(),
    );
    const desktopPage = await desktop.newPage();
    await check(
      "T16 desktop banner below 1024 px; resizing never redirects; desktop head unchanged",
      async () => {
        await desktopPage.goto(APP_URL + "/chat", { waitUntil: "networkidle" });
        await desktopPage.getByRole("button", { name: "نسخه موبایل" }).waitFor();
        assert(
          (await desktopPage.getAttribute("meta[name=viewport]", "content")) ===
            "width=device-width, initial-scale=1",
          "desktop viewport changed",
        );
        assert(!(await desktopPage.$("meta[name=robots]")), "desktop has robots meta");
        assert(
          (await desktopPage.getByRole("region", { name: "پیشنهاد نسخه موبایل" }).count()) === 0,
          "banner at 1440",
        );
        await desktopPage.setViewportSize({ width: 800, height: 900 });
        await desktopPage.getByRole("region", { name: "پیشنهاد نسخه موبایل" }).waitFor();
        await desktopPage.setViewportSize({ width: 1440, height: 900 });
        await desktopPage.waitForTimeout(300);
        assert(new URL(desktopPage.url()).pathname === "/chat", desktopPage.url());
      },
    );
    await desktop.close();

    // PWA (production build only): no API/server-function data or HTML in Cache Storage; offline reload (AC-26/27).
    const pwa = await newPhoneContext(browser);
    const pwaPage = await pwa.newPage();
    await check("AC-26 Cache Storage holds static assets only", async () => {
      await open(pwaPage, "/m");
      await pwaPage.evaluate(() => navigator.serviceWorker.ready);
      await open(pwaPage, "/m/market");
      await open(pwaPage, "/m/history");
      await open(pwaPage, threadRoute);
      const cached = await pwaPage.evaluate(async () => {
        const urls = [];
        for (const name of await caches.keys()) {
          for (const request of await (await caches.open(name)).keys())
            urls.push(`${name} ${request.url}`);
        }
        return urls;
      });
      assert(cached.length > 0, "nothing cached: the service worker did not register");
      const forbidden = cached.filter(
        (entry) =>
          /\/_serverFn\/|\/api\/|:55153/.test(entry) ||
          (/\.html$|\/m(\/|$)/.test(entry) && !entry.endsWith("/offline.html")),
      );
      assert(forbidden.length === 0, forbidden.join("; "));
      return `${cached.length} cached entries, all static`;
    });
    await check(
      "AC-27 reload while offline shows /offline.html; «تلاش مجدد» loads the app when back online",
      async () => {
        await pwa.setOffline(true);
        await pwaPage.reload().catch(() => undefined);
        await pwaPage
          .getByRole("heading", { name: "اتصال اینترنت برقرار نیست" })
          .waitFor({ timeout: 10_000 });
        await pwa.setOffline(false);
        await pwaPage.getByRole("button", { name: "تلاش مجدد" }).click();
        // The last page was a conversation, which has a top bar and composer but no tab bar.
        await pwaPage.waitForSelector("main", { timeout: 15_000 });
        await pwaPage.getByLabel("پرسش خود را بنویسید").waitFor({ timeout: 15_000 });
      },
    );
    await pwa.close();
  } finally {
    await browser.close();
    mock.server.close();
  }

  const failed = results.filter((result) => !result.pass);
  for (const result of results) {
    console.log(
      `${result.pass ? "PASS" : "FAIL"}  ${result.name}${result.detail ? `  — ${result.detail}` : ""}`,
    );
  }
  console.log(
    `\n${results.length - failed.length}/${results.length} checks passed. Screenshots: ${SCREENSHOT_DIR}`,
  );
  if (failed.length > 0) process.exitCode = 1;
}

await main();
