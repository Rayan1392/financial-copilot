import assert from "node:assert/strict";
import { existsSync } from "node:fs";
import { chromium } from "playwright-core";

const app = process.env.APP_URL ?? "http://127.0.0.1:5186";
const executablePath = [
  process.env.CHROME_PATH,
  "C:/Program Files/Google/Chrome/Application/chrome.exe",
  "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
  "/usr/bin/google-chrome",
  "/usr/bin/chromium",
].find((path) => path && existsSync(path));
if (!executablePath) throw new Error("A local browser is required.");

const browser = await chromium.launch({ executablePath, headless: true });
const threadId = "e855c05e-151a-4b07-98af-58e3d4ea358b";
const entryId = "24b7e15e-4cb2-471d-9165-777f1037cae4";
const userId = "593d6e64-33e7-4ed1-990a-74fb928b55dc";
const staffId = "28db7e02-1da0-46c3-b04c-4962fb4f8901";
const tenantId = "a1d71fee-8d59-46ba-8e4f-8e5868e46b2f";
const sentAtUtc = "2026-11-22T15:00:00Z";
let subject = "پیگیری حساب کاربری";
let startRequest = null;
let pendingSinceUtc = null;
let acknowledged = false;
let revision = 1;
const items = [
  { id: entryId, body: "پیام آزمایشی تیم ساپیو", fromStaff: true, sentAtUtc, readAtUtc: null },
];

async function session({ mobile = false, staff = false } = {}) {
  const context = await browser.newContext({
    viewport: mobile ? { width: 390, height: 780 } : { width: 1440, height: 900 },
    isMobile: mobile,
    hasTouch: mobile,
    locale: "fa-IR",
    serviceWorkers: "block",
  });
  const payload = Buffer.from(
    JSON.stringify({
      sub: staff ? staffId : userId,
      email: "test@example.test",
      exp: Math.floor(Date.now() / 1000) + 3600,
      "financial_copilot:tenant_id": tenantId,
      "financial_copilot:permission": staff
        ? ["admin.users.read", "admin.in-app-messages.support"]
        : [],
    }),
  ).toString("base64url");
  await context.addInitScript((value) => {
    sessionStorage.setItem("financial_copilot_access_token", value);
  }, `e30.${payload}.signature`);

  await context.route("**/api/**", async (route) => {
    const url = new URL(route.request().url());
    const pathname = url.pathname;
    const method = route.request().method();
    const cors = {
      "access-control-allow-origin": new URL(app).origin,
      "access-control-allow-credentials": "true",
      "access-control-allow-headers": "authorization, content-type, x-correlation-id",
      "access-control-allow-methods": "GET, POST, PUT, OPTIONS",
    };
    const summary = {
      conversationId: threadId,
      subject,
      latestActivityUtc: sentAtUtc,
      unread: !acknowledged,
      repliesEnabled: true,
      pendingSinceUtc,
      recipientLabel: "کاربر آزمایشی",
      recipientUserId: userId,
      supportStatus: startRequest ? (pendingSinceUtc ? "waitingForSupport" : "answered") : null,
    };
    const send = (json, status = 200) => route.fulfill({ json, status, headers: cors });
    const empty = () => route.fulfill({ status: 204, headers: cors });
    if (method === "OPTIONS") return empty();
    if (pathname.endsWith("/in-app-messages/me/summary"))
      return send({ unreadCount: Number(!acknowledged) });
    if (pathname.endsWith("/in-app-messages/me") && method === "POST") {
      const request = route.request().postDataJSON();
      if (!startRequest) {
        startRequest = request;
        subject = request.subject;
        pendingSinceUtc = sentAtUtc;
        acknowledged = true;
        revision = 1;
        items.splice(0, items.length, {
          id: entryId,
          body: request.body,
          fromStaff: false,
          sentAtUtc,
          readAtUtc: null,
        });
        return send({}, 503);
      }
      assert.deepEqual(request, startRequest, "a retry uses the original key and payload");
      return send({ conversationId: threadId, messageId: entryId, sentAtUtc });
    }
    if (pathname.endsWith("/in-app-messages/me"))
      return send({ items: [summary], nextCursor: null });
    if (pathname.endsWith("/in-app-messages/support")) {
      return send({
        items: pendingSinceUtc || url.searchParams.get("filter") === "all" ? [summary] : [],
        nextCursor: null,
      });
    }
    if (pathname.endsWith(`/${threadId}`))
      return send({ conversation: summary, revision, messages: { items, nextCursor: null } });
    if (pathname.endsWith(`/${threadId}/read`)) {
      acknowledged = true;
      for (const item of items.filter((message) => message.fromStaff)) item.readAtUtc = sentAtUtc;
      return empty();
    }
    if (pathname.endsWith(`/${threadId}/replies`)) {
      const reply = route.request().postDataJSON();
      revision += 1;
      pendingSinceUtc = staff ? null : sentAtUtc;
      if (staff) acknowledged = false;
      items.push({
        id: `entry-${revision}`,
        body: reply.body,
        fromStaff: staff,
        sentAtUtc,
        readAtUtc: null,
      });
      return send({ messageId: revision });
    }
    if (pathname.endsWith("/users/directory")) return send({ items: [], total: 0, hasMore: false });
    if (pathname.endsWith("/profile")) return send({ displayName: "کاربر آزمایشی" });
    return send({}, 404);
  });
  return context;
}

try {
  const desktop = await session();
  const page = await desktop.newPage();
  await page.goto(`${app}/messages`, { waitUntil: "domcontentloaded", timeout: 90_000 });
  await page.getByRole("button", { name: /پیام خوانده‌نشده/ }).waitFor({ timeout: 60_000 });
  const read = page.waitForResponse(
    (response) => response.url().endsWith("/read") && response.ok(),
  );
  await page.getByRole("button", { name: new RegExp(subject) }).click();
  await page.getByText("پیام آزمایشی تیم ساپیو").waitFor();
  await read;
  assert.ok(acknowledged, "opening detail acknowledges the displayed staff message");
  await page.goto(`${app}/messages`);
  await page.getByRole("button", { name: "درخواست جدید" }).click();
  await page.keyboard.type("Support request");
  await page.getByLabel("متن پیام").fill("متن درخواست ثبت‌شده");
  await page.getByRole("button", { name: "ارسال به پشتیبانی" }).click();
  await page
    .getByRole("alert")
    .filter({ hasText: /ثبت درخواست تأیید نشد/ })
    .waitFor();
  assert.equal(await page.getByLabel("متن پیام").inputValue(), "متن درخواست ثبت‌شده");
  await page.getByRole("button", { name: "ارسال به پشتیبانی" }).click();
  await page.getByText("در انتظار پاسخ", { exact: true }).waitFor();
  await page.getByText("متن درخواست ثبت‌شده").waitFor();
  await desktop.close();

  const staff = await session({ staff: true });
  const staffPage = await staff.newPage();
  await staffPage.goto(`${app}/admin`, { waitUntil: "domcontentloaded", timeout: 90_000 });
  await staffPage.getByRole("button", { name: /پیام‌های کاربران/ }).click();
  await staffPage.getByRole("button", { name: new RegExp(subject) }).click();
  await staffPage.getByText("متن درخواست ثبت‌شده").waitFor();
  await staffPage.getByLabel("پاسخ", { exact: true }).fill("پاسخ آزمایشی تیم ساپیو");
  await staffPage.getByRole("button", { name: "ارسال پاسخ" }).click();
  await staffPage.getByText("پاسخ آزمایشی تیم ساپیو").waitFor();
  assert.equal(pendingSinceUtc, null);
  await staff.close();

  const mobile = await session({ mobile: true });
  const mobilePage = await mobile.newPage();
  await mobilePage.goto(`${app}/m/account`, { waitUntil: "domcontentloaded", timeout: 90_000 });
  await mobilePage.getByRole("link", { name: /پیام‌ها/ }).click();
  await mobilePage.getByRole("button", { name: "درخواست جدید" }).click();
  await mobilePage.getByRole("dialog", { name: "درخواست جدید" }).waitFor();
  await mobilePage.keyboard.press("Escape");
  await mobilePage.getByRole("button", { name: new RegExp(subject) }).click();
  await mobilePage.getByText("پاسخ آزمایشی تیم ساپیو").waitFor();
  await mobilePage.getByText("پاسخ داده شد", { exact: true }).waitFor();
  assert.ok(mobilePage.url().includes("/m/account/messages/"));
  await mobilePage.reload();
  await mobilePage.getByText("متن درخواست ثبت‌شده").waitFor();
  await mobilePage.getByLabel("پاسخ", { exact: true }).fill("پیگیری از موبایل");
  await mobilePage.getByRole("button", { name: "ارسال پاسخ" }).click();
  await mobilePage.getByText("در انتظار پاسخ", { exact: true }).waitFor();
  assert.ok(pendingSinceUtc);
  await mobile.close();
  console.log(
    "Desktop request and retry, staff answer, and mobile reload and follow-up passed (mock API).",
  );
} finally {
  await browser.close();
}
