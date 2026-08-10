import type {
  MonthlyActivityTrendChartPoint,
  MonthlyActivityTrendResult,
} from "@/lib/chat.functions";
import { toPersianDigits } from "@/lib/format/persian";

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

export type MonthlyTrendChartCardViewModel = {
  title: string;
  companyLabel: string;
  unitLabel: string;
  currentYearLegend: string;
  previousYearLegend: string;
  averageLegend: string;
  points: MonthlyTrendChartPointViewModel[];
  explanationLines: string[];
  sourceContext: string;
  calculationContext: string;
  palette: MonthlyTrendExportPalette;
};

const jalaliMonthNames = [
  "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
  "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند",
] as const;

const exportPalettes: Record<MonthlyTrendChartTheme, MonthlyTrendExportPalette> = {
  dark: {
    currentYear: "#34d399",
    previousYear: "#818cf8",
    average: "#fbbf24",
    foreground: "#ffffff",
    mutedForeground: "#e4e4e7",
    grid: "rgba(255,255,255,0.22)",
    surface: "#24242a",
    watermark: "rgba(244,244,245,0.22)",
  },
  light: {
    currentYear: "#047857",
    previousYear: "#4338ca",
    average: "#b45309",
    foreground: "#18181b",
    mutedForeground: "#3f3f46",
    grid: "rgba(63,63,70,0.28)",
    surface: "#fafafa",
    watermark: "rgba(39,39,42,0.18)",
  },
};

export function createMonthlyTrendChartCardViewModel(
  data: MonthlyActivityTrendResult,
  theme: MonthlyTrendChartTheme = "dark",
): MonthlyTrendChartCardViewModel {
  const points = data.chartPoints.map((point) => mapPoint(point));
  const currentYear = data.chartPoints.find((point) => point.currentFiscalYear != null)?.currentFiscalYear;
  const previousYear = data.chartPoints.find((point) => point.previousFiscalYear != null)?.previousFiscalYear;
  const currentYearTotal = sumReportedSales(points, "currentYear");
  const previousYearTotal = sumReportedSales(points, "previousYear");
  const companyLabel = data.companyName
    ? `${data.companyName} (${data.companySymbol})`
    : data.companySymbol;
  const sourceContext = `منبع: ${data.sourceProviderName}`;
  const calculationContext = `محاسبه: ${formatJalaliDateTime(data.calculatedAtUtc)}`;

  return {
    title: "روند فروش ماهانه",
    companyLabel,
    unitLabel: data.unitLabelFa,
    currentYearLegend: formatCurrentYearLegend(
      currentYear,
      currentYearTotal,
      previousYear,
      previousYearTotal,
      "سال جاری",
    ),
    previousYearLegend: formatYearLegend(previousYear, previousYearTotal, "سال قبل"),
    averageLegend: "میانگین ۱۲ ماهه",
    points,
    explanationLines: [
      ...data.insights.map((insight) => insight.textFa),
      ...data.missingDataPoints.map((point) => `⚠ ${point.reasonFa}`),
    ],
    sourceContext,
    calculationContext,
    palette: exportPalettes[theme],
  };
}

function mapPoint(point: MonthlyActivityTrendChartPoint): MonthlyTrendChartPointViewModel {
  const currentYear = point.isCurrentYearReported ? (point.currentFiscalYearSalesAmount ?? null) : null;
  const previousYear = point.isPreviousYearReported ? (point.previousFiscalYearSalesAmount ?? null) : null;
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

function formatBarAmount(value: number | null): string | null {
  if (value === null) return null;
  return toPersianDigits(
    Math.round(value).toLocaleString("en-US", { maximumFractionDigits: 0 }),
  );
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

function formatYearLegend(
  year: number | null | undefined,
  total: number | null,
  fallback: string,
): string {
  const label = year ? toPersianDigits(String(year)) : fallback;
  const totalLabel = formatBarAmount(total);
  return totalLabel ? `${label}: ${totalLabel}` : label;
}

function formatCurrentYearLegend(
  currentYear: number | null | undefined,
  currentYearTotal: number | null,
  previousYear: number | null | undefined,
  previousYearTotal: number | null,
  fallback: string,
): string {
  const label = formatYearLegend(currentYear, currentYearTotal, fallback);
  if (currentYearTotal === null || previousYearTotal === null || previousYearTotal === 0) {
    return label;
  }

  const percentage = toPersianDigits(((currentYearTotal / previousYearTotal) * 100).toFixed(2));
  const previousYearLabel = previousYear ? toPersianDigits(String(previousYear)) : "سال قبل";
  return `${label} (${percentage}٪ از ${previousYearLabel})`;
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
