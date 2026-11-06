import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import type { MonthlyActivityTrendResult, MonthlyProductTrendResult } from "@/lib/chat.functions";
import { createMonthlyTrendSummaryCards } from "../monthly-activity-trend-chart-view-model";
import { MonthlyTrendSummaryCards } from "../monthly-trend-summary-cards";
import { AnalysisProgress } from "../analysis-progress";
import { analysisStageAt } from "../analysis-stages";
import { AnswerActions } from "../answer-actions";
import { MonthlyProductTrendChart } from "../monthly-product-trend-chart";
import { hasProductionData, PRODUCTION_UNAVAILABLE_MESSAGE } from "../monthly-product-trend-chart-model";

vi.mock("../monthly-product-trend-chart-image", () => ({
  downloadMonthlyProductTrendChartImage: vi.fn(),
}));

beforeAll(() => {
  vi.stubGlobal(
    "ResizeObserver",
    class {
      observe() {}
      unobserve() {}
      disconnect() {}
    },
  );
});

afterEach(() => vi.useRealTimers());

const trend = {
  companySymbol: "کگهر",
  latestReportYear: 1405,
  latestReportMonth: 7,
  unitLabelFa: "میلیارد تومان",
  latestMonthlySalesAmount: 9_216.639,
  previousMonthSalesAmount: 15_416.2,
  salesAmountMomGrowthPercent: -40.21,
  sameMonthPreviousYearSalesAmount: 8_980.08,
  salesAmountYoYGrowthPercent: 2.63,
  average12MonthSalesAmount: 10_500,
  chartPoints: [],
  insights: [],
  missingDataPoints: [],
  sourceProviderName: "نوآوران امین",
  calculatedAtUtc: "2026-11-06T00:00:00Z",
} satisfies MonthlyActivityTrendResult;

describe("monthly sales summary cards", () => {
  it("lifts the four headline figures out of the paragraph, with direction and Persian number style", () => {
    const cards = createMonthlyTrendSummaryCards(trend);

    expect(cards.map((card) => [card.id, card.value, card.direction, card.caption])).toEqual([
      ["latestMonth", "۹٬۲۱۶٫۶", null, "مهر ۱۴۰۵"],
      ["monthOverMonth", "۴۰٫۲٪", "down", "ماه قبل: ۱۵٬۴۱۶٫۲ میلیارد تومان"],
      ["yearOverYear", "۲٫۶٪", "up", "ماه مشابه: ۸٬۹۸۰٫۱ میلیارد تومان"],
      ["vsAverage", "—", null, "میانگین: ۱۰٬۵۰۰ میلیارد تومان"],
    ]);
    expect(cards[1].accessibleValue).toBe("کاهش ۴۰٫۲ درصد");
  });

  it("says a comparison is unavailable instead of showing zero (answers stored before the MoM field)", () => {
    const { previousMonthSalesAmount: _p, salesAmountMomGrowthPercent: _m, ...older } = trend;
    const monthOverMonth = createMonthlyTrendSummaryCards(older)[1];
    expect(monthOverMonth).toMatchObject({ value: "—", direction: null, caption: "داده ماه قبل موجود نیست" });
  });

  it("renders four labelled cards and nothing when the answer has no headline figures", () => {
    const { container, rerender } = render(<MonthlyTrendSummaryCards data={trend} />);
    const list = screen.getByRole("list", { name: "خلاصه فروش ماهانه" });
    expect(within(list).getAllByRole("listitem")).toHaveLength(4);
    expect(screen.getByLabelText("افزایش ۲٫۶ درصد")).toBeInTheDocument();

    rerender(
      <MonthlyTrendSummaryCards
        data={{ ...trend, latestMonthlySalesAmount: undefined, salesAmountMomGrowthPercent: undefined, salesAmountYoYGrowthPercent: undefined }}
      />,
    );
    expect(container).toBeEmptyDOMElement();
  });
});

describe("analysis progress", () => {
  it("advances through the stages and waits on the last one", () => {
    expect(analysisStageAt(0)).toBe(0);
    expect(analysisStageAt(3_000)).toBe(1);
    expect(analysisStageAt(60_000)).toBe(2);

    vi.useFakeTimers();
    const { container } = render(<AnalysisProgress />);
    const state = () => [...container.querySelectorAll("li")].map((item) => item.dataset.state);
    expect(state()).toEqual(["current", "pending", "pending"]);
    act(() => vi.advanceTimersByTime(6_500));
    expect(state()).toEqual(["done", "done", "current"]);
    expect(container.querySelector("img[src='/sapio-logo.png']")).not.toBeNull();
  });
});

describe("answer actions", () => {
  it("copies the answer as plain text and repeats the question only when allowed", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, "clipboard", { value: { writeText }, configurable: true });
    const onRegenerate = vi.fn();
    const { rerender } = render(<AnswerActions text={"**فروش:** ۱۲٬۰۰۰"} onRegenerate={onRegenerate} />);

    await act(async () => fireEvent.click(screen.getByRole("button", { name: "کپی" })));
    expect(writeText).toHaveBeenCalledWith("فروش: ۱۲٬۰۰۰");
    expect(screen.getByRole("button", { name: "کپی شد" })).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "پاسخ دوباره" }));
    expect(onRegenerate).toHaveBeenCalledTimes(1);

    rerender(<AnswerActions text="متن" onRegenerate={onRegenerate} disabled />);
    expect(screen.getByRole("button", { name: "پاسخ دوباره" })).toBeDisabled();
    rerender(<AnswerActions text="متن" />);
    expect(screen.queryByRole("button", { name: "پاسخ دوباره" })).not.toBeInTheDocument();
  });
});

describe("product chart without production data", () => {
  const point = {
    period: "1404/11",
    fiscalLabel: "۱۴۰۴/۱۱",
    productKey: "p",
    productTitle: "کالا",
    productUnit: "تن",
    productionQuantity: 0,
    saleQuantity: 1_360,
    salesValueMillionRial: 299_991,
    salesValueBillionToman: 29.9991,
    calculatedSaleRateToman: 22_058_161.7647,
    rateStatus: "ValidRate",
    isGap: false,
  };
  const product = {
    resultDiscriminator: "monthly_product_trend",
    resultVersion: 1,
    resolutionState: "Resolved",
    companyText: "شرکت",
    companySymbol: "نماد",
    productTitle: "کالا",
    productUnit: "تن",
    points: [point, { ...point, period: "1404/12", fiscalLabel: "۱۴۰۴/۱۲", productionQuantity: undefined }],
    candidates: [],
    evidence: [],
  } satisfies MonthlyProductTrendResult;

  it("treats an all-zero production series as unavailable and says so instead of drawing it", () => {
    expect(hasProductionData([{ production: 0 }, { production: null }])).toBe(false);
    expect(hasProductionData([{ production: 0 }, { production: 12 }])).toBe(true);

    render(<MonthlyProductTrendChart data={product} />);
    expect(screen.getByText(PRODUCTION_UNAVAILABLE_MESSAGE)).toBeInTheDocument();
    const quantityLegend = screen.getByRole("list", { name: "راهنمای مقادیر" });
    expect(within(quantityLegend).queryByText("تولید")).not.toBeInTheDocument();
    expect(within(quantityLegend).getByText("مقدار فروش")).toBeInTheDocument();
  });
});
