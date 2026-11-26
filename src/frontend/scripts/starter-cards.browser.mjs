// Change «valuation-gauge-starter-card» browser verification (T3): the new-chat starter cards on desktop and mobile,
// with and without the valuation gauge card, in both themes. It checks horizontal scroll, clipped text, full grid rows,
// text contrast >= 4.5:1, the reduced-motion dial pointer, and saves screenshots as evidence.
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
const evidenceDir = path.resolve(
  frontendRoot,
  "../..",
  evidenceArgument > 0
    ? process.argv[evidenceArgument + 1]
    : "specs/159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-starter-card/evidence/browser",
);
const fixturePath = "src/components/app/__tests__/starter-cards-browser-fixture.html";
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
    const targets = [...document.querySelectorAll("[data-testid='starter-root'] *")].filter(
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
  { name: "320", width: 320, height: 640 },
];
const views = [
  { view: "desktop", gauge: "1", layouts: ["1280", "768", "430", "375", "320"], cards: 9 },
  { view: "desktop", gauge: "0", layouts: ["1280", "768", "375"], cards: 8 },
  { view: "mobile", gauge: "1", layouts: ["430", "375", "320"], cards: 9 },
  { view: "mobile", gauge: "0", layouts: ["375"], cards: 8 },
];

await mkdir(evidenceDir, { recursive: true });
const server = await createServer({
  configFile: false,
  root: frontendRoot,
  appType: "mpa",
  plugins: [react(), tailwindcss()],
  resolve: { alias: [{ find: "@", replacement: path.join(frontendRoot, "src") }] },
  server: { host: "127.0.0.1", port: 0 },
});

let browser;
try {
  await server.listen();
  const { port } = server.httpServer.address();
  browser = await chromium.launch({ executablePath, headless: true, args: ["--no-sandbox"] });
  const url = (view, gauge) =>
    `http://127.0.0.1:${port}/${fixturePath}?view=${view}&gauge=${gauge}`;

  for (const { view, gauge, layouts: names, cards } of views) {
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
          first.includes("گیج‌های ارزش‌گذاری گدنا"),
          gauge === "1",
          `${label}: first card «${first}»`,
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
  for (const reducedMotion of ["no-preference", "reduce"]) {
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
  for (const theme of ["dark", "light"]) {
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
  await writeFile(path.join(evidenceDir, "browser-check.txt"), `${log.join("\n")}\n`, "utf8");
} finally {
  await browser?.close();
  await server.close();
}
