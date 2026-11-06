/**
 * Decides how stale quotes are presented. The API flags a quote `isStale` by age alone, so on a day without
 * trading every quote is "stale" and a per-symbol warning on all of them carries no information. Here the newest
 * trading session among the quotes is the reference: only a quote from an earlier session is really behind, and
 * when every quote is old and that session is not today, the market is closed and one line says so.
 */

const TEHRAN = "Asia/Tehran";
const sessionDateFormat = new Intl.DateTimeFormat("en-CA", {
  timeZone: TEHRAN,
  year: "numeric",
  month: "2-digit",
  day: "2-digit",
});
const weekdayFormat = new Intl.DateTimeFormat("fa-IR", { timeZone: TEHRAN, weekday: "long" });
const dayMonthFormat = new Intl.DateTimeFormat("fa-IR-u-ca-persian", {
  timeZone: TEHRAN,
  day: "numeric",
  month: "long",
});

export interface SessionQuote {
  asOf?: string | null;
  isStale: boolean;
  /** Exchange session from the API. Preferred over `asOf`, which is the sync time and can be a later day. */
  tradingDate?: string | null;
}

export interface MarketSessionStatus {
  /** Newest trading date (Tehran, YYYY-MM-DD) among the quotes, or null when none has a timestamp. */
  lastSessionDate: string | null;
  /** Every quote is old and the newest session is before today: no trading today. */
  closed: boolean;
  /** Every quote is old but the newest session is today: the feed is behind. */
  delayed: boolean;
  /** One summary line for the list, or null when nothing needs saying. */
  label: string | null;
}

/** Tehran calendar date of an ISO timestamp, as YYYY-MM-DD; null for a missing or invalid value. */
export function tehranSessionDate(iso: string | null | undefined): string | null {
  if (!iso) return null;
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? null : sessionDateFormat.format(date);
}

/**
 * The session a quote belongs to: its exchange trading date when the API sends one, else the Tehran date of
 * `asOf`. The trading date matters because a Wednesday price synced on Thursday must still read «چهارشنبه»,
 * and a holiday never appears as a session because no price carries it.
 */
export function quoteSessionDate(quote: SessionQuote): string | null {
  const tradingDate = quote.tradingDate?.slice(0, 10);
  if (tradingDate && /^\d{4}-\d{2}-\d{2}$/.test(tradingDate)) return tradingDate;
  return tehranSessionDate(quote.asOf);
}

export function marketSessionStatus(
  quotes: readonly SessionQuote[],
  now: Date = new Date(),
): MarketSessionStatus {
  const sessions = quotes.map(quoteSessionDate).filter((date): date is string => date !== null);
  const lastSessionDate = sessions.length > 0 ? sessions.reduce((a, b) => (a > b ? a : b)) : null;
  const allStale = quotes.length > 0 && quotes.every((quote) => quote.isStale);
  const today = sessionDateFormat.format(now);
  const closed = allStale && lastSessionDate !== null && lastSessionDate < today;
  const delayed = allStale && lastSessionDate === today;
  let label: string | null = null;
  if (closed && lastSessionDate)
    label = `بازار تعطیل است · آخرین معامله ${sessionName(lastSessionDate, now)}`;
  else if (delayed) label = "داده‌های امروز با تأخیر به‌روز می‌شوند";
  return { lastSessionDate, closed, delayed, label };
}

/** A quote is really behind when the API marks it stale and its last trade is from an earlier session. */
export function isLaggingQuote(quote: SessionQuote, status: MarketSessionStatus): boolean {
  if (!quote.isStale) return false;
  const session = quoteSessionDate(quote);
  if (session === null || status.lastSessionDate === null) return true;
  return session < status.lastSessionDate;
}

/** «چهارشنبه» for a session within the last week, otherwise «۱۲ آبان», so the name is never ambiguous. */
function sessionName(sessionDate: string, now: Date): string {
  const noonTehran = new Date(`${sessionDate}T12:00:00+03:30`);
  const ageDays = (now.getTime() - noonTehran.getTime()) / 86_400_000;
  return ageDays < 6 ? weekdayFormat.format(noonTehran) : dayMonthFormat.format(noonTehran);
}
