/*
 * Tehran calendar days as UTC instants (Feature 164). Day values are `YYYY-MM-DD`, as the Jalali date filter produces.
 * The offset comes from the IANA zone through Intl, so days before 2022 (when Iran still observed daylight saving)
 * convert correctly too.
 */

const tehran = "Asia/Tehran";
const dayPattern = /^(\d{4})-(\d{2})-(\d{2})$/;

const tehranParts = new Intl.DateTimeFormat("en-US", {
  timeZone: tehran,
  hourCycle: "h23",
  year: "numeric",
  month: "2-digit",
  day: "2-digit",
  hour: "2-digit",
  minute: "2-digit",
  second: "2-digit",
});

const tehranDay = new Intl.DateTimeFormat("en-CA", {
  timeZone: tehran,
  year: "numeric",
  month: "2-digit",
  day: "2-digit",
});

const dayMs = 24 * 60 * 60 * 1000;

function tehranOffsetMs(instant: number): number {
  const parts = Object.fromEntries(
    tehranParts.formatToParts(new Date(instant)).map((part) => [part.type, part.value]),
  );
  const wallClock = Date.UTC(
    Number(parts.year),
    Number(parts.month) - 1,
    Number(parts.day),
    Number(parts.hour),
    Number(parts.minute),
    Number(parts.second),
  );
  return wallClock - Math.floor(instant / 1000) * 1000;
}

/** The UTC instant at which the given Tehran calendar day starts; undefined for a value that is not a day. */
export function tehranDayStartUtc(day: string): Date | undefined {
  const match = dayPattern.exec(day);
  if (!match) return undefined;
  const wallClockMidnight = Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3]));
  if (Number.isNaN(wallClockMidnight)) return undefined;
  // Two passes settle the offset on either side of a daylight-saving change.
  let instant = wallClockMidnight - tehranOffsetMs(wallClockMidnight);
  instant = wallClockMidnight - tehranOffsetMs(instant);
  return new Date(instant);
}

/**
 * The API range for whole Tehran days: `fromUtc` is the start of `from` (inclusive) and `toUtc` the start of the day
 * after `to` (exclusive). An empty or invalid day leaves that side open.
 */
export function tehranDayRangeToUtc(
  from: string,
  to: string,
): { fromUtc?: string; toUtc?: string } {
  const start = from ? tehranDayStartUtc(from) : undefined;
  const lastDayStart = to ? tehranDayStartUtc(to) : undefined;
  const end = lastDayStart ? tehranDayStartUtc(addDays(to, 1)) : undefined;
  return { fromUtc: start?.toISOString(), toUtc: end?.toISOString() };
}

/** Today in Tehran as `YYYY-MM-DD`. */
export function tehranToday(now: Date = new Date()): string {
  return tehranDay.format(now);
}

/** The last `days` Tehran calendar days ending today, for a default filter. */
export function lastTehranDays(days: number, now: Date = new Date()): { from: string; to: string } {
  const to = tehranToday(now);
  return { from: addDays(to, -(days - 1)), to };
}

function addDays(day: string, amount: number): string {
  const match = dayPattern.exec(day)!;
  const date = new Date(
    Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3])) + amount * dayMs,
  );
  return date.toISOString().slice(0, 10);
}
