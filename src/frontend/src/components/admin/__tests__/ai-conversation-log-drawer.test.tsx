import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AiConversationLogDrawer } from "../ai-conversation-log-drawer";
import {
  adminApi,
  type AdminConversationLogAnswer,
  type AdminConversationLogTurn,
} from "@/integrations/financial-copilot/admin-client";
import type { AssistantContentResponse, ScannerTable } from "@/lib/chat.functions";
import { formatDisclosureReceiptDate } from "@/lib/format/disclosure";
// The same representative answers the chat's phone and browser checks use.
import {
  monthlySalesTrendResult,
  quarterlyReportResult,
} from "../../../../scripts/mobile-workspace/answer-fixtures.mjs";

vi.mock("@/integrations/financial-copilot/admin-client", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("@/integrations/financial-copilot/admin-client")>();
  return { ...actual, adminApi: { aiConversationLogTurn: vi.fn() } };
});

const questionTime = "2026-11-01T08:00:00Z";

const scannerTable: ScannerTable = {
  columns: [{ identifier: "SYMBOL", displayName: "نماد" }],
  rows: [
    {
      symbolCode: "خودرو",
      score: 1,
      cells: { SYMBOL: { formattedValue: "خودرو", freshnessStatus: "Persisted" } },
    },
  ],
  executionFacts: {
    matchingSymbolCount: 1,
    totalSymbolsEvaluated: 10,
    fromCache: false,
    page: 1,
    pageSize: 20,
    totalPages: 1,
  },
  missingDataWarnings: [],
};

const interactiveExtras: Partial<AssistantContentResponse> = {
  explainableAnswer: {
    filterChips: [],
    dataCitations: [],
    confidence: { score: 0.9 },
    suggestedFollowUpQuestions: ["پرسش پیشنهادی"],
  },
  suggestedActions: [
    {
      id: "a1",
      kind: "FollowUp",
      label: "کار پیشنهادی",
      message: "پیام",
      capabilityCode: "x",
      relevanceReason: "related",
      registryVersion: 1,
    },
  ],
  aiOrchestrationMode: "V2",
  workflowCorrelationId: "workflow-1",
};

function answer(
  messageId: string,
  createdAtUtc: string,
  content: Partial<AssistantContentResponse>,
): AdminConversationLogAnswer {
  return {
    messageId,
    content: "متن ذخیره‌شده",
    createdAtUtc,
    status: "Answered",
    outcomeReasonCode: null,
    usage: {
      providerName: "openai",
      modelName: "gpt-test",
      promptTokens: 100,
      completionTokens: 20,
      totalTokens: 120,
      creditsCharged: 2,
      completionStatus: "Succeeded",
      executionRoute: "agent",
    },
    assistantContent: {
      intent: "Unknown",
      clarificationRequired: false,
      ...interactiveExtras,
      ...content,
    } as AssistantContentResponse,
  };
}

const turn: AdminConversationLogTurn = {
  turnId: "turn-1",
  conversationId: "conversation-1",
  conversationTitle: null,
  channel: "Web",
  user: {
    userId: "user-1",
    displayName: "سارا",
    email: "sara@example.test",
    maskedMobile: "•••2222",
    hasUserAccount: true,
    actorKind: "User",
  },
  question: "روند فروش فولاد را نشان بده",
  questionCreatedAtUtc: questionTime,
  status: "Answered",
  answers: [
    answer("m1", questionTime, {
      textAnswer: "روند فروش ماهانه",
      monthlyActivityTrendResult:
        monthlySalesTrendResult as unknown as AssistantContentResponse["monthlyActivityTrendResult"],
    }),
    answer("m2", "2026-11-01T08:01:00Z", {
      textAnswer: "صورت سود و زیان",
      financialStatementTableResult: {
        quarterlyResult: quarterlyReportResult as unknown as NonNullable<
          AssistantContentResponse["financialStatementTableResult"]
        >["quarterlyResult"],
      },
    }),
    answer("m3", "2026-11-01T08:02:00Z", { textAnswer: "نتیجهٔ غربال", scannerTable }),
  ],
};

function renderDrawer() {
  return render(
    <QueryClientProvider
      client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}
    >
      <AiConversationLogDrawer turnId="turn-1" onClose={vi.fn()} />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  vi.mocked(adminApi.aiConversationLogTurn).mockReset().mockResolvedValue(turn);
  // The quarterly chart switch comes from the UI config endpoint; unavailable here.
  vi.stubGlobal(
    "fetch",
    vi.fn(async () => Promise.reject(new Error("offline"))),
  );
  // Recharts' ResponsiveContainer needs ResizeObserver, which jsdom lacks.
  vi.stubGlobal(
    "ResizeObserver",
    class {
      observe() {}
      unobserve() {}
      disconnect() {}
    },
  );
});
afterEach(() => vi.unstubAllGlobals());

// The first render loads the chart, quarterly table and scanner components; allow for a busy full-suite run.
describe("AiConversationLogDrawer", { timeout: 20_000 }, () => {
  it("renders stored chart, quarterly and scanner answers read-only", async () => {
    renderDrawer();
    const dialog = await screen.findByRole("dialog");
    await within(dialog).findByText("روند فروش فولاد را نشان بده");

    expect(adminApi.aiConversationLogTurn).toHaveBeenCalledWith("turn-1");
    expect(within(dialog).getByLabelText("راهنمای نمودار")).toBeInTheDocument();
    expect(within(dialog).getByText("خودرو")).toBeInTheDocument();
    expect(within(dialog).getAllByRole("table").length).toBeGreaterThanOrEqual(2);

    expect(within(dialog).queryByRole("group", { name: "کارهای پاسخ" })).not.toBeInTheDocument();
    expect(
      within(dialog).queryByRole("button", { name: "دنبال کردن نماد" }),
    ).not.toBeInTheDocument();
    expect(within(dialog).queryByRole("button", { name: "پرسش پیشنهادی" })).not.toBeInTheDocument();
    expect(within(dialog).queryByRole("button", { name: "کار پیشنهادی" })).not.toBeInTheDocument();
    expect(within(dialog).queryByText("workflow-1")).not.toBeInTheDocument();
    expect(within(dialog).queryByText("تحلیل کن")).not.toBeInTheDocument();
  });

  it("shows Jalali Tehran times, one completion time and the values that are not stored", async () => {
    renderDrawer();
    const dialog = await screen.findByRole("dialog");
    await within(dialog).findByText("روند فروش فولاد را نشان بده");

    expect(within(dialog).getAllByText("ذخیره نشده")).toHaveLength(2);
    expect(within(dialog).getByText("زمان تکمیل پاسخ")).toBeInTheDocument();
    expect(within(dialog).queryByText("زمان ثبت پرسش")).not.toBeInTheDocument();
    expect(within(dialog).getByText(formatDisclosureReceiptDate(questionTime))).toBeInTheDocument();
    expect(
      within(dialog).getByText(formatDisclosureReceiptDate("2026-11-01T08:02:00Z")),
    ).toBeInTheDocument();
    expect(within(dialog).getByText("بدون عنوان")).toBeInTheDocument();
    expect(within(dialog).getAllByText("gpt-test").length).toBeGreaterThan(0);
  });

  it("names an API client actor instead of a missing user name", async () => {
    vi.mocked(adminApi.aiConversationLogTurn).mockResolvedValue({
      ...turn,
      user: {
        userId: "6c1f0a52-0000-4000-8000-000000000001",
        displayName: null,
        email: null,
        maskedMobile: null,
        hasUserAccount: false,
        actorKind: "ApiClient",
        apiClientName: "Telegram gateway",
      },
    });
    renderDrawer();
    const dialog = await screen.findByRole("dialog");

    expect(await within(dialog).findByText("کلاینت API: Telegram gateway")).toBeInTheDocument();
    expect(within(dialog).getByText("6c1f0a52-0000-4000-8000-000000000001")).toBeInTheDocument();
    expect(within(dialog).queryByText("بدون نام")).not.toBeInTheDocument();
  });

  it("shows the error with its correlation id", async () => {
    vi.mocked(adminApi.aiConversationLogTurn).mockRejectedValue(
      Object.assign(new Error("ناموفق"), { correlationId: "corr-9" }),
    );
    renderDrawer();

    expect(await screen.findByRole("alert")).toHaveTextContent("شناسه پیگیری: corr-9");
  });
});
