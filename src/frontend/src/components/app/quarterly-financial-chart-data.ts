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

function rowValues(rows: QuarterlyFinancialTableRow[], sourceItemId: number | null, grossMargin = false) {
  const row = rows.find((item) => grossMargin
    ? item.rowId === "gross_margin" || (item.rowId == null && item.grossMargin && !item.bankingMargin)
    : item.sourceItemId === sourceItemId || item.rowId === `source_${sourceItemId}`);
  return row?.chartValues ?? [];
}

export function quarterlyChartPoints(model: QuarterlyFinancialTableModel): QuarterlyChartPoint[] {
  const banking = model.templateId === "BankingIncomeStatement";
  const sales = banking ? [] : rowValues(model.rows, 15);
  const grossProfit = banking ? [] : rowValues(model.rows, 139);
  const netProfit = rowValues(model.rows, 143);
  const grossMargin = banking ? [] : rowValues(model.rows, null, true);
  const bankValue = (rowId: string) => model.rows.find((row) => row.rowId === rowId)?.chartValues ?? [];
  const interestIncome = banking ? bankValue("source_304") : [];
  const netInterestIncome = banking ? bankValue("source_306") : [];
  const bankingGrossMargin = banking ? bankValue("banking_gross_margin") : [];
  const feeProfitMargin = banking ? bankValue("banking_fee_margin") : [];
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
