// Change «sol-deep-analysis-web-search» (T12) browser check: the analysis controls, the 163 offer card and the per-item
// source links (amendment A3) in light and dark themes, at desktop and phone widths, with no horizontal scroll and 44 px phone touch targets.
// Run: npm run test:analysis-settings-browser
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
  "../../specs/162-context-aware-financial-report-follow-up/changes/sol-deep-analysis-web-search/evidence/browser",
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
  resolve: { alias: [{ find: "@", replacement: path.join(frontendRoot, "src") }] },
  server: { host: "127.0.0.1", port: 0 },
});

let browser;
try {
  await server.listen();
  const { port } = server.httpServer.address();
  browser = await chromium.launch({ executablePath, headless: true, args: ["--no-sandbox"] });

  for (const theme of ["dark", "light"]) {
    for (const width of [1280, 360, 320]) {
      const compact = width < 768;
      const context = await browser.newContext({ viewport: { width, height: 900 } });
      const page = await context.newPage();
      await page.goto(
        `http://127.0.0.1:${port}/${fixtureDir}/analysis-settings-browser-fixture.html?theme=${theme}&compact=${compact ? 1 : 0}`,
        { waitUntil: "domcontentloaded", timeout: 120000 },
      );
      await page.getByTestId("offer").waitFor({ timeout: 120000 });
      await page.waitForTimeout(300);
      const label = `${theme} at ${width}px`;

      const overflow = await page.evaluate(
        () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
      );
      assert.ok(overflow <= 1, `${label} overflows by ${overflow}px`);

      const clipped = await page.evaluate(() =>
        [...document.querySelectorAll("main *")]
          .filter(
            (node) =>
              node.scrollWidth > node.clientWidth + 1 &&
              getComputedStyle(node).display !== "inline",
          )
          .map((node) => node.textContent?.slice(0, 40)),
      );
      assert.deepEqual(clipped, [], `${label} has clipped content`);

      if (compact) {
        const small = await page.evaluate(() =>
          [...document.querySelectorAll("main button")]
            .filter((button) => button.getBoundingClientRect().height < 44)
            .map((button) => button.textContent),
        );
        assert.deepEqual(small, [], `${label} has touch targets under 44px`);
      }

      const offer = page.getByTestId("offer");
      const short = page.getByTestId("offer-short");
      assert.ok(await offer.getByRole("button", { name: "تحلیل کن" }).isEnabled());
      assert.ok(await short.getByRole("button", { name: "تحلیل کن" }).isDisabled());
      assert.equal(await short.getByRole("link", { name: "خرید اعتبار" }).count(), 1);
      assert.ok((await offer.textContent()).includes("۲۵ اعتبار"));

      // Amendment A3: sources sit at the end of the item that used them, as short links; no list at the end.
      const sources = page.getByTestId("sources");
      assert.equal(await sources.getByRole("listitem").nth(0).getByRole("link").count(), 1);
      assert.equal(await sources.getByRole("listitem").nth(1).getByRole("link").count(), 2);
      assert.ok(!(await sources.textContent()).includes("[W"));
      assert.ok(!(await sources.textContent()).includes("**منابع**"));

      // The live cost follows the choice.
      await offer.getByRole("radio", { name: "ساده" }).click();
      await offer.getByRole("button", { name: "جست‌وجوی وب" }).click();
      assert.ok((await offer.textContent()).includes("۸ اعتبار"));
      assert.ok((await page.getByTestId("bar").textContent()).includes("۸ اعتبار"));
      await offer.getByRole("radio", { name: "عمیق" }).click();
      await offer.getByRole("button", { name: "جست‌وجوی وب" }).click();

      if (width === 1280 || width === 320) {
        await page.screenshot({
          path: path.join(evidenceDir, `analysis-settings-${theme}-${width}.png`),
          fullPage: true,
        });
      }
      console.log(`ok - ${label}`);
      await context.close();
    }
  }
} finally {
  await browser?.close();
  await server.close();
}
