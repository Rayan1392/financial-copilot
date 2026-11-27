import assert from "node:assert/strict";
import { once } from "node:events";
import { existsSync } from "node:fs";
import { chromium, devices } from "playwright-core";
import { createViteServer, startMockApi } from "./market-guide-browser-server.mjs";

const guideTitle = "راهنمای تحلیل و رصد بازار";
const executablePath = [
  process.env.CHROME_PATH,
  "C:/Program Files/Google/Chrome/Application/chrome.exe",
  "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
  "/usr/bin/google-chrome",
  "/usr/bin/chromium",
].find((candidate) => candidate && existsSync(candidate));
assert.ok(executablePath, "Chrome or Chromium is required");

const { mock, requests, token } = await startMockApi();
process.env.VITE_MOBILE_AUTO_REDIRECT = "true";
const vite = await createViteServer();
const url = `http://127.0.0.1:${vite.httpServer.address().port}`;
const browser = await chromium.launch({ executablePath });

const countQueries = () =>
  requests.filter(({ method, path }) => method === "POST" && path === "/api/ai/v1/query").length;
const waitForGuide = (page) => page.getByRole("heading", { level: 1, name: guideTitle }).waitFor();
const button = (page, name) => page.getByRole("button", { name, exact: true });

try {
  const guest = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  await guest.grantPermissions(["clipboard-read", "clipboard-write"], { origin: url });
  const page = await guest.newPage();
  page.setDefaultTimeout(60_000);
  const fullImages = [];
  page.on("request", (request) => {
    if (request.url().endsWith("-full.png")) fullImages.push(request.url());
  });
  await page.goto(url, { waitUntil: "commit", timeout: 120_000 });
  const homeLink = page.getByRole("link", { name: guideTitle });
  await homeLink.focus();
  await page.keyboard.press("Tab");
  assert.equal(await page.getByLabel("ایمیل").evaluate((el) => el === document.activeElement), true);
  assert.equal(await homeLink.evaluate((el) => el.closest("form")), null);
  await page.setViewportSize({ width: 320, height: 800 });
  const guidePosition = await homeLink.boundingBox();
  const signInPosition = await page.getByRole("heading", { name: "ورود به حساب" }).boundingBox();
  assert.ok(guidePosition.y + guidePosition.height <= signInPosition.y);
  await page.setViewportSize({ width: 1280, height: 900 });
  await homeLink.click();
  await waitForGuide(page);

  const apiCount = requests.length;
  const topics = page.getByRole("group", { name: "موضوع‌ها" }).getByRole("button");
  const examples = page
    .getByRole("group", { name: "قابلیت‌های موضوع انتخاب‌شده" })
    .getByRole("button");
  let exampleCount = 0;
  let imageCount = 0;
  assert.equal(await topics.count(), 5);
  for (const topic of await topics.all()) {
    await topic.click();
    for (const example of await examples.all()) {
      await example.click();
      exampleCount++;
      const inline = page.locator("article img");
      if (await inline.count()) {
        await inline.scrollIntoViewIfNeeded();
        await inline.evaluate((image) => image.decode());
        imageCount++;
      }
    }
  }
  assert.equal(exampleCount, 15);
  assert.equal(imageCount, 14);
  assert.equal(
    requests.slice(apiCount).some(({ path }) => !path.startsWith("/api/auth/")),
    false,
  );
  assert.equal(fullImages.length, 0);
  console.log("Public home link, 15 examples, and lazy full-size images passed.");

  await topics.first().click();
  await button(page, "کپی پرسش").click();
  await page.getByText("پرسش کپی شد", { exact: true }).waitFor();
  assert.match(await page.evaluate(() => navigator.clipboard.readText()), /خزامیا/);
  const expand = button(page, "بزرگ‌نمایی");
  await expand.focus();
  await page.keyboard.press("Enter");
  const viewer = page.getByRole("dialog");
  await viewer.getByRole("img").evaluate((image) => image.decode());
  assert.equal(fullImages.length, 1);
  await button(page, "اندازهٔ اصلی").click();
  assert.equal(
    await viewer.getByLabel("تصویر قابل پیمایش").evaluate((el) => el.scrollWidth > el.clientWidth),
    true,
  );
  await page.keyboard.press("Escape");
  await viewer.waitFor({ state: "hidden" });
  assert.equal(await expand.evaluate((el) => el === document.activeElement), true);

  await page
    .getByRole("group", { name: "موضوع‌ها" })
    .getByText("صورت‌های مالی", { exact: true })
    .click();
  await examples.getByText("صورت جریان وجوه نقد", { exact: true }).click();
  await button(page, "باز کردن در گفتگو").click();
  await page.waitForURL((target) => target.pathname === "/auth");
  assert.equal(
    new URL(page.url()).searchParams.get("redirect"),
    "/market-guide?example=cash&source=desktop",
  );
  await page.getByLabel("ایمیل").fill("tester@example.com");
  await page.getByLabel("گذرواژه").fill("mock-password");
  await button(page, "ورود").click();
  await page.waitForURL(
    (target) =>
      target.pathname === "/market-guide" && target.searchParams.get("example") === "cash",
  );
  await page.getByRole("heading", { name: "صورت جریان وجوه نقد", exact: true }).waitFor();

  const before = countQueries();
  await button(page, "باز کردن در گفتگو").click();
  await page.waitForURL((target) => target.pathname === "/chat");
  const draft = page.locator("textarea");
  await draft.waitFor();
  await page.waitForFunction(
    () => document.querySelector("textarea")?.value === "جریان وجه نقد فملی را نشان بده.",
  );
  assert.equal(countQueries(), before);
  await Promise.all([
    once(mock.server, "market-guide-query", { signal: AbortSignal.timeout(15_000) }),
    button(page, "ارسال").click(),
  ]);
  assert.equal(countQueries(), before + 1);
  await page
    .getByRole("navigation", { name: "ناوبری اصلی" })
    .getByRole("link", { name: guideTitle })
    .click();
  await waitForGuide(page);
  await guest.close();

  for (const [width, theme] of [
    [320, "light"],
    [390, "dark"],
  ]) {
    const mobile = await browser.newContext({
      ...devices["iPhone 13"],
      viewport: { width, height: 844 },
    });
    await mobile.addInitScript(
      ([accessToken, color]) => {
        sessionStorage.setItem("financial_copilot_access_token", accessToken);
        localStorage.setItem("financial-copilot-theme", color);
      },
      [token, theme],
    );
    const phone = await mobile.newPage();
    phone.setDefaultTimeout(60_000);
    await phone.goto(`${url}/m/account`, { waitUntil: "commit", timeout: 120_000 });
    await phone.getByRole("link", { name: guideTitle }).tap();
    await waitForGuide(phone);
    await phone.waitForFunction(
      (color) => document.documentElement.classList.contains(color),
      theme,
    );
    assert.equal(
      await phone.evaluate(
        () => document.documentElement.scrollWidth > document.documentElement.clientWidth,
      ),
      false,
    );
    await button(phone, "بزرگ‌نمایی").tap();
    await phone.getByRole("dialog").getByRole("img").evaluate((image) => image.decode());
    await button(phone, "اندازهٔ اصلی").tap();
    const pans = await phone.getByLabel("تصویر قابل پیمایش").evaluate((el) => {
      el.scrollBy({ left: -150, top: 150 });
      return el.scrollLeft !== 0 && el.scrollTop !== 0;
    });
    assert.equal(pans, true);
    await button(phone, "بستن").tap();
    const count = countQueries();
    await button(phone, "باز کردن در گفتگو").tap();
    await phone.waitForURL((target) => target.pathname === "/m");
    await phone.waitForFunction(
      () => document.querySelector("textarea")?.value === "روند فروش ماهانهٔ خزامیا را نشان بده.",
    );
    assert.equal(countQueries(), count);

    await phone.goto(`${url}/market-guide?source=mobile&example=events`, { waitUntil: "commit", timeout: 120_000 });
    await phone.waitForFunction((color) => document.documentElement.classList.contains(color), theme);
    await button(phone, "باز کردن رویدادهای دیده‌بان").tap();
    await phone.waitForURL(
      (target) =>
        target.pathname === "/followed-symbols" && target.searchParams.get("tab") === "events",
    );
    await phone.getByRole("heading", { name: "رویدادهای نمادهای دیده‌بان" }).waitFor();
    await mobile.close();
  }
  console.log("Market guide passed guest, desktop, and mobile browser flows.");
} finally {
  await browser.close();
  await vite.close();
  mock.server.closeAllConnections();
  await new Promise((resolve, reject) =>
    mock.server.close((err) => (err ? reject(err) : resolve())),
  );
}
