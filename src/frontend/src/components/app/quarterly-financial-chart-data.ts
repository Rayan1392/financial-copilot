import type { QuarterlyFinancialTableModel, QuarterlyFinancialTableRow } from "./quarterly-financial-table";
import { quarterlyPeriodHeaderLines } from "./quarterly-financial-table-header";

export const QUARTERLY_CHART_SERIES = [
  { key: "sales", label: "فروش", color: "#3b82f6", unit: "میلیون ریال" },
  { key: "grossProfit", label: "سود ناخالص", color: "#06b6d4", unit: "میلیون ریال" },
  { key: "netProfit", label: "سود خالص", color: "#10b981", unit: "میلیون ریال" },
  { key: "grossMargin", label: "مارجین ناخالص", color: "#f59e0b", unit: "٪" },
] as const;

export const BANKING_CHART_SERIES = [
  { key: "interestIncome", label: "درآمد تسهیلات و سپرده‌گذاری", color: "#2563eb", unit: "میلیون ریال", type: "bar" },
  { key: "netInterestIncome", label: "خالص درآمد تسهیلات و سپرده‌گذاری", color: "#0891b2", unit: "میلیون ریال", type: "bar" },
  { key: "netProfit", label: "سود خالص", color: "#059669", unit: "میلیون ریال", type: "bar" },
  { key: "bankingGrossMargin", label: "مارجین ناخالص عملیات بانکی", color: "#d97706", unit: "٪", type: "line" },
  { key: "feeProfitMargin", label: "حاشیه سود کارمزد", color: "#7c3aed", unit: "٪", type: "line" },
] as const;

export interface QuarterlyChartPoint {
  label: string;
  shortLabel: string;
  sales: number | null;
  grossProfit: number | null;
  netProfit: number | null;
  grossMargin: number | null;
  interestIncome: number | null;
  netInterestIncome: number | null;
  bankingGrossMargin: number | null;
  feeProfitMargin: number | null;
}

export type QuarterlyChartSeriesKey = Exclude<keyof QuarterlyChartPoint, "label" | "shortLabel">;

function findRow(rows: QuarterlyFinancialTableRow[], sourceItemId: number | null, grossMargin = false) {
  return rows.find((item) => grossMargin
    ? item.rowId === "gross_margin" || (item.rowId == null && item.grossMargin && !item.bankingMargin)
    : item.sourceItemId === sourceItemId || item.rowId === `source_${sourceItemId}`);
}

/** The table row behind each chart series, so a chart value can be shown with the table's own cell text. */
export function quarterlyChartSeriesRows(
  model: QuarterlyFinancialTableModel,
): Partial<Record<QuarterlyChartSeriesKey, QuarterlyFinancialTableRow>> {
  if (model.templateId === "BankingIncomeStatement") {
    const bankRow = (rowId: string) => model.rows.find((row) => row.rowId === rowId);
    return {
      netProfit: findRow(model.rows, 143),
      interestIncome: bankRow("source_304"),
      netInterestIncome: bankRow("source_306"),
      bankingGrossMargin: bankRow("banking_gross_margin"),
      feeProfitMargin: bankRow("banking_fee_margin"),
    };
  }
  return {
    sales: findRow(model.rows, 15),
    grossProfit: findRow(model.rows, 139),
    netProfit: findRow(model.rows, 143),
    grossMargin: findRow(model.rows, null, true),
  };
}

export function quarterlyChartPoints(model: QuarterlyFinancialTableModel): QuarterlyChartPoint[] {
  const seriesRows = quarterlyChartSeriesRows(model);
  const values = (key: QuarterlyChartSeriesKey) => seriesRows[key]?.chartValues ?? [];
  const sales = values("sales");
  const grossProfit = values("grossProfit");
  const netProfit = values("netProfit");
  const grossMargin = values("grossMargin");
  const interestIncome = values("interestIncome");
  const netInterestIncome = values("netInterestIncome");
  const bankingGrossMargin = values("bankingGrossMargin");
  const feeProfitMargin = values("feeProfitMargin");
  return model.quarters.map((quarter, index) => {
    const labels = quarterlyPeriodHeaderLines(quarter);
    const year = labels.join(" ").match(/[۰-۹٠-٩0-9]{4}/)?.[0] ?? "";
    const shortLabel = quarter.quarterName
      ? `${quarter.quarterName} ${year}`
      : quarter.quarter != null
        ? `فصل ${"۰۱۲۳۴۵۶۷۸۹"[quarter.quarter]} ${year}`
        : labels.at(-1)?.split(/\s+/).slice(-2).join(" ") ?? labels[0];
    return {
      label: labels.join(" "),
      shortLabel,
      sales: sales[index] ?? null,
      grossProfit: grossProfit[index] ?? null,
      netProfit: netProfit[index] ?? null,
      grossMargin: grossMargin[index] ?? null,
      interestIncome: interestIncome[index] ?? null,
      netInterestIncome: netInterestIncome[index] ?? null,
      bankingGrossMargin: bankingGrossMargin[index] ?? null,
      feeProfitMargin: feeProfitMargin[index] ?? null,
    };
  });
}

/** The parser normalizes periods chronologically; preserve that order for the chart axis. */
export function quarterlyChartPointsChronologically(model: QuarterlyFinancialTableModel): QuarterlyChartPoint[] {
  return quarterlyChartPoints(model);
}
