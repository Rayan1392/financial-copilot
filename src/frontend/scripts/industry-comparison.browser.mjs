// Feature 125 change «responsive-comparison-table»: no horizontal overflow, the expected layout per width,
// follow-up actions below the comparison and readable text in both site themes.
import assert from "node:assert/strict";
import { existsSync } from "node:fs";
import { mkdir } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import { chromium } from "playwright-core";
import { createServer } from "vite";

const frontendRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const evidenceDir = path.resolve(
  frontendRoot,
  "../../specs/125-industry-relative-valuation-ranking/changes/responsive-comparison-table/evidence/browser",
);
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

// [name, viewport width, viewport height, density, sidebar, long values, root font size (text zoom)]
const cases = [
  ["desktop-1440", 1440, 1000, "default", false, false, null],
  ["desktop-1440-sidebar", 1440, 1000, "default", true, false, null],
  ["desktop-1440-long", 1440, 1000, "default", false, true, null],
  ["desktop-1024-sidebar", 1024, 900, "default", true, false, null],
  ["desktop-1024-text-zoom", 1024, 900, "default", false, false, "150%"],
  ["tablet-768", 768, 1000, "default", false, false, null],
  ["tablet-768-sidebar-long", 768, 1000, "default", true, true, null],
  ["narrow-600", 600, 900, "default", false, false, null],
  ["mobile-landscape-844", 844, 390, "compact", false, false, null],
  ["mobile-390", 390, 844, "compact", false, false, null],
  ["mobile-390-text-zoom", 390, 844, "compact", false, false, "150%"],
  ["mobile-320-long", 320, 700, "compact", false, true, null],
];

function measure() {
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
  const lum = ([r, g, b]) =>
    [r, g, b]
      .map((v) => {
        v /= 255;
        return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4;
      })
      .reduce((sum, v, i) => sum + v * [0.2126, 0.7152, 0.0722][i], 0);
  // Composite translucent backgrounds (theme «soft» tokens) over their parents.
  const background = (element) => {
    const layers = [];
    for (let node = element; node; node = node.parentElement) {
      const color = parse(getComputedStyle(node).backgroundColor);
      if (color[3] > 0) layers.push(color);
      if (color[3] >= 0.99) break;
    }
    let result = [0, 0, 0];
    for (const [r, g, b, a] of layers.reverse())
      result = [
        r * a + result[0] * (1 - a),
        g * a + result[1] * (1 - a),
        b * a + result[2] * (1 - a),
      ];
    return result;
  };
  const comparison = document.querySelector("[data-testid='industry-comparison']");
  const visibleLayouts = [...comparison.querySelectorAll("[data-layout]")]
    .filter((element) => element.checkVisibility())
    .map((element) => element.dataset.layout);
  // Screen-reader-only text is a deliberately clipped 1px box.
  const visible = (element) => element.checkVisibility() && !element.closest(".sr-only");
  const overflowing = [...comparison.querySelectorAll("*")]
    .filter(visible)
    .filter(
      (element) =>
        element.scrollWidth - element.clientWidth > 1 &&
        getComputedStyle(element).overflowX !== "visible",
    )
    .map((element) => element.tagName);
  const comparisonBox = comparison.getBoundingClientRect();
  const actionGroup = document.querySelector("[role='group'][aria-label='پیشنهادهای ادامه گفتگو']");
  const actions = [...actionGroup.querySelectorAll("button")].map((button) =>
    button.getBoundingClientRect(),
  );
  const texts = [...comparison.querySelectorAll("*")].filter(
    (element) =>
      visible(element) &&
      !element.closest("[aria-hidden='true']") &&
      [...element.childNodes].some(
        (node) => node.nodeType === Node.TEXT_NODE && node.textContent.trim(),
      ),
  );
  const contrast = texts.map((element) => {
    const fg = lum(parse(getComputedStyle(element).color));
    const bg = lum(background(element));
    return {
      text: element.textContent.trim().slice(0, 24),
      ratio: Math.round(((Math.max(fg, bg) + 0.05) / (Math.min(fg, bg) + 0.05)) * 100) / 100,
    };
  });
  const clipped = texts
    .filter(
      (element) =>
        element.scrollWidth - element.clientWidth > 1 &&
        getComputedStyle(element).overflow !== "visible",
    )
    .map((element) => element.textContent.trim().slice(0, 24));
  return {
    pageOverflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
    comparisonOverflow: comparison.scrollWidth - comparison.clientWidth,
    comparisonWidth: Math.round(comparisonBox.width),
    visibleLayouts,
    overflowing,
    clipped,
    actionsBelow: actions.every((box) => box.top >= comparisonBox.bottom),
    actionsInside: actions.every(
      (box) => box.left >= -1 && box.right <= document.documentElement.clientWidth + 1,
    ),
    contrast,
  };
}

await mkdir(evidenceDir, { recursive: true });
const server = await createServer({
  configFile: false,
  root: frontendRoot,
  appType: "mpa",
  plugins: [react(), tailwindcss()],
  resolve: { alias: { "@": path.join(frontendRoot, "src") } },
  server: { host: "127.0.0.1", port: 0 },
});
let browser;
try {
  await server.listen();
  const { port } = server.httpServer.address();
  browser = await chromium.launch({ executablePath, headless: true, args: ["--no-sandbox"] });
  for (const [name, width, height, density, sidebar, long, textZoom] of cases) {
    for (const theme of ["dark", "light"]) {
      const context = await browser.newContext({ viewport: { width, height } });
      const page = await context.newPage();
      const query = new URLSearchParams({
        density,
        theme,
        sidebar: sidebar ? "open" : "closed",
        long: long ? "1" : "0",
      });
      await page.goto(
        `http://127.0.0.1:${port}/src/components/app/__tests__/industry-comparison-browser-fixture.html?${query}`,
        { waitUntil: "domcontentloaded", timeout: 120000 },
      );
      await page.getByTestId("industry-comparison").waitFor({ timeout: 120000 });
      if (textZoom)
        await page.evaluate((size) => (document.documentElement.style.fontSize = size), textZoom);
      await page.waitForTimeout(300);
      const result = await page.evaluate(measure);
      const label = `${name} ${theme}`;

      assert.ok(result.pageOverflow <= 1, `${label}: page overflow ${result.pageOverflow}px`);
      assert.ok(
        result.comparisonOverflow <= 1,
        `${label}: comparison overflow ${result.comparisonOverflow}px`,
      );
      assert.deepEqual(
        result.overflowing,
        [],
        `${label}: scrolling elements ${result.overflowing}`,
      );
      assert.deepEqual(result.clipped, [], `${label}: clipped text ${result.clipped}`);
      assert.equal(result.visibleLayouts.length, 1, `${label}: layouts ${result.visibleLayouts}`);
      if (density === "compact")
        assert.equal(result.visibleLayouts[0], "cards", `${label}: phone chat must use cards`);
      assert.ok(result.actionsBelow, `${label}: follow-up actions are not below the comparison`);
      assert.ok(result.actionsInside, `${label}: follow-up actions leave the viewport`);
      const low = result.contrast.filter((item) => item.ratio < 4.5);
      assert.deepEqual(low, [], `${label}: low contrast ${JSON.stringify(low.slice(0, 4))}`);

      await page.screenshot({
        path: path.join(evidenceDir, `${name}-${theme}.png`),
        fullPage: true,
      });
      console.log(
        `ok - ${label}: ${result.visibleLayouts[0]} at ${result.comparisonWidth}px, ${result.contrast.length} text nodes, min contrast ${Math.min(...result.contrast.map((item) => item.ratio))}:1`,
      );
      await context.close();
    }
  }
} finally {
  await browser?.close();
  await server.close();
}
