// Browser verification of the new-chat starter cards (changes «valuation-gauge-starter-card» T3 and
// «starter-cards-symbol-picker» 6.1) on desktop and mobile, with and without the valuation gauge card, in both themes.
// It checks horizontal scroll, clipped text, full grid rows, text contrast >= 4.5:1, the reduced-motion dial pointer
// and the symbol picker (open, close, search, keyboard, what is sent), and saves screenshots as evidence.
import assert from "node:assert/strict";
import { existsSync } from "node:fs";
import { mkdir, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import { chromium } from "playwright-core";
import { createServer } from "vite";

const frontendRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
// `--evidence <dir>` (relative to the repository root) saves the log and screenshots for a later change.
const evidenceArgument = process.argv.indexOf("--evidence");
// `--only cards` or `--only picker` runs one half of the check, for a quick rerun; the log then gets that suffix.
const onlyArgument = process.argv.indexOf("--only");
const only = onlyArgument > 0 ? process.argv[onlyArgument + 1] : null;
const runs = (part) => only === null || only === part;
const evidenceDir = path.resolve(
  frontendRoot,
  "../..",
  evidenceArgument > 0
    ? process.argv[evidenceArgument + 1]
    : "specs/153-mobile-pwa-workspace/changes/starter-cards-symbol-picker/evidence/browser",
);
const fixturePath = "src/components/app/__tests__/starter-cards-browser-fixture.html";
const stubsPath = "src/components/app/__tests__/starter-cards-browser-stubs.ts";
const executablePath = [
  process.env.CHROME_PATH,
  "C:/Program Files/Google/Chrome/Application/chrome.exe",
  "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
  "/usr/bin/google-chrome",
  "/usr/bin/chromium",
]
  .filter(Boolean)
  .find(existsSync);
if (!executablePath) throw new Error("A local Chrome or Chromium executable is required.");

const log = [];
const record = (line) => {
  log.push(line);
  console.log(`ok - ${line}`);
};

// Every text node's element against its own resolved background (oklch resolved through a 1-pixel canvas).
async function minContrast(page) {
  return page.evaluate(() => {
    const canvas = document.createElement("canvas");
    canvas.width = canvas.height = 1;
    const context = canvas.getContext("2d", { willReadFrequently: true });
    const parse = (value) => {
      context.clearRect(0, 0, 1, 1);
      context.fillStyle = "#000";
      context.fillStyle = value;
      context.fillRect(0, 0, 1, 1);
      const [r, g, b, a] = context.getImageData(0, 0, 1, 1).data;
      return [r, g, b, a / 255];
    };
    const blend = (top, bottom) =>
      top.slice(0, 3).map((v, i) => v * top[3] + bottom[i] * (1 - top[3]));
    // Gradients have no background-color; their lightest stop is taken as the worst case for light text.
    const gradientStops = (node) =>
      [...getComputedStyle(node).backgroundImage.matchAll(/(oklch|rgba?|color)\([^)]*\)/g)].map(
        (m) => parse(m[0]),
      );
    const background = (element) => {
      const layers = [];
      for (let node = element; node; node = node.parentElement) {
        const color = parse(getComputedStyle(node).backgroundColor);
        if (color[3] > 0) layers.push(color);
        if (color[3] >= 0.99) break;
      }
      return layers.reduceRight((acc, layer) => blend(layer, acc), [255, 255, 255]);
    };
    const lum = (rgb) =>
      rgb
        .map((v) => {
          v /= 255;
          return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4;
        })
        .reduce((sum, v, i) => sum + v * [0.2126, 0.7152, 0.0722][i], 0);
    const ratio = (a, b) => (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
    const targets = [
      ...document.querySelectorAll("[data-testid='starter-root'] *, [role='dialog'] *"),
    ].filter(
      (element) =>
        !element.closest("svg") &&
        [...element.childNodes].some((node) => node.nodeType === 3 && node.textContent.trim()),
    );
    return targets
      .map((element) => {
        const base = background(element);
        const card = element.closest("button");
        const backdrops = [
          base,
          ...(card ? gradientStops(card).map((stop) => blend(stop, base)) : []),
        ];
        const color = getComputedStyle(element).color;
        const fg = parse(color);
        const value = Math.min(...backdrops.map((bg) => ratio(lum(blend(fg, bg)), lum(bg))));
        return {
          value,
          text: element.textContent.trim().slice(0, 40),
          color,
          backdrops: backdrops.map(String),
        };
      })
      .reduce((worst, item) => (item.value < worst.value ? item : worst), { value: Infinity });
  });
}

const layouts = [
  { name: "1280", width: 1280, height: 1000 },
  { name: "768", width: 768, height: 1000 },
  { name: "430", width: 430, height: 932 },
  { name: "375", width: 375, height: 812 },
  { name: "360", width: 360, height: 740 },
  { name: "320", width: 320, height: 640 },
];
const views = [
  { view: "desktop", gauge: "1", layouts: ["1280", "768", "430", "375", "320"], cards: 9 },
  { view: "desktop", gauge: "0", layouts: ["1280", "768", "375"], cards: 8 },
  { view: "mobile", gauge: "1", layouts: ["430", "375", "360", "320"], cards: 9 },
  { view: "mobile", gauge: "0", layouts: ["375"], cards: 8 },
];

await mkdir(evidenceDir, { recursive: true });
const server = await createServer({
  configFile: false,
  root: frontendRoot,
  appType: "mpa",
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: [
      // The fixture has no server: `useServerFn` and the symbol search are stand-ins.
      { find: "@tanstack/react-start", replacement: path.join(frontendRoot, stubsPath) },
      { find: "@/lib/metadata.functions", replacement: path.join(frontendRoot, stubsPath) },
      { find: "@", replacement: path.join(frontendRoot, "src") },
    ],
  },
  server: { host: "127.0.0.1", port: 0 },
});

let browser;
try {
  await server.listen();
  const { port } = server.httpServer.address();
  browser = await chromium.launch({ executablePath, headless: true, args: ["--no-sandbox"] });
  const url = (view, gauge, watch = "0") =>
    `http://127.0.0.1:${port}/${fixturePath}?view=${view}&gauge=${gauge}&watch=${watch}`;

  for (const { view, gauge, layouts: names, cards } of runs("cards") ? views : []) {
    for (const layout of layouts.filter((item) => names.includes(item.name))) {
      for (const theme of ["dark", "light"]) {
        const context = await browser.newContext({
          viewport: { width: layout.width, height: layout.height },
        });
        const page = await context.newPage();
        await page.goto(url(view, gauge), { waitUntil: "domcontentloaded", timeout: 120000 });
        const root = page.getByTestId("starter-root");
        await root.waitFor({ timeout: 120000 });
        await page.evaluate(
          (light) => document.documentElement.classList.toggle("light", light),
          theme === "light",
        );
        await page.waitForTimeout(200);
        const label = `${view} gauge=${gauge} ${layout.name} ${theme}`;

        const buttons = await root.locator("button").count();
        assert.equal(buttons, cards, `${label}: ${buttons} cards`);
        const first = await root.locator("button").first().textContent();
        assert.equal(
          first.includes("گیج‌های ارزش‌گذاری نماد"),
          gauge === "1",
          `${label}: first card «${first}»`,
        );
        // Six symbol cards show the «نماد» slot and no fixed symbol; two fixed examples are marked «نمونه».
        const slots = await root.getByTestId("starter-symbol-slot").count();
        assert.equal(slots, gauge === "1" ? 6 : 5, `${label}: ${slots} symbol slots`);
        assert.equal(await root.getByTestId("starter-example-mark").count(), 2, `${label}: marks`);
        const symbolCardTexts = await root
          .locator("button[aria-haspopup='dialog']")
          .allTextContents();
        for (const symbol of ["گدنا", "کگهر", "شاراک", "فولاد", "فاذر", "اخابر"])
          assert.ok(
            symbolCardTexts.every((text) => !text.includes(symbol)),
            `${label}: a symbol card names ${symbol}`,
          );

        const overflow = await page.evaluate(
          () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
        );
        assert.ok(overflow <= 1, `${label}: horizontal scroll of ${overflow}px`);

        const clipped = await root.evaluate((element) =>
          [...element.querySelectorAll("*")]
            .filter(
              (node) =>
                !(node instanceof SVGElement) &&
                node.scrollWidth > node.clientWidth + 1 &&
                getComputedStyle(node).display !== "inline",
            )
            .map((node) => node.textContent?.slice(0, 40)),
        );
        assert.deepEqual(clipped, [], `${label}: clipped content`);

        // Rows: on the two-column desktop grid every row is full; the featured card fills its row alone.
        if (view === "desktop") {
          const rows = await page.getByTestId("starter-card-grid").evaluate((grid) => {
            const width = grid.getBoundingClientRect().width;
            const byTop = new Map();
            for (const button of grid.querySelectorAll(":scope > button")) {
              const r = button.getBoundingClientRect();
              const key = Math.round(r.top);
              byTop.set(key, [...(byTop.get(key) ?? []), r.width / width]);
            }
            return [...byTop.values()];
          });
          const columns = layout.width >= 768 ? 2 : 1;
          for (const row of rows) {
            const full = row.length === columns || (row.length === 1 && row[0] > 0.98);
            assert.ok(
              full,
              `${label}: a row of ${row.length} card(s) at widths ${row.map((w) => w.toFixed(2))}`,
            );
          }
          record(`${label}: ${rows.length} full rows`);
        }

        const worst = await minContrast(page);
        const contrast = worst.value;
        assert.ok(
          contrast >= 4.5,
          `${label}: text contrast ${contrast.toFixed(2)} ${JSON.stringify(worst)}`,
        );
        record(
          `${label}: ${cards} cards, no scroll, nothing clipped, contrast >= ${contrast.toFixed(2)}`,
        );
        await page.screenshot({
          path: path.join(evidenceDir, `${view}-gauge${gauge}-${layout.name}-${theme}.png`),
          fullPage: true,
        });
        if (view === "desktop" && gauge === "1" && ["1280", "375"].includes(layout.name)) {
          await root.locator("button").first().hover();
          await page.waitForTimeout(700);
          await root
            .locator("button")
            .first()
            .screenshot({
              path: path.join(evidenceDir, `desktop-featured-hover-${layout.name}-${theme}.png`),
            });
        }
        await context.close();
      }
    }
  }

  // The dial pointer eases on hover and stays still under reduced motion (AC-06).
  for (const reducedMotion of runs("cards") ? ["no-preference", "reduce"] : []) {
    const context = await browser.newContext({
      viewport: { width: 1280, height: 900 },
      reducedMotion,
    });
    const page = await context.newPage();
    await page.goto(url("desktop", "1"), { waitUntil: "domcontentloaded", timeout: 120000 });
    await page.getByTestId("starter-root").waitFor({ timeout: 120000 });
    await page.getByTestId("starter-card-grid").locator("button").first().hover();
    await page.waitForTimeout(700);
    // Tailwind v4 rotates through the CSS `rotate` property, not `transform`.
    const rotate = await page
      .getByTestId("starter-card-dial-pointer")
      .evaluate((node) => getComputedStyle(node).rotate);
    const still = rotate === "none" || rotate === "0deg";
    assert.equal(
      still,
      reducedMotion === "reduce",
      `reduced motion ${reducedMotion}: pointer rotate ${rotate}`,
    );
    record(`reduced motion ${reducedMotion}: pointer rotate ${rotate} after hover`);
    await context.close();
  }
  // Keyboard focus reaches the featured card first and draws a visible ring (AC-05).
  for (const theme of runs("cards") ? ["dark", "light"] : []) {
    const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
    const page = await context.newPage();
    await page.goto(url("desktop", "1"), { waitUntil: "domcontentloaded", timeout: 120000 });
    await page.getByTestId("starter-root").waitFor({ timeout: 120000 });
    await page.evaluate(
      (light) => document.documentElement.classList.toggle("light", light),
      theme === "light",
    );
    await page.keyboard.press("Tab");
    await page.waitForTimeout(500); // the card transitions its shadow, ring included, over 200 ms
    const focus = await page.evaluate(() => {
      const element = document.activeElement;
      return {
        featured: element?.hasAttribute("data-featured") ?? false,
        visible: element?.matches(":focus-visible") ?? false,
        ring: element ? getComputedStyle(element).boxShadow : "none",
      };
    });
    assert.ok(focus.featured && focus.visible, `focus ${theme}: ${JSON.stringify(focus)}`);
    // The ring is the box-shadow layer with no offset or blur; the card's own shadow-sm layers have a blur.
    const spread = Math.max(
      0,
      ...[...focus.ring.matchAll(/ 0px 0px 0px ([\d.]+)px/g)].map((match) => Number(match[1])),
    );
    assert.ok(spread >= 2, `focus ${theme}: ring spread ${spread}px in ${focus.ring}`);
    record(`focus ${theme}: Tab reaches the featured card first, ${spread}px ring`);
    await page
      .getByTestId("starter-card-grid")
      .locator("button")
      .first()
      .screenshot({ path: path.join(evidenceDir, `desktop-featured-focus-${theme}.png`) });
    await context.close();
  }
  // The symbol picker (change «starter-cards-symbol-picker»): desktop dialog and phone bottom sheet.
  const pickerRuns = [
    { view: "desktop", layout: "1280", watch: "1" },
    { view: "desktop", layout: "1280", watch: "0" },
    { view: "desktop", layout: "360", watch: "1" },
    { view: "mobile", layout: "360", watch: "1" },
    { view: "mobile", layout: "360", watch: "0" },
    { view: "mobile", layout: "430", watch: "1" },
  ];
  for (const run of runs("picker") ? pickerRuns : []) {
    const layout = layouts.find((item) => item.name === run.layout);
    for (const theme of ["dark", "light"]) {
      const context = await browser.newContext({
        viewport: { width: layout.width, height: layout.height },
        hasTouch: run.view === "mobile",
      });
      const page = await context.newPage();
      await page.goto(url(run.view, "1", run.watch), {
        waitUntil: "domcontentloaded",
        timeout: 120000,
      });
      const root = page.getByTestId("starter-root");
      await root.waitFor({ timeout: 120000 });
      await page.evaluate(
        (light) => document.documentElement.classList.toggle("light", light),
        theme === "light",
      );
      const label = `picker ${run.view} ${run.layout} watch=${run.watch} ${theme}`;
      const sent = () => page.getByTestId("sent").textContent();
      const dialog = page.getByRole("dialog");
      const options = () => dialog.getByRole("option").allTextContents();
      const preview = dialog.getByTestId("starter-picker-preview");
      const gaugeCard = root.locator("button").first();
      const noScroll = async (step) => {
        const overflow = await page.evaluate(
          () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
        );
        assert.ok(overflow <= 1, `${label}: horizontal scroll of ${overflow}px ${step}`);
      };
      const shot = (step) =>
        page.screenshot({
          path: path.join(
            evidenceDir,
            `picker-${run.view}-${run.layout}-watch${run.watch}-${theme}-${step}.png`,
          ),
        });

      // Opening sends nothing; the picker is titled by the card's topic and offers watchlist and example rows.
      await gaugeCard.click();
      await dialog.waitFor({ timeout: 10000 });
      await page.waitForTimeout(600); // the dialog and the sheet animate in
      assert.equal(await sent(), "", `${label}: opening the picker sent «${await sent()}»`);
      assert.ok(
        (await dialog.getByRole("heading").textContent()).includes("ارزش‌گذاری"),
        `${label}: picker title`,
      );
      assert.deepEqual(
        await options(),
        run.watch === "1" ? ["شپنا", "فملی", "گدنا"] : ["گدنا"],
        `${label}: rows before typing`,
      );
      assert.equal(
        await dialog.getByText("دیده‌بان شما").count(),
        run.watch === "1" ? 1 : 0,
        `${label}: watchlist heading`,
      );
      const focused = await page.evaluate(() => document.activeElement?.getAttribute("role"));
      assert.equal(focused, "combobox", `${label}: focus starts in ${focused}`);
      await noScroll("with the picker open");
      const heights = await dialog
        .getByRole("option")
        .evaluateAll((rows) => rows.map((row) => row.getBoundingClientRect().height));
      assert.ok(Math.min(...heights) >= 44, `${label}: row height ${Math.min(...heights)}px`);
      const worst = await minContrast(page);
      assert.ok(
        worst.value >= 4.5,
        `${label}: text contrast ${worst.value.toFixed(2)} ${JSON.stringify(worst)}`,
      );
      await shot("open");

      // Escape closes, sends nothing and returns focus to the card.
      await page.keyboard.press("Escape");
      await dialog.waitFor({ state: "hidden", timeout: 10000 });
      assert.equal(await sent(), "", `${label}: closing the picker sent «${await sent()}»`);
      const back = await gaugeCard.evaluate((card) => card === document.activeElement);
      assert.ok(back, `${label}: focus did not return to the card`);

      // Keyboard only: open, type, see the states and the preview, choose with the arrows and Enter.
      await page.keyboard.press("Enter");
      await dialog.waitFor({ timeout: 10000 });
      await page.keyboard.type("ققق");
      await dialog.getByText("در حال جست‌وجو…").waitFor({ timeout: 5000 });
      await dialog.getByText("نمادی یافت نشد").waitFor({ timeout: 5000 });
      assert.ok((await preview.textContent()).includes("‹نماد›"), `${label}: preview with no row`);
      for (let i = 0; i < 3; i++) await page.keyboard.press("Backspace");
      await page.keyboard.type("خطا");
      await dialog.getByText("جست‌وجو ناموفق بود").waitFor({ timeout: 5000 });
      await dialog.getByRole("button", { name: "تلاش مجدد" }).waitFor({ timeout: 5000 });
      await shot("failed");
      for (let i = 0; i < 3; i++) await page.keyboard.press("Backspace");
      await page.keyboard.type("فولا");
      await dialog.getByRole("option").first().waitFor({ timeout: 5000 });
      // The rights issue «فولادح» is left out.
      assert.deepEqual(
        await options(),
        ["فولادفولاد مبارکه اصفهان", "فولاژفولاد آلیاژی ایران"],
        `${label}: search rows`,
      );
      await page.waitForTimeout(200);
      assert.ok(
        (await preview.textContent()).includes("«گیج‌های ارزش‌گذاری فولاد»"),
        `${label}: preview «${await preview.textContent()}»`,
      );
      await page.keyboard.press("ArrowDown");
      await page.waitForTimeout(200);
      assert.ok(
        (await preview.textContent()).includes("«گیج‌های ارزش‌گذاری فولاژ»"),
        `${label}: preview after ArrowDown «${await preview.textContent()}»`,
      );
      await noScroll("with search results");
      await shot("results");
      await page.keyboard.press("ArrowUp");
      await page.keyboard.press("Enter");
      await dialog.waitFor({ state: "hidden", timeout: 10000 });
      assert.equal(await sent(), "گیج‌های ارزش‌گذاری فولاد", `${label}: sent «${await sent()}»`);

      // A long company name is cut, not overflowing.
      await root.locator("button[aria-haspopup='dialog']").nth(1).click();
      await dialog.waitFor({ timeout: 10000 });
      await page.keyboard.type("وتوصا");
      await dialog.getByRole("option").first().waitFor({ timeout: 5000 });
      await noScroll("with a long company name");
      await page.keyboard.press("Escape");
      await dialog.waitFor({ state: "hidden", timeout: 10000 });

      // Pointer: the example row sends the example question; a direct card sends in one tap.
      await gaugeCard.click();
      await dialog.waitFor({ timeout: 10000 });
      await page.waitForTimeout(600);
      await dialog.getByRole("option", { name: "گدنا" }).click();
      await dialog.waitFor({ state: "hidden", timeout: 10000 });
      await root.getByRole("button", { name: /رتبه‌بندی گزارش‌های ماهانه را نشان بده/ }).click();
      assert.equal(
        await sent(),
        "گیج‌های ارزش‌گذاری فولاد|گیج‌های ارزش‌گذاری گدنا|رتبه‌بندی گزارش‌های ماهانه را نشان بده",
        `${label}: sent «${await sent()}»`,
      );
      assert.equal(await dialog.count(), 0, `${label}: a direct card opened the picker`);
      record(
        `${label}: opens without sending, Escape returns focus, states shown, keyboard choice sends the previewed question, rows >= ${Math.min(...heights)}px, contrast >= ${worst.value.toFixed(2)}`,
      );
      await context.close();
    }
  }
  await writeFile(
    path.join(evidenceDir, only ? `browser-check-${only}.txt` : "browser-check.txt"),
    `${log.join("\n")}\n`,
    "utf8",
  );
} finally {
  await browser?.close();
  await server.close();
}
