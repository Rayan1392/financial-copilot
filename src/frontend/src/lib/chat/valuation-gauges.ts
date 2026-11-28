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
    /** Exact value range of a ratio sector; absent on cards stored before the visual refresh. */
    from?: string | null;
    to?: string | null;
  }>;
  markers: Array<{ role: string; angle: number; value: string }>;
  needle?: { angle: number; value: string; side: ValuationGaugeNeedleSide } | null;
}

/** Server-derived figures (change «valuation-gauge-cards-visual-refresh»); each is absent when its inputs are. */
export interface ValuationGaugeInsights {
  distanceToAveragePercent?: string | null;
  currentBand?: number | null;
  daysBelowBandPercent?: string | null;
  daysInBandPercent?: string | null;
  priceVsMidpointPercent?: string | null;
  priceVsLowerPercent?: string | null;
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
  insights?: ValuationGaugeInsights | null;
  /** The forward value of a P/E or P/S card, from the provider or Sapio's estimate; absent on older cards. */
  forward?: ValuationGaugeForwardEstimate | null;
}

/** Whose forward value it is. A stored card without a source carries a Sapio estimate. */
export type ValuationGaugeForwardSource = "provider" | "sapio";

/**
 * The forward value of a P/E or P/S card's ratio: the provider's own forward ratio (change
 * «valuation-gauge-provider-forward») or Sapio's estimate (change «valuation-gauge-forward-ratio»). The value has two
 * decimals; the angle and side are on the card's own axis with the same pin rule as the current needle. The method
 * members exist for a Sapio estimate only.
 */
export interface ValuationGaugeForwardEstimate {
  value: string;
  angle: number;
  side: ValuationGaugeNeedleSide;
  source?: ValuationGaugeForwardSource | null;
  methodVersion?: string | null;
  salesGrowthFactor?: string | null;
  marginPercent?: string | null;
  latestSalesMonthEnd?: string | null;
  latestQuarterEnd?: string | null;
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

/**
 * Rounds an invariant decimal string half away from zero, digit by digit, so long provider values never drift through
 * floating point. Returns the input unchanged when it is not a plain decimal.
 */
export function roundDecimalString(value: string, decimals: number): string {
  let text = value.trim();
  const negative = text.startsWith("-");
  if (negative || text.startsWith("+")) text = text.slice(1);
  const [rawInteger = "", fraction = ""] = text.split(".");
  const integer = rawInteger === "" ? "0" : rawInteger;
  if (!/^\d+$/.test(integer) || !/^\d*$/.test(fraction)) return value;
  const kept =
    fraction.length >= decimals ? fraction.slice(0, decimals) : fraction.padEnd(decimals, "0");
  let scaled = BigInt(integer + kept);
  if (fraction.length > decimals && fraction[decimals] >= "5") scaled += 1n;
  const digits = scaled.toString().padStart(decimals + 1, "0");
  const rounded =
    decimals === 0 ? digits : `${digits.slice(0, -decimals)}.${digits.slice(-decimals)}`;
  return negative && scaled !== 0n ? `-${rounded}` : rounded;
}

/** A P/E or P/S value with exactly two decimals («۷٫۰۰»). */
export function formatRatio2(value: string): string {
  return formatExactDecimal(roundDecimalString(value, 2));
}

/** A price or equilibrium bound in whole rials («۴٬۱۲۰»). */
export function formatRial0(value: string): string {
  return formatExactDecimal(roundDecimalString(value, 0));
}

/** A server percentage string («19.0») as «۱۹٫۰٪»; signed distances get an explicit plus («‎+۱۹٫۰٪»). */
export function formatPercent(value: string, signed = false): string {
  const text = `${formatExactDecimal(value)}٪`;
  const positive = !value.trim().startsWith("-") && roundDecimalString(value, 1) !== "0.0";
  return signed && positive ? `‎+${text}` : text;
}

/**
 * The equilibrium card's price-vs-lower-bound figure (OQ-137, owner 2026-11-26): below the lower bound it reads
 * «زیر حد پایین» with the absolute value; otherwise «بالای حد پایین» signed, as released. A value that rounds to zero
 * keeps the released label.
 */
export function lowerBoundComparison(value: string): { label: string; percent: string } {
  const trimmed = value.trim();
  const magnitude = trimmed.startsWith("-") ? trimmed.slice(1) : null;
  if (magnitude !== null && roundDecimalString(magnitude, 1) !== "0.0")
    return { label: "زیر حد پایین", percent: formatPercent(magnitude) };
  return { label: "بالای حد پایین", percent: formatPercent(trimmed, true) };
}

/** The provider's name for users; the payload keeps the internal source code. */
export const SOURCE_NAME = "تحلیل‌اپ";

/** Follows the ratio name, for example «P/E آینده‌نگر (برآورد ساپیو)»; the same label the backend text uses. */
export const FORWARD_LABEL = "آینده‌نگر (برآورد ساپیو)";

/** Follows the ratio name for the provider's own forward ratio, for example «P/E آینده‌نگر». */
export const PROVIDER_FORWARD_LABEL = "آینده‌نگر";

/** True when the forward value is the provider's own and not Sapio's estimate. */
export function isProviderForward(forward: ValuationGaugeForwardEstimate): boolean {
  return forward.source === "provider";
}

/** The label that follows the ratio name: no qualifier for the provider's value, «(برآورد ساپیو)» for the estimate. */
export function forwardLabel(forward: ValuationGaugeForwardEstimate): string {
  return isProviderForward(forward) ? PROVIDER_FORWARD_LABEL : FORWARD_LABEL;
}

const NEEDLE_SIDES: ValuationGaugeNeedleSide[] = ["Inside", "PinnedBelow", "PinnedAbove"];

/**
 * The card's forward estimate when it can be drawn: a P/E or P/S card with a dial and a well-formed estimate. A
 * malformed estimate is ignored so the card still renders as before.
 */
export function forwardEstimate(card: ValuationGaugeCard): ValuationGaugeForwardEstimate | null {
  const forward = card.forward;
  if (!forward || (card.kind !== "Pe" && card.kind !== "Ps") || !card.geometry) return null;
  const { value, angle, side } = forward;
  if (typeof value !== "string" || !/^-?\d+(\.\d+)?$/.test(value.trim())) return null;
  if (typeof angle !== "number" || !Number.isFinite(angle) || angle < 0 || angle > 180) return null;
  if (!NEEDLE_SIDES.includes(side)) return null;
  return forward;
}

/** The provider's forward value in «جزئیات»: whose calculation it is, and that it is neither certain nor advice. */
export const PROVIDER_FORWARD_NOTE = `به محاسبهٔ ${SOURCE_NAME} است؛ پیش‌بینی قطعی و توصیهٔ سرمایه‌گذاری نیست.`;

/** The sentence for «جزئیات» that explains the dashed needle of a card, by the source of its forward value. */
export function forwardDetailsText(
  name: string,
  kind: ValuationGaugeKind,
  forward: ValuationGaugeForwardEstimate,
): string {
  return isProviderForward(forward)
    ? `نشانگر خط‌چین با حلقه ${name} ${PROVIDER_FORWARD_LABEL} ${PROVIDER_FORWARD_NOTE}`
    : `نشانگر خط‌چین با حلقه ${name} ${FORWARD_LABEL} است. ${forwardMethodText(kind)}`;
}

/** The method sentence of Sapio's estimate for «جزئیات»; the P/S estimate does not use the margin. */
export function forwardMethodText(kind: ValuationGaugeKind): string {
  return kind === "Pe"
    ? "برآورد ساپیو بر پایهٔ فروش ۱۲ ماه اخیر، رشد سالانهٔ ۴ ماه اخیر و میانهٔ حاشیهٔ سود ۸ فصل؛ پیش‌بینی قطعی و توصیهٔ سرمایه‌گذاری نیست."
    : "برآورد ساپیو بر پایهٔ فروش ۱۲ ماه اخیر و رشد سالانهٔ ۴ ماه اخیر؛ پیش‌بینی قطعی و توصیهٔ سرمایه‌گذاری نیست.";
}

/** Tehran local date in the Persian calendar, for example «۱۴۰۵/۰۹/۰۵». */
export function formatTehranDate(isoUtc: string): string {
  const date = new Date(isoUtc);
  if (Number.isNaN(date.getTime())) return "—";
  const parts = new Intl.DateTimeFormat("en-US-u-ca-persian-nu-latn", {
    timeZone: "Asia/Tehran",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).formatToParts(date);
  const part = (type: string) => parts.find((item) => item.type === type)?.value ?? "";
  return toPersianDigits(`${part("year")}/${part("month")}/${part("day")}`);
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
  if (card.reasonCode === "coverage_miss") return `این نماد در پوشش دادهٔ گیج ${SOURCE_NAME} نیست`;
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
export function bandPath(
  startAngle: number,
  endAngle: number,
  outer = 124,
  inner = 82,
  cx = 150,
  cy = 142,
): string {
  const a = dialPoint(startAngle, outer, cx, cy);
  const b = dialPoint(endAngle, outer, cx, cy);
  const c = dialPoint(endAngle, inner, cx, cy);
  const d = dialPoint(startAngle, inner, cx, cy);
  const large = endAngle - startAngle > 180 ? 1 : 0;
  const f = (value: number) => value.toFixed(2);
  return `M ${f(a.x)} ${f(a.y)} A ${outer} ${outer} 0 ${large} 1 ${f(b.x)} ${f(b.y)} L ${f(c.x)} ${f(c.y)} A ${inner} ${inner} 0 ${large} 0 ${f(d.x)} ${f(d.y)} Z`;
}
