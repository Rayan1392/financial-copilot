import type {
  MonthlyActivityTrendChartPoint,
  MonthlyActivityTrendResult,
} from "@/lib/chat.functions";
import { formatNumber, toPersianDigits, toPersianNumerals } from "@/lib/format/persian";

export type MonthlyTrendChartTheme = "light" | "dark";

export type MonthlyTrendExportPalette = {
  currentYear: string;
  previousYear: string;
  average: string;
  foreground: string;
  mutedForeground: string;
  grid: string;
  surface: string;
  watermark: string;
  positive: string;
  negative: string;
};

export type MonthlyTrendExplanationLine = {
  beforeValue: string;
  valueLabel: string | null;
  afterValue: string;
  tone: "positive" | "negative" | "neutral";
};

export type MonthlyTrendChartPointViewModel = {
  fiscalMonthIndex: number;
  fiscalMonthLabel: string;
  currentYear: number | null;
  previousYear: number | null;
  average: number | null;
  currentYearValueLabel: string | null;
  previousYearValueLabel: string | null;
  averageValueLabel: string | null;
  isCurrentYearReported: boolean;
  isPreviousYearReported: boolean;
};

/**
 * One legend row in parts, so a screen renders the year, the total and the share as separate bidi-isolated runs
 * instead of one mixed string such as «۱۴۰۴: ۸۲٬۶۴۲ (…)», which the bidi algorithm reorders.
 */
export type MonthlyTrendLegendEntry = {
  key: "previousYear" | "currentYear" | "average";
  kind: "bar" | "line";
  /** Short series name: the fiscal year, or «میانگین ۱۲ ماهه». Also the chart series name shown in tooltips. */
  label: string;
  /** Sum of the reported months of that year, already formatted. */
  totalLabel: string | null;
  /** Current year only: its total as a share of the previous year's total, e.g. «۸۹٫۸٪ از ۱۴۰۴». */
  note: string | null;
  color: string;
};

export type MonthlyTrendChartCardViewModel = {
  title: string;
  companyLabel: string;
  unitLabel: string;
  /** Single-string legends for the PNG export, which draws each entry as one text run. */
  currentYearLegend: string;
  previousYearLegend: string;
  averageLegend: string;
  legendEntries: MonthlyTrendLegendEntry[];
  points: MonthlyTrendChartPointViewModel[];
  explanationLines: MonthlyTrendExplanationLine[];
  sourceContext: string;
  calculationContext: string;
  palette: MonthlyTrendExportPalette;
};

const jalaliMonthNames = [
  "فروردین",
  "اردیبهشت",
  "خرداد",
  "تیر",
  "مرداد",
  "شهریور",
  "مهر",
  "آبان",
  "آذر",
  "دی",
  "بهمن",
  "اسفند",
] as const;

/**
 * Brand palette (emerald and gold): the current year is strong emerald, the previous year a neutral gray, and the
 * 12-month average gold. A familiar "this year vs last year" convention that keeps the eye on the current year.
 */
const exportPalettes: Record<MonthlyTrendChartTheme, MonthlyTrendExportPalette> = {
  dark: {
    currentYear: "#34d399",
    previousYear: "#71717a",
    average: "#fbbf24",
    foreground: "#ffffff",
    mutedForeground: "#e4e4e7",
    grid: "rgba(255,255,255,0.22)",
    surface: "#24242a",
    watermark: "rgba(244,244,245,0.22)",
    positive: "#34d399",
    negative: "#fb7185",
  },
  light: {
    currentYear: "#047857",
    previousYear: "#a1a1aa",
    average: "#b45309",
    foreground: "#18181b",
    mutedForeground: "#3f3f46",
    grid: "rgba(63,63,70,0.28)",
    surface: "#fafafa",
    watermark: "rgba(39,39,42,0.18)",
    positive: "#047857",
    negative: "#be123c",
  },
};

const AVERAGE_LABEL = "میانگین ۱۲ ماهه";

export function createMonthlyTrendChartCardViewModel(
  data: MonthlyActivityTrendResult,
  theme: MonthlyTrendChartTheme = "dark",
): MonthlyTrendChartCardViewModel {
  const palette = exportPalettes[theme];
  const points = data.chartPoints.map((point) => mapPoint(point));
  const currentYear = data.chartPoints.find(
    (point) => point.currentFiscalYear != null,
  )?.currentFiscalYear;
  const previousYear = data.chartPoints.find(
    (point) => point.previousFiscalYear != null,
  )?.previousFiscalYear;
  const currentYearTotal = sumReportedSales(points, "currentYear");
  const previousYearTotal = sumReportedSales(points, "previousYear");
  const companyLabel = data.companyName
    ? `${data.companyName} (${data.companySymbol})`
    : data.companySymbol;
  const sourceContext = `منبع: ${data.sourceProviderName}`;
  const calculationContext = `محاسبه: ${formatJalaliDateTime(data.calculatedAtUtc)}`;

  const previousYearLabel = yearLabel(previousYear, "سال قبل");
  const currentYearLabel = yearLabel(currentYear, "سال جاری");
  const legendEntries: MonthlyTrendLegendEntry[] = [
    {
      key: "previousYear",
      kind: "bar",
      label: previousYearLabel,
      totalLabel: formatBarAmount(previousYearTotal),
      note: null,
      color: palette.previousYear,
    },
    {
      key: "currentYear",
      kind: "bar",
      label: currentYearLabel,
      totalLabel: formatBarAmount(currentYearTotal),
      note: shareOfPreviousYear(currentYearTotal, previousYearTotal, previousYearLabel),
      color: palette.currentYear,
    },
    {
      key: "average",
      kind: "line",
      label: AVERAGE_LABEL,
      totalLabel: null,
      note: null,
      color: palette.average,
    },
  ];

  return {
    title: "روند فروش ماهانه",
    companyLabel,
    unitLabel: data.unitLabelFa,
    currentYearLegend: legendText(legendEntries[1]),
    previousYearLegend: legendText(legendEntries[0]),
    averageLegend: AVERAGE_LABEL,
    legendEntries,
    points,
    explanationLines: [
      ...data.insights.map((insight) => formatInsightLine(data, insight.kind, insight.textFa)),
      ...data.missingDataPoints.map((point) => neutralExplanationLine(`⚠ ${point.reasonFa}`)),
    ],
    sourceContext,
    calculationContext,
    palette,
  };
}

export type MonthlyTrendSummaryCard = {
  id: "latestMonth" | "monthOverMonth" | "yearOverYear" | "vsAverage";
  label: string;
  /** The large figure: an amount for the latest month, a percentage for the comparisons. «—» when unavailable. */
  value: string;
  unit: string | null;
  direction: "up" | "down" | "flat" | null;
  tone: "positive" | "negative" | "neutral";
  /** Spoken form of the figure, e.g. «افزایش ۷۴٫۱ درصد». */
  accessibleValue: string;
  caption: string;
};

/**
 * The four headline figures of a monthly sales answer — latest month, month-over-month, year-over-year and the
 * 12-month average — as small cards, so they are not buried in one dense paragraph. All figures come from the API
 * result; nothing is recomputed here.
 */
export function createMonthlyTrendSummaryCards(
  data: MonthlyActivityTrendResult,
): MonthlyTrendSummaryCard[] {
  const unit = data.unitLabelFa;
  const amountWithUnit = (value: number | undefined) =>
    value == null ? null : `${formatMonthlyTrendAmount(value)} ${unit}`;
  const latestAmount = data.latestMonthlySalesAmount;
  const previousMonth = amountWithUnit(data.previousMonthSalesAmount);
  const sameMonthLastYear = amountWithUnit(data.sameMonthPreviousYearSalesAmount);
  const average = amountWithUnit(data.average12MonthSalesAmount);

  return [
    {
      id: "latestMonth",
      label: "فروش آخرین ماه",
      value: latestAmount == null ? "—" : formatMonthlyTrendAmount(latestAmount),
      unit: latestAmount == null ? null : unit,
      direction: null,
      tone: "neutral",
      accessibleValue: amountWithUnit(latestAmount) ?? "گزارش نشده",
      caption: latestPeriodLabel(data.latestReportYear, data.latestReportMonth),
    },
    changeCard(
      "monthOverMonth",
      "تغییر ماهانه",
      data.salesAmountMomGrowthPercent,
      previousMonth ? `ماه قبل: ${previousMonth}` : "داده ماه قبل موجود نیست",
    ),
    changeCard(
      "yearOverYear",
      "نسبت به سال قبل",
      data.salesAmountYoYGrowthPercent,
      sameMonthLastYear ? `ماه مشابه: ${sameMonthLastYear}` : "ماه مشابه سال قبل گزارش نشده",
    ),
    changeCard(
      "vsAverage",
      "نسبت به میانگین ۱۲ ماهه",
      data.salesVsAverage12MonthPercent,
      average ? `میانگین: ${average}` : "میانگین قابل محاسبه نیست",
    ),
  ];
}

/** Whether the answer carries enough headline figures for the cards to add anything over the chart. */
export function hasMonthlyTrendSummary(data: MonthlyActivityTrendResult): boolean {
  return (
    data.latestMonthlySalesAmount != null ||
    data.salesAmountMomGrowthPercent != null ||
    data.salesAmountYoYGrowthPercent != null ||
    data.salesVsAverage12MonthPercent != null
  );
}

function changeCard(
  id: MonthlyTrendSummaryCard["id"],
  label: string,
  percent: number | undefined,
  caption: string,
): MonthlyTrendSummaryCard {
  if (percent == null || !Number.isFinite(percent)) {
    return {
      id,
      label,
      value: "—",
      unit: null,
      direction: null,
      tone: "neutral",
      accessibleValue: "قابل محاسبه نیست",
      caption,
    };
  }
  const rounded = Math.round(percent * 10) / 10;
  const magnitude = formatPercentMagnitude(rounded);
  const spoken = magnitude.replace("٪", " درصد");
  const direction = rounded > 0 ? "up" : rounded < 0 ? "down" : "flat";
  return {
    id,
    label,
    value: magnitude,
    unit: null,
    direction,
    tone: direction === "up" ? "positive" : direction === "down" ? "negative" : "neutral",
    accessibleValue:
      direction === "up"
        ? `افزایش ${spoken}`
        : direction === "down"
          ? `کاهش ${spoken}`
          : "بدون تغییر",
    caption,
  };
}

function latestPeriodLabel(year: number, month: number): string {
  const monthName = jalaliMonthNames[month - 1];
  return monthName ? `${monthName} ${toPersianDigits(year)}` : toPersianDigits(`${year}/${month}`);
}

function mapPoint(point: MonthlyActivityTrendChartPoint): MonthlyTrendChartPointViewModel {
  const currentYear = point.isCurrentYearReported
    ? (point.currentFiscalYearSalesAmount ?? null)
    : null;
  const previousYear = point.isPreviousYearReported
    ? (point.previousFiscalYearSalesAmount ?? null)
    : null;
  const average = point.average12MonthSalesAmount ?? null;

  return {
    fiscalMonthIndex: point.fiscalMonthIndex,
    fiscalMonthLabel: jalaliMonthNames[point.fiscalMonthIndex - 1] ?? point.fiscalMonthNameFa,
    currentYear,
    previousYear,
    average,
    currentYearValueLabel: formatBarAmount(currentYear),
    previousYearValueLabel: formatBarAmount(previousYear),
    averageValueLabel: formatBarAmount(average),
    isCurrentYearReported: point.isCurrentYearReported,
    isPreviousYearReported: point.isPreviousYearReported,
  };
}

/** Exact amount for tooltips, cards and the mobile detail panel: grouped with «٬», up to one «٫» decimal. */
export function formatMonthlyTrendAmount(value: number): string {
  return formatNumber(value, { maximumFractionDigits: 1 });
}

/** Whole amount for bar labels, legend totals and chart axes. */
export function formatMonthlyTrendWholeAmount(value: number): string {
  return formatNumber(Math.round(value), { maximumFractionDigits: 0 });
}

function formatBarAmount(value: number | null): string | null {
  if (value === null) return null;
  return formatMonthlyTrendWholeAmount(value);
}

const percentagePattern = /[+-]?\s*[0-9۰-۹٠-٩]+(?:[.,٫][0-9۰-۹٠-٩]+)?\s*[%٪]/;

function formatInsightLine(
  data: MonthlyActivityTrendResult,
  kind: string,
  text: string,
): MonthlyTrendExplanationLine {
  const persianText = toPersianNumerals(text);
  const match = percentagePattern.exec(persianText);
  const percentage = insightPercentage(data, kind, match?.[0]);
  if (!match || percentage === null) return neutralExplanationLine(persianText);

  return {
    beforeValue: persianText.slice(0, match.index),
    valueLabel: formatSignedPercentage(percentage),
    afterValue: persianText.slice(match.index + match[0].length),
    tone: percentage > 0 ? "positive" : percentage < 0 ? "negative" : "neutral",
  };
}

function insightPercentage(
  data: MonthlyActivityTrendResult,
  kind: string,
  matchedText?: string,
): number | null {
  const structuredValue =
    kind === "YoYGrowth"
      ? data.salesAmountYoYGrowthPercent
      : kind === "VsAverage12Month"
        ? data.salesVsAverage12MonthPercent
        : undefined;
  if (structuredValue !== undefined) return structuredValue;
  if (!matchedText) return null;

  const normalized = matchedText
    .replace(/[۰-۹]/g, (digit) => String("۰۱۲۳۴۵۶۷۸۹".indexOf(digit)))
    .replace(/[٠-٩]/g, (digit) => String("٠١٢٣٤٥٦٧٨٩".indexOf(digit)))
    .replace("٫", ".")
    .replace(/[%٪\s]/g, "");
  const parsed = Number(normalized);
  return Number.isFinite(parsed) ? parsed : null;
}

/** Absolute percentage with one decimal in the product's number style, e.g. «۷۴٫۱٪». */
function formatPercentMagnitude(value: number): string {
  return `${toPersianNumerals(Math.abs(value).toFixed(1))}٪`;
}

function formatSignedPercentage(value: number): string {
  const digits = formatPercentMagnitude(value);
  if (value > 0) return `+${digits}`;
  if (value < 0) return `(${digits})`;
  return digits;
}

function neutralExplanationLine(text: string): MonthlyTrendExplanationLine {
  return {
    beforeValue: toPersianNumerals(text),
    valueLabel: null,
    afterValue: "",
    tone: "neutral",
  };
}

function sumReportedSales(
  points: MonthlyTrendChartPointViewModel[],
  yearKey: "currentYear" | "previousYear",
): number | null {
  const reportedAmounts = points
    .map((point) => point[yearKey])
    .filter((value): value is number => value !== null);

  return reportedAmounts.length > 0
    ? reportedAmounts.reduce((total, value) => total + value, 0)
    : null;
}

function yearLabel(year: number | null | undefined, fallback: string): string {
  return year ? toPersianDigits(String(year)) : fallback;
}

function shareOfPreviousYear(
  currentYearTotal: number | null,
  previousYearTotal: number | null,
  previousYearLabel: string,
): string | null {
  if (currentYearTotal === null || previousYearTotal === null || previousYearTotal === 0)
    return null;
  return `${formatPercentMagnitude((currentYearTotal / previousYearTotal) * 100)} از ${previousYearLabel}`;
}

function legendText(entry: MonthlyTrendLegendEntry): string {
  const base = entry.totalLabel ? `${entry.label}: ${entry.totalLabel}` : entry.label;
  return entry.note ? `${base} (${entry.note})` : base;
}

function formatJalaliDateTime(isoTimestamp: string): string {
  const value = new Date(isoTimestamp);
  if (Number.isNaN(value.getTime())) return isoTimestamp;

  return new Intl.DateTimeFormat("fa-IR-u-ca-persian", {
    timeZone: "Asia/Tehran",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    hourCycle: "h23",
  }).format(value);
}
