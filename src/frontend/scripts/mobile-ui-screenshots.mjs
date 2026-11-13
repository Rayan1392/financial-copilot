// Screenshots of every /m screen at 360×800 in both themes, for before/after evidence of a mobile UI change.
// Same setup as scripts/mobile-workspace.browser.mjs: a production build that points at the mock API, served by
// `wrangler dev` on APP_URL. Do not run both scripts at once; they share the mock API port.
//
//   node scripts/mobile-ui-screenshots.mjs <output-directory>
import { existsSync } from "node:fs";
import { mkdir } from "node:fs/promises";
import path from "node:path";
import { chromium } from "playwright-core";
import { createMockApi } from "./mobile-workspace/mock-api.mjs";

const APP_URL = process.env.APP_URL ?? "http://127.0.0.1:5197";
const MOCK_PORT = Number(process.env.MOCK_API_PORT ?? 55153);
const outputDirectory = process.argv[2];
if (!outputDirectory)
  throw new Error("Usage: node scripts/mobile-ui-screenshots.mjs <output-directory>");

const executablePath = [
  process.env.CHROME_PATH,
  "C:/Program Files/Google/Chrome/Application/chrome.exe",
  "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
  "/usr/bin/google-chrome",
  "/usr/bin/chromium",
].find((candidate) => candidate && existsSync(candidate));
if (!executablePath) throw new Error("A local Chrome, Chromium, or Edge is required.");

function fakeAccessToken() {
  const encode = (value) => Buffer.from(JSON.stringify(value)).toString("base64url");
  return [
    encode({ alg: "none", typ: "JWT" }),
    encode({
      sub: "fc3b4b7b-61cf-4295-aa53-5273e4c0d5a0",
      "financial_copilot:tenant_id": "39fd0553-95fb-4882-ae72-d96e67320611",
      email: "tester@example.com",
      exp: Math.floor(Date.now() / 1000) + 3600,
    }),
    "signature",
  ].join(".");
}

await mkdir(outputDirectory, { recursive: true });
const token = fakeAccessToken();
const mock = createMockApi({ loginToken: token });
await new Promise((resolve) => mock.server.listen(MOCK_PORT, "127.0.0.1", resolve));
const browser = await chromium.launch({ executablePath, headless: true });

const screens = [
  ["home", "/m"],
  ["history", "/m/history"],
  ["market", "/m/market"],
  ["watchlist", "/m/watchlist"],
  ["account", "/m/account"],
  ["profile", "/m/account/profile"],
  ["conversation", `/m/c/${mock.richConversationId}`],
  ["answers", `/m/c/${mock.analysisConversationId}`],
];

try {
  for (const theme of ["dark", "light"]) {
    const context = await browser.newContext({
      viewport: { width: 360, height: 800 },
      deviceScaleFactor: 2,
      isMobile: true,
      hasTouch: true,
      locale: "fa-IR",
    });
    await context.addInitScript(
      ([accessToken, chosenTheme]) => {
        sessionStorage.setItem("financial_copilot_access_token", accessToken);
        localStorage.setItem("financial-copilot-theme", chosenTheme);
      },
      [token, theme],
    );
    const page = await context.newPage();
    for (const [name, route] of screens) {
      await page.goto(APP_URL + route, { waitUntil: "networkidle" });
      await page.waitForSelector("nav[aria-label='ناوبری اصلی'], h1", { timeout: 15_000 });
      await page.waitForLoadState("networkidle");
      await page.evaluate(() => document.fonts.ready);
      await page.screenshot({ path: path.join(outputDirectory, `${name}-${theme}.png`) });
    }
    // The symbol sheet, opened from the first watchlist row.
    await page.goto(APP_URL + "/m/watchlist", { waitUntil: "networkidle" });
    const row = page.locator("main ul li button").first();
    if (await row.count()) {
      await row.click();
      await page.waitForSelector("[role='dialog']", { timeout: 5_000 });
      await page.waitForTimeout(600);
      await page.screenshot({ path: path.join(outputDirectory, `symbol-sheet-${theme}.png`) });
    }
    await context.close();
  }
  console.log(`Screenshots written to ${outputDirectory}`);
} finally {
  await browser.close();
  await new Promise((resolve) => mock.server.close(resolve));
}
