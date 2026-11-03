import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MessageList } from "@/components/app/message-list";
import { quarterlyFinancialTableModelFromTypedResult } from "@/components/app/quarterly-financial-table";
import { formatQuarterlyCell } from "../quarterly-report-mobile-model";
import type {
  AssistantChatBlock,
  ChatMessage,
  QuarterlyFinancialReportTableResult,
} from "@/lib/chat.functions";
// The same representative answers the phone browser checks use.
import {
  monthlySalesTrendResult,
  quarterlyReportResult,
} from "../../../../../scripts/mobile-workspace/answer-fixtures.mjs";

const quarterly = quarterlyReportResult as unknown as QuarterlyFinancialReportTableResult;

function answer(extra: Partial<AssistantChatBlock>): ChatMessage {
  return {
    id: crypto.randomUUID(),
    role: "assistant",
    created_at: "",
    content: {
      message: "پاسخ",
      intent: "Unknown",
      creditsUsed: 1,
      suggestedQuestions: [],
      suggestedActions: [],
      filters: [],
      citations: [],
      ...extra,
    },
  };
}

function renderAnswers(density: "default" | "compact") {
  return render(
    <QueryClientProvider client={new QueryClient()}>
      <MessageList
        density={density}
        loading={false}
        streaming={false}
        onSuggested={vi.fn()}
        messages={[
          answer({
            monthlyActivityTrendResult:
              monthlySalesTrendResult as AssistantChatBlock["monthlyActivityTrendResult"],
          }),
          answer({ financialStatementTableResult: { quarterlyResult: quarterly } }),
        ]}
      />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  // The quarterly chart switch comes from the UI config endpoint; unavailable here, so no chart.
  vi.stubGlobal(
    "fetch",
    vi.fn(async () => Promise.reject(new Error("offline"))),
  );
  // Recharts' ResponsiveContainer needs ResizeObserver, which jsdom lacks; the chart itself is not measured here.
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

describe("monthly sales trend on phones", () => {
  it("opens on the latest reported month and steps through months with the buttons", () => {
    renderAnswers("compact");
    const panel = screen.getByText(/· ماه ۷ از ۱۲/).closest("[aria-live]") as HTMLElement;
    expect(panel).toHaveTextContent("مهر");
    expect(panel).toHaveTextContent("مهر ۱۴۰۵");
    expect(panel).toHaveTextContent("۵۸,۴۳۰.۹ میلیارد ریال");
    expect(panel).toHaveTextContent("تغییر نسبت به ماه مشابه سال قبل");

    fireEvent.click(screen.getByRole("button", { name: "ماه قبل" }));
    expect(panel).toHaveTextContent("شهریور");
    fireEvent.click(screen.getByRole("button", { name: "ماه بعد" }));
    fireEvent.click(screen.getByRole("button", { name: "ماه بعد" }));
    // آبان ۱۴۰۵ is not reported yet: shown as such, never as zero.
    expect(panel).toHaveTextContent("آبان");
    expect(within(panel).getAllByText("گزارش نشده").length).toBe(1);
  });

  it("keeps every legend comparison, insight, missing-data note, unit and source", () => {
    renderAnswers("compact");
    expect(screen.getByText(/۱۴۰۵: .*٪ از ۱۴۰۴\)/, { selector: "li span" })).toBeInTheDocument();
    expect(screen.getByText("میانگین ۱۲ ماهه", { selector: "li span" })).toBeInTheDocument();
    expect(screen.getByText("واحد: میلیارد ریال")).toBeInTheDocument();
    expect(screen.getByText(/گزارش مرداد ۱۴۰۴ منتشر نشده است/)).toBeInTheDocument();
    expect(screen.getByText(/منبع: نوآوران امین/)).toBeInTheDocument();
    // All 12 months are in the screen-reader table.
    const srTable = document.querySelector("table.sr-only") as HTMLTableElement;
    expect(srTable.tBodies[0].rows).toHaveLength(12);
  });

  it("desktop keeps the original chart card", () => {
    renderAnswers("default");
    expect(screen.queryByRole("button", { name: "ماه قبل" })).not.toBeInTheDocument();
  });
});

describe("quarterly report on phones", () => {
  const model = quarterlyFinancialTableModelFromTypedResult(quarterly);
  const periods = model.quarters.length;

  it("shows one card per line item with the change badge and latest two periods", () => {
    renderAnswers("compact");
    const cards = within(screen.getByRole("list", { name: "اقلام گزارش فصلی" })).getAllByRole(
      "listitem",
    );
    expect(cards).toHaveLength(model.rows.length);
    const sales = cards[0];
    expect(sales).toHaveTextContent("فروش");
    expect(sales).toHaveTextContent(model.rows[0].values[periods - 1]);
    expect(sales).toHaveTextContent(model.rows[0].values[periods - 2]);
    expect(
      within(sales).getByText(/درصد تغییر به فصل قبل:/, { selector: ".sr-only" }),
    ).toBeInTheDocument();
    // Every period's exact end date is listed once.
    const list = screen.getByLabelText("دوره‌های گزارش");
    expect(list).toHaveTextContent("منتهی به");
    expect(within(list).getAllByRole("term")).toHaveLength(periods);
  });

  it("expanding a card lists every period", () => {
    renderAnswers("compact");
    fireEvent.click(screen.getAllByRole("button", { name: /همه فصل‌ها/ })[0]);
    const sales = within(screen.getByRole("list", { name: "اقلام گزارش فصلی" })).getAllByRole(
      "listitem",
    )[0];
    for (const value of model.rows[0].values.slice(0, periods))
      expect(sales).toHaveTextContent(value);
  });

  it("the table view contains every value, change and period of the desktop table", () => {
    renderAnswers("compact");
    fireEvent.click(screen.getByRole("button", { name: "جدول", exact: true } as never));
    const region = screen.getByRole("region", { name: /جدول کامل گزارش فصلی/ });
    expect(region).toHaveAttribute("tabindex", "0");
    const table = within(region).getByRole("table");
    const bodyRows = table.querySelectorAll("tbody tr");
    expect(bodyRows).toHaveLength(model.rows.length);
    model.rows.forEach((row, index) => {
      const cells = bodyRows[index];
      expect(cells).toHaveTextContent(row.label);
      for (const value of row.values.slice(0, periods)) {
        expect(cells).toHaveTextContent(formatQuarterlyCell(row, value));
      }
    });
    expect(table.querySelectorAll("thead th")).toHaveLength(periods + 2);
  });

  it("desktop keeps the original fixed table", () => {
    const { container } = renderAnswers("default");
    expect(screen.queryByRole("list", { name: "اقلام گزارش فصلی" })).not.toBeInTheDocument();
    expect(container.querySelector("table.table-fixed")).not.toBeNull();
  });
});
