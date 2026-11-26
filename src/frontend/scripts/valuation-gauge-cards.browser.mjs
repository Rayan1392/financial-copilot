// Feature 159 change «standalone-valuation-gauge-cards» browser check: every card state at phone, tablet and desktop
// widths (also beside the desktop sidebar), in the dark (default) and light themes and at 200 % zoom. It asserts no
// horizontal scroll, no clipped text, no needle without a current value, the average/midpoint marker on top of the
// dial, and text contrast >= 4.5:1 outside the dial; it saves screenshots as evidence.
// Run: npm run test:valuation-gauges-browser
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
const evidenceDir = path.resolve(
  frontendRoot,
  "../../specs/159-capability-source-map-comprehensive-analysis/changes/standalone-valuation-gauge-cards/evidence/browser",
);
const fixturePath = "src/components/app/__tests__/valuation-gauge-browser-fixture.html";
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

// Text outside the SVG dial against its own resolved background (oklch resolved through a 1-pixel canvas).
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
    const targets = [
      ...document.querySelectorAll("[data-testid='valuation-gauge-cards'] *"),
    ].filter(
      (element) =>
        !element.closest("svg") &&
        !element.classList.contains("sr-only") &&
        [...element.childNodes].some((node) => node.nodeType === 3 && node.textContent.trim()),
    );
    return Math.min(
      ...targets.map((element) => {
        const backdrop = background(element);
        const fg = lum(blend(parse(getComputedStyle(element).color), backdrop));
        const bg = lum(backdrop);
        return (Math.max(fg, bg) + 0.05) / (Math.min(fg, bg) + 0.05);
      }),
    );
  });
}

const fixtures = {
  single: { cards: ["Pe"], needles: 1 },
  all: { cards: ["Pe", "Ps", "Equilibrium"], needles: 3 },
  "range-only": { cards: ["Equilibrium"], needles: 0 },
  stale: { cards: ["Pe", "Ps", "Equilibrium"], needles: 0 },
  missing: { cards: ["Ps"], needles: 0 },
  pinned: { cards: ["Pe"], needles: 1 },
};
const layouts = [
  { name: "320", width: 320, height: 900 },
  { name: "360", width: 360, height: 900 },
  { name: "390", width: 390, height: 900 },
  { name: "tablet-768", width: 768, height: 1024 },
  { name: "desktop-1280", width: 1280, height: 900 },
  { name: "desktop-sidebar-1280", width: 1280, height: 900, sidebar: true },
  // 200 % zoom of a 1280 px window: the page lays out at 640 CSS px with twice the pixel density.
  { name: "zoom200-1280", width: 640, height: 450, scale: 2 },
];
const screenshotLayouts = new Set(["360", "tablet-768", "desktop-sidebar-1280", "zoom200-1280"]);

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
    for (const layout of layouts) {
      for (const theme of ["dark", "light"]) {
        const context = await browser.newContext({
          viewport: { width: layout.width, height: layout.height },
          deviceScaleFactor: layout.scale ?? 1,
        });
        const page = await context.newPage();
        await page.goto(
          `http://127.0.0.1:${port}/${fixturePath}?fixture=${fixture}${layout.sidebar ? "&sidebar=1" : ""}`,
          { waitUntil: "domcontentloaded", timeout: 120000 },
        );
        const root = page.getByTestId("valuation-gauge-cards");
        await root.waitFor({ timeout: 120000 });
        await page.evaluate(
          (light) => document.documentElement.classList.toggle("light", light),
          theme === "light",
        );
        await page.waitForTimeout(200);
        const label = `${fixture} ${layout.name} ${theme}`;

        const overflow = await page.evaluate(
          () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
        );
        assert.ok(overflow <= 1, `${label}: horizontal scroll of ${overflow}px`);

        const clipped = await root.evaluate((element) =>
          [...element.querySelectorAll("*")]
            .filter(
              (node) =>
                !(node instanceof SVGElement) &&
                !node.classList.contains("sr-only") &&
                node.scrollWidth > node.clientWidth + 1 &&
                getComputedStyle(node).display !== "inline",
            )
            .map((node) => node.textContent?.slice(0, 40)),
        );
        assert.deepEqual(clipped, [], `${label}: clipped content`);

        // Dial labels stay inside the SVG box (no clipped end labels at narrow widths).
        const outside = await root.evaluate((element) =>
          [...element.querySelectorAll("svg")].flatMap((svg) => {
            const box = svg.getBoundingClientRect();
            return [...svg.querySelectorAll("text")]
              .map((text) => ({ text: text.textContent, r: text.getBoundingClientRect() }))
              .filter(
                ({ r }) =>
                  r.left < box.left - 0.5 ||
                  r.right > box.right + 0.5 ||
                  r.top < box.top - 0.5 ||
                  r.bottom > box.bottom + 0.5,
              )
              .map(({ text }) => text);
          }),
        );
        assert.deepEqual(outside, [], `${label}: dial labels outside the dial`);

        const order = await page
          .locator("[data-testid^='valuation-gauge-card-']")
          .evaluateAll((cards) =>
            cards.map((card) => card.dataset.testid.replace("valuation-gauge-card-", "")),
          );
        assert.deepEqual(order, expected.cards, `${label}: card order`);
        assert.equal(
          await page.getByTestId("valuation-gauge-needle").count(),
          expected.needles,
          `${label}: needles`,
        );

        // The emphasised marker (average or midpoint) is vertical: it sits at 90°.
        const topMarkers = await root.evaluate((element) =>
          [...element.querySelectorAll("svg g line[stroke-width='2']")].map((line) =>
            Math.abs(Number(line.getAttribute("x1")) - Number(line.getAttribute("x2"))),
          ),
        );
        assert.ok(
          topMarkers.every((dx) => dx < 0.01),
          `${label}: average marker not at 90°`,
        );

        const contrast = await minContrast(page);
        assert.ok(contrast >= 4.5, `${label}: text contrast ${contrast.toFixed(2)}:1`);

        if (screenshotLayouts.has(layout.name) || (fixture === "all" && layout.name !== "320")) {
          await page.screenshot({
            path: path.join(evidenceDir, `${fixture}-${layout.name}-${theme}.png`),
            fullPage: true,
          });
        }
        record(
          `${label}: no overflow, order ${order.join("/")}, ${expected.needles} needle(s), min contrast ${contrast.toFixed(2)}:1`,
        );
        await context.close();
      }
    }
  }
  await writeFile(path.join(evidenceDir, "browser-check.txt"), `${log.join("\n")}\n`, "utf8");
} finally {
  await browser?.close();
  await server.close();
}
