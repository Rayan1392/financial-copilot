import type { AdminUserAnalyticsCell } from "@/integrations/financial-copilot/admin-client";

/**
 * Pure slicing, pivoting and drill-down over the aggregated admin user analytics cube.
 * The server owns classification (plan precedence, status, activity buckets, reporting day);
 * this module only filters and re-aggregates those cells for presentation.
 */

export type DimensionKey = "acquisitionSource" | "plan" | "status" | "activity";
export type PeriodKey = "all" | "7d" | "30d" | "90d" | "thisMonth" | "12m";
export type Selections = Partial<Record<DimensionKey, string[]>>;
export type DimensionValue = { code: string; label: string; color: string };
export type DateRange = { from: string; to: string };

export const NO_PLAN = "none";
export const RESTRICTED_PLAN = "restricted";
const NON_PAID_PLANS = new Set(["Free", NO_PLAN, RESTRICTED_PLAN]);

export const dimensionLabels: Record<DimensionKey, string> = {
  acquisitionSource: "منبع جذب",
  plan: "اشتراک",
  status: "وضعیت حساب",
  activity: "آخرین ورود",
};

export const periodOptions: { value: PeriodKey; label: string }[] = [
  { value: "all", label: "همه" },
  { value: "7d", label: "۷ روز" },
  { value: "30d", label: "۳۰ روز" },
  { value: "90d", label: "۹۰ روز" },
  { value: "thisMonth", label: "ماه جاری" },
  { value: "12m", label: "۱۲ ماه" },
];

/** Categorical slots in fixed order (validated reference palette, light surface). */
const SERIES = [
  "#2a78d6",
  "#eb6834",
  "#1baf7a",
  "#eda100",
  "#e87ba4",
  "#008300",
  "#6250d6",
  "#e34948",
];
const NEUTRAL = "#a3a29c";
/** Activity is ordinal (recency), so it uses one hue dark→light instead of categorical slots. */
const ACTIVITY_RAMP = ["#174f93", "#2a78d6", "#7eaee8", "#c3d8f3"];

const knownValues: Record<DimensionKey, DimensionValue[]> = {
  acquisitionSource: [
    { code: "native", label: "ثبت‌نام مستقیم", color: SERIES[0] },
    { code: "tahlilapp", label: "تحلیل‌اپ", color: SERIES[1] },
    { code: "unknown", label: "نامشخص", color: NEUTRAL },
  ],
  plan: [
    { code: "Free", label: "رایگان", color: SERIES[0] },
    { code: "Plus", label: "پلاس", color: SERIES[1] },
    { code: "Pro", label: "پرو", color: SERIES[2] },
    { code: "Max", label: "مکس", color: SERIES[3] },
  ],
  status: [
    { code: "enabled", label: "فعال", color: SERIES[0] },
    { code: "locked", label: "قفل‌شده", color: SERIES[1] },
    { code: "disabled", label: "غیرفعال", color: NEUTRAL },
  ],
  activity: [
    { code: "active7d", label: "۷ روز اخیر", color: ACTIVITY_RAMP[0] },
    { code: "active30d", label: "۸ تا ۳۰ روز پیش", color: ACTIVITY_RAMP[1] },
    { code: "active90d", label: "۳۱ تا ۹۰ روز پیش", color: ACTIVITY_RAMP[2] },
    { code: "dormant", label: "بیش از ۹۰ روز", color: ACTIVITY_RAMP[3] },
    { code: "never", label: "بدون ورود", color: NEUTRAL },
  ],
};

const tailValues: Partial<Record<DimensionKey, DimensionValue[]>> = {
  plan: [
    { code: NO_PLAN, label: "بدون اشتراک فعال", color: NEUTRAL },
    { code: RESTRICTED_PLAN, label: "بدون مجوز مشاهده", color: NEUTRAL },
  ],
};

/**
 * Ordered values of a dimension: the known order first, then any unexpected codes present in the
 * data (so nothing is silently dropped), then the "no value" tail. Colors follow the entity, not rank.
 */
export function dimensionValues(
  dimension: DimensionKey,
  cells: AdminUserAnalyticsCell[],
  planNames: Record<string, string> = {},
): DimensionValue[] {
  const known = knownValues[dimension];
  const tail = tailValues[dimension] ?? [];
  const listed = new Set([...known, ...tail].map((value) => value.code));
  const extras = [...new Set(cells.map((cell) => cell[dimension]))]
    .filter((code) => !listed.has(code))
    .sort()
    .map((code, index) => ({
      code,
      label: planNames[code] ?? code,
      color: SERIES[(known.length + index) % SERIES.length],
    }));
  return [...known, ...extras, ...tail];
}

export function isPaidPlan(plan: string) {
  return !NON_PAID_PLANS.has(plan);
}

export function sumUsers(cells: AdminUserAnalyticsCell[]) {
  return cells.reduce((total, cell) => total + cell.users, 0);
}

export function share(part: number, whole: number) {
  return whole > 0 ? (part / whole) * 100 : 0;
}

// ---- Dates (ISO yyyy-MM-dd day keys; Jalali only for bucketing and labels) ----

const DAY_MS = 86_400_000;
const isoDay = (date: Date) => date.toISOString().slice(0, 10);
const parseIso = (iso: string) => {
  const [year, month, day] = iso.split("-").map(Number);
  return new Date(Date.UTC(year, month - 1, day, 12));
};
const daysBetween = (from: string, to: string) =>
  Math.round((parseIso(to).getTime() - parseIso(from).getTime()) / DAY_MS);

export function addDays(iso: string, days: number) {
  const date = parseIso(iso);
  date.setUTCDate(date.getUTCDate() + days);
  return isoDay(date);
}

/** The reporting-zone calendar day of an instant (the server buckets registrations the same way). */
export function reportingDay(instantUtc: string, timeZone = "Asia/Tehran") {
  return new Intl.DateTimeFormat("en-CA", {
    timeZone,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(new Date(instantUtc));
}

const jalaliParts = new Intl.DateTimeFormat("en-US-u-ca-persian-nu-latn", {
  timeZone: "UTC",
  year: "numeric",
  month: "numeric",
  day: "numeric",
});
const jalaliCache = new Map<string, { year: number; month: number; day: number }>();

export function toJalali(iso: string) {
  let parts = jalaliCache.get(iso);
  if (!parts) {
    const values: Record<string, number> = {};
    for (const part of jalaliParts.formatToParts(parseIso(iso))) {
      if (part.type === "year" || part.type === "month" || part.type === "day") {
        values[part.type] = Number.parseInt(part.value, 10);
      }
    }
    parts = { year: values.year, month: values.month, day: values.day };
    jalaliCache.set(iso, parts);
  }
  return parts;
}

export function jalaliMonthKey(iso: string) {
  const { year, month } = toJalali(iso);
  return `${year}-${String(month).padStart(2, "0")}`;
}

function jalaliMonthStart(iso: string) {
  let day = iso;
  while (toJalali(day).day > 1) day = addDays(day, -1);
  return day;
}

const faMonth = new Intl.DateTimeFormat("fa-IR-u-ca-persian", {
  timeZone: "UTC",
  month: "short",
  year: "2-digit",
});
const faDay = new Intl.DateTimeFormat("fa-IR-u-ca-persian", {
  timeZone: "UTC",
  month: "short",
  day: "numeric",
});
const faFullDay = new Intl.DateTimeFormat("fa-IR-u-ca-persian", {
  timeZone: "UTC",
  dateStyle: "medium",
});

export const formatJalaliMonth = (iso: string) => faMonth.format(parseIso(iso));
export const formatJalaliDay = (iso: string) => faDay.format(parseIso(iso));
export const formatJalaliFullDay = (iso: string) => faFullDay.format(parseIso(iso));

/** Inclusive registration-day range for a period ending on `today`; null means no date restriction. */
export function periodRange(period: PeriodKey, today: string): DateRange | null {
  switch (period) {
    case "all":
      return null;
    case "7d":
      return { from: addDays(today, -6), to: today };
    case "30d":
      return { from: addDays(today, -29), to: today };
    case "90d":
      return { from: addDays(today, -89), to: today };
    case "thisMonth":
      return { from: jalaliMonthStart(today), to: today };
    case "12m": {
      let start = jalaliMonthStart(today);
      for (let index = 0; index < 11; index += 1) start = jalaliMonthStart(addDays(start, -1));
      return { from: start, to: today };
    }
  }
}

/**
 * The comparable window right before the period, for growth deltas: the same number of days for
 * rolling periods, and the same elapsed days of the previous Jalali month for "this month".
 */
export function previousRange(period: PeriodKey, today: string): DateRange | null {
  const current = periodRange(period, today);
  if (!current || period === "12m") return null;
  if (period === "thisMonth") {
    const from = jalaliMonthStart(addDays(current.from, -1));
    const sameElapsed = addDays(from, daysBetween(current.from, current.to));
    const lastOfPrevious = addDays(current.from, -1);
    return { from, to: sameElapsed < lastOfPrevious ? sameElapsed : lastOfPrevious };
  }
  const length = daysBetween(current.from, current.to) + 1;
  return { from: addDays(current.from, -length), to: addDays(current.from, -1) };
}

const inRange = (day: string | null, range: DateRange) =>
  day !== null && day >= range.from && day <= range.to;

// ---- Filtering ----

/**
 * Applies the period and dimension selections. `ignore` leaves one dimension unfiltered so a
 * slicer can keep showing its sibling values (cross-filtering, as in common BI tools).
 */
export function filterCells(
  cells: AdminUserAnalyticsCell[],
  selections: Selections,
  range: DateRange | null,
  ignore?: DimensionKey,
) {
  const active = (Object.entries(selections) as [DimensionKey, string[] | undefined][]).filter(
    (entry): entry is [DimensionKey, string[]] =>
      entry[0] !== ignore && !!entry[1] && entry[1].length > 0,
  );
  return cells.filter(
    (cell) =>
      (range === null || inRange(cell.registeredOn, range)) &&
      active.every(([dimension, values]) => values.includes(cell[dimension])),
  );
}

export function toggleSelection(
  selections: Selections,
  dimension: DimensionKey,
  code: string,
): Selections {
  const current = selections[dimension] ?? [];
  const next = current.includes(code)
    ? current.filter((value) => value !== code)
    : [...current, code];
  return { ...selections, [dimension]: next.length > 0 ? next : undefined };
}

/** Drill into one value of each given dimension (replaces earlier picks on those dimensions). */
export function drillInto(
  selections: Selections,
  picks: Partial<Record<DimensionKey, string>>,
): Selections {
  const next = { ...selections };
  for (const [dimension, code] of Object.entries(picks) as [DimensionKey, string][])
    next[dimension] = [code];
  return next;
}

export function countBy(cells: AdminUserAnalyticsCell[], dimension: DimensionKey) {
  const counts = new Map<string, number>();
  for (const cell of cells)
    counts.set(cell[dimension], (counts.get(cell[dimension]) ?? 0) + cell.users);
  return counts;
}

// ---- Pivot ----

export type PivotMode = "count" | "row" | "column" | "total";

export type Pivot = {
  rows: DimensionValue[];
  columns: DimensionValue[];
  values: number[][];
  rowTotals: number[];
  columnTotals: number[];
  total: number;
};

/** Cross-tab of two dimensions; empty rows and columns are dropped so only existing combinations show. */
export function buildPivot(
  cells: AdminUserAnalyticsCell[],
  rowDimension: DimensionKey,
  rowValues: DimensionValue[],
  columnDimension: DimensionKey,
  columnValues: DimensionValue[],
): Pivot {
  const index = new Map<string, number>();
  for (const cell of cells) {
    const key = `${cell[rowDimension]}\u0000${cell[columnDimension]}`;
    index.set(key, (index.get(key) ?? 0) + cell.users);
  }
  const allValues = rowValues.map((row) =>
    columnValues.map((column) => index.get(`${row.code}\u0000${column.code}`) ?? 0),
  );
  const allRowTotals = allValues.map((row) => row.reduce((sum, value) => sum + value, 0));
  const allColumnTotals = columnValues.map((_, column) =>
    allValues.reduce((sum, row) => sum + row[column], 0),
  );
  const keptRows = rowValues.map((_, row) => row).filter((row) => allRowTotals[row] > 0);
  const keptColumns = columnValues
    .map((_, column) => column)
    .filter((column) => allColumnTotals[column] > 0);
  return {
    rows: keptRows.map((row) => rowValues[row]),
    columns: keptColumns.map((column) => columnValues[column]),
    values: keptRows.map((row) => keptColumns.map((column) => allValues[row][column])),
    rowTotals: keptRows.map((row) => allRowTotals[row]),
    columnTotals: keptColumns.map((column) => allColumnTotals[column]),
    total: allRowTotals.reduce((sum, value) => sum + value, 0),
  };
}

export function pivotDisplayValue(pivot: Pivot, mode: PivotMode, row: number, column: number) {
  const value = pivot.values[row][column];
  if (mode === "row") return share(value, pivot.rowTotals[row]);
  if (mode === "column") return share(value, pivot.columnTotals[column]);
  if (mode === "total") return share(value, pivot.total);
  return value;
}

// ---- Drill-down tree ----

export type DrillNode = {
  id: string;
  dimension: DimensionKey;
  value: DimensionValue;
  users: number;
  shareOfParent: number;
  path: Partial<Record<DimensionKey, string>>;
  children: DrillNode[];
};

/** Hierarchical breakdown (e.g. source → plan → status); each node carries its share of its parent. */
export function buildDrillTree(
  cells: AdminUserAnalyticsCell[],
  levels: DimensionKey[],
  valuesFor: (dimension: DimensionKey) => DimensionValue[],
  parentPath: Partial<Record<DimensionKey, string>> = {},
  parentTotal = sumUsers(cells),
): DrillNode[] {
  const [dimension, ...rest] = levels;
  if (!dimension) return [];
  const counts = countBy(cells, dimension);
  return valuesFor(dimension)
    .filter((value) => (counts.get(value.code) ?? 0) > 0)
    .map((value) => {
      const users = counts.get(value.code) ?? 0;
      const path = { ...parentPath, [dimension]: value.code };
      return {
        id: Object.entries(path)
          .map(([key, code]) => `${key}=${code}`)
          .join("|"),
        dimension,
        value,
        users,
        shareOfParent: share(users, parentTotal),
        path,
        children: buildDrillTree(
          cells.filter((cell) => cell[dimension] === value.code),
          rest,
          valuesFor,
          path,
          users,
        ),
      };
    });
}

// ---- Trend ----

export type TrendBucket = {
  key: string;
  label: string;
  fullLabel: string;
  counts: Map<string, number>;
  total: number;
};

/**
 * Registrations over time, stacked by `stack`. Periods up to a month bucket by day; longer ones by
 * Jalali month. "All time" spans the first registration to today, capped to the latest `maxMonths`.
 */
export function buildTrend(
  cells: AdminUserAnalyticsCell[],
  stack: DimensionKey,
  range: DateRange | null,
  today: string,
  maxMonths = 24,
): { buckets: TrendBucket[]; granularity: "day" | "month"; truncated: boolean } {
  let earliest: string | null = null;
  for (const cell of cells) {
    if (cell.registeredOn !== null && (earliest === null || cell.registeredOn < earliest))
      earliest = cell.registeredOn;
  }
  const span: DateRange = range ?? { from: earliest ?? today, to: today };
  const granularity = daysBetween(span.from, span.to) + 1 <= 31 ? "day" : "month";
  const keyOf = (day: string) => (granularity === "day" ? day : jalaliMonthKey(day));

  const buckets = new Map<string, TrendBucket>();
  for (let day = span.from; day <= span.to; day = addDays(day, 1)) {
    const key = keyOf(day);
    if (!buckets.has(key)) {
      buckets.set(key, {
        key,
        label: granularity === "day" ? formatJalaliDay(day) : formatJalaliMonth(day),
        fullLabel: granularity === "day" ? formatJalaliFullDay(day) : formatJalaliMonth(day),
        counts: new Map(),
        total: 0,
      });
    }
  }
  for (const cell of cells) {
    if (cell.registeredOn === null) continue;
    const bucket = buckets.get(keyOf(cell.registeredOn));
    if (!bucket) continue;
    bucket.counts.set(cell[stack], (bucket.counts.get(cell[stack]) ?? 0) + cell.users);
    bucket.total += cell.users;
  }
  const ordered = [...buckets.values()];
  const truncated = granularity === "month" && ordered.length > maxMonths;
  return { buckets: truncated ? ordered.slice(-maxMonths) : ordered, granularity, truncated };
}

// ---- Export ----

const UTF8_BOM = String.fromCharCode(0xfeff);

const csvField = (value: string | number) => {
  const text = String(value);
  return /[",\n]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text;
};

/** Pivot as CSV (with a BOM so spreadsheet apps detect UTF-8 Persian text). */
export function pivotToCsv(
  pivot: Pivot,
  rowDimension: DimensionKey,
  columnDimension: DimensionKey,
) {
  const header = [
    `${dimensionLabels[rowDimension]} / ${dimensionLabels[columnDimension]}`,
    ...pivot.columns.map((column) => column.label),
    "جمع",
  ];
  const lines = pivot.rows.map((row, index) => [
    row.label,
    ...pivot.values[index],
    pivot.rowTotals[index],
  ]);
  const footer = ["جمع", ...pivot.columnTotals, pivot.total];
  return `${UTF8_BOM}${[header, ...lines, footer].map((line) => line.map(csvField).join(",")).join("\n")}\n`;
}
