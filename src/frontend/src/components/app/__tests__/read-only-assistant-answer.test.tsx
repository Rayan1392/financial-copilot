import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactElement } from "react";
import { MessageList, ReadOnlyAssistantAnswer } from "../message-list";
import type { AssistantChatBlock, SymbolSnapshotResult } from "@/lib/chat.functions";

function renderWithQuery(ui: ReactElement) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}

const snapshot: SymbolSnapshotResult = {
  symbol: "فولاد",
  companyName: "فولاد مبارکه",
  introFa: "خلاصهٔ نماد فولاد",
  price: { status: "Available", latestPrice: 5000, dailyChangePercent: 1.5 },
  monthlySales: { status: "Unavailable", noticeFa: "فروش ماهانه در دسترس نیست." },
  quarterlyReport: { status: "Unavailable", noticeFa: "گزارش فصلی در دسترس نیست." },
};

function block(overrides: Partial<AssistantChatBlock> = {}): AssistantChatBlock {
  return {
    message: "نتیجهٔ جست‌وجو",
    intent: "Scanner",
    replyLanguage: "fa",
    creditsUsed: 2,
    suggestedQuestions: ["پرسش پیشنهادی"],
    suggestedActions: [
      {
        id: "action-1",
        kind: "FollowUp",
        label: "کار پیشنهادی",
        message: "پیام کار پیشنهادی",
        capabilityCode: "scanner",
        relevanceReason: "related",
        registryVersion: 1,
      },
    ],
    filters: [{ label: "P/E", value: "< 5" }],
    citations: [],
    table: {
      columns: [{ identifier: "SYMBOL", displayName: "نماد" }],
      rows: [
        {
          symbolCode: "خودرو",
          score: 1,
          cells: { SYMBOL: { formattedValue: "خودرو", freshnessStatus: "Persisted" } },
        },
      ],
      executionFacts: {
        matchingSymbolCount: 40,
        totalSymbolsEvaluated: 400,
        fromCache: false,
        page: 1,
        pageSize: 20,
        totalPages: 2,
      },
      missingDataWarnings: [],
    },
    ...overrides,
  };
}

describe("ReadOnlyAssistantAnswer", () => {
  it("keeps the interactive chat controls in the default mode", () => {
    renderWithQuery(
      <MessageList
        messages={[
          { id: "a1", role: "assistant", content: block(), created_at: "2026-11-01T00:00:00Z" },
        ]}
        loading={false}
        streaming={false}
        onSuggested={vi.fn()}
        onPageChange={vi.fn()}
      />,
    );

    expect(screen.getByRole("group", { name: "کارهای پاسخ" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "کار پیشنهادی" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "پرسش پیشنهادی" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "دنبال کردن نماد" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "صفحه بعد" })).toBeEnabled();
  });

  it("renders the same answer content with no control that acts", () => {
    const { container } = renderWithQuery(<ReadOnlyAssistantAnswer block={block()} />);

    expect(container).toHaveTextContent("نتیجهٔ جست‌وجو");
    expect(container).toHaveTextContent("P/E: < 5");
    expect(within(container.querySelector("table")!).getByText("خودرو")).toBeInTheDocument();
    expect(container).toHaveTextContent("اعتبار مصرف شد");
    expect(screen.queryByRole("group", { name: "کارهای پاسخ" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "کار پیشنهادی" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "پرسش پیشنهادی" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "دنبال کردن نماد" })).not.toBeInTheDocument();
    // The page the user saw is still shown; its paging buttons cannot be used.
    for (const button of screen.queryAllByRole("button")) expect(button).toBeDisabled();
  });

  it("shows a symbol snapshot without the follow button", () => {
    const { container } = renderWithQuery(
      <ReadOnlyAssistantAnswer
        block={block({ table: undefined, symbolSnapshotResult: snapshot, suggestedQuestions: [] })}
      />,
    );

    expect(screen.getByTestId("symbol-snapshot-card")).toBeInTheDocument();
    expect(container).toHaveTextContent("فولاد مبارکه");
    expect(screen.queryByRole("button", { name: "دنبال کردن نماد" })).not.toBeInTheDocument();
  });
});
