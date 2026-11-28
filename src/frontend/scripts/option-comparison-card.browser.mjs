// Feature 168 browser check: the option comparison card at desktop and phone widths, in both themes. It asserts no
// page-level horizontal scroll (the table scrolls inside the card), that tabs switch the view and expiry, that rows
// without orders stay collapsed until asked, and text contrast >= 4.5:1; it saves screenshots as evidence.
// Run: npm run test:option-comparison-browser
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
  "../../specs/168-option-comparison-answers/evidence/browser",
);
const fixtureDir = "src/components/app/__tests__";
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
      ...document.querySelectorAll("[data-testid='option-comparison-card'] *"),
    ].filter((element) =>
      [...element.childNodes].some((node) => node.nodeType === 3 && node.textContent.trim()),
    );
    let worst = { ratio: Infinity, text: "" };
    for (const element of targets) {
      const backdrop = background(element);
      const fg = lum(blend(parse(getComputedStyle(element).color), backdrop));
      const bg = lum(backdrop);
      const ratio = (Math.max(fg, bg) + 0.05) / (Math.min(fg, bg) + 0.05);
      if (ratio < worst.ratio) worst = { ratio, text: element.textContent.trim().slice(0, 30) };
    }
    return worst;
  });
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

const report = [];
let browser;
try {
  await server.listen();
  const { port } = server.httpServer.address();
  browser = await chromium.launch({ executablePath, headless: true, args: ["--no-sandbox"] });

  for (const fixture of ["calls", "stale"]) {
    for (const theme of ["dark", "light"]) {
      for (const width of [1280, 390, 360, 320]) {
        const label = `${fixture}/${theme}/${width}px`;
        const context = await browser.newContext({ viewport: { width, height: 900 } });
        const page = await context.newPage();
        await page.goto(
          `http://127.0.0.1:${port}/${fixtureDir}/option-comparison-browser-fixture.html?fixture=${fixture}&theme=${theme}`,
          { waitUntil: "domcontentloaded", timeout: 120000 },
        );
        const card = page.getByTestId("option-comparison-card");
        await card.waitFor({ timeout: 120000 });
        await page.waitForTimeout(300);

        const overflow = await page.evaluate(
          () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
        );
        assert.ok(overflow <= 1, `${label}: the page overflows by ${overflow}px`);
        const box = await card.boundingBox();
        assert.ok(
          box.x >= 0 && box.x + box.width <= width + 1,
          `${label}: the card leaves the viewport`,
        );

        const contrast = await minContrast(page);
        assert.ok(
          contrast.ratio >= 4.5,
          `${label}: text contrast ${contrast.ratio.toFixed(2)}:1 at «${contrast.text}»`,
        );

        if (fixture === "calls") {
          // Fillable rows only until the collapsed group is opened.
          assert.equal(await card.locator("tbody tr").count(), 9, `${label}: fillable call rows`);
          await card.getByRole("button", { name: /نمایش بدون سفارش‌ها/ }).click();
          assert.equal(await card.locator("tbody tr").count(), 11, `${label}: all call rows`);
          assert.equal(await card.getByText("کمترین هزینهٔ مؤثر").count(), 1);
          assert.equal(await card.getByText("کمترین نرخ ضمنی").count(), 1);
          assert.equal(await card.getByRole("columnheader", { name: "قیمت تئوریک" }).count(), 1);

          await card.getByRole("tab", { name: "اختیار فروش" }).click();
          await card.getByText("طمخا۹۰۲۵").waitFor();
          assert.equal(await card.getByText("بیشترین قیمت فروش مؤثر").count(), 1);
          await card.getByRole("tab", { name: "کاورد کال" }).click();
          await card.getByText("بیشترین بازده سالانه در صورت اعمال").waitFor();
          await card.getByRole("tab", { name: "اختیار خرید", exact: true }).click();
          await card.getByRole("tab", { name: /۱۴۰۵\/۱۰\/۲۳/ }).click();
          await card.getByText("ضمخا۱۰۵۱", { exact: true }).waitFor();
          await card.getByRole("tab", { name: /۱۴۰۵\/۰۹\/۱۸/ }).click();
          const afterTabs = await minContrast(page);
          assert.ok(
            afterTabs.ratio >= 4.5,
            `${label}: contrast after tabs ${afterTabs.ratio.toFixed(2)}:1`,
          );
        } else {
          assert.equal(await card.getByRole("status").count(), 1, `${label}: not-current note`);
          assert.equal(await card.getByRole("columnheader", { name: "هزینهٔ مؤثر" }).count(), 0);
          assert.equal(await card.locator("tbody tr").count(), 11, `${label}: trade-price rows`);
        }

        if (width === 1280 || width === 360) {
          await page.screenshot({
            path: path.join(evidenceDir, `${fixture}-${theme}-${width}.png`),
            fullPage: true,
          });
        }
        const line = `ok - ${label}: no page overflow, min contrast ${contrast.ratio.toFixed(2)}:1`;
        report.push(line);
        console.log(line);
        await context.close();
      }
    }
  }
  await writeFile(path.join(evidenceDir, "browser-check.txt"), `${report.join("\n")}\n`, "utf8");
} finally {
  await browser?.close();
  await server.close();
}
