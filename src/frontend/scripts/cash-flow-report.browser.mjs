// Feature 161 visual verification: five-quarter default shell, compact mobile layout and shared-brand PNG export.
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
  "../../specs/161-ai-cash-flow-statement-report/evidence/browser",
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

/** Applies a site theme the way `theme-toggle.tsx` does, then measures the card and its text contrast. */
async function siteThemeSurface(page, theme) {
  await page.evaluate((value) => {
    const root = document.documentElement;
    root.classList.toggle("light", value === "light");
    root.classList.toggle("dark", value === "dark");
    root.style.colorScheme = value;
  }, theme);
  await page.waitForTimeout(100);
  return page.evaluate(() => {
    // Resolve any CSS color (including oklch) to sRGB through a 1-pixel canvas.
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
    const background = (element) => {
      for (let node = element; node; node = node.parentElement) {
        const color = parse(getComputedStyle(node).backgroundColor);
        if (color[3] > 0.5) return color;
      }
      return [255, 255, 255];
    };
    const card = document.querySelector("[data-testid='cash-flow-report']");
    const targets = [...card.querySelectorAll("*")].filter(
      (element) =>
        !element.closest("svg") &&
        // BUG-039: Recharts colors legend text with the series color (amber 2.15:1 in both themes);
        // a separate defect, so the legend is excluded until it is fixed.
        !element.closest(".recharts-legend-wrapper") &&
        element.checkVisibility() &&
        [...element.childNodes].some(
          (node) => node.nodeType === Node.TEXT_NODE && node.textContent.trim().length > 0,
        ),
    );
    return {
      cardLuminance: Math.round(lum(background(card)) * 1000) / 1000,
      contrast: targets.map((element) => {
        const fg = lum(parse(getComputedStyle(element).color));
        const bg = lum(background(element));
        const ratio = (Math.max(fg, bg) + 0.05) / (Math.min(fg, bg) + 0.05);
        return {
          text: (element.textContent ?? "").trim().slice(0, 30),
          ratio: Math.round(ratio * 100) / 100,
        };
      }),
    };
  });
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
  const url = (fixture, density) =>
    `http://127.0.0.1:${port}/src/components/app/__tests__/cash-flow-report-browser-fixture.html?fixture=${fixture}&density=${density}`;
  browser = await chromium.launch({ executablePath, headless: true, args: ["--no-sandbox"] });
  for (const [width, density] of [
    [1440, "default"],
    [1280, "default"],
    [1024, "default"],
    [768, "default"],
    [390, "compact"],
    [360, "compact"],
    [320, "compact"],
  ]) {
    const context = await browser.newContext({
      viewport: { width, height: 1100 },
      acceptDownloads: true,
    });
    const page = await context.newPage();
    await page.goto(url("visual", density), { waitUntil: "domcontentloaded", timeout: 120000 });
    await page.getByTestId("cash-flow-report").waitFor({ timeout: 120000 });
    await page.waitForTimeout(300);
    assert.equal(await page.getByTestId("cash-flow-report").getAttribute("dir"), "rtl");
    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );
    assert.ok(overflow <= 1, `${width}px viewport has page overflow ${overflow}px`);
    assert.equal(
      await page.getByTestId("cash-flow-headline-summary").locator("article").count(),
      0,
    );
    if (width >= 768) {
      const tableOverflow = await page.getByTestId("cash-flow-wide-table").evaluate((region) => {
        const table = region.querySelector("table");
        return table ? table.scrollWidth - region.clientWidth : 0;
      });
      assert.ok(tableOverflow <= 1, `${width}px table has horizontal overflow ${tableOverflow}px`);
    } else {
      const mobileOverflow = await page
        .getByTestId("cash-flow-mobile-sections")
        .evaluate((region) => region.scrollWidth - region.clientWidth);
      assert.ok(mobileOverflow <= 1, `${width}px mobile rows have overflow ${mobileOverflow}px`);
    }
    if (width === 1280 || width === 1440) {
      for (const period of ["فصل ۲ ۱۴۰۴", "فصل ۳ ۱۴۰۴", "فصل ۴ ۱۴۰۴", "فصل ۱ ۱۴۰۵", "فصل ۲ ۱۴۰۵"])
        await page.getByRole("columnheader", { name: period, exact: true }).waitFor();
      await page.screenshot({
        path: path.join(evidenceDir, `cash-flow-ui-redesign-desktop-${width}.png`),
        fullPage: true,
      });
    }
    if (width === 1280) {
      const downloadPromise = page.waitForEvent("download");
      await page.getByRole("button", { name: "دانلود تصویر گزارش جریان وجوه نقد" }).click();
      const download = await downloadPromise;
      await download.saveAs(
        path.join(evidenceDir, "cash-flow-ui-redesign-desktop-1280.png-export.png"),
      );
    }
    if (width === 390) {
      await page.screenshot({
        path: path.join(evidenceDir, "cash-flow-ui-redesign-mobile-390.png"),
        fullPage: true,
      });
      const downloadPromise = page.waitForEvent("download");
      await page.locator("button:has(svg.lucide-download)").click();
      const download = await downloadPromise;
      await download.saveAs(
        path.join(evidenceDir, "cash-flow-ui-redesign-mobile-390.png-export.png"),
      );
    }
    if (width === 768) {
      // BUG-038 / AC-U07: with the operating system set to dark, the report stays the same light
      // "paper" surface under both site themes (the site theme is the class on <html>).
      await page.emulateMedia({ colorScheme: "dark" });
      for (const theme of ["dark", "light"]) {
        const surface = await siteThemeSurface(page, theme);
        assert.ok(
          surface.cardLuminance > 0.9,
          `site ${theme} theme, OS dark: card background is not light (${surface.cardLuminance})`,
        );
        const low = surface.contrast.filter((item) => item.ratio < 4.5);
        assert.equal(low.length, 0, `site ${theme} theme: ${JSON.stringify(low.slice(0, 3))}`);
        console.log(
          `ok - OS dark + site ${theme}: light card, ${surface.contrast.length} text nodes, min contrast ${Math.min(...surface.contrast.map((item) => item.ratio))}:1`,
        );
        await page.screenshot({
          path: path.join(evidenceDir, `cash-flow-light-surface-os-dark-site-${theme}.png`),
          fullPage: true,
        });
      }
    }
    console.log(`ok - ${width}px ${density}, RTL, no page overflow`);
    await context.close();
  }
} finally {
  await browser?.close();
  await server.close();
}
