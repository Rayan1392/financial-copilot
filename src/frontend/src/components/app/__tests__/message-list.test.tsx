import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { MessageList } from "../message-list";
import type { AssistantChatBlock, ChatMessage } from "@/lib/chat.functions";

describe("MessageList", () => {
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

    const { container } = render(
      <MessageList
        messages={messages}
        loading={false}
        streaming={false}
        onSuggested={vi.fn()}
      />,
    );

    const metadata = screen.getByTestId("table-metadata");
    expect(metadata).toHaveTextContent("واحد: میلیون ریال");
    expect(container).not.toHaveTextContent("Unit: million Rials");
    expect(screen.getByRole("columnheader", { name: "متوسط فروش ۱۲ ماهه" })).toBeInTheDocument();
    expect(container).not.toHaveTextContent("AVG_12M_MONTHLY_SALES");
    expect(container).not.toHaveTextContent("Average 12 Month Sales");

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

    render(
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

    render(
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
});
