export interface QuarterlyFinancialPeriod {
  label: string;
  year?: number;
  quarter?: number;
  sourceIndex: number;
}

function persianDigits(value: string): string {
  return value.replace(/[0-9]/g, (digit) => "۰۱۲۳۴۵۶۷۸۹"[Number(digit)]);
}

/** Use the selected report's formatted PeriodEnd label in both web and PNG. */
export function quarterlyPeriodHeaderLines(period: QuarterlyFinancialPeriod): string[] {
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
