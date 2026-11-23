import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AdminPage } from "../admin-page";
import { AiConversationLogsPanel } from "../ai-conversation-logs-panel";
import {
  adminApi,
  type AdminConversationLogItem,
  type AdminConversationLogPage,
  type AdminCustomer360,
} from "@/integrations/financial-copilot/admin-client";
import { FinancialCopilotApiError } from "@/integrations/financial-copilot/api-client";
import { formatDisclosureReceiptDate } from "@/lib/format/disclosure";

// Other admin calls stay pending unless the test gives them a response.
const api = vi.hoisted(() => ({
  aiConversationLogs: vi.fn(),
  aiConversationLogTurn: vi.fn(),
  userDirectory: vi.fn(),
  user: vi.fn(),
  customer360: vi.fn(),
  customerCreditHistory: vi.fn(),
  adjustCredits: vi.fn(),
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
      hasUserAccount: true,
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
  api.user.mockReset();
  api.customer360.mockReset();
  api.customerCreditHistory.mockReset();
  api.adjustCredits.mockReset();
  api.userDirectory.mockReturnValue(new Promise(() => {}));
  api.aiConversationLogTurn.mockReturnValue(new Promise(() => {}));
});
afterEach(() => vi.useRealTimers());

describe("Shared Customer 360", () => {
  const admin = {
    userId: "admin",
    email: "admin@example.test",
    tenantId: "t",
    roles: [],
    permissions: ["admin.users.read"],
  };
  const hidden = { status: "not_authorized", data: null } as const;
  const identity = {
    userId: "user-1",
    email: "sara@example.test",
    isEnabled: true,
    isLockedOut: false,
    roles: [],
    tenantIds: ["t"],
  };
  const details: AdminCustomer360 = {
    userId: "user-1",
    tenantId: "t",
    isEnabled: true,
    isLockedOut: false,
    acquisitionSourceCode: "native",
    registeredAtUtc: null,
    lastSuccessfulLoginAtUtc: null,
    lastLoginProviderCode: null,
    hasNativeCredential: true,
    emailConfirmed: true,
    activeRefreshSessions: 2,
    tenantMemberships: ["t"],
    linkedProviders: [],
    subscription: hidden,
    credits: hidden,
    purchases: hidden,
    asOfUtc: now.toISOString(),
    creditAdjustmentAccountId: "billing-1",
  };

  function openLogs() {
    fireEvent.click(screen.getByRole("button", { name: /تاریخچه پرسش و پاسخ کاربران/ }));
  }

  it("uses the same view from both lists and restores focus for an unnamed log user", async () => {
    api.user.mockResolvedValue(identity);
    api.customer360.mockResolvedValue(details);
    api.userDirectory.mockResolvedValue({
      items: [
        {
          ...item().user,
          status: "enabled",
          linkedProviderCodes: [],
          subscription: hidden,
          credits: hidden,
        },
      ],
      nextCursor: null,
      hasMore: false,
    });
    api.aiConversationLogs.mockResolvedValue(
      page({
        items: [
          item({
            user: { ...item().user, displayName: null, email: null, maskedMobile: null },
          }),
        ],
      }),
    );
    render(<AdminPage user={admin} />);

    const directoryUser = await screen.findByRole("button", {
      name: "نمایش جزئیات sara@example.test",
    });
    fireEvent.click(directoryUser);
    const directoryDialog = screen.getByRole("dialog", { name: /سارا/ });
    await within(directoryDialog).findByText("مشخصات حساب");
    expect(
      within(directoryDialog).queryByRole("button", { name: "افزودن اعتبار" }),
    ).not.toBeInTheDocument();
    fireEvent.click(within(directoryDialog).getByRole("button", { name: "Close" }));
    await waitFor(() => expect(directoryUser).toHaveFocus());

    openLogs();
    const logUser = await screen.findByRole("button", { name: "بدون نام" });
    fireEvent.click(logUser);
    const logDialog = screen.getByRole("dialog", { name: /بدون نام/ });
    await within(logDialog).findByText("مشخصات حساب");
    expect(within(logDialog).getByRole("tab", { name: "اطلاعات حساب" })).toHaveAttribute(
      "aria-selected",
      "true",
    );
    expect(api.customer360.mock.calls).toEqual([["user-1"], ["user-1"]]);
    fireEvent.keyDown(logDialog, { key: "Escape" });
    await waitFor(() => expect(logUser).toHaveFocus());
    expect(api.aiConversationLogs).toHaveBeenCalledTimes(1);
  }, 15_000);

  it("ignores a previous account response and requires an existing Billing account", async () => {
    const second = {
      ...item().user,
      userId: "user-2",
      displayName: "مریم",
      email: null,
      maskedMobile: null,
    };
    api.aiConversationLogs.mockResolvedValue(
      page({
        items: [item(), item({ turnId: "turn-2", user: second })],
      }),
    );
    let resolvePrevious!: (value: AdminCustomer360) => void;
    api.user.mockResolvedValue(identity);
    api.customer360
      .mockImplementationOnce(
        () =>
          new Promise((resolve) => {
            resolvePrevious = resolve;
          }),
      )
      .mockResolvedValueOnce({
        ...details,
        userId: second.userId,
        creditAdjustmentAccountId: null,
      });
    render(
      <AdminPage
        user={{ ...admin, permissions: [...admin.permissions, "admin.credits.adjust"] }}
      />,
    );

    openLogs();
    fireEvent.click(await screen.findByRole("button", { name: "سارا" }));
    fireEvent.keyDown(screen.getByRole("dialog", { name: /سارا/ }), { key: "Escape" });
    fireEvent.click(await screen.findByRole("button", { name: "مریم" }));
    const dialog = screen.getByRole("dialog", { name: /مریم/ });
    await within(dialog).findByText("مشخصات حساب");
    await act(async () => resolvePrevious(details));
    expect(within(dialog).getByText("user-2")).toBeInTheDocument();
    expect(within(dialog).queryByText("user-1")).not.toBeInTheDocument();
    expect(within(dialog).queryByRole("button", { name: "افزودن اعتبار" })).not.toBeInTheDocument();
  });

  it.each([false, undefined])(
    "keeps the turn detail available without opening an unverified customer account (%s)",
    async (hasUserAccount) => {
      api.aiConversationLogs.mockResolvedValue(
        page({
          items: [item({ user: { ...item().user, hasUserAccount } })],
        }),
      );
      render(<AdminPage user={admin} />);
      openLogs();

      const unavailable = await screen.findByText("حساب کاربری در دسترس نیست");
      const row = within(unavailable.closest("tr")!);
      expect(row.queryByRole("button", { name: "سارا" })).not.toBeInTheDocument();
      expect(row.queryByText("sara@example.test")).not.toBeInTheDocument();
      expect(row.getByText("سود فولاد چقدر است؟")).toBeInTheDocument();
      fireEvent.click(row.getByRole("button", { name: "مشاهده" }));
      expect(api.aiConversationLogTurn).toHaveBeenCalledWith("turn-1");
      expect(api.user).not.toHaveBeenCalled();
      expect(api.customer360).not.toHaveBeenCalled();
    },
  );

  it.each([
    ["user", 403, "tenant-scope-violation", "حساب این کاربر دیگر در دسترس نیست."],
    ["customer360", 404, "resource-not-found", "حساب این کاربر دیگر در دسترس نیست."],
    ["user", 403, "permission-denied", "Original error"],
  ] as const)("handles a %s error after listing (%s)", async (endpoint, status, type, message) => {
    api.aiConversationLogs.mockResolvedValue(page());
    api.user.mockResolvedValue(identity);
    api.customer360.mockResolvedValue(details);
    api[endpoint].mockRejectedValue(
      new FinancialCopilotApiError(
        "Original error",
        status,
        "https://financialcopilot/errors/" + type,
      ),
    );
    render(<AdminPage user={admin} />);
    openLogs();
    fireEvent.click(await screen.findByRole("button", { name: "سارا" }));
    const alert = await within(screen.getByRole("dialog", { name: /سارا/ })).findByRole("alert");
    expect(alert).toHaveTextContent(message);
  });

  it("reuses the idempotency key on retry and reloads credit history after success", async () => {
    api.aiConversationLogs.mockResolvedValue(page());
    api.user.mockResolvedValue(identity);
    api.customer360.mockResolvedValue({
      ...details,
      credits: {
        status: "available",
        data: {
          activePeriods: [],
          spendableCredits: 12,
          reservedCredits: 0,
          approvedCreditLine: null,
          activeSubscriptionCredits: 0,
          nextExpiryUtc: null,
          sourceSystem: "Billing",
          asOfUtc: now.toISOString(),
        },
      },
    });
    api.customerCreditHistory.mockResolvedValue({ items: [], nextCursor: null, hasMore: false });
    let finishRetry!: (value: { alreadyApplied: boolean; updatedBalance: number }) => void;
    api.adjustCredits.mockRejectedValueOnce(new Error("اتصال قطع شد")).mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finishRetry = resolve;
        }),
    );
    render(
      <AdminPage
        user={{
          ...admin,
          permissions: [...admin.permissions, "admin.credits.adjust", "admin.usage-ledger.read"],
        }}
      />,
    );
    openLogs();
    fireEvent.click(await screen.findByRole("button", { name: "سارا" }));
    const customerDialog = screen.getByRole("dialog", { name: /سارا/ });
    const action = await within(customerDialog).findByRole("button", { name: "افزودن اعتبار" });
    await act(async () => {
      fireEvent.click(
        within(customerDialog).getByRole("button", { name: "بارگذاری تاریخچه اعتبار" }),
      );
    });
    await waitFor(() => expect(action).toBeEnabled());

    async function submit(amount: string) {
      fireEvent.click(action);
      const form = await screen.findByRole("dialog", { name: "افزودن اعتبار به مشتری" });
      fireEvent.change(within(form).getByRole("spinbutton"), { target: { value: amount } });
      fireEvent.change(within(form).getByRole("textbox"), { target: { value: "جبران" } });
      await act(async () => {
        fireEvent.submit(form.querySelector("form")!);
      });
    }
    await submit("1.5");
    await within(customerDialog).findByText(/مقدار اعتبار باید عدد صحیح/);
    expect(api.adjustCredits).not.toHaveBeenCalled();

    await submit("5");
    await within(customerDialog).findByText("اتصال قطع شد");
    await submit("5");
    await waitFor(() => expect(action).toBeDisabled());
    fireEvent.click(action);
    expect(api.adjustCredits).toHaveBeenCalledTimes(2);
    await act(async () => finishRetry({ alreadyApplied: true, updatedBalance: 17 }));
    await within(customerDialog).findByText(/ثبت ۵ اعتبار تأیید شد/);
    await waitFor(() =>
      expect(api.customerCreditHistory.mock.calls).toEqual([
        ["user-1", 10, undefined],
        ["user-1", 10],
      ]),
    );
    expect(api.adjustCredits).toHaveBeenCalledTimes(2);
    expect(api.adjustCredits.mock.calls[0]).toEqual(["billing-1", 5, "جبران", expect.any(String)]);
    expect(api.adjustCredits.mock.calls[1]).toEqual(api.adjustCredits.mock.calls[0]);
    expect(api.customer360).toHaveBeenCalledTimes(2);
  }, 15_000);
});

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
