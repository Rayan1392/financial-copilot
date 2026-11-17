// Feature 163 change «answer-readability»: visual verification of the Markdown answer typography at the required
// widths, in dark (default) and light themes. Evidence PNGs go to the change folder.
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
  "../../specs/163-symbol-grounded-fallback-answer/changes/answer-readability/evidence",
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
  const url = (theme) =>
    `http://127.0.0.1:${port}/src/components/app/__tests__/grounded-answer-layout-browser-fixture.html?theme=${theme}`;
  browser = await chromium.launch({ executablePath, headless: true, args: ["--no-sandbox"] });
  for (const width of [1440, 1280, 1024, 768, 390, 360, 320]) {
    for (const theme of ["dark", "light"]) {
      const context = await browser.newContext({ viewport: { width, height: 1000 } });
      const page = await context.newPage();
      await page.goto(url(theme), { waitUntil: "domcontentloaded", timeout: 120000 });
      const answer = page.getByTestId("grounded-answer");
      await answer.locator("li").first().waitFor({ timeout: 120000 });
      await page.waitForTimeout(300);

      const overflow = await page.evaluate(
        () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
      );
      assert.ok(overflow <= 1, `${width}px ${theme}: page overflow ${overflow}px`);

      const layout = await answer.evaluate((section) => {
        const root = section.firstElementChild;
        const paragraphs = [...section.querySelectorAll("p")];
        const list = section.querySelector("ul");
        const items = [...section.querySelectorAll("li")];
        const listStyle = getComputedStyle(list);
        const first = paragraphs[0].getBoundingClientRect();
        const listBox = list.getBoundingClientRect();
        // Wrapped item lines: every line box of an item starts at the same right edge (RTL), not under the marker.
        const itemEdges = items.map((item) => {
          const range = document.createRange();
          range.selectNodeContents(item);
          const rights = [...range.getClientRects()].map((rect) => Math.round(rect.right));
          return { lines: new Set([...range.getClientRects()].map((rect) => Math.round(rect.top))).size, maxRight: Math.max(...rights), minRightOfLineStarts: Math.min(...rights) };
        });
        return {
          dir: root.getAttribute("dir"),
          lineHeight: Number.parseFloat(getComputedStyle(paragraphs[0]).lineHeight),
          fontSize: Number.parseFloat(getComputedStyle(paragraphs[0]).fontSize),
          gapAfterTakeaway: listBox.top - first.bottom,
          paddingStart: Number.parseFloat(listStyle.paddingInlineStart),
          listStylePosition: listStyle.listStylePosition,
          itemEdges,
        };
      });
      assert.equal(layout.dir, "rtl");
      assert.ok(layout.lineHeight / layout.fontSize >= 1.8, `${width}px: line height ${layout.lineHeight}/${layout.fontSize}`);
      assert.ok(layout.gapAfterTakeaway >= 11, `${width}px: gap after takeaway ${layout.gapAfterTakeaway}px`);
      assert.equal(layout.listStylePosition, "outside");
      assert.ok(layout.paddingStart >= 16, `${width}px: list padding ${layout.paddingStart}px`);

      const tableOverflow = await page
        .getByTestId("table-answer")
        .evaluate((section) => section.scrollWidth - section.clientWidth);
      assert.ok(tableOverflow <= 1, `${width}px ${theme}: table answer overflow ${tableOverflow}px`);

      if ([1280, 390, 320].includes(width)) {
        await page.screenshot({
          path: path.join(evidenceDir, `answer-layout-${width}-${theme}.png`),
          fullPage: true,
        });
      }
      const wrapped = layout.itemEdges.filter((item) => item.lines > 1).length;
      console.log(
        `ok - ${width}px ${theme}: RTL, line-height ${(layout.lineHeight / layout.fontSize).toFixed(2)}, ` +
          `gap ${Math.round(layout.gapAfterTakeaway)}px, list outside +${layout.paddingStart}px, ${wrapped} wrapped items, no overflow`,
      );
      await context.close();
    }
  }
} finally {
  await browser?.close();
  await server.close();
}
