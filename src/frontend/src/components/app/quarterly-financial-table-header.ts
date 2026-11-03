export interface QuarterlyFinancialPeriod {
  label: string;
  quarterName?: string;
  dateLabel?: string;
  year?: number;
  quarter?: number;
  sourceIndex: number;
}

function persianDigits(value: string): string {
  return value.replace(/[0-9]/g, (digit) => "۰۱۲۳۴۵۶۷۸۹"[Number(digit)]);
}

/** Use the selected report's formatted PeriodEnd label in both web and PNG. */
export function quarterlyPeriodHeaderLines(period: QuarterlyFinancialPeriod): string[] {
  if (period.quarterName && period.dateLabel) {
    return [period.quarterName, "\u0645\u0646\u062a\u0647\u06cc \u0628\u0647", period.dateLabel];
  }
  const label = persianDigits(period.label);
  const date = label.match(/^(دوره منتهی به)\s+(.+)$/);
  if (date) return [date[1], date[2]];
  if (period.year != null && period.quarter != null) {
    return [
      `سال مالی ${persianDigits(String(period.year))}`,
      `فصل ${persianDigits(String(period.quarter))}`,
    ];
  }
  return [label];
}
