// Feature 156 browser check: the card at desktop and phone widths, with no horizontal scroll.
// Run: npm run test:symbol-snapshot-browser
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
  "../../specs/156-symbol-snapshot-default-card/evidence/browser",
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

await mkdir(evidenceDir, { recursive: true });
const server = await createServer({
  configFile: false,
  root: frontendRoot,
  appType: "mpa",
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: [
      {
        find: "@/components/app/follow-symbol-button",
        replacement: path.join(frontendRoot, fixtureDir, "symbol-snapshot-browser-follow-stub.tsx"),
      },
      { find: "@", replacement: path.join(frontendRoot, "src") },
    ],
  },
  server: { host: "127.0.0.1", port: 0 },
});

let browser;
try {
  await server.listen();
  const { port } = server.httpServer.address();
  browser = await chromium.launch({ executablePath, headless: true, args: ["--no-sandbox"] });

  for (const fixture of ["full", "partial", "bank"]) {
    for (const width of [1280, 390, 360, 320]) {
      const context = await browser.newContext({ viewport: { width, height: 900 } });
      const page = await context.newPage();
      await page.goto(
        `http://127.0.0.1:${port}/${fixtureDir}/symbol-snapshot-browser-fixture.html?fixture=${fixture}`,
        {
          waitUntil: "domcontentloaded",
          timeout: 120000,
        },
      );
      const card = page.getByTestId("symbol-snapshot-card");
      await card.waitFor({ timeout: 120000 });
      await page.waitForTimeout(300);

      const overflow = await page.evaluate(
        () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
      );
      assert.ok(overflow <= 1, `${fixture} at ${width}px overflows by ${overflow}px`);

      const box = await card.boundingBox();
      assert.ok(
        box.x >= 0 && box.x + box.width <= width + 1,
        `${fixture} at ${width}px leaves the viewport`,
      );

      const clipped = await card.evaluate((element) =>
        [...element.querySelectorAll("*")]
          .filter(
            (node) =>
              !node.classList.contains("sr-only") &&
              node.scrollWidth > node.clientWidth + 1 &&
              getComputedStyle(node).display !== "inline",
          )
          .map((node) => node.textContent?.slice(0, 40)),
      );
      assert.deepEqual(clipped, [], `${fixture} at ${width}px has clipped content`);

      assert.equal(await page.locator("[data-testid='symbol-snapshot-card'] h3").count(), 3);
      assert.equal(
        await card.getAttribute("aria-label"),
        `خلاصهٔ نماد ${fixture === "bank" ? "وبملت" : "اخابر"}`,
      );

      if (width === 1280 || width === 360) {
        await page.screenshot({
          path: path.join(evidenceDir, `${fixture}-${width}.png`),
          fullPage: true,
        });
      }
      console.log(`ok - ${fixture} at ${width}px`);
      await context.close();
    }
  }
} finally {
  await browser?.close();
  await server.close();
}
