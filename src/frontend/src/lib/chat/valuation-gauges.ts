/*
 * Feature 159 change «standalone-valuation-gauge-cards»: the typed cards the API returns (live and from history).
 * Provider values are exact decimal strings; angles are degrees on a semicircle, 0° at the low end and 180° at the
 * high end. The server owns every fact and every angle; the client only formats and draws them.
 */

import {
  PERSIAN_DECIMAL_SEPARATOR,
  PERSIAN_THOUSANDS_SEPARATOR,
  toPersianDigits,
} from "@/lib/format/persian";

export type ValuationGaugeKind = "Pe" | "Ps" | "Equilibrium";
export type ValuationGaugeCardState =
  | "Available"
  | "Partial"
  | "NoData"
  | "Stale"
  | "TemporarilyUnavailable";
export type ValuationRatioPosition =
  | "BelowMin"
  | "MinToAverage"
  | "AverageToMax"
  | "AboveMax"
  | "Unranked";
export type ValuationEquilibriumPosition =
  | "BelowRange"
  | "LowerHalf"
  | "AtMidpoint"
  | "UpperHalf"
  | "AboveRange";
export type ValuationGaugeNeedleSide = "Inside" | "PinnedBelow" | "PinnedAbove";
export type ValuationGaugeQuoteState = "Available" | "Unavailable" | "TimedOut";

export interface ValuationGaugeGeometry {
  sectors: Array<{
    order: number;
    startAngle: number;
    endAngle: number;
    dayCount?: number | null;
    percentage?: string | null;
  }>;
  markers: Array<{ role: string; angle: number; value: string }>;
  needle?: { angle: number; value: string; side: ValuationGaugeNeedleSide } | null;
}

export interface ValuationGaugeCard {
  kind: ValuationGaugeKind;
  state: ValuationGaugeCardState;
  reasonCode: string;
  source: string;
  fetchedAtUtc?: string | null;
  ratio?: {
    current?: string | null;
    start: string;
    min: string;
    average: string;
    max: string;
    end: string;
    dayCounts: number[];
    dayPercentages?: string[] | null;
    providerStatus?: number | null;
    position: ValuationRatioPosition;
    currentSide?: ValuationGaugeNeedleSide | null;
  } | null;
  equilibrium?: {
    lower: string;
    midpoint: string;
    upper: string;
    quoteState: ValuationGaugeQuoteState;
    quote?: {
      price: string;
      providerName: string;
      sourceLabel: string;
      asOf: string;
      tradingDate?: string | null;
    } | null;
    position?: ValuationEquilibriumPosition | null;
  } | null;
  geometry?: ValuationGaugeGeometry | null;
}

export interface ValuationGaugeCardsResult {
  version: number;
  symbol: string;
  companyName?: string | null;
  vendorTicker?: string | null;
  requestedKinds: ValuationGaugeKind[];
  cards: ValuationGaugeCard[];
  generatedAtUtc: string;
  maxFetchAgeHours: number;
}

/**
 * An exact invariant decimal («-4312.68») as Persian text («‎−۴٬۳۱۲٫۶۸»): grouping «٬», decimal mark «٫», and an LRM so
 * the minus sign stays attached in right-to-left text. Never rounds.
 */
export function formatExactDecimal(value: string): string {
  let text = value.trim();
  const negative = text.startsWith("-");
  if (negative || text.startsWith("+")) text = text.slice(1);
  const [integer, fraction] = text.split(".");
  const grouped = integer.replace(/\B(?=(\d{3})+(?!\d))/g, PERSIAN_THOUSANDS_SEPARATOR);
  const body = toPersianDigits(
    fraction ? `${grouped}${PERSIAN_DECIMAL_SEPARATOR}${fraction}` : grouped,
  );
  return negative ? `‎−${body}` : body;
}

/** Tehran local time in the Persian calendar, for example «۱۴۰۵/۰۹/۰۵ ۱۲:۴۰». */
export function formatTehranDateTime(isoUtc: string): string {
  const date = new Date(isoUtc);
  if (Number.isNaN(date.getTime())) return "—";
  const parts = new Intl.DateTimeFormat("en-US-u-ca-persian-nu-latn", {
    timeZone: "Asia/Tehran",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    hourCycle: "h23",
  }).formatToParts(date);
  const part = (type: string) => parts.find((item) => item.type === type)?.value ?? "";
  return toPersianDigits(
    `${part("year")}/${part("month")}/${part("day")} ${part("hour")}:${part("minute")}`,
  );
}

export const KIND_TITLE: Record<ValuationGaugeKind, string> = {
  Pe: "گیج P/E",
  Ps: "گیج P/S",
  Equilibrium: "بازهٔ قیمت تعادلی",
};

export const RATIO_POSITION_TEXT: Record<ValuationRatioPosition, string> = {
  BelowMin: "پایین‌تر از حد پایین تاریخی",
  MinToAverage: "بین حد پایین و میانگین",
  AverageToMax: "بین میانگین و حد بالا",
  AboveMax: "بالاتر از حد بالای تاریخی",
  Unranked: "بدون رتبه‌بندی منبع",
};

export const EQUILIBRIUM_POSITION_TEXT: Record<ValuationEquilibriumPosition, string> = {
  BelowRange: "زیر بازهٔ تعادلی",
  LowerHalf: "در نیمهٔ پایین بازه",
  AtMidpoint: "روی میانهٔ بازه",
  UpperHalf: "در نیمهٔ بالای بازه",
  AboveRange: "بالاتر از بازهٔ تعادلی",
};

/** The same wording the backend text composer uses for the quote source (Telegram and history text). */
export function quoteSourceText(sourceLabel: string): string {
  if (sourceLabel === "IntradayToday") return "معاملات امروز";
  if (sourceLabel === "LatestDailyFallback") return "آخرین روز معاملاتی";
  return sourceLabel;
}

export function unavailableText(card: ValuationGaugeCard): string {
  // The card footer shows the fetch time, so a stale card names only the state here.
  if (card.state === "Stale") return "دادهٔ منبع قدیمی است";
  if (card.state === "TemporarilyUnavailable") return "موقتاً در دسترس نیست";
  if (card.reasonCode === "coverage_miss") return "این نماد در پوشش دادهٔ گیج CyclicalWaves نیست";
  if (card.reasonCode === "ambiguous_match") return "تطبیق یکتای نماد با منبع گیج ممکن نشد";
  return "داده‌ای ثبت نشده است";
}

export function isDisplayable(card: ValuationGaugeCard): boolean {
  return card.state === "Available" || card.state === "Partial";
}

/**
 * Point on the dial for a server angle. The dial keeps numeric direction left to right even in RTL pages (as the
 * approved semicircle design does): 0° is the left end (low values), 90° the top, 180° the right end (high values).
 */
export function dialPoint(
  angle: number,
  radius: number,
  cx = 150,
  cy = 142,
): { x: number; y: number } {
  const clamped = Math.min(180, Math.max(0, angle));
  const radians = Math.PI - (clamped * Math.PI) / 180;
  return { x: cx + radius * Math.cos(radians), y: cy - radius * Math.sin(radians) };
}

/** SVG path of a band between two angles and two radii. */
export function bandPath(startAngle: number, endAngle: number, outer = 124, inner = 82): string {
  const a = dialPoint(startAngle, outer);
  const b = dialPoint(endAngle, outer);
  const c = dialPoint(endAngle, inner);
  const d = dialPoint(startAngle, inner);
  const large = endAngle - startAngle > 180 ? 1 : 0;
  const f = (value: number) => value.toFixed(2);
  return `M ${f(a.x)} ${f(a.y)} A ${outer} ${outer} 0 ${large} 1 ${f(b.x)} ${f(b.y)} L ${f(c.x)} ${f(c.y)} A ${inner} ${inner} 0 ${large} 0 ${f(d.x)} ${f(d.y)} Z`;
}
