import type {
  QuarterlyChartPoint,
  QuarterlyChartSeriesKey,
} from "@/components/app/quarterly-financial-chart-data";
import type { QuarterlyFinancialPeriod } from "@/components/app/quarterly-financial-table-header";
import { toPersianDigits } from "@/lib/format/persian";

/**
 * Series colors for the phone chart. The desktop chart keeps its fixed light colors; on phones the chart sits on the
 * themed surface, so each series gets a lighter tone on dark and a darker tone on light (≥ 3:1 against the surface).
 * Hues follow the desktop series so the two charts read the same.
 */
export const QUARTERLY_TREND_SERIES_COLORS: Record<
  "light" | "dark",
  Record<QuarterlyChartSeriesKey, string>
> = {
  dark: {
    sales: "#60a5fa",
    grossProfit: "#22d3ee",
    netProfit: "#34d399",
    grossMargin: "#fbbf24",
    interestIncome: "#60a5fa",
    netInterestIncome: "#22d3ee",
    bankingGrossMargin: "#fbbf24",
    feeProfitMargin: "#c4b5fd",
  },
  light: {
    sales: "#1d4ed8",
    grossProfit: "#0e7490",
    netProfit: "#047857",
    grossMargin: "#b45309",
    interestIncome: "#1d4ed8",
    netInterestIncome: "#0e7490",
    bankingGrossMargin: "#b45309",
    feeProfitMargin: "#6d28d9",
  },
};

export interface AmountAxisScale {
  divisor: number;
  unitLabel: string;
}

/**
 * Million-rial amounts reach nine digits, which would take a third of a phone-width chart as axis ticks. The axis is
 * scaled to keep ticks at four digits or fewer; exact values stay in the detail panel, in million rials.
 */
export function amountAxisScale(maxAbsMillionRials: number): AmountAxisScale {
  if (maxAbsMillionRials >= 10_000_000)
    return { divisor: 1_000_000, unitLabel: "هزار میلیارد ریال" };
  if (maxAbsMillionRials >= 10_000) return { divisor: 1_000, unitLabel: "میلیارد ریال" };
  return { divisor: 1, unitLabel: "میلیون ریال" };
}

/** The latest period with any charted value, so the detail panel opens on the newest data. */
export function defaultSelectedPeriod(
  points: readonly QuarterlyChartPoint[],
  keys: readonly QuarterlyChartSeriesKey[],
): number {
  for (let index = points.length - 1; index >= 0; index -= 1) {
    if (keys.some((key) => typeof points[index][key] === "number")) return index;
  }
  return Math.max(points.length - 1, 0);
}

/**
 * Two short axis lines per period («فصل ۲» over «۱۴۰۵») so up to seven periods fit side by side at 320 px without
 * rotating Persian text. Periods without a quarter number split their short label at the last word.
 */
export function periodTickLines(
  period: QuarterlyFinancialPeriod,
  shortLabel: string,
): [string, string] {
  if (period.quarter != null && period.year != null)
    return [`فصل ${toPersianDigits(period.quarter)}`, toPersianDigits(period.year)];
  const split = shortLabel.lastIndexOf(" ");
  return split > 0 ? [shortLabel.slice(0, split), shortLabel.slice(split + 1)] : [shortLabel, ""];
}
