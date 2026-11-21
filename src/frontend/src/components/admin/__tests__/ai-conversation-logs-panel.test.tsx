import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AdminPage } from "../admin-page";
import { AiConversationLogsPanel } from "../ai-conversation-logs-panel";
import {
  adminApi,
  type AdminConversationLogItem,
  type AdminConversationLogPage,
} from "@/integrations/financial-copilot/admin-client";
import { formatDisclosureReceiptDate } from "@/lib/format/disclosure";

// Other admin calls stay pending unless the test gives them a response.
const api = vi.hoisted(() => ({
  aiConversationLogs: vi.fn(),
  aiConversationLogTurn: vi.fn(),
  userDirectory: vi.fn(),
}));
vi.mock("@/integrations/financial-copilot/admin-client", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("@/integrations/financial-copilot/admin-client")>();
  return {
    ...actual,
    adminApi: new Proxy(api as Record<string, unknown>, {
      get: (target, key: string) => target[key] ?? (() => new Promise(() => {})),
    }),
  };
});

const now = new Date("2026-11-01T21:00:00Z"); // 2026-11-02 00:30 in Tehran

function item(overrides: Partial<AdminConversationLogItem> = {}): AdminConversationLogItem {
  return {
    turnId: "turn-1",
    conversationId: "conversation-1",
    conversationTitle: "گفتگوی آزمایشی",
    user: {
      userId: "user-1",
      displayName: "سارا",
      email: "sara@example.test",
      maskedMobile: "•••2222",
    },
    questionPreview: "سود فولاد چقدر است؟",
    answerPreview: "سود خالص فولاد ...",
    questionCreatedAtUtc: "2026-11-01T08:00:00Z",
    answerCreatedAtUtc: "2026-11-01T08:00:00Z",
    status: "Failed",
    channel: "Telegram",
    ...overrides,
  };
}

function page(overrides: Partial<AdminConversationLogPage> = {}): AdminConversationLogPage {
  return { items: [item()], page: 1, pageSize: 20, totalCount: 51, totalPages: 3, ...overrides };
}

function lastQuery() {
  return vi.mocked(adminApi.aiConversationLogs).mock.calls.at(-1)![0];
}

beforeEach(() => {
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(now);
  api.aiConversationLogs.mockReset();
  api.aiConversationLogTurn.mockReset();
  api.userDirectory.mockReset();
  api.userDirectory.mockReturnValue(new Promise(() => {}));
  api.aiConversationLogTurn.mockReturnValue(new Promise(() => {}));
});
afterEach(() => vi.useRealTimers());

describe("Admin Users directory usage filters", () => {
  it("keeps the initial directory request unchanged and applies a period-specific zero filter", async () => {
    api.userDirectory.mockResolvedValue({ items: [], nextCursor: null, hasMore: false, asOfUtc: now.toISOString() });
    render(<AdminPage user={{
      userId: "admin", email: "admin@example.test", tenantId: "t", roles: [], permissions: ["admin.users.read"],
    }} />);

    const toggle = screen.getByRole("checkbox", { name: "نمایش پرسش‌های ثبت‌شده" });
    await waitFor(() => expect(toggle).toBeEnabled());
    expect(api.userDirectory).toHaveBeenLastCalledWith({ pageSize: 10, cursor: null, status: "any" });
    fireEvent.click(toggle);
    await waitFor(() => expect(api.userDirectory).toHaveBeenLastCalledWith(expect.objectContaining({
      includeUsage: true,
      questionFromUtc: "2026-10-03T20:30:00.000Z",
      questionToUtc: "2026-11-02T20:30:00.000Z",
    })));

    fireEvent.change(screen.getByRole("combobox", { name: "بازهٔ پرسش‌ها" }), { target: { value: "7" } });
    fireEvent.click(screen.getByRole("checkbox", { name: "بدون پرسش ثبت‌شده در بازهٔ انتخابی" }));
    const apply = screen.getByRole("button", { name: "اعمال فیلترها" });
    await waitFor(() => expect(apply).toBeEnabled());
    fireEvent.click(apply);
    await waitFor(() => expect(api.userDirectory).toHaveBeenLastCalledWith(expect.objectContaining({
      cursor: null,
      questionFromUtc: "2026-10-26T20:30:00.000Z",
      questionToUtc: "2026-11-02T20:30:00.000Z",
      maxQuestions: 0,
    })));
    expect(screen.queryByRole("combobox", { name: "طرح فعلی" })).not.toBeInTheDocument();
  });
});

describe("AiConversationLogsPanel", () => {
  it("shows the tab only to administrators who can read users", () => {
    const base = { userId: "admin", email: "admin@example.test", tenantId: "t", roles: [] };
    const { unmount } = render(<AdminPage user={{ ...base, permissions: ["admin.users.read"] }} />);
    expect(screen.getByRole("button", { name: /تاریخچه پرسش و پاسخ کاربران/ })).toBeInTheDocument();
    unmount();

    render(<AdminPage user={{ ...base, permissions: ["admin.roles.read"] }} />);
    expect(
      screen.queryByRole("button", { name: /تاریخچه پرسش و پاسخ کاربران/ }),
    ).not.toBeInTheDocument();
  });

  it("asks for the last 30 Tehran days on page 1 with 20 rows by default", async () => {
    api.aiConversationLogs.mockResolvedValue(page());
    render(<AiConversationLogsPanel />);

    await screen.findByText("سود فولاد چقدر است؟");
    expect(lastQuery()).toEqual({
      page: 1,
      pageSize: 20,
      q: undefined,
      user: undefined,
      fromUtc: "2026-10-03T20:30:00.000Z",
      toUtc: "2026-11-02T20:30:00.000Z",
      status: undefined,
    });
  });

  it("shows rows with status, channel, masked mobile and Jalali Tehran time", async () => {
    api.aiConversationLogs.mockResolvedValue(page());
    render(<AiConversationLogsPanel />);

    const row = (await screen.findByText("سود فولاد چقدر است؟")).closest("tr")!;
    expect(within(row).getByText("ناموفق")).toBeInTheDocument();
    expect(within(row).getByText("تلگرام")).toBeInTheDocument();
    expect(within(row).getByText("•••2222")).toBeInTheDocument();
    expect(
      within(row).getByText(formatDisclosureReceiptDate("2026-11-01T08:00:00Z")),
    ).toBeInTheDocument();
    expect(screen.getByText("صفحه ۱ از ۳ · ۵۱ پرسش")).toBeInTheDocument();
  });

  it("pages forward and returns to page 1 when the page size changes", async () => {
    api.aiConversationLogs.mockResolvedValue(page());
    render(<AiConversationLogsPanel />);
    await screen.findByText("سود فولاد چقدر است؟");

    fireEvent.click(screen.getByRole("button", { name: "صفحه بعد" }));
    await waitFor(() => expect(lastQuery()).toMatchObject({ page: 2, pageSize: 20 }));

    fireEvent.change(screen.getByLabelText("تعداد در هر صفحه"), { target: { value: "50" } });
    await waitFor(() => expect(lastQuery()).toMatchObject({ page: 1, pageSize: 50 }));
  });

  it("applies trimmed filters from page 1", async () => {
    api.aiConversationLogs.mockResolvedValue(page());
    render(<AiConversationLogsPanel />);
    await screen.findByText("سود فولاد چقدر است؟");
    fireEvent.click(screen.getByRole("button", { name: "صفحه بعد" }));
    await waitFor(() => expect(lastQuery()).toMatchObject({ page: 2 }));

    fireEvent.change(screen.getByPlaceholderText("جست‌وجو در متن پرسش"), {
      target: { value: "  سود  " },
    });
    fireEvent.change(screen.getByPlaceholderText("شناسه، ایمیل، نام یا شماره همراه"), {
      target: { value: " 09121234567 " },
    });
    fireEvent.change(screen.getByLabelText("وضعیت"), { target: { value: "NoResponse" } });
    fireEvent.click(screen.getByRole("button", { name: "اعمال فیلتر" }));

    await waitFor(() =>
      expect(lastQuery()).toMatchObject({
        page: 1,
        q: "سود",
        user: "09121234567",
        status: "NoResponse",
      }),
    );
  });

  it("shows an empty state", async () => {
    api.aiConversationLogs.mockResolvedValue(page({ items: [], totalCount: 0, totalPages: 0 }));
    render(<AiConversationLogsPanel />);

    expect(await screen.findByText("پرسشی با این فیلترها پیدا نشد.")).toBeInTheDocument();
  });

  it("shows the correlation id of a failed request", async () => {
    api.aiConversationLogs.mockRejectedValue(
      Object.assign(new Error("درخواست ناموفق بود"), { correlationId: "corr-164" }),
    );
    render(<AiConversationLogsPanel />);

    expect(await screen.findByRole("alert")).toHaveTextContent("شناسه پیگیری: corr-164");
  });

  it("ignores a response that arrives after a newer request", async () => {
    let resolveFirst!: (value: AdminConversationLogPage) => void;
    api.aiConversationLogs
      .mockReturnValueOnce(new Promise((resolve) => (resolveFirst = resolve)))
      .mockResolvedValue(page({ items: [item({ turnId: "new", questionPreview: "پرسش تازه" })] }));
    render(<AiConversationLogsPanel />);

    fireEvent.change(screen.getByPlaceholderText("جست‌وجو در متن پرسش"), {
      target: { value: "تازه" },
    });
    fireEvent.click(screen.getByRole("button", { name: "اعمال فیلتر" }));
    await screen.findByText("پرسش تازه");
    resolveFirst(page({ items: [item({ turnId: "old", questionPreview: "پرسش قدیمی" })] }));

    await Promise.resolve();
    expect(screen.queryByText("پرسش قدیمی")).not.toBeInTheDocument();
    expect(screen.getByText("پرسش تازه")).toBeInTheDocument();
  });
});
