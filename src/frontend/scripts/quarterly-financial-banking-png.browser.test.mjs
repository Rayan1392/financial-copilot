import assert from "node:assert/strict";
import { existsSync } from "node:fs";
import { mkdir } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import { chromium } from "playwright-core";
import sharp from "sharp";
import { createServer } from "vite";

const frontendRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const evidenceDir = path.resolve(frontendRoot, "../../specs/144-quarterly-financial-reports-table/banking/evidence");
const executablePath = [process.env.CHROME_PATH,
  "C:/Program Files/Google/Chrome/Application/chrome.exe",
  "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
  "/usr/bin/google-chrome", "/usr/bin/chromium", "/usr/bin/chromium-browser"]
  .filter(Boolean).find(existsSync);
if (!executablePath) throw new Error("A local Chrome or Chromium executable is required for banking PNG verification.");

const server = await createServer({ configFile: false, root: frontendRoot, appType: "mpa",
  plugins: [react(), tailwindcss()], server: { host: "127.0.0.1", port: 0 } });
let browser;
try {
  await mkdir(evidenceDir, { recursive: true });
  await server.listen();
  const address = server.httpServer.address();
  assert.equal(typeof address, "object");
  browser = await chromium.launch({ executablePath, headless: true, args: ["--no-sandbox"] });
  const context = await browser.newContext({ acceptDownloads: true, viewport: { width: 1300, height: 1100 } });
  const page = await context.newPage();
  await page.addInitScript(() => {
    const original = CanvasRenderingContext2D.prototype.fillText;
    window.__quarterlyCanvasText = [];
    CanvasRenderingContext2D.prototype.fillText = function (text, x, y, ...rest) {
      window.__quarterlyCanvasText.push({ text: String(text), x, y });
      return original.call(this, text, x, y, ...rest);
    };
  });
  await page.route("**/api/ui/v1/config", (route) => route.fulfill({ status: 200,
    contentType: "application/json", body: JSON.stringify({ quarterlyFinancialTable: { showChart: false } }) }));
  await page.goto(`http://127.0.0.1:${address.port}/src/components/app/__tests__/quarterly-financial-table-browser-fixture.html?banking`,
    { waitUntil: "domcontentloaded", timeout: 120000 });
  await page.getByRole("button", { name: "دانلود تصویر جدول" }).waitFor({ timeout: 120000 });
  await page.getByRole("img", { name: /نمودار درآمد تسهیلات/ }).waitFor();
  const table = page.locator("section > table").first();
  assert.equal(await table.locator("tbody tr").count(), 20);
  const text = await table.innerText();
  for (const omitted of ["خالص درآمدهای عملیاتی", "استهلاک", "عملیات در حال تداوم"])
    assert.equal(text.includes(omitted), false, `Unexpected omitted row: ${omitted}`);
  const yellowMarginCells = await table.locator("tbody tr").nth(3).locator("th,td").evaluateAll((cells) =>
    cells.map((cell) => getComputedStyle(cell).backgroundColor));
  const yellowFeeCells = await table.locator("tbody tr").nth(7).locator("th,td").evaluateAll((cells) =>
    cells.map((cell) => getComputedStyle(cell).backgroundColor));
  assert.ok([...yellowMarginCells, ...yellowFeeCells].every((color) => color === yellowMarginCells[0]),
    `Banking margin rows are not fully yellow: ${JSON.stringify({ yellowMarginCells, yellowFeeCells })}`);
  await page.setViewportSize({ width: 390, height: 900 });
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth) <= 1);
  await page.setViewportSize({ width: 1300, height: 1100 });
  const downloadPromise = page.waitForEvent("download");
  await page.getByRole("button", { name: "دانلود تصویر جدول" }).click();
  const download = await downloadPromise;
  const pngPath = path.join(evidenceDir, "banking-quarterly-table.png");
  const webPath = path.join(evidenceDir, "banking-quarterly-web.png");
  await download.saveAs(pngPath);
  await page.screenshot({ path: webPath, fullPage: true });
  const image = await sharp(pngPath).metadata();
  assert.equal(image.width, 2400);
  const canvasText = await page.evaluate(() => window.__quarterlyCanvasText ?? []);
  const chartLegendText = canvasText.filter((entry) => entry.y <= 450).map((entry) => entry.text);
  for (const label of ["درآمد تسهیلات و سپرده‌گذاری", "خالص درآمد تسهیلات و سپرده‌گذاری", "سود خالص", "مارجین ناخالص عملیات بانکی", "حاشیه سود کارمزد"])
    assert.ok(chartLegendText.some((entry) => entry.includes(label)), `Banking chart is missing ${label}`);
  assert.equal(chartLegendText.some((entry) => entry.includes("هزینه سود سپرده‌ها")), false,
    "Deposit interest expense must not appear in the chart legend");
  assert.ok(canvasText.some((entry) => entry.text.includes("هزینه سود سپرده‌ها")),
    "Deposit interest expense must remain in the PNG table");
  assert.ok(text.includes("سود هر سهم بر اساس آخرین سرمایه"), "Updated EPS label is missing from the banking table");
  console.log(JSON.stringify({ pngPath, webPath, width: image.width, height: image.height,
    rows: 20, marginHighlightColors: yellowMarginCells, feeHighlightColors: yellowFeeCells }, null, 2));
} finally {
  await browser?.close();
  await server.close();
}
