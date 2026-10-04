import { beforeAll, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactElement } from "react";
import { MonthlyProductTrendChart, ProductTooltip } from "../monthly-product-trend-chart";
import {
  buildMonthlyProductTrendExportFileName,
  createMonthlyProductTrendChartData,
  formatProductTrendNumber,
  formatProductTrendPeriod,
  formatProductTrendRate,
  formatProductTrendRateLabel,
  PRODUCT_TREND_PANEL_LABELS,
  productTrendQuantitySeries,
  productTrendSeries,
} from "../monthly-product-trend-chart-model";
import { MessageList } from "../message-list";
import type { AssistantChatBlock, MonthlyProductTrendResult } from "@/lib/chat.functions";

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

const productTrend = {
  resultDiscriminator: "monthly_product_trend",
  resultVersion: 1,
  resolutionState: "Resolved",
  companyText: "کچاد",
  companySymbol: "کچاد",
  productTitle: "آهن اسفنجی",
  productUnit: "تن",
  points: [
    {
      period: "1404/05",
      fiscalLabel: "۱۴۰۴/۰۵",
      productKey: "product-key",
      productTitle: "آهن اسفنجی",
      productUnit: "تن",
      productionQuantity: 148_868,
      saleQuantity: 0,
      salesValueMillionRial: 0,
      salesValueBillionToman: 0,
      calculatedSaleRateToman: undefined,
      rateStatus: "ZeroQuantity",
      isGap: true,
    },
    {
      period: "1404/11",
      fiscalLabel: "۱۴۰۴/۱۱",
      productKey: "product-key",
      productTitle: "آهن اسفنجی",
      productUnit: "تن",
      productionQuantity: 147_143,
      saleQuantity: 1_360,
      salesValueMillionRial: 299_991,
      salesValueBillionToman: 29.9991,
      calculatedSaleRateToman: 22_058_161.7647,
      rateStatus: "ValidRate",
      isGap: false,
    },
  ],
  candidates: [],
  evidence: [],
} satisfies MonthlyProductTrendResult;

function renderMessageList(ui: ReactElement) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}

describe("MonthlyProductTrendChart", () => {
  it("keeps unavailable periods as rate gaps and labels only valid rates", () => {
    const chartData = createMonthlyProductTrendChartData(productTrend);

    expect(formatProductTrendPeriod("05/1404")).toBe("۱۴۰۴/۰۵");
    expect(chartData[0]?.label).toBe("۱۴۰۴/۰۵");
    expect(formatProductTrendNumber(22_058_161.7647)).toBe("۲۲٬۰۵۸٬۱۶۱٫۷۶");
    expect(formatProductTrendRateLabel("ValidRate", 22_058_161.7647)).toBe("۲۲٬۰۵۸٬۱۶۲");
    expect(buildMonthlyProductTrendExportFileName(productTrend)).toBe(
      "روند-فروش-آهن-اسفنجی-کچاد.png",
    );
    expect(chartData.map((point) => point.rate)).toEqual([null, 22_058_161.7647]);
    expect(formatProductTrendRateLabel("ZeroQuantity", undefined)).toBeNull();
    expect(formatProductTrendRateLabel("ValidZeroRate", 0)).toBe("۰");
  });

  it("uses aligned two-panel series and exposes all monthly metrics in the synchronized tooltip", () => {
    const chartData = createMonthlyProductTrendChartData(productTrend);
    render(<ProductTooltip active label={chartData[1]?.label} points={chartData} />);

    expect(screen.getByText(/تولید/u)).toBeInTheDocument();
    expect(screen.getByText(/مقدار فروش/u)).toBeInTheDocument();
    expect(screen.getByText(/مبلغ فروش/u)).toBeInTheDocument();
    expect(screen.getByText(/نرخ فروش/u)).toBeInTheDocument();
    expect(chartData.map((point) => point.label)).toEqual(["۱۴۰۴/۰۵", "۱۴۰۴/۱۱"]);
    expect(chartData[0]).toMatchObject({ production: 148_868, quantity: 0, rate: null });
    expect(chartData[1]).toMatchObject({
      production: 147_143,
      quantity: 1_360,
      rate: 22_058_161.7647,
    });
  });

  it.each([
    [22_058_161.7647, "۲۲٬۰۵۸٬۱۶۲"],
    [26_976_680.46, "۲۶٬۹۷۶٬۶۸۰"],
    [100.5, "۱۰۱"],
    [101.5, "۱۰۲"],
    [-100.5, "\u200e−۱۰۱"],
  ])("rounds only visible rate text for %s", (rate, expected) => {
    const input = {
      ...productTrend,
      points: [{ ...productTrend.points[1], calculatedSaleRateToman: rate }],
    };
    const model = createMonthlyProductTrendChartData(input);
    expect(formatProductTrendRate(model[0].rate)).toBe(expected);
    expect(model[0].rate).toBe(rate);
    expect(input.points[0].calculatedSaleRateToman).toBe(rate);
    expect(model[0].value).toBe(29.9991);
    expect(formatProductTrendNumber(29.46)).toBe("۲۹٫۴۶");
  });

  it("renders the product chart without duplicate prose, tables, detail rows, or internal statuses", () => {
    const block: AssistantChatBlock = {
      message: "| دوره | فروش |\n| --- | --- |\n| ۱۴۰۴/۱۱ | ۲۹ |\n\nZeroQuantity ValidRate",
      intent: "MonthlyProductTrend",
      replyLanguage: "fa",
      creditsUsed: 1,
      suggestedQuestions: [],
      suggestedActions: [],
      filters: [],
      citations: [],
      monthlyProductTrendResult: productTrend,
    };

    renderMessageList(
      <MessageList
        messages={[
          {
            id: "product-trend",
            role: "assistant",
            content: block,
            created_at: "2026-10-04T00:00:00Z",
          },
        ]}
        loading={false}
        streaming={false}
        onSuggested={vi.fn()}
      />,
    );

    expect(screen.getByTestId("monthly-product-trend-chart")).toBeInTheDocument();
    expect(screen.getByTestId("product-sales-rate-panel")).toBeInTheDocument();
    expect(screen.getByTestId("product-quantity-panel")).toBeInTheDocument();
    expect(screen.getByText(PRODUCT_TREND_PANEL_LABELS.main)).toBeInTheDocument();
    expect(screen.getByText(PRODUCT_TREND_PANEL_LABELS.quantity)).toBeInTheDocument();
    expect(screen.getByText(productTrendQuantitySeries().production.label)).toBeInTheDocument();
    expect(screen.getByText(productTrendQuantitySeries().quantity.label)).toBeInTheDocument();
    const salesRatePanel = within(document.body);
    const quantityPanel = within(screen.getByTestId("product-quantity-panel"));
    expect(salesRatePanel.getByText(productTrendSeries("تن").sales.label)).toBeInTheDocument();
    expect(salesRatePanel.getByText(productTrendSeries("تن").rate.label)).toBeInTheDocument();
    expect(
      quantityPanel.getByText(productTrendQuantitySeries().production.label),
    ).toBeInTheDocument();
    expect(quantityPanel.queryByText(productTrendSeries("تن").sales.label)).not.toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "روند فروش آهن اسفنجی کچاد" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "دانلود تصویر" })).toBeInTheDocument();
    expect(
      screen.getByText("مبلغ فروش به میلیارد تومان و نرخ فروش به تومان/تن"),
    ).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
    expect(screen.queryByText("ZeroQuantity")).not.toBeInTheDocument();
    expect(screen.queryByText("ValidRate")).not.toBeInTheDocument();
    expect(screen.queryByText("| دوره | فروش |")).not.toBeInTheDocument();
    expect(screen.queryByText("روند فروش آهن اسفنجی کچاد کچاد")).not.toBeInTheDocument();
  });

  it("keeps explanatory text for unresolved product results", () => {
    const unresolved = {
      ...productTrend,
      resolutionState: "Ambiguous",
      message: "چند محصول پیدا شد.",
    } satisfies MonthlyProductTrendResult;
    render(<MonthlyProductTrendChart data={unresolved} />);
    expect(screen.getByText("چند محصول پیدا شد.")).toBeInTheDocument();
  });
});
