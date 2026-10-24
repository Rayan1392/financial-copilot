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
const evidenceDir = path.resolve(frontendRoot, "../../specs/144-quarterly-financial-reports-table/general/evidence");
const executablePath = [process.env.CHROME_PATH,
  "C:/Program Files/Google/Chrome/Application/chrome.exe",
  "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
  "/usr/bin/google-chrome", "/usr/bin/chromium", "/usr/bin/chromium-browser"]
  .filter(Boolean).find(existsSync);
if (!executablePath) throw new Error("Chrome or Chromium is required for General chart verification.");

const server = await createServer({ configFile: false, root: frontendRoot, appType: "mpa",
  plugins: [react(), tailwindcss()], server: { host: "127.0.0.1", port: 0 } });
let browser;
try {
  await mkdir(evidenceDir, { recursive: true });
  await server.listen();
  const address = server.httpServer.address();
  assert.equal(typeof address, "object");
  browser = await chromium.launch({ executablePath, headless: true, args: ["--no-sandbox"] });
  const page = await browser.newPage({ viewport: { width: 1300, height: 900 }, deviceScaleFactor: 1 });
  await page.route("**/api/ui/v1/config", (route) => route.fulfill({ status: 200,
    contentType: "application/json", body: JSON.stringify({ quarterlyFinancialTable: { showChart: true } }) }));
  await page.goto(`http://127.0.0.1:${address.port}/src/components/app/__tests__/quarterly-financial-table-browser-fixture.html?general-typed`,
    { waitUntil: "domcontentloaded", timeout: 120000 });
  const chart = page.getByRole("img", { name: /نمودار فروش/ });
  await chart.waitFor();
  await page.waitForFunction(() => document.querySelectorAll(".recharts-bar-rectangle path").length >= 3 &&
    Boolean(document.querySelector(".recharts-line-curve")?.getAttribute("d")));
  assert.equal(await chart.locator(".recharts-bar").count(), 3);
  assert.equal(await chart.locator(".recharts-line").count(), 1);
  assert.ok(await chart.locator(".recharts-bar-rectangle path").count() > 0);
  assert.ok((await chart.locator(".recharts-line-curve").getAttribute("d"))?.length > 10);

  const screenshotPath = path.join(evidenceDir, "general-quarterly-chart-regression.png");
  await chart.screenshot({ path: screenshotPath });
  const { data, info } = await sharp(screenshotPath).removeAlpha().raw().toBuffer({ resolveWithObject: true });
  const colors = [
    [59, 130, 246], [6, 182, 212], [16, 185, 129], [245, 158, 11],
  ];
  const counts = colors.map(([red, green, blue]) => {
    let count = 0;
    for (let i = 0; i < data.length; i += info.channels) {
      if (Math.abs(data[i] - red) < 12 && Math.abs(data[i + 1] - green) < 12 &&
        Math.abs(data[i + 2] - blue) < 12) count++;
    }
    return count;
  });
  assert.ok(counts.slice(0, 3).every((count) => count > 100), `General bars are not visible: ${counts}`);
  assert.ok(counts[3] > 20, `General margin line is not visible: ${counts}`);
  console.log(JSON.stringify({ screenshotPath, bars: 3, lines: 1, coloredPixels: counts }));
} finally {
  await browser?.close();
  await server.close();
}
