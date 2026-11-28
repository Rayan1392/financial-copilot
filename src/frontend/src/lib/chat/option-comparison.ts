import { toPersianNumerals } from "@/lib/format/persian";

/*
 * Feature 168 option comparison answers. The server owns every figure (fees, fill price, measures, highlights,
 * freshness); this module only names and formats them. It is the single owner of the card's Persian wording. The
 * answers compare and never recommend, so no string here may contain «بهترین», «مناسب», «پیشنهاد» or «فرصت».
 */

export type OptionComparisonView = "Call" | "Put" | "CoveredCall";
export type OptionRowState = "Fillable" | "NotFillable" | "NoOrder";
export type OptionUnderlyingClass = "Share" | "FundUnit" | "Unknown";

export interface OptionComparisonRow {
  symbol: string;
  strikePrice: number;
  contractSize: number;
  openInterest: number;
  state: OptionRowState;
  fillPrice?: number | null;
  availableVolume: number;
  lastPrice?: number | null;
  isNearestToMoney: boolean;
  isRequestedContract: boolean;
  highlights: string[];
  effectivePurchaseCost?: number | null;
  breakEvenDistance?: number | null;
  intrinsicValue?: number | null;
  timeValue?: number | null;
  impliedAnnualRate?: number | null;
  leverage?: number | null;
  cashNow?: number | null;
  cashAtExpiry?: number | null;
  effectiveSalePrice?: number | null;
  distanceFromMarket?: number | null;
  protectionCost?: number | null;
  protectionCostAnnualized?: number | null;
  premiumIncome?: number | null;
  yieldIfExercised?: number | null;
  yieldIfExercisedAnnualized?: number | null;
  yieldIfNotExercised?: number | null;
  yieldIfNotExercisedAnnualized?: number | null;
  downsideCushion?: number | null;
  theoreticalPrice?: number | null;
  impliedVolatility?: number | null;
}

export interface OptionComparisonExpiry {
  expiryDate: string;
  daysToExpiry: number;
  rows: OptionComparisonRow[];
}

export interface OptionComparisonViewResult {
  view: OptionComparisonView;
  expiries: OptionComparisonExpiry[];
}

export interface OptionComparison {
  version: number;
  underlyingSymbol: string;
  underlyingClass: OptionUnderlyingClass;
  underlyingPrice: number;
  underlyingTradingDate: string;
  underlyingObservedAt: string;
  quantityInContracts: number;
  requestedView: OptionComparisonView;
  views: OptionComparisonViewResult[];
  availableExpiryDates: string[];
  fees: { trade: number; exercise: number; shareTransferTax: number };
  orderBookIsCurrent: boolean;
  orderBookObservedAt?: string | null;
  openInterestObservedAt?: string | null;
  theoreticalInputs?: {
    riskFreeAnnualRate: number;
    historicalVolatility: number;
    volatilityWindowDays: number;
  } | null;
}

export const OPTION_VIEW_TITLE: Record<OptionComparisonView, string> = {
  Call: "مقایسهٔ هزینهٔ خرید از مسیر اختیار خرید",
  Put: "مقایسهٔ قیمت فروش از مسیر اختیار فروش",
  CoveredCall: "مقایسهٔ بازده فروش اختیار خرید روی سهم",
};

export const OPTION_VIEW_TAB: Record<OptionComparisonView, string> = {
  Call: "اختیار خرید",
  Put: "اختیار فروش",
  CoveredCall: "کاورد کال",
};

export const OPTION_HIGHLIGHT_TEXT: Record<string, string> = {
  lowest_effective_purchase_cost: "کمترین هزینهٔ مؤثر",
  lowest_implied_annual_rate: "کمترین نرخ ضمنی",
  highest_effective_sale_price: "بیشترین قیمت فروش مؤثر",
  highest_annualized_yield_if_exercised: "بیشترین بازده سالانه در صورت اعمال",
};

export const OPTION_VIEW_NOTE: Record<OptionComparisonView, string> = {
  Call: "اعمال اختیار خرید یعنی پرداخت قیمت اعمال برای هر سهم در سررسید. اگر قیمت سهم در سررسید زیر قیمت اعمال باشد، مبلغ پرداختی بابت اختیار از دست می‌رود.",
  Put: "اعمال اختیار فروش یعنی تحویل سهم در سررسید؛ بدون داشتن سهم، اعمال فیزیکی ممکن نیست.",
  CoveredCall:
    "برای این موقعیت باید سهم را در اختیار داشته باشید. اگر قیمت سهم از قیمت اعمال بالاتر برود، سود شما به قیمت اعمال محدود می‌شود.",
};

export const OPTION_VOLUME_NOTE = "قیمت‌ها فقط برای حجم نمایش‌داده‌شده در سفارش‌های فعلی معتبرند.";
export const OPTION_ADVICE_NOTE =
  "این مقایسه بر پایهٔ دادهٔ بازار است؛ پیش‌بینی قطعی و توصیهٔ سرمایه‌گذاری نیست.";
export const OPTION_BOOK_NOT_CURRENT_NOTE =
  "عمق سفارش به‌روز نیست؛ فقط آخرین قیمت معامله و موقعیت‌های باز نمایش داده می‌شود.";
export const OPTION_UNKNOWN_CLASS_NOTE =
  "نوع دارایی پایه مشخص نشد؛ مالیات نقل‌وانتقال مانند سهم محاسبه شده است.";
export const OPTION_THEORETICAL_NOTE = "قیمت تئوریک با مدل بلک‌شولز محاسبه شده و قیمت هدف نیست.";
export const OPTION_SHOW_EMPTY_ROWS = "نمایش بدون سفارش‌ها";
export const OPTION_HIDE_EMPTY_ROWS = "پنهان‌کردن بدون سفارش‌ها";
export const OPTION_NO_FILLABLE_ROWS =
  "در این سررسید سفارشی با حجم کافی برای این تعداد قرارداد وجود ندارد.";

export const OPTION_ROW_STATE_TEXT: Record<OptionRowState, string> = {
  Fillable: "",
  NotFillable: "حجم ناکافی",
  NoOrder: "بدون سفارش",
};

/** Every user-facing string of the card, for the wording check. */
export const OPTION_COMPARISON_WORDING: readonly string[] = [
  ...Object.values(OPTION_VIEW_TITLE),
  ...Object.values(OPTION_VIEW_TAB),
  ...Object.values(OPTION_HIGHLIGHT_TEXT),
  ...Object.values(OPTION_VIEW_NOTE),
  ...Object.values(OPTION_ROW_STATE_TEXT),
  OPTION_VOLUME_NOTE,
  OPTION_ADVICE_NOTE,
  OPTION_BOOK_NOT_CURRENT_NOTE,
  OPTION_UNKNOWN_CLASS_NOTE,
  OPTION_THEORETICAL_NOTE,
  OPTION_SHOW_EMPTY_ROWS,
  OPTION_HIDE_EMPTY_ROWS,
  OPTION_NO_FILLABLE_ROWS,
];

/** Views that have at least one row, in a fixed order; the card offers a tab only for these. */
export function optionViewsWithRows(data: OptionComparison): OptionComparisonViewResult[] {
  const order: OptionComparisonView[] = ["Call", "Put", "CoveredCall"];
  return order
    .map((view) => data.views.find((item) => item.view === view))
    .filter(
      (item): item is OptionComparisonViewResult =>
        item !== undefined && item.expiries.some((expiry) => expiry.rows.length > 0),
    );
}

/** The view the card opens on: the asked one when it has rows, otherwise the first that has. */
export function initialOptionView(data: OptionComparison): OptionComparisonView | null {
  const views = optionViewsWithRows(data);
  return (views.find((item) => item.view === data.requestedView) ?? views[0])?.view ?? null;
}

/** The expiry the card opens on: the one holding the asked contract, otherwise the nearest with rows. */
export function initialOptionExpiry(view: OptionComparisonViewResult): string | null {
  const withRows = view.expiries.filter((expiry) => expiry.rows.length > 0);
  return (
    (withRows.find((expiry) => expiry.rows.some((row) => row.isRequestedContract)) ?? withRows[0])
      ?.expiryDate ?? null
  );
}

/** A row belongs to the collapsed group when the order book cannot serve the requested quantity. */
export function isEmptyOptionRow(row: OptionComparisonRow, bookIsCurrent: boolean): boolean {
  return bookIsCurrent && row.state !== "Fillable";
}

const hasValue = (value: number | null | undefined): value is number =>
  value !== null && value !== undefined && Number.isFinite(value);

/** Whole rials with the product's separators; «—» when absent. */
export function formatOptionMoney(value: number | null | undefined): string {
  if (!hasValue(value)) return "—";
  return toPersianNumerals(Math.round(value).toLocaleString("en-US"));
}

export function formatOptionCount(value: number | null | undefined): string {
  if (!hasValue(value)) return "—";
  return toPersianNumerals(Math.round(value).toLocaleString("en-US"));
}

/** A fraction as a percentage with the given decimals; «—» when absent. */
export function formatOptionPercent(value: number | null | undefined, decimals = 1): string {
  if (!hasValue(value)) return "—";
  return `${toPersianNumerals((value * 100).toFixed(decimals))}٪`;
}

/** A signed fraction as «۳٫۸٪ بالاتر» / «۱٫۲٪ پایین‌تر», so the sign never depends on text direction. */
export function formatOptionDistance(value: number | null | undefined): string {
  if (!hasValue(value)) return "—";
  const text = formatOptionPercent(Math.abs(value));
  if (value > 0) return `${text} بالاتر`;
  if (value < 0) return `${text} پایین‌تر`;
  return text;
}

export function formatOptionRatio(value: number | null | undefined): string {
  if (!hasValue(value)) return "—";
  return toPersianNumerals(value.toFixed(1));
}

const JALALI_DATE = new Intl.DateTimeFormat("fa-IR-u-ca-persian-nu-latn", {
  year: "numeric",
  month: "2-digit",
  day: "2-digit",
  timeZone: "UTC",
});

const TEHRAN_DATE_TIME = new Intl.DateTimeFormat("fa-IR-u-ca-persian-nu-latn", {
  year: "numeric",
  month: "2-digit",
  day: "2-digit",
  hour: "2-digit",
  minute: "2-digit",
  hourCycle: "h23",
  timeZone: "Asia/Tehran",
});

/** «۱۴۰۵/۰۹/۱۸» from an ISO date (yyyy-MM-dd). */
export function formatOptionDate(isoDate: string): string {
  const date = new Date(`${isoDate.slice(0, 10)}T00:00:00Z`);
  if (Number.isNaN(date.getTime())) return isoDate;
  return toPersianNumerals(JALALI_DATE.format(date));
}

/** «۱۴۰۵/۰۹/۰۷ ۱۰:۰۰» in Tehran time from an ISO instant. */
export function formatOptionDateTime(isoInstant: string | null | undefined): string | null {
  if (!isoInstant) return null;
  const date = new Date(isoInstant);
  if (Number.isNaN(date.getTime())) return null;
  return toPersianNumerals(TEHRAN_DATE_TIME.format(date).replace(",", "").replace("،", ""));
}

/** «معامله ۰٫۱۰٪، اعمال ۰٫۰۵٪» plus the transfer tax for the views where a share is delivered. */
export function optionFeesText(data: OptionComparison, view: OptionComparisonView): string {
  const parts = [
    `معامله ${formatOptionPercent(data.fees.trade, 2)}`,
    `اعمال ${formatOptionPercent(data.fees.exercise, 2)}`,
  ];
  if (view !== "Call") {
    const tax = data.underlyingClass === "FundUnit" ? 0 : data.fees.shareTransferTax;
    parts.push(`مالیات نقل‌وانتقال ${formatOptionPercent(tax, 2)}`);
  }
  return `کارمزدها: ${parts.join("، ")}`;
}

/** The questions the entry points send; they are the phrases a user can type. */
export const optionCallQuestion = (symbol: string) => `اختیار خرید ${symbol.trim()}`;
