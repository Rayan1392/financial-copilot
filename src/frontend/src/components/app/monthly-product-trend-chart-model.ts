import type { MonthlyProductTrendResult } from "@/lib/chat.functions";
import { toPersianDigits } from "@/lib/format/persian";

const VALID_RATE_STATUSES = new Set(["ValidRate", "ValidZeroRate"]);

export function productTrendSeries(productUnit?: string | null) {
  return {
    production: { label: "ØªÙˆÙ„ÛŒØ¯", color: "#4f46e5" },
    quantity: { label: "Ù…Ù‚Ø¯Ø§Ø± ÙØ±ÙˆØ´", color: "#0f766e" },
    sales: { label: "مبلغ فروش", color: "#4f9f82" },
    rate: { label: `نرخ فروش (تومان/${productUnit ?? "واحد محصول"})`, color: "#f59e0b" },
  };
}

export const PRODUCT_TREND_PANEL_LABELS = {
  main: "\u0645\u0628\u0644\u063a \u0641\u0631\u0648\u0634 \u0648 \u0646\u0631\u062e \u0641\u0631\u0648\u0634",
  quantity:
    "\u0645\u0642\u062f\u0627\u0631 \u062a\u0648\u0644\u06cc\u062f \u0648 \u0645\u0642\u062f\u0627\u0631 \u0641\u0631\u0648\u0634",
  production: "\u062a\u0648\u0644\u06cc\u062f",
  saleQuantity: "\u0645\u0642\u062f\u0627\u0631 \u0641\u0631\u0648\u0634",
} as const;

export function productTrendQuantitySeries() {
  return {
    production: { label: PRODUCT_TREND_PANEL_LABELS.production, color: "#4f46e5" },
    quantity: { label: PRODUCT_TREND_PANEL_LABELS.saleQuantity, color: "#0f766e" },
  };
}

export interface MonthlyProductTrendChartPoint {
  label: string;
  value: number | null;
  rate: number | null;
  quantity: number | null;
  production: number | null;
  unit: string;
  status: string;
}

export function createMonthlyProductTrendChartData(
  data: MonthlyProductTrendResult,
): MonthlyProductTrendChartPoint[] {
  return data.points.map((point) => ({
    label: formatProductTrendPeriod(point.fiscalLabel),
    value: point.salesValueBillionToman ?? null,
    rate: VALID_RATE_STATUSES.has(point.rateStatus)
      ? (point.calculatedSaleRateToman ?? null)
      : null,
    quantity: point.saleQuantity ?? null,
    production: point.productionQuantity ?? null,
    unit: point.productUnit ?? data.productUnit ?? "واحد محصول",
    status: point.rateStatus,
  }));
}

export function formatProductTrendPeriod(value: string): string {
  const normalized = toLatinDigits(value.trim());
  const parts = normalized.split(/[-/]/u).filter(Boolean);
  if (parts.length === 2) {
    const [first, second] = parts;
    const year = first.length === 4 ? first : second.length === 4 ? second : null;
    const month = first.length === 4 ? second : second.length === 4 ? first : null;
    if (year && month && /^\d{4}$/u.test(year) && /^\d{1,2}$/u.test(month)) {
      return toPersianDigits(`${year}/${month.padStart(2, "0")}`);
    }
  }
  return toPersianDigits(value);
}

export function isRateVisible(value: number | null | undefined): value is number {
  return value != null && Number.isFinite(value);
}

export function formatProductTrendNumber(value: number | null | undefined): string {
  if (value == null) return "—";
  return new Intl.NumberFormat("fa-IR", { maximumFractionDigits: 2 }).format(value);
}

export function formatProductTrendIntegerNumber(value: number): string {
  return new Intl.NumberFormat("fa-IR", { maximumFractionDigits: 0 }).format(value);
}

export function createProductTrendQuantityAxis(values: Array<number | null | undefined>) {
  const maxValue = Math.max(
    0,
    ...values.filter((value): value is number => value != null && Number.isFinite(value)),
  );
  const targetStep = (maxValue || 1) / 3;
  const magnitude = 10 ** Math.floor(Math.log10(targetStep));
  const normalizedStep = targetStep / magnitude;
  const niceFactor =
    [1, 2, 2.5, 3, 4, 5, 6, 8, 10].find((factor) => factor >= normalizedStep) ?? 10;
  const step = niceFactor * magnitude;
  const max = maxValue === 0 ? step * 3 : Math.ceil(maxValue / step) * step;
  const ticks = Array.from({ length: Math.round(max / step) + 1 }, (_, index) => index * step);
  return { max, ticks };
}

// Display only: Intl's default halfExpand matches monetary AwayFromZero rounding.
// Keep the precise rate in the chart model and use this only at text-rendering boundaries.
export function formatProductTrendRate(value: number | null | undefined): string {
  if (!isRateVisible(value)) return "—";
  return new Intl.NumberFormat("fa-IR", { maximumFractionDigits: 0 }).format(value);
}

export function formatProductTrendRateLabel(
  rateStatus: string,
  value: number | null | undefined,
): string | null {
  if (!VALID_RATE_STATUSES.has(rateStatus) || !isRateVisible(value)) return null;
  return formatProductTrendRate(value);
}

export function buildMonthlyProductTrendExportFileName(data: MonthlyProductTrendResult): string {
  const product = sanitizeFileNamePart(data.productTitle ?? "محصول");
  const company = sanitizeFileNamePart(
    data.companySymbol?.trim() || data.companyText?.trim() || "",
  );
  const companyAlreadyIncluded =
    company.length > 0 && (product === company || product.endsWith(`-${company}`));
  const parts = ["روند", "فروش", product, companyAlreadyIncluded ? "" : company].filter(
    (part) => part.length > 0,
  );
  return `${parts.join("-") || "روند-فروش-محصول"}.png`;
}

function toLatinDigits(value: string): string {
  return value
    .replace(/[۰-۹]/gu, (digit) => String("۰۱۲۳۴۵۶۷۸۹".indexOf(digit)))
    .replace(/[٠-٩]/gu, (digit) => String("٠١٢٣٤٥٦٧٨٩".indexOf(digit)));
}

function sanitizeFileNamePart(value: string): string {
  return value
    .trim()
    .replace(/[\\/:*?"<>|\p{Cc}]/gu, "-")
    .replace(/\s+/gu, "-")
    .replace(/-+/gu, "-")
    .replace(/^-|-$/gu, "");
}
