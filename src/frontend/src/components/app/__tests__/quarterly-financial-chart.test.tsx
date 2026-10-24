import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, waitFor } from "@testing-library/react";
import { parseQuarterlyFinancialTable, QuarterlyFinancialTable } from "../quarterly-financial-table";
import { BANKING_CHART_SERIES, quarterlyChartPoints, quarterlyChartPointsChronologically } from "../quarterly-financial-chart-data";

const body = [
  "فروش", "بهای تمام‌شده کالای فروش‌رفته", "سود (زیان) ناخالص", "مارجین ناخالص",
  "هزینه‌های عمومی، اداری و تشکیلاتی", "خالص سایر درآمدها (هزینه‌ها)ی عملیاتی",
  "سود (زیان) عملیاتی", "هزینه‌های مالی", "خالص سایر درآمدها و هزینه‌های غیرعملیاتی",
  "سود (زیان) عملیات در حال تداوم قبل از مالیات", "مالیات",
  "سود (زیان) عملیات متوقف‌شده پس از اثر مالیاتی",
  "سود (زیان) خالص", "سود هر سهم پس از کسر مالیات", "تعداد سهام شرکت (هزار سهم)",
];

function model() {
  const rows = body.map((label, index) => {
    const values: Record<number, string[]> = {
      0: ["100", "200", "300", "400", "500"],
      2: ["20", "40", "-", "80", "100"],
      3: ["20", "20", "—", "20", "20"],
      12: ["-10", "—", "0", "30", "40"],
    };
    return `| ${label} | ${(values[index] ?? ["1", "1", "1", "1", "1"]).join(" | ")} | - |`;
  });
  return parseQuarterlyFinancialTable([
    "### جدول گزارش فصلی شغدیر",
    "| شرح | سال مالی 1405، فصل 1 | سال مالی 1404، فصل 4 | سال مالی 1404، فصل 3 | سال مالی 1404، فصل 2 | سال مالی 1404، فصل 1 | تغییر فصل‌به‌فصل |",
    "|---|---:|---:|---:|---:|---:|---:|",
    ...rows,
  ].join("\n"))!.model;
}

beforeEach(() => vi.stubGlobal("ResizeObserver", class {
  observe() {}
  unobserve() {}
  disconnect() {}
}));
afterEach(() => vi.unstubAllGlobals());

describe("Feature 144 comparison chart", () => {
  it("accepts the configured 1–12 quarter range", () => {
    for (const count of [1, 12]) {
      const headers = Array.from({ length: count }, (_, index) => `سال مالی ${1403 + Math.floor(index / 4)}، فصل ${(index % 4) + 1}`);
      const markdown = [
        "### جدول گزارش فصلی شغدیر",
        `| شرح | ${headers.join(" | ")} | تغییر فصل‌به‌فصل |`,
        `|---|${headers.map(() => "---:").join("|")}|---:|`,
        ...body.map((label) => `| ${label} | ${headers.map(() => "1").join(" | ")} | - |`),
      ].join("\n");
      const parsed = parseQuarterlyFinancialTable(markdown);
      expect(parsed?.model.quarters).toHaveLength(count);
      expect(quarterlyChartPoints(parsed!.model)).toHaveLength(count);
    }
  });

  it("keeps table order, reported zero, missing data, and negative profit", () => {
    const points = quarterlyChartPoints(model());
    expect(points.map((point) => point.sales)).toEqual([500, 400, 300, 200, 100]);
    expect(points.map((point) => point.grossProfit)).toEqual([100, 80, 0, 40, 20]);
    expect(points.map((point) => point.grossMargin)).toEqual([20, 20, null, 20, 20]);
    expect(points.map((point) => point.netProfit)).toEqual([40, 30, 0, null, -10]);
  });

  it("feeds the web chart from oldest on the left to newest on the right", () => {
    const points = quarterlyChartPointsChronologically(model());
    expect(points.map((point) => point.sales)).toEqual([500, 400, 300, 200, 100]);
  });

  it("defines three banking amount bars and two percentage lines, omitting deposit interest expense", () => {
    expect(BANKING_CHART_SERIES.filter((item) => item.type === "bar")).toHaveLength(3);
    expect(BANKING_CHART_SERIES.filter((item) => item.type === "line")).toHaveLength(2);
    const bankModel = {
      ...model(), templateId: "BankingIncomeStatement",
      rows: [
        { rowId: "source_304", chartValues: [10, 20, 30, 40, 50] },
        { rowId: "source_305", chartValues: [-2, -3, -4, -5, -6] },
        { rowId: "source_306", chartValues: [8, 17, 26, 35, 44] },
        { rowId: "source_194", chartValues: [100, 200, 300, 400, 500] },
        { rowId: "banking_operating_income", chartValues: [111, 222, 333, 444, 555] },
        { rowId: "banking_gross_margin", chartValues: [80, null, 86, 87.5, 88] },
        { rowId: "banking_fee_margin", chartValues: [60, 61, 62, 63, 64] },
      ],
    };
    const points = quarterlyChartPointsChronologically(bankModel as ReturnType<typeof model>);
    expect(points.map((point) => point.interestIncome)).toEqual([10, 20, 30, 40, 50]);
    expect(points[0]).not.toHaveProperty("depositCost");
    expect(points[0]).not.toHaveProperty("otherOperatingIncome");
    expect(points[0]).not.toHaveProperty("bankingOperatingIncome");
    expect(points.map((point) => point.bankingGrossMargin)).toEqual([80, null, 86, 87.5, 88]);
  });

  it("shows the chart only when the API effective setting is true", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: true, json: async () => ({ quarterlyFinancialTable: { showChart: true } }) }));
    const enabled = render(<QuarterlyFinancialTable model={model()} />);
    await waitFor(() => expect(enabled.getByRole("img", { name: /نمودار فروش/ })).toBeTruthy());
    enabled.unmount();

    vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: true, json: async () => ({ quarterlyFinancialTable: { showChart: false } }) }));
    const disabled = render(<QuarterlyFinancialTable model={model()} />);
    await waitFor(() => expect(fetch).toHaveBeenCalled());
    expect(disabled.queryByRole("img", { name: /نمودار فروش/ })).toBeNull();
  });
});
