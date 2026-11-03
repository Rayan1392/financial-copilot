import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import type { MonthlyProductTrendResult } from "@/lib/chat.functions";
import { MonthlyProductTrendChart } from "../monthly-product-trend-chart";

// Supply deterministic chart dimensions and an active tooltip; use the real chart,
// labels and tooltip content so formatting regressions are visible in rendered text.
vi.mock("recharts", async (importOriginal) => {
  const original = await importOriginal<typeof import("recharts")>();
  const { cloneElement } = await import("react");
  return {
    ...original,
    ResponsiveContainer: ({ children }: { children: React.ReactElement }) =>
      cloneElement(children as React.ReactElement<{ width: number; height: number }>, {
        width: 900,
        height: 400,
      }),
    Tooltip: ({ content }: { content: React.ReactElement }) =>
      cloneElement(content as React.ReactElement<Record<string, unknown>>, {
        active: true,
        payload: [
          { payload: { label: "۱۴۰۴/۱۱", rate: 22_058_161.7647, value: 29.46, unit: "تن" } },
        ],
      }),
  };
});

describe("product sale-rate display", () => {
  it("renders integer rate point/axis labels and tooltip text while preserving sales decimals", () => {
    const data: MonthlyProductTrendResult = {
      resultDiscriminator: "monthly_product_trend",
      resultVersion: 1,
      resolutionState: "Resolved",
      companyText: "کچاد",
      companySymbol: "کچاد",
      productTitle: "آهن اسفنجی",
      productUnit: "تن",
      candidates: [],
      evidence: [],
      points: [
        {
          period: "1404/11",
          fiscalLabel: "1404/11",
          productKey: "product-key",
          productTitle: "آهن اسفنجی",
          productUnit: "تن",
          salesValueBillionToman: 29.46,
          calculatedSaleRateToman: 22_058_161.7647,
          rateStatus: "ValidRate",
          isGap: false,
        },
      ],
    };
    const { container } = render(<MonthlyProductTrendChart data={data} />);
    expect(screen.getByText("نرخ فروش: ۲۲٬۰۵۸٬۱۶۲ تومان/تن")).toBeInTheDocument();
    expect(screen.getByText("مبلغ فروش: ۲۹٫۴۶ میلیارد تومان")).toBeInTheDocument();
    expect(screen.getAllByText("نرخ فروش (تومان/تن)")).toHaveLength(2);
    expect(screen.getByText("مبلغ فروش")).toBeInTheDocument();
    expect(screen.queryByText("نرخ فروش (تن)")).not.toBeInTheDocument();
    expect(container.querySelector("[aria-label='راهنمای نمودار']")).toBeInTheDocument();
    expect(container.querySelector(".h-60")).toBeInTheDocument();
    expect(container.querySelector(".h-52")).toBeInTheDocument();
    expect(container.querySelector(".h-72")).not.toBeInTheDocument();
    const salesRatePanel = container.querySelector('[data-testid="product-sales-rate-panel"]')!;
    const quantityPanel = container.querySelector('[data-testid="product-quantity-panel"]')!;
    expect(salesRatePanel.querySelectorAll(".recharts-bar")).toHaveLength(1);
    expect(salesRatePanel.querySelectorAll(".recharts-line")).toHaveLength(1);
    expect(quantityPanel.querySelectorAll(".recharts-bar")).toHaveLength(0);
    expect(quantityPanel.querySelectorAll(".recharts-line")).toHaveLength(2);
    expect(quantityPanel.querySelectorAll(".recharts-label-list")).toHaveLength(0);
    const seriesLegend = container.querySelector("[aria-label='راهنمای نمودار']")!;
    expect(seriesLegend.querySelector("svg rect")?.getAttribute("fill")).toBe("#4f9f82");
    expect(seriesLegend.querySelector("svg path")?.getAttribute("stroke")).toBe("#f59e0b");
    const plotGrid = container.querySelector(".recharts-cartesian-grid-horizontal line")!;
    expect(Number(plotGrid.getAttribute("width"))).toBeGreaterThan(700);
    expect(Number(plotGrid.getAttribute("height"))).toBe(370);
    expect(container.querySelector(".recharts-line .recharts-label-list")?.textContent).toBe(
      "۲۲٬۰۵۸٬۱۶۲",
    );
    const rateTicks = container
      .querySelector(".yAxis")!
      .querySelectorAll(".recharts-cartesian-axis-tick-value");
    expect(rateTicks.length).toBeGreaterThan(0);
    for (const tick of rateTicks) expect(tick.textContent).not.toMatch(/[.٫]/u);
    expect(data.points[0].calculatedSaleRateToman).toBe(22_058_161.7647);
  });
});
