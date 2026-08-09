import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactElement } from "react";
import { MessageList } from "../message-list";
import type { AssistantChatBlock, ChatMessage } from "@/lib/chat.functions";

function renderMessageList(ui: ReactElement) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}

describe("MessageList", () => {
  it("uses reply language to right-align a Persian assistant answer that starts in English", () => {
    const assistantBlock: AssistantChatBlock = {
      message: "Found metric data for 0 symbol(s). 1 unresolved.\n\nتحلیل بنیادی فولاژ",
      intent: "ComprehensiveAnalysis",
      replyLanguage: "fa",
      creditsUsed: 1,
      suggestedQuestions: [],
      filters: [],
      citations: [],
    };

    const { container } = renderMessageList(<MessageList
      messages={[{ id: "assistant-rtl", role: "assistant", content: assistantBlock, created_at: "2026-08-08T00:00:00Z" }]}
      loading={false}
      streaming={false}
      onSuggested={vi.fn()}
    />);

    const content = screen.getByText("تحلیل بنیادی فولاژ").closest("[dir='rtl']");
    expect(content).not.toBeNull();
    expect(content?.className).toContain("text-right");
    expect(container.querySelector("[dir='rtl'] .text-right")).not.toBeNull();
  });

  it("renders persisted suggested actions and submits the visible message once", () => {
    const onSuggested = vi.fn();
    const assistantBlock: AssistantChatBlock = {
      message: "لطفاً درخواست را کامل کنید.",
      intent: "Clarification",
      replyLanguage: "fa",
      creditsUsed: 0,
      suggestedQuestions: [],
      suggestedActions: [{
        id: "fill:monthly_activity_trend",
        kind: "FillSlot",
        label: "تکمیل درخواست",
        message: "چارت روند فروش فولاد",
        capabilityCode: "monthly_activity_trend",
        relevanceReason: "required_input_missing",
        registryVersion: 1,
      }],
      filters: [],
      citations: [],
    };

    renderMessageList(<MessageList
      messages={[{ id: "assistant-action", role: "assistant", content: assistantBlock, created_at: "2026-08-07T00:00:00Z" }]}
      loading={false}
      streaming={false}
      onSuggested={onSuggested}
    />);

    fireEvent.click(screen.getByRole("button", { name: "تکمیل درخواست" }));
    expect(onSuggested).toHaveBeenCalledTimes(1);
    expect(onSuggested).toHaveBeenCalledWith("چارت روند فروش فولاد", "fill:monthly_activity_trend");
  });

  it("renders monthly-sales unit as localized table metadata for آخرین فروش کچاد چقدر بوده؟", () => {
    const table = {
      columns: [
        { identifier: "SYMBOL", displayName: "نماد" },
        { identifier: "COMPANY", displayName: "شرکت" },
        { identifier: "MONTHLY_SALES", displayName: "فروش ماهانه" },
        { identifier: "AVG_12M_MONTHLY_SALES", displayName: "AVG_12M_MONTHLY_SALES" },
        { identifier: "MONTHLY_SALES_YTD", displayName: "فروش YTD" },
      ],
      rows: [
        {
          symbolCode: "کچاد",
          companyName: "معدنی و صنعتی چادرملو",
          score: 1,
          cells: {
            SYMBOL: { formattedValue: "کچاد", freshnessStatus: "Persisted" },
            COMPANY: { formattedValue: "معدنی و صنعتی چادرملو", freshnessStatus: "Persisted" },
            MONTHLY_SALES: { formattedValue: "90,879,722", value: 90_879_722_000_000, freshnessStatus: "Persisted" },
            AVG_12M_MONTHLY_SALES: { formattedValue: "82,500,000", value: 82_500_000_000_000, freshnessStatus: "Persisted" },
            MONTHLY_SALES_YTD: { formattedValue: "787,016,400", value: 787_016_400_000_000, freshnessStatus: "Persisted" },
          },
        },
      ],
      executionFacts: {
        matchingSymbolCount: 1,
        totalSymbolsEvaluated: 1,
        fromCache: false,
        page: 1,
        pageSize: 20,
        totalPages: 1,
      },
      missingDataWarnings: [],
    };

    const assistantBlock: AssistantChatBlock = {
      message: "Unit: million Rials",
      intent: "SymbolLookup",
      confidence: 0.86,
      creditsUsed: 1,
      suggestedQuestions: [],
      filters: [],
      table,
      citations: [],
    };

    const messages: ChatMessage[] = [
      {
        id: "assistant-1",
        role: "assistant",
        content: assistantBlock,
        created_at: "2026-06-17T12:00:00Z",
      },
    ];

    const { container } = renderMessageList(
      <MessageList
        messages={messages}
        loading={false}
        streaming={false}
        onSuggested={vi.fn()}
        followedSymbols={new Set(["کچاد"])}
      />,
    );

    const metadata = screen.getByTestId("table-metadata");
    expect(metadata).toHaveTextContent("واحد: میلیون ریال");
    expect(container).not.toHaveTextContent("Unit: million Rials");
    expect(screen.getByRole("columnheader", { name: "متوسط فروش ۱۲ ماهه" })).toBeInTheDocument();
    expect(container).not.toHaveTextContent("AVG_12M_MONTHLY_SALES");
    expect(container).not.toHaveTextContent("Average 12 Month Sales");
    expect(container).toHaveTextContent("دنبال می‌شود");
    expect(container).not.toHaveTextContent("دنبال کردن نماد");

    const tableContainer = metadata.closest("[dir='rtl']");
    expect(tableContainer).not.toBeNull();
    expect(tableContainer?.querySelector("table")).not.toBeNull();
    expect(tableContainer?.querySelector("p")).toBeNull();
  });

  it("does not display zero confidence when backend confidence is missing", () => {
    const assistantBlock: AssistantChatBlock = {
      message: "پاسخ متنی بدون امتیاز",
      intent: "Unknown",
      creditsUsed: 1,
      suggestedQuestions: [],
      filters: [],
      citations: [],
    };

    const messages: ChatMessage[] = [
      {
        id: "assistant-1",
        role: "assistant",
        content: assistantBlock,
        created_at: "2026-06-18T12:00:00Z",
      },
    ];

    renderMessageList(
      <MessageList
        messages={messages}
        loading={false}
        streaming={false}
        onSuggested={vi.fn()}
      />,
    );

    expect(screen.queryByText(/اطمینان/)).not.toBeInTheDocument();
    expect(screen.getByText(/اعتبار مصرف شد/)).toBeInTheDocument();
  });

  it("does not render an empty result table", () => {
    const assistantBlock: AssistantChatBlock = {
      message: "نماد پیدا نشد.",
      intent: "SymbolLookup",
      creditsUsed: 1,
      suggestedQuestions: [],
      filters: [],
      citations: [],
      table: {
        columns: [
          { identifier: "SYMBOL", displayName: "نماد" },
          { identifier: "PE_TTM", displayName: "PE_TTM" },
        ],
        rows: [],
        executionFacts: {
          matchingSymbolCount: 0,
          totalSymbolsEvaluated: 1,
          fromCache: false,
          page: 1,
          pageSize: 1,
          totalPages: 1,
        },
        missingDataWarnings: [],
      },
    };

    renderMessageList(
      <MessageList
        messages={[
          {
            id: "assistant-empty",
            role: "assistant",
            content: assistantBlock,
            created_at: "2026-06-18T12:00:00Z",
          },
        ]}
        loading={false}
        streaming={false}
        onSuggested={vi.fn()}
      />,
    );

    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("renders governed sales-growth metadata and partial-data status without another fetch", () => {
    const assistantBlock: AssistantChatBlock = {
      message: "sales growth",
      intent: "Scanner",
      creditsUsed: 1,
      suggestedQuestions: [],
      filters: [],
      citations: [],
      table: {
        columns: [
          { identifier: "SYMBOL", displayName: "SYMBOL" },
          { identifier: "MONTHLY_SALES_BASELINE_PREVIOUS_MONTH", displayName: "MONTHLY_SALES_BASELINE_PREVIOUS_MONTH" },
          { identifier: "MONTHLY_SALES_GROWTH_PERCENT", displayName: "MONTHLY_SALES_GROWTH_PERCENT" },
        ],
        rows: [{
          symbolCode: "TEST",
          companyName: "Test company",
          score: 1,
          cells: {
            SYMBOL: { formattedValue: "TEST", freshnessStatus: "Persisted" },
            MONTHLY_SALES_BASELINE_PREVIOUS_MONTH: { value: 10, freshnessStatus: "Persisted" },
            MONTHLY_SALES_GROWTH_PERCENT: { value: 25.5, freshnessStatus: "Persisted" },
          },
          salesGrowthMetadata: {
            currentPeriod: "2026-06-01",
            baselinePeriod: "2026-05-01",
            unit: "Rial",
            scale: "Raw",
            freshnessSource: "Official filing",
            latestObservedAtUtc: "2026-07-01T00:00:00Z",
          },
        }],
        executionFacts: {
          matchingSymbolCount: 1,
          totalSymbolsEvaluated: 1,
          fromCache: false,
          page: 1,
          pageSize: 20,
          totalPages: 1,
        },
        missingDataWarnings: ["baseline unavailable for one symbol"],
        salesGrowthMetadata: {
          targetCommonPeriod: "2026-06-01",
          coverageNumerator: 1,
          coverageDenominator: 2,
          coveragePercent: 50,
          selectionStatus: "Partial",
          mixedPeriods: false,
          policyVersion: "sales-growth-v1",
          evaluationPeriodPolicyVersion: "sales-growth-period-v1",
        },
      },
    };

    renderMessageList(
      <MessageList
        messages={[{ id: "sales-growth", role: "assistant", content: assistantBlock, created_at: "2026-07-01T12:00:00Z" }]}
        loading={false}
        streaming={false}
        onSuggested={vi.fn()}
      />,
    );

    expect(screen.getByTestId("sales-growth-table-status")).toBeInTheDocument();
    expect(screen.getByTestId("sales-growth-table-status")).toHaveTextContent("Official filing");
    expect(screen.getByRole("status")).toHaveTextContent("baseline unavailable");
    expect(screen.getByRole("table")).toBeInTheDocument();
  });
});
