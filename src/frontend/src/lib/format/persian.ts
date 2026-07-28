// Persian number formatting + finance helpers.
const PERSIAN_DIGITS = ["۰", "۱", "۲", "۳", "۴", "۵", "۶", "۷", "۸", "۹"];

export function toPersianDigits(input: string | number): string {
  return String(input).replace(/[0-9]/g, (d) => PERSIAN_DIGITS[Number(d)]);
}

export function formatNumber(n: number, opts?: Intl.NumberFormatOptions): string {
  return toPersianDigits(
    new Intl.NumberFormat("en-US", opts).format(n),
  );
}

export function formatPercent(n: number, withSign = true): string {
  const sign = withSign && n > 0 ? "+" : "";
  return `${sign}${toPersianDigits(n.toFixed(2))}٪`;
}

export function formatRial(n: number): string {
  return `${formatNumber(n)} ریال`;
}

export function formatBig(n: number, unit = "میلیارد"): string {
  return `${formatNumber(n)} ${unit}`;
}

export function relativeTime(iso: string): string {
  const diff = (Date.now() - new Date(iso).getTime()) / 1000;
  if (diff < 60) return "همین الان";
  if (diff < 3600) return toPersianDigits(Math.floor(diff / 60)) + " دقیقه پیش";
  if (diff < 86400) return toPersianDigits(Math.floor(diff / 3600)) + " ساعت پیش";
  return toPersianDigits(Math.floor(diff / 86400)) + " روز پیش";
}
