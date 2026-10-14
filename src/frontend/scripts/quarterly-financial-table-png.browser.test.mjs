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
const evidenceDir = path.resolve(frontendRoot, "../../specs/144-quarterly-financial-reports-table/png-header-evidence");
const captureOnly = process.argv.includes("--capture-only");
const chromeCandidates = [
  process.env.CHROME_PATH,
  "C:/Program Files/Google/Chrome/Application/chrome.exe",
  "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
  "/usr/bin/google-chrome",
  "/usr/bin/chromium",
  "/usr/bin/chromium-browser",
].filter(Boolean);
const executablePath = chromeCandidates.find(existsSync);
if (!executablePath) throw new Error("A local Chrome or Chromium executable is required for PNG verification.");

const WIDTH = 1200;
const PAD = 36;
const HEADER_TOP = PAD + 104 + 54;
const HEADER_HEIGHT = 64;
const ROW_HEIGHT = 54;
const ROW_COUNT = 16;
const SCALE = 2;
const widths = [0.22, ...Array(5).fill(0.62 / 5), 0.16].map((fraction) => (WIDTH - PAD * 2) * fraction);

function sampleRegion(image, rectangle, predicate) {
  const x0 = Math.ceil(rectangle.left * SCALE);
  const x1 = Math.floor(rectangle.right * SCALE);
  const y0 = Math.ceil(rectangle.top * SCALE);
  const y1 = Math.floor(rectangle.bottom * SCALE);
  let count = 0;
  let minX = Infinity;
  let minY = Infinity;
  let maxX = -Infinity;
  let maxY = -Infinity;
  for (let y = y0; y < y1; y++) {
    for (let x = x0; x < x1; x++) {
      const offset = (y * image.info.width + x) * image.info.channels;
      if (!predicate(image.data[offset], image.data[offset + 1], image.data[offset + 2])) continue;
      count++;
      minX = Math.min(minX, x);
      minY = Math.min(minY, y);
      maxX = Math.max(maxX, x);
      maxY = Math.max(maxY, y);
    }
  }
  return { count, minX, minY, maxX, maxY };
}

const darkInk = (r, g, b) => r < 130 && g < 160 && b < 175;
const lightInk = (r, g, b) => r > 210 && g > 210 && b > 210;

async function main() {
  await mkdir(evidenceDir, { recursive: true });
  const server = await createServer({
    configFile: false,
    root: frontendRoot,
    appType: "mpa",
    plugins: [react(), tailwindcss()],
    server: { host: "127.0.0.1", port: 0 },
  });
  let browser;
  try {
    await server.listen();
    const address = server.httpServer.address();
    assert.equal(typeof address, "object");
    browser = await chromium.launch({ executablePath, headless: true, args: ["--no-sandbox"] });
    const context = await browser.newContext({
      acceptDownloads: true,
      viewport: { width: 1300, height: 1100 },
      deviceScaleFactor: 1,
    });
    const page = await context.newPage();
    await page.addInitScript(() => {
      const original = CanvasRenderingContext2D.prototype.fillText;
      window.__quarterlyCanvasText = [];
      CanvasRenderingContext2D.prototype.fillText = function (text, x, y, ...rest) {
        window.__quarterlyCanvasText.push({ text: String(text), x, y, color: String(this.fillStyle), font: this.font });
        return original.call(this, text, x, y, ...rest);
      };
    });
    await page.goto(`http://127.0.0.1:${address.port}/src/components/app/__tests__/quarterly-financial-table-browser-fixture.html`, {
      waitUntil: "domcontentloaded", timeout: 120000,
    });
    const button = page.getByRole("button", { name: "دانلود تصویر جدول" });
    await button.waitFor({ timeout: 120000 });
    assert.equal(await page.locator("thead th").count(), 7);
    assert.equal(await page.locator("tbody tr").count(), ROW_COUNT);
    const webHeaders = await page.locator("thead th").allTextContents();
    assert.equal(webHeaders[0].trim(), "شرح");
    assert.equal(webHeaders[6].trim(), "درصد تغییر به فصل قبل");
    const expectedDates = [
      "۳۱ شهریور ۱۴۰۴", "۳۰ آذر ۱۴۰۴", "۲۹ اسفند ۱۴۰۴",
      "۳۱ خرداد ۱۴۰۵", "۳۱ شهریور ۱۴۰۵",
    ];
    for (const [index, date] of expectedDates.entries()) {
      assert.ok(webHeaders[index + 1].includes("دوره منتهی به"));
      assert.ok(webHeaders[index + 1].includes(date));
      assert.doesNotMatch(webHeaders[index + 1], /سال مالی|فصل|[0-9]/);
      assert.equal(await page.locator("thead th").nth(index + 1).getAttribute("aria-label"),
        `دوره منتهی به ${date}`);
    }
    const grossWeb = await page.locator("tbody tr").nth(3).locator("th, td").evaluateAll((cells) =>
      cells.map((cell) => ({
        background: getComputedStyle(cell).backgroundColor,
        yellowClass: cell.classList.contains("bg-yellow-200"),
        size: Number.parseFloat(getComputedStyle(cell).fontSize),
        text: cell.textContent,
      })),
    );
    const normalSize = await page.locator("tbody tr").first().locator("td").first().evaluate((cell) =>
      Number.parseFloat(getComputedStyle(cell).fontSize));
    assert.equal(grossWeb.length, 7);
    assert.ok(grossWeb.every((cell) => cell.yellowClass && cell.background === grossWeb[0].background));
    assert.notEqual(grossWeb[0].background, "rgba(0, 0, 0, 0)");
    assert.ok(grossWeb.every((cell) => cell.size > normalSize));
    assert.equal(grossWeb[5].text, "۴۴٫۴٪");
    await page.setViewportSize({ width: 390, height: 900 });
    const horizontalOverflow = await page.evaluate(() =>
      document.documentElement.scrollWidth - document.documentElement.clientWidth);
    assert.ok(horizontalOverflow <= 1, `The web table adds ${horizontalOverflow}px of horizontal scroll.`);
    await page.setViewportSize({ width: 1300, height: 1100 });

    const label = captureOnly ? "period-end-capture" : "period-end";
    const downloadPromise = page.waitForEvent("download");
    await button.click();
    const download = await downloadPromise;
    assert.match(download.suggestedFilename(), /شاراک.*\.png$/);
    const canvasText = await page.evaluate(() => window.__quarterlyCanvasText);
    const pngPath = path.join(evidenceDir, `quarterly-sharak-${label}.png`);
    const webPath = path.join(evidenceDir, `quarterly-sharak-web-${label}.png`);
    const headerPath = path.join(evidenceDir, `quarterly-sharak-header-${label}.png`);
    const grossPath = path.join(evidenceDir, `quarterly-sharak-gross-${label}.png`);
    await download.saveAs(pngPath);
    await page.screenshot({ path: webPath, fullPage: true });
    await sharp(pngPath).extract({
      left: PAD * SCALE,
      top: HEADER_TOP * SCALE,
      width: (WIDTH - PAD * 2) * SCALE,
      height: HEADER_HEIGHT * SCALE,
    }).toFile(headerPath);
    await sharp(pngPath).extract({
      left: PAD * SCALE,
      top: (HEADER_TOP + HEADER_HEIGHT + ROW_HEIGHT * 3) * SCALE,
      width: (WIDTH - PAD * 2) * SCALE,
      height: ROW_HEIGHT * SCALE,
    }).toFile(grossPath);

    const image = await sharp(pngPath).ensureAlpha().raw().toBuffer({ resolveWithObject: true });
    assert.equal(image.info.width, WIDTH * SCALE);
    assert.equal(image.info.height, (PAD * 2 + 104 + 54 + HEADER_HEIGHT + ROW_HEIGHT * ROW_COUNT) * SCALE);
    let right = WIDTH - PAD;
    const headerRegions = widths.map((width, index) => {
      const left = right - width;
      right = left;
      const region = { left: left + 1, right: left + width - 1, top: HEADER_TOP + 1, bottom: HEADER_TOP + HEADER_HEIGHT - 1 };
      return { index, cellLeft: left, cellRight: left + width, ...sampleRegion(image, region, darkInk) };
    });
    let grossRight = WIDTH - PAD;
    const grossTop = HEADER_TOP + HEADER_HEIGHT + ROW_HEIGHT * 3;
    const grossBackgrounds = widths.map((width) => {
      const left = grossRight - width;
      grossRight = left;
      const x = Math.round((left + width / 2) * SCALE);
      const y = Math.round((grossTop + ROW_HEIGHT - 6) * SCALE);
      const offset = (y * image.info.width + x) * image.info.channels;
      return Array.from(image.data.subarray(offset, offset + 3));
    });
    const headerDraws = canvasText.filter((draw) => draw.y >= HEADER_TOP && draw.y < HEADER_TOP + HEADER_HEIGHT);
    const canvasHeaders = widths.map((width, index) => {
      const right = WIDTH - PAD - widths.slice(0, index).reduce((sum, part) => sum + part, 0);
      return headerDraws.filter((draw) => draw.x > right - width && draw.x <= right)
        .map((draw) => draw.text).join(" ");
    });
    const titleInk = sampleRegion(image, {
      left: PAD + 15, right: WIDTH - PAD - 15, top: PAD + 8, bottom: PAD + 58,
    }, lightInk).count;
    const firstBodyInk = sampleRegion(image, {
      left: WIDTH - PAD - widths[0] + 5, right: WIDTH - PAD - 5,
      top: HEADER_TOP + HEADER_HEIGHT + 5, bottom: HEADER_TOP + HEADER_HEIGHT + ROW_HEIGHT - 5,
    }, darkInk).count;
    const lastBodyInk = sampleRegion(image, {
      left: WIDTH - PAD - widths[0] + 5, right: WIDTH - PAD - 5,
      top: HEADER_TOP + HEADER_HEIGHT + ROW_HEIGHT * (ROW_COUNT - 1) + 5,
      bottom: HEADER_TOP + HEADER_HEIGHT + ROW_HEIGHT * ROW_COUNT - 5,
    }, darkInk).count;

    console.log(JSON.stringify({
      pngPath, webPath, headerPath, grossPath, width: image.info.width, height: image.info.height,
      headerDarkPixels: headerRegions.map((region) => region.count), canvasHeaders, grossBackgrounds,
      titleInk, firstBodyInk, lastBodyInk,
    }, null, 2));
    if (!captureOnly) {
      assert.match(canvasHeaders[0], /شرح/);
      assert.match(canvasHeaders[6], /درصد تغییر به فصل قبل/);
      for (const [index, date] of expectedDates.entries()) {
        assert.equal(canvasHeaders[index + 1], `دوره منتهی به ${date}`);
      }
      assert.ok(grossBackgrounds.every(([r, g, b]) => r === 254 && g === 240 && b === 138),
        "Gross Margin is not yellow across the full PNG row.");
      assert.ok(canvasText.some((draw) => draw.text === "مارجین ناخالص" && /20px/.test(draw.font)));
      for (const region of headerRegions) {
        assert.ok(region.count > 30, `Header cell ${region.index + 1} has no visible dark text.`);
        assert.ok(region.minX > (region.cellLeft + 4) * SCALE, `Header cell ${region.index + 1} clips text on the left.`);
        assert.ok(region.maxX < (region.cellRight - 4) * SCALE, `Header cell ${region.index + 1} clips text on the right.`);
        assert.ok(region.minY > (HEADER_TOP + 4) * SCALE, `Header cell ${region.index + 1} clips text at the top.`);
        assert.ok(region.maxY < (HEADER_TOP + HEADER_HEIGHT - 4) * SCALE, `Header cell ${region.index + 1} clips text at the bottom.`);
      }
      assert.ok(titleInk > 100, "The PNG title is missing.");
      assert.ok(firstBodyInk > 30, "The PNG first financial row is missing.");
      assert.ok(lastBodyInk > 30, "The PNG does not cover the final financial row.");
    }
  } finally {
    await browser?.close();
    await server.close();
  }
}

await main();
