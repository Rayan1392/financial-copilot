// Feature 159 change «valuation-gauge-answer-image-share» browser check: in a real Chrome, «کپی تصویر» on a gauge
// answer puts a PNG on the clipboard. The picture has the fixed width (every card of the answer, no segmented control)
// at a phone and at a desktop viewport, in the dark and the light theme, and matches a browser screenshot of the same
// picture rendered on screen. The off-screen frame is inert, adds no page scroll and is gone afterwards.
// It does not cover the share sheet or Safari: those need real devices (tasks.md, section 6).
// Feature 125 change «comparison-answer-image-share» adds the own-industry comparison fixtures: the picture is the whole
// answer with the full table, also where the screen (phone density) shows cards.
// Run: npm run test:answer-image-browser [-- --evidence <dir relative to the repository root>]
//      npm run test:comparison-image-browser   (the comparison fixtures, evidence under the Feature 125 change)
//      npm run test:product-mix-image-browser  (the product revenue mix fixtures, Feature 075 change)
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
const evidenceArgument = process.argv.indexOf("--evidence");
const evidenceDir = path.resolve(
  frontendRoot,
  "../..",
  evidenceArgument > 0 && process.argv[evidenceArgument + 1]
    ? process.argv[evidenceArgument + 1]
    : "specs/159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-answer-image-share/evidence/browser",
);
const fixturePath = "src/components/app/__tests__/answer-image-share-browser-fixture.html";
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

const PIXEL_RATIO = 2;
// Share of pixels that may differ clearly between the produced picture and the browser's own screenshot. The two are
// rasterised by different paths (SVG foreignObject and the compositor), so text edges differ slightly; a missing card,
// or a wrong layout differs far more than this. A pointer caught mid-animation is too small to show up here, so the
// check also asserts that nothing in the frame is animated.
const MAX_DIFFERENT_PIXELS = 0.015;

const comparisonTexts = [
  "مقایسه نماد با هم‌گروهی‌ها",
  "گروه صنعتی:",
  "اندازه گروه:",
  "کمتر از میانگین گروه",
  "این اطلاعات توصیه به سرمایه گذاری نیست",
];
const mixTexts = [
  "ترکیب درآمد محصولات — معدنی و صنعتی گهر زمین (کگهر)",
  "دوره: ۱۴۰۵/۰۷",
  "فروش (تومان)",
  "گندله بورس کالا",
];
const allFixtures = {
  single: { cards: 1, width: 480 },
  all: { cards: 3, width: 800 },
  forward: { cards: 3, width: 800 },
  // Markdown answers. rows: body rows of the table in the picture (a comparison adds the group-average row).
  comparison: { markdown: true, layout: "wide", rows: 5, width: 800, texts: comparisonTexts },
  "comparison-four": {
    markdown: true,
    layout: "compact-table",
    rows: 5,
    width: 560,
    texts: comparisonTexts,
  },
  "comparison-large": {
    markdown: true,
    layout: "wide",
    rows: 41,
    width: 800,
    texts: comparisonTexts,
  },
  mix: { markdown: true, rows: 8, width: 640, texts: mixTexts },
  // Wrapped product names fill this picture with text, so more edge pixels differ between the two rasterisers
  // (measured 2.0%). A wrong wrap or a missing row moves whole lines and changes the height, which is checked apart.
  "mix-long": { markdown: true, rows: 25, width: 640, texts: mixTexts, maxDifferent: 0.03 },
};
const fixturesArgument = process.argv.indexOf("--fixtures");
const selected =
  fixturesArgument > 0 && process.argv[fixturesArgument + 1]
    ? process.argv[fixturesArgument + 1].split(",")
    : ["single", "all", "forward"];
const fixtures = Object.fromEntries(
  selected.map((name) => {
    assert.ok(allFixtures[name], `unknown fixture ${name}`);
    return [name, allFixtures[name]];
  }),
);
const viewports = [
  { name: "phone-390", width: 390, height: 844 },
  { name: "desktop-1280", width: 1280, height: 900 },
];

const log = [];
const record = (line) => {
  log.push(line);
  console.log(`ok - ${line}`);
};

async function open(context, port, query, theme) {
  const page = await context.newPage();
  await page.goto(`http://127.0.0.1:${port}/${fixturePath}?${query}`, {
    waitUntil: "domcontentloaded",
    timeout: 120000,
  });
  await page
    .locator(
      "[data-testid='valuation-gauge-cards'], [data-testid='industry-comparison'], [role='region'][aria-label='جدول']",
    )
    .first()
    .waitFor({ timeout: 120000 });
  await page.evaluate(
    (light) => document.documentElement.classList.toggle("light", light),
    theme === "light",
  );
  await page.evaluate(() => document.fonts.ready);
  return page;
}

// Loads two PNG data URLs and reports their sizes and the share of clearly different pixels.
async function compare(page, produced, reference) {
  return page.evaluate(
    async ([a, b]) => {
      const load = async (url) => {
        const image = new Image();
        image.src = url;
        await image.decode();
        const canvas = document.createElement("canvas");
        canvas.width = image.naturalWidth;
        canvas.height = image.naturalHeight;
        const context = canvas.getContext("2d", { willReadFrequently: true });
        context.drawImage(image, 0, 0);
        return { width: canvas.width, height: canvas.height, context };
      };
      const first = await load(a);
      const second = await load(b);
      const width = Math.min(first.width, second.width);
      const height = Math.min(first.height, second.height);
      const one = first.context.getImageData(0, 0, width, height).data;
      const two = second.context.getImageData(0, 0, width, height).data;
      let different = 0;
      const colours = new Set();
      for (let index = 0; index < one.length; index += 4) {
        const delta =
          Math.abs(one[index] - two[index]) +
          Math.abs(one[index + 1] - two[index + 1]) +
          Math.abs(one[index + 2] - two[index + 2]);
        if (delta > 96) different++;
        if (index % 400 === 0)
          colours.add((one[index] << 16) | (one[index + 1] << 8) | one[index + 2]);
      }
      return {
        produced: [first.width, first.height],
        reference: [second.width, second.height],
        different: different / (width * height),
        colours: colours.size,
      };
    },
    [produced, reference],
  );
}

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

  for (const [fixture, expected] of Object.entries(fixtures)) {
    for (const viewport of viewports) {
      for (const theme of ["dark", "light"]) {
        const label = `${fixture} ${viewport.name} ${theme}`;
        const context = await browser.newContext({
          viewport: { width: viewport.width, height: viewport.height },
          deviceScaleFactor: PIXEL_RATIO,
          permissions: ["clipboard-read", "clipboard-write"],
        });
        // The phone workspace renders answers in the compact density.
        const density = viewport.width < 720 ? "&density=compact" : "";
        const page = await open(context, port, `fixture=${fixture}${density}`, theme);
        const errors = [];
        page.on("pageerror", (error) => errors.push(error.message));

        // What the frame looks like while it is mounted, recorded from inside the page.
        await page.evaluate(() => {
          window.__frames = [];
          new MutationObserver(() => {
            const picture = document.querySelector("[data-share-frame]");
            if (!picture || picture.dataset.seen) return;
            picture.dataset.seen = "1";
            const visible = (node) => node.getClientRects().length > 0;
            window.__frames.push({
              width: picture.getBoundingClientRect().width,
              cards: [...picture.querySelectorAll("[data-testid^='valuation-gauge-card-']")].filter(
                visible,
              ).length,
              tabs: [...picture.querySelectorAll("[role='tablist']")].filter(visible).length,
              details: [...picture.querySelectorAll("details")].filter(visible).length,
              animated: [...picture.querySelectorAll("*")].filter(
                (node) => getComputedStyle(node).animationName !== "none",
              ).length,
              layouts: [...picture.querySelectorAll("[data-layout]")]
                .filter(visible)
                .map((node) => node.dataset.layout),
              rows: [...picture.querySelectorAll("tbody tr")].filter(visible).length,
              text: picture.textContent,
              // A table wider than the picture would be cut off at its scroll container.
              clipped: [...picture.querySelectorAll("[role=region]")].filter(
                (node) => node.scrollWidth > node.clientWidth + 1,
              ).length,
              buttons: picture.querySelectorAll("button").length,
              inert: picture.parentElement.inert,
              hidden: picture.parentElement.getAttribute("aria-hidden"),
              scroll: document.documentElement.scrollWidth - document.documentElement.clientWidth,
            });
          }).observe(document.body, { childList: true, subtree: true });
        });

        const button = page.getByRole("button", { name: "کپی تصویر" });
        await button.waitFor({ timeout: 30000 });
        const onScreenTabs = await page.getByTestId("valuation-gauge-tabs").isVisible();
        const onScreenLayouts = await page.evaluate(() =>
          [...document.querySelectorAll("[data-layout]")]
            .filter((node) => node.getClientRects().length > 0)
            .map((node) => node.dataset.layout),
        );
        const started = Date.now();
        await button.click();
        await page.getByRole("button", { name: "تصویر کپی شد" }).waitFor({ timeout: 30000 });
        const drawMs = Date.now() - started;

        const frames = await page.evaluate(() => window.__frames);
        assert.equal(frames.length, 1, `${label}: one frame is drawn`);
        const { layouts, rows, text, clipped, buttons, ...frame } = frames[0];
        assert.deepEqual(
          frame,
          {
            width: expected.width,
            cards: expected.markdown ? 0 : expected.cards,
            tabs: 0,
            details: 0,
            animated: 0,
            inert: true,
            hidden: "true",
            scroll: 0,
          },
          `${label}: the frame while mounted`,
        );
        if (expected.markdown) {
          const missingText = [...expected.texts, "sapioai.ir"].filter(
            (part) => !text.includes(part),
          );
          assert.deepEqual(
            { layouts, rows, missingText, clipped, buttons },
            {
              layouts: expected.layout ? [expected.layout] : [],
              rows: expected.rows,
              missingText: [],
              clipped: 0,
              buttons: 0,
            },
            `${label}: the picture is the whole answer with the full table and no button`,
          );
          if (expected.layout && viewport.width < 720)
            assert.deepEqual(onScreenLayouts, ["cards"], `${label}: the screen shows cards`);
          assert.ok(
            await page.getByTestId("follow-up").isVisible(),
            `${label}: follow-up on screen`,
          );
          // AC-06: inside the 15-second draw limit.
          assert.ok(drawMs < 15000, `${label}: drawn in ${drawMs} ms`);
        }
        assert.equal(
          await page.locator("[data-share-frame]").count(),
          0,
          `${label}: the frame is removed`,
        );
        if (!expected.markdown && viewport.width < 720 && expected.cards > 1)
          assert.ok(onScreenTabs, `${label}: the screen shows one card at a time`);

        const produced = await page.evaluate(async () => {
          const [item] = await navigator.clipboard.read();
          if (!item.types.includes("image/png")) return null;
          const blob = await item.getType("image/png");
          return new Promise((resolve) => {
            const reader = new FileReader();
            reader.onload = () => resolve(reader.result);
            reader.readAsDataURL(blob);
          });
        });
        assert.ok(produced, `${label}: the clipboard holds an image/png item`);

        // The same picture rendered on screen and photographed by the browser, at the same viewport: the cards'
        // viewport breakpoints (their padding) apply to the picture as they do on screen.
        const referenceContext = await browser.newContext({
          viewport: { width: viewport.width, height: viewport.height },
          deviceScaleFactor: PIXEL_RATIO,
        });
        const referencePage = await open(
          referenceContext,
          port,
          `fixture=${fixture}&reference=${expected.width}`,
          theme,
        );
        await referencePage.waitForTimeout(300);
        // The picture is wider than a phone viewport. A full-page clip photographs all of it; the reference page is
        // laid out left-to-right so the overflow is scrollable (the picture sets its own direction).
        const box = await referencePage.getByTestId("answer-share-frame").boundingBox();
        const screenshot = await referencePage.screenshot({ fullPage: true, clip: box });
        const result = await compare(
          referencePage,
          produced,
          `data:image/png;base64,${screenshot.toString("base64")}`,
        );
        const png = Buffer.from(produced.split(",")[1], "base64");
        const name = `${fixture}-${viewport.name}-${theme}`;
        await writeFile(path.join(evidenceDir, `${name}.png`), png);
        // On a mismatch the browser's screenshot is kept beside the picture, so the two can be looked at.
        if (result.different > (expected.maxDifferent ?? MAX_DIFFERENT_PIXELS))
          await writeFile(path.join(evidenceDir, `${name}.mismatch-reference.png`), screenshot);
        assert.equal(result.produced[0], expected.width * PIXEL_RATIO, `${label}: picture width`);
        assert.ok(
          Math.abs(result.produced[1] - result.reference[1]) <= 2,
          `${label}: picture height ${result.produced[1]} vs screenshot ${result.reference[1]}`,
        );
        assert.ok(
          result.colours > 20,
          `${label}: the picture is not blank (${result.colours} colours)`,
        );
        assert.ok(
          result.different <= (expected.maxDifferent ?? MAX_DIFFERENT_PIXELS),
          `${label}: ${(result.different * 100).toFixed(2)}% of pixels differ from the screenshot`,
        );
        assert.deepEqual(errors, [], `${label}: no page error`);

        record(
          `${label}: ${result.produced[0]}x${result.produced[1]} px, ` +
            (expected.markdown
              ? `${expected.layout ?? "markdown"} table with ${expected.rows} rows, copied in ${drawMs} ms, `
              : `${expected.cards} card(s), `) +
            `${(result.different * 100).toFixed(2)}% pixels differ from the screenshot, ${png.length} bytes`,
        );
        await referenceContext.close();
        await context.close();
      }
    }
  }
  await writeFile(path.join(evidenceDir, "browser-check.log"), `${log.join("\n")}\n`);
  console.log(`\n${log.length} checks passed. Evidence: ${evidenceDir}`);
} finally {
  await browser?.close();
  await server.close();
}
