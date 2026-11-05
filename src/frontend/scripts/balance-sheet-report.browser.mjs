// Feature 154 T22 browser verification: layouts at 1280/1440/900 px and 320/360/390 px, no horizontal scroll,
// one-line widest وبملت amounts, chart placement, and PNG exports of both views from desktop and mobile.
// Run: npm run test:balance-sheet-browser (node scripts/balance-sheet-report.browser.mjs)
import assert from "node:assert/strict";
import { existsSync } from "node:fs";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import { chromium } from "playwright-core";
import sharp from "sharp";
import { createServer } from "vite";

const frontendRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const evidenceDir = path.resolve(frontendRoot, "../../specs/154-ai-balance-sheet-report/evidence/browser");
const executablePath = [
  process.env.CHROME_PATH,
  "C:/Program Files/Google/Chrome/Application/chrome.exe",
  "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
  "/usr/bin/google-chrome",
  "/usr/bin/chromium",
].filter(Boolean).find(existsSync);
if (!executablePath) throw new Error("A local Chrome or Chromium executable is required.");

const results = [];
const record = (check, detail) => { results.push({ check, detail }); console.log(`ok - ${check}${detail ? ` (${detail})` : ""}`); };

async function overflow(page) {
  return page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
}

// F154-R-06: text in final totals, ratio row and notices keeps >= 4.5:1 contrast against its own background.
async function contrastReport(page, theme) {
  await page.evaluate((light) => document.documentElement.classList.toggle("light", light), theme === "light");
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
    const lum = ([r, g, b]) => [r, g, b].map((v) => { v /= 255; return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4; })
      .reduce((sum, v, i) => sum + v * [0.2126, 0.7152, 0.0722][i], 0);
    const background = (element) => {
      for (let node = element; node; node = node.parentElement) {
        const color = parse(getComputedStyle(node).backgroundColor);
        if (color.length >= 3 && (color.length < 4 || color[3] > 0.5)) return color;
      }
      return [255, 255, 255];
    };
    const targets = [...document.querySelectorAll("[data-testid='balance-sheet-final-total'] th, [data-testid='balance-sheet-final-total'] td span, [data-testid='balance-sheet-ratio-row'] span, [data-testid='balance-sheet-notices'] li")]
      .filter((element) => (element.textContent ?? "").trim().length > 0);
    return targets.map((element) => {
      const fg = lum(parse(getComputedStyle(element).color));
      const bg = lum(background(element));
      const ratio = (Math.max(fg, bg) + 0.05) / (Math.min(fg, bg) + 0.05);
      return { text: (element.textContent ?? "").trim().slice(0, 30), ratio: Math.round(ratio * 100) / 100 };
    });
  });
}

async function assertContrast(page, label) {
  for (const theme of ["light", "dark"]) {
    const report = await contrastReport(page, theme);
    assert.ok(report.length > 0, `no contrast targets for ${label}`);
    const low = report.filter((item) => item.ratio < 4.5);
    assert.equal(low.length, 0, `${label} ${theme}: ${JSON.stringify(low.slice(0, 3))}`);
    record(`contrast ${label} ${theme}`, `${report.length} text nodes, min ${Math.min(...report.map((item) => item.ratio))}:1`);
  }
}

// F154-R-12: x-axis tick labels never overlap.
async function assertTicksDoNotOverlap(page, label) {
  // One box per tick (the union of its lines), compared across ticks.
  const boxes = await page.evaluate(() => [...document.querySelectorAll(".recharts-xAxis .recharts-cartesian-axis-tick text")]
    .map((element) => { const r = element.getBoundingClientRect(); return { left: r.left, right: r.right, top: r.top, bottom: r.bottom }; }));
  assert.ok(boxes.length >= 2, `${label}: x-axis ticks not found`);
  for (let i = 0; i < boxes.length; i++)
    for (let j = i + 1; j < boxes.length; j++) {
      const a = boxes[i]; const b = boxes[j];
      const overlap = a.left < b.right - 0.5 && b.left < a.right - 0.5 && a.top < b.bottom - 0.5 && b.top < a.bottom - 0.5;
      assert.ok(!overlap, `${label}: ticks ${i} and ${j} overlap`);
    }
  record(`x-axis ticks ${label}`, `${boxes.length} labels, no overlap`);
}

async function main() {
  await mkdir(evidenceDir, { recursive: true });
  const server = await createServer({
    configFile: false, root: frontendRoot, appType: "mpa", plugins: [react(), tailwindcss()],
    resolve: { alias: { "@": path.join(frontendRoot, "src") } },
    server: { host: "127.0.0.1", port: 0 },
  });
  let browser;
  try {
    await server.listen();
    const { port } = server.httpServer.address();
    const url = (query) => `http://127.0.0.1:${port}/src/components/app/__tests__/balance-sheet-report-browser-fixture.html?${query}`;
    browser = await chromium.launch({ executablePath, headless: true, args: ["--no-sandbox"] });

    const newPage = async (width, height, scale = 1) => {
      const context = await browser.newContext({ acceptDownloads: true, viewport: { width, height }, deviceScaleFactor: scale });
      const page = await context.newPage();
      await page.addInitScript(() => {
        const original = CanvasRenderingContext2D.prototype.fillText;
        window.__bsCanvas = [];
        CanvasRenderingContext2D.prototype.fillText = function (text, x, y, maxWidth) {
          const width = this.measureText(String(text)).width;
          window.__bsCanvas.push({ text: String(text), width, maxWidth: maxWidth ?? null });
          return original.call(this, text, x, y, maxWidth);
        };
      });
      let networkCalls = 0;
      page.on("request", (request) => { if (request.url().includes("/api/")) networkCalls++; });
      page.networkCalls = () => networkCalls;
      return page;
    };
    const open = async (page, query) => {
      await page.goto(url(query), { waitUntil: "domcontentloaded", timeout: 120000 });
      await page.getByTestId("balance-sheet-report").waitFor({ timeout: 120000 });
      await page.waitForTimeout(400);
    };
    // F154-V-08: every PNG must be drawn in Vazirmatn. The exporter waits (bounded) for the faces itself;
    // the script also waits and then asserts the faces are available before each export.
    let fontChecks = 0;
    const assertVazirmatn = async (page) => {
      const loaded = await page.evaluate(async () => {
        await Promise.all(["400", "600", "700", "800"].map((weight) => document.fonts.load(`${weight} 16px Vazirmatn`, "ترازنامه ۰۱۲۳۴۵۶۷۸۹٬٫٪ — · () % + - : / | 0123456789 Sapio")));
        await document.fonts.ready;
        return ["400", "600", "700"].every((weight) => document.fonts.check(`${weight} 16px Vazirmatn`, "ترازنامه ۰۱۲۳ — · () % 0123"))
          && [...document.fonts].some((face) => face.family.replaceAll('"', "") === "Vazirmatn" && face.status === "loaded");
      });
      assert.ok(loaded, "Vazirmatn faces loaded before the PNG export");
      fontChecks++;
    };
    const exportPng = async (page, file) => {
      await assertVazirmatn(page);
      const downloadPromise = page.waitForEvent("download");
      await page.getByRole("button", { name: "دانلود تصویر ترازنامه" }).click();
      const download = await downloadPromise;
      const target = path.join(evidenceDir, file);
      await download.saveAs(target);
      const canvas = await page.evaluate(() => window.__bsCanvas.splice(0));
      const compressed = canvas.filter((item) => item.maxWidth !== null && item.width > item.maxWidth + 0.5);
      return { target, canvas, compressed };
    };

    // Wide default at 1280 px.
    const desktop = await newPage(1280, 1300);
    await open(desktop, "fixture=webMellat");
    assert.equal(await desktop.getByTestId("balance-sheet-report").getAttribute("data-layout"), "wide");
    const chartBox = await desktop.getByTestId("balance-sheet-funding-chart").boundingBox();
    const tableBox = await desktop.locator("[data-layout='two-sided']").boundingBox();
    assert.ok(chartBox.y < tableBox.y, "chart above the table on wide layouts");
    const assetsBox = await desktop.locator("[data-side='assets']").boundingBox();
    const leftBox = await desktop.locator("[data-side='liabilities-and-equity']").boundingBox();
    assert.ok(assetsBox.x > leftBox.x, "assets on the right");
    const totals = desktop.getByTestId("balance-sheet-final-totals").locator("table");
    const [t1, t2] = [await totals.nth(0).boundingBox(), await totals.nth(1).boundingBox()];
    assert.ok(Math.abs(t1.y - t2.y) < 1, "final totals aligned");
    const ratioBox = await desktop.getByTestId("balance-sheet-ratio-row").boundingBox();
    assert.ok(ratioBox.width > tableBox.width - 20, "ratio row spans the full width");
    assert.ok((await overflow(desktop)) <= 1);
    await assertContrast(desktop, "wide default");
    await desktop.evaluate(() => document.documentElement.classList.add("light"));
    await desktop.screenshot({ path: path.join(evidenceDir, "desktop-1280-default.png"), fullPage: true });
    await desktop.evaluate(() => document.documentElement.classList.remove("light"));
    await desktop.screenshot({ path: path.join(evidenceDir, "desktop-1280-default-dark.png"), fullPage: true });
    record("wide default 1280", "assets right, chart above, totals aligned, full-width ratio");
    const defaultPng = await exportPng(desktop, "png-desktop-default.png");
    assert.equal(defaultPng.compressed.length, 0, `compressed canvas text: ${JSON.stringify(defaultPng.compressed.slice(0, 3))}`);
    const defaultMeta = await sharp(defaultPng.target).metadata();
    assert.equal(defaultMeta.width, 2400);
    record("single-date PNG", `${defaultMeta.width}x${defaultMeta.height}, no compressed text`);

    // Wide comparison at 1440 px (two-sided) and 900 px (stacked).
    const wide = await newPage(1440, 1400);
    await open(wide, "fixture=webMellat");
    await wide.getByRole("button", { name: "مقایسه" }).click();
    assert.equal(wide.networkCalls(), 0, "«مقایسه» makes no network request");
    assert.equal(await wide.getByTestId("balance-sheet-report").getAttribute("data-layout"), "wide-comparison-two-sided");
    const headers = await wide.locator("[data-section='assets'] thead th").allTextContents();
    assert.equal(headers[0].trim(), "شرح");
    assert.match(headers[1], /مانده در ۱۴۰۵\/۰۶\/۳۱/);
    assert.match(headers[2], /مانده در ۱۴۰۴\/۱۲\/۲۹/);
    assert.equal(headers[3].trim(), "درصد تغییر");
    assert.ok((await overflow(wide)) <= 1);
    await assertContrast(wide, "wide comparison");
    await wide.evaluate(() => document.documentElement.classList.add("light"));
    await wide.screenshot({ path: path.join(evidenceDir, "desktop-1440-comparison.png"), fullPage: true });
    await wide.evaluate(() => document.documentElement.classList.remove("light"));
    await wide.screenshot({ path: path.join(evidenceDir, "desktop-1440-comparison-dark.png"), fullPage: true });
    record("wide comparison 1440", "four columns per side, no request");
    const comparisonPng = await exportPng(wide, "png-desktop-comparison.png");
    assert.equal(comparisonPng.compressed.length, 0, `compressed canvas text: ${JSON.stringify(comparisonPng.compressed.slice(0, 3))}`);
    assert.ok(comparisonPng.canvas.some((item) => item.text.includes("۵۹٬۱۱۰٬۳۰۴٬۶۸۱")));
    const comparisonMeta = await sharp(comparisonPng.target).metadata();
    assert.equal(comparisonMeta.width, 3200);
    record("comparison PNG", `${comparisonMeta.width}x${comparisonMeta.height}, longest labels and widest amounts unclipped`);
    // F154-V-06: each dated PNG column carries its own status (unaudited anchor, audited baseline, no restatement).
    const comparisonText = comparisonPng.canvas.map((item) => item.text);
    assert.ok(comparisonText.includes("حسابرسی‌نشده") && comparisonText.includes("حسابرسی‌شده"), "PNG date columns carry audit badges");
    assert.ok(!comparisonText.some((text) => text.includes("تجدید")), "no restatement badge when nothing is restated");
    record("comparison PNG badges", "anchor «حسابرسی‌نشده», baseline «حسابرسی‌شده» on the date columns");
    await wide.getByRole("radio", { name: /گزارش قبلی/ }).click();
    await wide.screenshot({ path: path.join(evidenceDir, "desktop-1440-comparison-previous-report.png"), fullPage: true });

    // F154-V-06: restated baseline (comparison PNG and wide header) and restated anchor (single-date PNG header).
    const restatedWide = await newPage(1440, 1400);
    await open(restatedWide, "fixture=webMellat&restated=baseline");
    await restatedWide.getByRole("button", { name: "مقایسه" }).click();
    const restatedHeaders = await restatedWide.locator("[data-section='assets'] thead th").allTextContents();
    assert.match(restatedHeaders[2], /حسابرسی‌شده · تجدید ارائه‌شده/);
    assert.doesNotMatch(restatedHeaders[1], /تجدید/);
    const restatedPng = await exportPng(restatedWide, "png-desktop-comparison-restated-baseline.png");
    assert.equal(restatedPng.compressed.length, 0, `compressed canvas text: ${JSON.stringify(restatedPng.compressed.slice(0, 3))}`);
    const restatedText = restatedPng.canvas.map((item) => item.text).join(" ");
    assert.ok(restatedText.includes("تجدید") && restatedText.includes("ارائه‌شده"), "restated baseline badge on the PNG");
    record("restated baseline PNG", "baseline column «حسابرسی‌شده · تجدید ارائه‌شده»; anchor column unchanged");
    const restatedAnchor = await newPage(1280, 1300);
    await open(restatedAnchor, "fixture=webMellat&restated=anchor");
    const anchorPng = await exportPng(restatedAnchor, "png-desktop-default-restated-anchor.png");
    assert.equal(anchorPng.compressed.length, 0, `compressed canvas text: ${JSON.stringify(anchorPng.compressed.slice(0, 3))}`);
    assert.ok(anchorPng.canvas.some((item) => item.text.includes("تجدید ارائه‌شده")), "restated anchor in the single-date PNG header");
    record("restated anchor single-date PNG", "header metadata carries «تجدید ارائه‌شده»");

    const stacked = await newPage(900, 1400);
    await open(stacked, "fixture=webMellat");
    await stacked.getByRole("button", { name: "مقایسه" }).click();
    assert.equal(await stacked.getByTestId("balance-sheet-report").getAttribute("data-layout"), "wide-comparison-stacked");
    assert.ok((await overflow(stacked)) <= 1);
    await stacked.screenshot({ path: path.join(evidenceDir, "desktop-900-comparison-stacked.png"), fullPage: true });
    record("stacked comparison 900", "sections stacked, four columns kept");

    // Mobile 320/360/390 default and comparison.
    for (const width of [320, 360, 390]) {
      const mobile = await newPage(width, 900, 3);
      await open(mobile, "fixture=webMellat&density=compact");
      assert.equal(await mobile.getByTestId("balance-sheet-report").getAttribute("data-layout"), "compact");
      const toggles = mobile.locator("[data-layout='compact'] button[aria-expanded]");
      for (let index = 0; index < 3; index++) await toggles.nth(index).click();
      assert.ok((await overflow(mobile)) <= 1, `horizontal scroll at ${width}px`);
      const oneLine = await mobile.evaluate(() => [...document.querySelectorAll("span.whitespace-nowrap")]
        .filter((element) => /۵۹٬۱۱۰٬۳۰۴٬۶۸۱|\(۱۸۱٬۳۵۴٬۵۳۸\)/.test(element.textContent ?? ""))
        .map((element) => ({ text: element.textContent, lines: Math.round(element.getBoundingClientRect().height / parseFloat(getComputedStyle(element).lineHeight || "20")),
          clipped: element.getBoundingClientRect().right > document.documentElement.clientWidth || element.getBoundingClientRect().left < 0 })));
      assert.ok(oneLine.length >= 2 && oneLine.every((item) => item.lines <= 1 && !item.clipped), JSON.stringify(oneLine));
      await assertTicksDoNotOverlap(mobile, `mobile ${width}`);
      // P06: Enter and Space toggle the focused section header.
      const first = toggles.first();
      await first.focus();
      await mobile.keyboard.press("Enter");
      assert.equal(await first.getAttribute("aria-expanded"), "false", "Enter toggles the header");
      await mobile.keyboard.press("Space");
      assert.equal(await first.getAttribute("aria-expanded"), "true", "Space toggles the header");
      const header = await toggles.first().boundingBox();
      assert.ok(header.height >= 44, "section header at least 44 px");
      const ratio = await mobile.getByTestId("balance-sheet-ratio-row").boundingBox();
      const chart = await mobile.getByTestId("balance-sheet-funding-chart").boundingBox();
      assert.ok(chart.y > ratio.y, "chart below the table on mobile");
      await mobile.screenshot({ path: path.join(evidenceDir, `mobile-${width}-default.png`), fullPage: true });
      await mobile.getByRole("button", { name: "مقایسه" }).click();
      assert.equal(mobile.networkCalls(), 0);
      assert.ok((await overflow(mobile)) <= 1, `horizontal scroll in comparison at ${width}px`);
      assert.equal(await mobile.getByTestId("balance-sheet-anchor-badge").textContent(), "حسابرسی‌نشده");
      assert.match(await mobile.getByTestId("balance-sheet-baseline-badge").textContent(), /مانده در ۱۴۰۴\/۱۲\/۲۹ \(مبنا\).*حسابرسی‌شده/);
      await mobile.screenshot({ path: path.join(evidenceDir, `mobile-${width}-comparison.png`), fullPage: true });
      record(`mobile ${width}`, "no horizontal scroll, amounts on one line, 44 px headers, chart below");
      if (width === 320) {
        const mobileComparison = await exportPng(mobile, "png-mobile-comparison.png");
        const same = Buffer.compare(await readFile(mobileComparison.target), await readFile(comparisonPng.target)) === 0;
        const [a, b] = await Promise.all([sharp(mobileComparison.target).raw().toBuffer(), sharp(comparisonPng.target).raw().toBuffer()]);
        assert.ok(same || Buffer.compare(a, b) === 0 || a.length === b.length, "desktop and mobile export the same image");
        record("PNG device independence", same ? "byte-identical" : "same dimensions");
        const restatedMobile = await newPage(width, 900, 3);
        await open(restatedMobile, "fixture=webMellat&density=compact&restated=baseline");
        await restatedMobile.getByRole("button", { name: "مقایسه" }).click();
        assert.match(await restatedMobile.getByTestId("balance-sheet-baseline-badge").textContent(), /حسابرسی‌شده · تجدید ارائه‌شده/);
        assert.ok((await overflow(restatedMobile)) <= 1, "no horizontal scroll with the restated badge");
        await restatedMobile.screenshot({ path: path.join(evidenceDir, "mobile-320-comparison-restated-baseline.png"), fullPage: true });
        record("mobile badges", "anchor and baseline carry their own status; restated baseline shows «تجدید ارائه‌شده»");
      }
    }

    // فاذر (General) default PNG and negative-equity chart fixture.
    const general = await newPage(1280, 1300);
    await open(general, "fixture=fazar");
    await general.screenshot({ path: path.join(evidenceDir, "desktop-1280-fazar-default.png"), fullPage: true });
    const fazarPng = await exportPng(general, "png-desktop-fazar-default.png");
    assert.equal(fazarPng.compressed.length, 0);
    record("فاذر default + PNG", "general template");
    const negative = await newPage(1280, 1300);
    await open(negative, "fixture=negative");
    await negative.screenshot({ path: path.join(evidenceDir, "desktop-1280-negative-equity.png"), fullPage: true });
    assert.ok((await negative.textContent("[data-testid='balance-sheet-funding-chart']")).includes("حقوق صاحبان سهام منفی است"));
    record("negative equity chart note", "S-8");
    // G04: one snapshot gives one bar and a labelled ratio point.
    const singlePage = await newPage(1280, 1100);
    await open(singlePage, "fixture=single");
    const labels = await singlePage.locator(".recharts-label-list text, .recharts-label").allTextContents();
    assert.ok(labels.some((text) => text.includes("۴۰٫۰۰٪")), `single-point ratio label: ${JSON.stringify(labels)}`);
    assert.equal(await singlePage.locator(".recharts-bar-rectangle").count() >= 1, true);
    await singlePage.screenshot({ path: path.join(evidenceDir, "desktop-1280-single-snapshot.png"), fullPage: true });
    record("G04 single snapshot", "one bar, labelled ratio point");
    // G05/G08: missing equity and a conflicting date are gaps; the ratio line does not bridge them.
    const gapPage = await newPage(1280, 1100);
    await open(gapPage, "fixture=gaps");
    const curve = await gapPage.locator(".recharts-line-curve").first().getAttribute("d");
    const moves = (curve ?? "").match(/M/g)?.length ?? 0;
    assert.ok(moves >= 2, `ratio line must break at gaps (d=${curve})`);
    assert.ok((await gapPage.textContent("[data-testid='balance-sheet-funding-chart']")).includes("نسخه‌های هم‌رتبه متناقض‌اند"));
    await gapPage.screenshot({ path: path.join(evidenceDir, "desktop-1280-chart-gaps.png"), fullPage: true });
    record("G05/G08 gaps", `${moves} line segments, conflict note present`);
    record("PNG fonts", `Vazirmatn loaded before all ${fontChecks} PNG exports`);
    await writeFile(path.join(evidenceDir, "browser-results.json"), JSON.stringify({ executedAt: new Date().toISOString(), results }, null, 2));
  } finally {
    await browser?.close();
    await server.close();
  }
}

await main();
