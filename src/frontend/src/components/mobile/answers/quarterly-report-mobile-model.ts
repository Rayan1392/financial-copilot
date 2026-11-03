import type { QuarterlyFinancialTableRow } from "@/components/app/quarterly-financial-table";
import type { QuarterlyFinancialPeriod } from "@/components/app/quarterly-financial-table-header";

const PERCENT_SIGN = String.fromCharCode(0x066a);

/** Period indexes newest first. The model stores periods oldest first, with the QoQ change as the last value. */
export function periodsNewestFirst(periodCount: number): number[] {
  return Array.from({ length: periodCount }, (_, index) => periodCount - 1 - index);
}

/** "—" marks an unavailable cell; "-" is an available zero, exactly as the desktop table shows them. */
export function isMissingQuarterlyCell(value: string | undefined): boolean {
  return value === undefined || value.trim() === "" || value.trim() === "—";
}

/** The desktop cell text, with the percent sign desktop appends to margin rows. Nothing is recomputed. */
export function formatQuarterlyCell(
  row: QuarterlyFinancialTableRow,
  value: string | undefined,
): string {
  if (value === undefined || value.trim() === "") return "—";
  const margin = row.grossMargin || row.bankingMargin;
  return margin && value !== "-" && value !== "—" ? `${value}${PERCENT_SIGN}` : value;
}

/**
 * Bar widths (0–100) proportional to each period's absolute value within one line item, so a card shows the shape
 * of the series without a chart. Missing values get no bar.
 */
export function barWidthsPercent(values: ReadonlyArray<number | null>): number[] {
  const maximum = Math.max(0, ...values.map((value) => (value === null ? 0 : Math.abs(value))));
  return values.map((value) =>
    value === null || maximum === 0 ? 0 : (Math.abs(value) / maximum) * 100,
  );
}

const PERSIAN_DIGITS = "۰۱۲۳۴۵۶۷۸۹";
const toPersian = (value: number | string) =>
  String(value).replace(/[0-9]/g, (digit) => PERSIAN_DIGITS[Number(digit)]);

/**
 * Short period name for cards («فصل دوم ۱۴۰۵»). The full end date («منتهی به ۳۱ شهریور ۱۴۰۵») is listed once above
 * the cards and in the table header, so it is not repeated on every line item.
 */
export function shortPeriodLabel(period: QuarterlyFinancialPeriod, fullLines: string[]): string {
  if (period.year != null && period.quarterName)
    return `${period.quarterName} ${toPersian(period.year)}`;
  if (period.year != null && period.quarter != null)
    return `فصل ${toPersian(period.quarter)} ${toPersian(period.year)}`;
  return fullLines.join(" ");
}
