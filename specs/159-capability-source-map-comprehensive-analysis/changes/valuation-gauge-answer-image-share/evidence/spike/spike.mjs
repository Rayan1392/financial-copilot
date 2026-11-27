import { createRequire } from "node:module";
import { pathToFileURL, fileURLToPath } from "node:url";
import { writeFile } from "node:fs/promises";
import { existsSync } from "node:fs";
import path from "node:path";
const here = path.dirname(fileURLToPath(import.meta.url));
const frontendRoot = "C:/Source/financial-copilot/src/frontend";
const require = createRequire(frontendRoot + "/package.json");
const imp = (name) => import(pathToFileURL(require.resolve(name)).href);
const { createServer } = await import(pathToFileURL(frontendRoot + "/node_modules/vite/dist/node/index.js").href);
const react = (await imp("@vitejs/plugin-react")).default;
const tailwindcss = (await imp("@tailwindcss/vite")).default;
const { chromium } = require("playwright-core");
const executablePath = ["C:/Program Files/Google/Chrome/Application/chrome.exe","C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe"].find(existsSync);
const lib = process.argv[2] ?? "modern";
const bundle = lib === "modern" ? here + "/node_modules/modern-screenshot/dist/index.js" : here + "/node_modules/html-to-image/dist/html-to-image.js";
const server = await createServer({ configFile: false, root: frontendRoot, appType: "mpa", plugins: [react(), tailwindcss()],
  resolve: { alias: [{ find: "@", replacement: path.join(frontendRoot, "src") }] }, server: { host: "127.0.0.1", port: 0 }, logLevel: "error" });
let browser;
try {
  await server.listen();
  const { port } = server.httpServer.address();
  browser = await chromium.launch({ executablePath, headless: true, args: ["--no-sandbox"] });
  for (const fixture of ["forward", "single"]) for (const theme of ["dark", "light"]) {
    const context = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 3 });
    const page = await context.newPage();
    page.on("console", (m) => { if (m.type() === "error" || m.type() === "warning") console.log("  console:", m.text().slice(0, 200)); });
    await page.goto(`http://127.0.0.1:${port}/src/components/app/__tests__/valuation-gauge-browser-fixture.html?fixture=${fixture}`, { waitUntil: "domcontentloaded", timeout: 120000 });
    const root = page.getByTestId("valuation-gauge-cards");
    await root.waitFor({ timeout: 120000 });
    await page.evaluate((light) => document.documentElement.classList.toggle("light", light), theme === "light");
    await page.addScriptTag({ path: bundle });
    await page.evaluate(() => document.fonts.ready);
    await page.waitForTimeout(900);
    const width = fixture === "single" ? 480 : 800;
    const result = await page.evaluate(async ({ width, lib }) => {
      const root = document.querySelector("[data-testid='valuation-gauge-cards']");
      const frame = document.createElement("div");
      frame.style.cssText = `position:fixed;left:-10000px;top:0;width:${width}px;padding:24px;background:var(--background);color:var(--foreground);box-sizing:border-box`;
      root.parentElement.insertBefore(frame, root); frame.appendChild(root); frame.setAttribute("data-share-frame",""); const st=document.createElement("style"); st.textContent="[data-share-frame] *{animation:none !important;transition:none !important}"; document.head.appendChild(st);
      const footer = document.createElement("div");
      footer.style.cssText = "margin-top:16px;display:flex;justify-content:space-between;font-size:13px;opacity:.75";
      footer.innerHTML = "<span>ساپیو | دستیار هوشمند بازار سرمایه</span><span dir='ltr'>sapioai.ir</span>";
      frame.appendChild(footer);
      await new Promise((r) => setTimeout(r, 300));
      const t0 = performance.now();
      const url = lib === "modern"
        ? await window.modernScreenshot.domToPng(frame, { scale: 2 })
        : await window.htmlToImage.toPng(frame, { pixelRatio: 2 });
      const ms = Math.round(performance.now() - t0);
      frame.style.left = "0"; // for the reference screenshot
      const r = frame.getBoundingClientRect();
      return { url, ms, w: r.width, h: r.height, tabsVisible: !!document.querySelector("[data-testid='valuation-gauge-tabs']")?.getClientRects().length };
    }, { width, lib });
    const png = Buffer.from(result.url.split(",")[1], "base64");
    await writeFile(`${here}/${lib}-${fixture}-${theme}.png`, png);
    console.log(lib, fixture, theme, `${result.ms}ms`, `${png.length}B`, `frame ${result.w}x${result.h}`, "tabsVisible", result.tabsVisible, "png", png.readUInt32BE(16), "x", png.readUInt32BE(20));
    if (lib === "modern") { await page.setViewportSize({ width: 800, height: 1400 }); await page.locator("div[style*='position: fixed']").first().screenshot({ path: `${here}/reference-${fixture}-${theme}.png` }).catch((e) => console.log("ref fail", e.message.slice(0, 100))); }
    await context.close();
  }
} finally { await browser?.close(); await server.close(); }
