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
      await page.emulateMedia({ colorScheme: "dark" });
      await page.screenshot({
        path: path.join(evidenceDir, "cash-flow-ui-redesign-tablet-dark.png"),
        fullPage: true,
      });
    }
    console.log(`ok - ${width}px ${density}, RTL, no page overflow`);
    await context.close();
  }
} finally {
  await browser?.close();
  await server.close();
}
