import type { QuarterlyFinancialTableModel, QuarterlyFinancialTableRow } from "./quarterly-financial-table";
import { quarterlyPeriodHeaderLines } from "./quarterly-financial-table-header";

export const QUARTERLY_CHART_SERIES = [
  { key: "sales", label: "فروش", color: "#3b82f6", unit: "میلیون ریال" },
  { key: "grossProfit", label: "سود ناخالص", color: "#06b6d4", unit: "میلیون ریال" },
  { key: "netProfit", label: "سود خالص", color: "#10b981", unit: "میلیون ریال" },
  { key: "grossMargin", label: "مارجین ناخالص", color: "#f59e0b", unit: "٪" },
] as const;

export interface QuarterlyChartPoint {
  label: string;
  shortLabel: string;
  sales: number | null;
  grossProfit: number | null;
  netProfit: number | null;
  grossMargin: number | null;
}

function rowValues(rows: QuarterlyFinancialTableRow[], sourceItemId: number | null, grossMargin = false) {
  const row = rows.find((item) => grossMargin ? item.grossMargin : item.sourceItemId === sourceItemId);
  return row?.chartValues ?? [];
}

export function quarterlyChartPoints(model: QuarterlyFinancialTableModel): QuarterlyChartPoint[] {
  const sales = rowValues(model.rows, 15);
  const grossProfit = rowValues(model.rows, 139);
  const netProfit = rowValues(model.rows, 143);
  const grossMargin = rowValues(model.rows, null, true);
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
    };
  });
}
