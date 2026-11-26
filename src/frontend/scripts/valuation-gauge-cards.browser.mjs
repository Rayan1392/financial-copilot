// Feature 159 browser check (changes «standalone-valuation-gauge-cards» and «valuation-gauge-cards-visual-refresh»):
// every card state at phone, tablet and desktop widths (also beside the desktop sidebar), in the dark (default) and
// light themes and at 200 % zoom. It asserts no horizontal scroll, no clipped text, no needle without a current value,
// the average marker on top of the dial, no overlapping dial labels, green/red bound markers, the segmented control on
// a narrow answer, reduced motion, and text contrast >= 4.5:1 outside the dial; it saves screenshots as evidence.
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
  "../../specs/159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-cards-visual-refresh/evidence/browser",
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

// Bound markers: the lower bound reads green and the upper bound red in both themes (owner feedback, AC-06).
async function markerColours(root) {
  return root.evaluate((element) => {
    const canvas = document.createElement("canvas");
    canvas.width = canvas.height = 1;
    const context = canvas.getContext("2d", { willReadFrequently: true });
    const rgb = (node) => {
      context.clearRect(0, 0, 1, 1);
      context.fillStyle = "#000";
      context.fillStyle = getComputedStyle(node).fill;
      context.fillRect(0, 0, 1, 1);
      return [...context.getImageData(0, 0, 1, 1).data.slice(0, 3)];
    };
    return [...element.querySelectorAll("[data-testid='valuation-gauge-dial']")].map((dial) => ({
      min: rgb(dial.querySelector("[data-testid='valuation-gauge-marker-min'] text")),
      max: rgb(dial.querySelector("[data-testid='valuation-gauge-marker-max'] text")),
    }));
  });
}

// Visible dial labels (each <text>: a bound's name and value, a band share, an axis end) must not overlap each other or
// the HTML hero value.
async function labelCollisions(root) {
  return root.evaluate((element) => {
    const overlap = (a, b) =>
      a.left < b.right - 0.5 &&
      b.left < a.right - 0.5 &&
      a.top < b.bottom - 0.5 &&
      b.top < a.bottom - 0.5;
    const visible = (node) => node.getClientRects().length > 0;
    return [...element.querySelectorAll("[data-testid='valuation-gauge-dial']")]
      .filter(visible)
      .flatMap((dial) => {
        const boxes = [...dial.querySelectorAll("text")].map((text) => ({
          label: text.textContent,
          r: text.getBoundingClientRect(),
        }));
        const hero = dial.parentElement.querySelector("[data-testid='valuation-gauge-hero']");
        if (hero)
          boxes.push({ label: `hero ${hero.textContent}`, r: hero.getBoundingClientRect() });
        // The pointer's hub (its inner end) must stay clear of the hero, also when pinned at a foot.
        const hub = dial.querySelector("[data-testid='valuation-gauge-needle'] circle");
        if (hub) boxes.push({ label: "pointer hub", r: hub.getBoundingClientRect() });
        const hits = [];
        for (let i = 0; i < boxes.length; i++)
          for (let j = i + 1; j < boxes.length; j++)
            if (overlap(boxes[i].r, boxes[j].r)) hits.push(`${boxes[i].label} × ${boxes[j].label}`);
        return hits;
      });
  });
}

const fixtures = {
  single: { cards: ["Pe"], needles: 1 },
  all: { cards: ["Pe", "Ps", "Equilibrium"], needles: 3 },
  "range-only": { cards: ["Equilibrium"], needles: 0 },
  stale: { cards: ["Pe", "Ps", "Equilibrium"], needles: 0 },
  missing: { cards: ["Ps"], needles: 0 },
  pinned: { cards: ["Pe"], needles: 1 },
  sharak: { cards: ["Pe"], needles: 1 },
  legacy: { cards: ["Pe"], needles: 1 },
};
const layouts = [
  { name: "320", width: 320, height: 900 },
  { name: "360", width: 360, height: 900 },
  { name: "390", width: 390, height: 900 },
  { name: "430", width: 430, height: 932 },
  { name: "tablet-768", width: 768, height: 1024 },
  { name: "desktop-1280", width: 1280, height: 900 },
  { name: "desktop-sidebar-1280", width: 1280, height: 900, sidebar: true },
  // 200 % zoom of a 1280 px window: the page lays out at 640 CSS px with twice the pixel density.
  { name: "zoom200-1280", width: 640, height: 450, scale: 2 },
];
// Optional filters for a quick local loop, for example GAUGE_FIXTURES=single,all GAUGE_LAYOUTS=320,430.
const only = (name) => process.env[name]?.split(",").filter(Boolean);
const fixtureFilter = only("GAUGE_FIXTURES");
const layoutFilter = only("GAUGE_LAYOUTS");
const screenshotLayouts = new Set([
  "360",
  "430",
  "tablet-768",
  "desktop-sidebar-1280",
  "zoom200-1280",
]);

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
    if (fixtureFilter && !fixtureFilter.includes(fixture)) continue;
    for (const layout of layouts) {
      if (layoutFilter && !layoutFilter.includes(layout.name)) continue;
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

        // The average marker stem is vertical: it sits at 90°.
        const topMarkers = await root.evaluate((element) =>
          [...element.querySelectorAll("[data-testid='valuation-gauge-marker-average'] line")].map(
            (line) => Math.abs(Number(line.getAttribute("x1")) - Number(line.getAttribute("x2"))),
          ),
        );
        assert.ok(
          topMarkers.every((dx) => dx < 0.01),
          `${label}: average marker not at 90°`,
        );

        const collisions = await labelCollisions(root);
        assert.deepEqual(collisions, [], `${label}: overlapping dial labels`);

        for (const { min, max } of await markerColours(root)) {
          assert.ok(
            min[1] > min[0] && min[1] > min[2],
            `${label}: lower bound marker is not green (${min})`,
          );
          assert.ok(
            max[0] > max[1] && max[0] > max[2],
            `${label}: upper bound marker is not red (${max})`,
          );
        }

        // Several cards: a segmented control below a 720 px answer, all cards side by side above it.
        if (expected.cards.length > 1) {
          const state = await root.evaluate((element) => ({
            width: element.getBoundingClientRect().width,
            tabs: element.querySelector("[role='tablist']").getClientRects().length > 0,
            visible: [...element.querySelectorAll("[role='tabpanel']")].filter(
              (panel) => panel.getClientRects().length > 0,
            ).length,
            targets: [...element.querySelectorAll("[role='tab']")].map(
              (tab) => tab.getBoundingClientRect().height,
            ),
          }));
          if (state.width >= 720) {
            assert.ok(
              !state.tabs && state.visible === expected.cards.length,
              `${label}: cards not side by side`,
            );
          } else {
            assert.ok(state.tabs && state.visible === 1, `${label}: segmented control missing`);
            assert.ok(
              state.targets.every((h) => h >= 38),
              `${label}: tab targets ${state.targets}`,
            );
            await page.getByRole("tab", { name: "قیمت تعادلی" }).click();
            await page.waitForTimeout(350); // let the 200 ms highlight transition finish
            const highlight = await root.evaluate((element) => {
              const tablist = element.querySelector("[role='tablist']");
              const pill = tablist.querySelector("span[aria-hidden]").getBoundingClientRect();
              const tab = tablist.querySelector("[aria-selected='true']").getBoundingClientRect();
              return Math.abs(pill.left + pill.width / 2 - (tab.left + tab.width / 2));
            });
            assert.ok(
              highlight < 4,
              `${label}: segmented highlight is ${highlight}px off the selected tab`,
            );
            assert.equal(
              await page.getByTestId("valuation-gauge-card-Equilibrium").isVisible(),
              true,
              `${label}: tab did not switch`,
            );
            if (screenshotLayouts.has(layout.name)) {
              await page.screenshot({
                path: path.join(
                  evidenceDir,
                  `${fixture}-${layout.name}-${theme}-equilibrium-tab.png`,
                ),
                fullPage: true,
              });
            }
            await page.getByRole("tab", { name: "P/E" }).click();
          }
        }

        const contrast = await minContrast(page);
        assert.ok(contrast >= 4.5, `${label}: text contrast ${contrast.toFixed(2)}:1`);

        if (
          screenshotLayouts.has(layout.name) ||
          (["all", "sharak"].includes(fixture) && layout.name !== "320")
        ) {
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
  // Reduced motion: the needle sweeps in only when the user allows motion.
  for (const reducedMotion of ["no-preference", "reduce"]) {
    const context = await browser.newContext({
      viewport: { width: 390, height: 900 },
      reducedMotion,
    });
    const page = await context.newPage();
    await page.goto(`http://127.0.0.1:${port}/${fixturePath}?fixture=sharak`, {
      waitUntil: "domcontentloaded",
      timeout: 120000,
    });
    const needle = page.getByTestId("valuation-gauge-needle");
    await needle.waitFor({ timeout: 120000 });
    const animations = await needle.evaluate((node) => node.getAnimations().length);
    assert.equal(
      animations > 0,
      reducedMotion === "no-preference",
      `reduced motion ${reducedMotion}: ${animations} animation(s)`,
    );
    record(`reduced motion ${reducedMotion}: ${animations} needle animation(s)`);
    await context.close();
  }
  await writeFile(path.join(evidenceDir, "browser-check.txt"), `${log.join("\n")}\n`, "utf8");
} finally {
  await browser?.close();
  await server.close();
}
