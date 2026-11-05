import type {
  BalanceSheetBaselineCandidate,
  BalanceSheetChange,
  BalanceSheetReportResult,
  BalanceSheetRow,
  BalanceSheetSection,
  BalanceSheetSnapshot,
} from "./balance-sheet-report-types";

/**
 * Feature 154 view models. Everything here is formatting and selection of server-computed values: no balance,
 * change, percentage or ratio is calculated in the browser (AC-C12).
 */

/** Initial design value for the two-sided comparison (PD-25); validated in T22. */
export const BALANCE_SHEET_COMPARISON_TWO_SIDED_MIN_WIDTH = 1100;
/** Below this container width the compact (mobile) layout is used. */
export const BALANCE_SHEET_WIDE_MIN_WIDTH = 768;
export const BALANCE_SHEET_RATIO_LABEL = "نسبت حقوق صاحبان سهام به کل دارایی‌ها";
export const BALANCE_SHEET_CHANGE_HEADER = "درصد تغییر";
export const BALANCE_SHEET_AUDITED = "حسابرسی‌شده";
export const BALANCE_SHEET_UNAUDITED = "حسابرسی‌نشده";
export const BALANCE_SHEET_RESTATED = "تجدید ارائه‌شده";
export const MISSING = "—";

const amountFormat = new Intl.NumberFormat("fa-IR-u-nu-arabext", { maximumFractionDigits: 2 });
const twoDecimals = new Intl.NumberFormat("fa-IR-u-nu-arabext", {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

/** Half away from zero at two decimals, matching the backend rounding policy. */
function roundHalfAwayFromZero(value: number): number {
  const scaled = Math.round(Math.abs(value) * 100 + 1e-9) / 100;
  return value < 0 ? -scaled : scaled;
}

export function toPersianDigits(value: string): string {
  return value.replace(/[0-9]/g, (digit) => String.fromCharCode(0x06f0 + Number(digit)));
}

/** «۰» for zero, «—» for missing, accounting parentheses for negatives. */
export function formatBalanceAmount(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return MISSING;
  if (value === 0) return "۰";
  const text = amountFormat.format(Math.abs(value));
  return value < 0 ? `(${text})` : text;
}

export function formatRatio(value: number | null | undefined): string {
  if (value === null || value === undefined) return MISSING;
  const rounded = roundHalfAwayFromZero(value);
  const text = `${twoDecimals.format(Math.abs(rounded))}٪`;
  return rounded < 0 ? `−${text}` : text;
}

export function formatSignedPercent(value: number | null | undefined): string {
  if (value === null || value === undefined) return MISSING;
  const rounded = roundHalfAwayFromZero(value);
  const text = `${twoDecimals.format(Math.abs(rounded))}٪`;
  return rounded > 0 ? `+${text}` : rounded < 0 ? `−${text}` : text;
}

export function formatPercentagePoints(value: number | null | undefined): string {
  if (value === null || value === undefined) return MISSING;
  const rounded = roundHalfAwayFromZero(value);
  const text = `${twoDecimals.format(Math.abs(rounded))} واحد درصد`;
  return rounded > 0 ? `+${text}` : rounded < 0 ? `−${text}` : text;
}

export function formatSignedAmount(value: number | null | undefined): string {
  if (value === null || value === undefined) return MISSING;
  if (value === 0) return "۰";
  const text = amountFormat.format(Math.abs(value));
  return value > 0 ? `+${text}` : `−${text}`;
}

export function jalaliDisplay(jalali: string | null | undefined): string {
  return jalali ? toPersianDigits(jalali) : MISSING;
}

export const BASELINE_REASON_TEXT: Record<string, string> = {
  missing_fiscal_metadata: "اطلاعات سال مالی ناشر موجود نیست",
  fiscal_calendar_changed: "سال مالی ناشر تغییر کرده است",
  anchor_not_on_fiscal_grid: "تاریخ گزارش بر پایان فصل مالی منطبق نیست",
  no_earlier_report: "گزارش قبلی موجود نیست",
  no_matching_stage: "گزارش هم‌دوره سال قبل موجود نیست",
  no_snapshot_at_date: "در این تاریخ ترازنامه جداگانه موجود نیست",
  conflicting_report_revision: "نسخه‌های هم‌رتبه متناقض‌اند",
  too_many_report_versions: "نسخه‌های هم‌رتبه بیش از حد مجاز خواندن است",
};

export const CHANGE_REASON_TEXT: Record<string, string> = {
  zero_baseline: "مانده مبنا صفر است",
  negative_baseline: "مانده مبنا منفی است",
  baseline_not_observed: "در گزارش مبنا گزارش نشده است",
  anchor_missing: "داده موجود نیست",
  baseline_missing: "داده مبنا موجود نیست",
  anchor_ambiguous: "مشاهده تکراری",
  baseline_ambiguous: "مشاهده تکراری در مبنا",
};

export function baselineLabel(
  candidate: Pick<BalanceSheetBaselineCandidate, "label" | "jalaliBaselineDate">,
): string {
  return candidate.jalaliBaselineDate
    ? `${candidate.label} (${jalaliDisplay(candidate.jalaliBaselineDate)})`
    : candidate.label;
}

export interface BalanceSheetViewRow {
  key: string;
  rowId: string;
  label: string;
  kind: string;
  /** group heading rows have no values */
  heading: boolean;
  indent: boolean;
  value: string;
  negative: boolean;
  missing: boolean;
  missingReason?: string;
  baselineValue?: string;
  baselineNegative?: boolean;
  percent?: string;
  amountChange?: string;
  changeNote?: string;
  direction?: "up" | "down" | "flat";
}

export interface BalanceSheetViewSide {
  sectionId: string;
  label: string;
  caption: string;
  rows: BalanceSheetViewRow[];
  total?: BalanceSheetViewRow;
}

export interface BalanceSheetStatementBadge {
  date: string;
  audit: string;
  restated: boolean;
}

export interface BalanceSheetView {
  title: string;
  companyLine: string;
  symbol: string;
  metadata: string[];
  anchor: BalanceSheetStatementBadge;
  templateId: string;
  assets: BalanceSheetViewSide;
  liabilities: BalanceSheetViewSide;
  equity: BalanceSheetViewSide;
  finalAssets: BalanceSheetViewRow;
  finalLiabilitiesAndEquity: BalanceSheetViewRow;
  ratio: { anchor: string; baseline?: string; pp?: string; negative: boolean };
  notices: string[];
  analytical: BalanceSheetViewRow[];
  comparison?: {
    kind: string;
    label: string;
    available: boolean;
    unavailableText?: string;
    alternatives: string[];
    baseline?: BalanceSheetStatementBadge;
  };
}

function badge(snapshot: BalanceSheetSnapshot): BalanceSheetStatementBadge {
  return {
    date: jalaliDisplay(snapshot.statement.jalaliPeriodEnd),
    audit: snapshot.statement.isAudited ? BALANCE_SHEET_AUDITED : BALANCE_SHEET_UNAUDITED,
    restated: snapshot.statement.isRepresented,
  };
}

/**
 * One owner for a dated balance's status text (spec §8.3 item 1, §8.5): its own audit status, plus
 * «تجدید ارائه‌شده» exactly when the statement is restated. Wide, compact and PNG all render this text.
 */
export function statementBadgeText(statement: BalanceSheetStatementBadge): string {
  return statement.restated ? `${statement.audit} · ${BALANCE_SHEET_RESTATED}` : statement.audit;
}

function rowsOf(
  section: BalanceSheetSection,
): Array<{ row: BalanceSheetRow; heading?: string; indent: boolean }> {
  const out: Array<{ row: BalanceSheetRow; heading?: string; indent: boolean }> = [];
  for (const group of section.groups) {
    const grouped = group.groupId !== "none";
    if (grouped && group.label)
      out.push({ row: group.subtotal ?? group.rows[0], heading: group.label, indent: false });
    for (const row of group.rows) out.push({ row, indent: grouped });
    if (group.subtotal) out.push({ row: group.subtotal, indent: false });
  }
  return out;
}

function toViewRow(
  row: BalanceSheetRow,
  key: string,
  indent: boolean,
  baselineRows?: Map<string, BalanceSheetRow>,
  change?: BalanceSheetChange,
): BalanceSheetViewRow {
  const value = row.cell.state === "available" ? row.cell.value : null;
  const baselineRow = baselineRows?.get(row.rowId);
  const baselineValue =
    baselineRow && baselineRow.cell.state === "available" ? baselineRow.cell.value : null;
  const view: BalanceSheetViewRow = {
    key,
    rowId: row.rowId,
    label: row.label,
    kind: row.kind,
    heading: false,
    indent,
    value: formatBalanceAmount(value),
    negative: value !== null && value < 0,
    missing: value === null,
    missingReason: value === null ? "داده موجود نیست" : undefined,
  };
  if (baselineRows) {
    view.baselineValue = formatBalanceAmount(baselineValue);
    view.baselineNegative = baselineValue !== null && baselineValue < 0;
    view.percent =
      change && change.state === "available" ? formatSignedPercent(change.percent) : MISSING;
    view.amountChange =
      change && change.state !== "unavailable" ? formatSignedAmount(change.amount) : MISSING;
    view.changeNote = change?.reason
      ? (CHANGE_REASON_TEXT[change.reason] ?? change.reason)
      : undefined;
    view.direction =
      change?.amount == null
        ? undefined
        : change.amount > 0
          ? "up"
          : change.amount < 0
            ? "down"
            : "flat";
  }
  return view;
}

function side(
  section: BalanceSheetSection,
  baselineRows?: Map<string, BalanceSheetRow>,
  changes?: Record<string, BalanceSheetChange>,
): BalanceSheetViewSide {
  const rows = rowsOf(section).map(({ row, heading, indent }, index) =>
    heading
      ? {
          key: `${section.sectionId}-h-${index}`,
          rowId: `${row.rowId}-heading`,
          label: heading,
          kind: "group",
          heading: true,
          indent: false,
          value: "",
          negative: false,
          missing: false,
        }
      : toViewRow(
          row,
          `${section.sectionId}-${row.rowId}`,
          indent,
          baselineRows,
          changes?.[row.rowId],
        ),
  );
  return {
    sectionId: section.sectionId,
    label: section.label,
    caption: section.caption,
    rows,
    total: section.total
      ? toViewRow(
          section.total,
          `${section.sectionId}-total`,
          false,
          baselineRows,
          changes?.[section.total.rowId],
        )
      : undefined,
  };
}

function allRows(snapshot: BalanceSheetSnapshot): BalanceSheetRow[] {
  return [
    ...snapshot.sections.flatMap((section) => [
      ...section.groups.flatMap((group) =>
        group.subtotal ? [...group.rows, group.subtotal] : group.rows,
      ),
      ...(section.total ? [section.total] : []),
    ]),
    snapshot.finalTotals.totalAssets,
    snapshot.finalTotals.totalLiabilitiesAndEquity,
    ...snapshot.analyticalItems,
  ];
}

export function snapshotNotices(snapshot: BalanceSheetSnapshot): string[] {
  const date = jalaliDisplay(snapshot.statement.jalaliPeriodEnd);
  const notices: string[] = [];
  if (snapshot.reconciliation.r1.state === "discrepancy")
    notices.push(
      `در ${date} جمع کل دارایی‌ها با جمع بدهی‌ها و حقوق صاحبان سهام گزارش‌شده برابر نیست (اختلاف ${formatBalanceAmount(snapshot.reconciliation.r1.difference ?? null)}).`,
    );
  if (snapshot.reconciliation.r2.state === "discrepancy")
    notices.push(
      `در ${date} جمع کل دارایی‌ها با مجموع جمع کل بدهی‌ها و جمع حقوق صاحبان سهام برابر نیست (اختلاف ${formatBalanceAmount(snapshot.reconciliation.r2.difference ?? null)}).`,
    );
  for (const row of allRows(snapshot)) {
    const discrepancy = row.cell.aliasDiscrepancy;
    if (discrepancy)
      notices.push(
        `${row.label}: مقدار منبع جایگزین ${toPersianDigits(String(discrepancy.otherSourceItemId))} برابر ${formatBalanceAmount(discrepancy.otherValue)} است (اختلاف ${formatBalanceAmount(discrepancy.difference)}).`,
      );
  }
  return notices;
}

export function anchorSnapshot(result: BalanceSheetReportResult): BalanceSheetSnapshot | null {
  if (result.outcome !== "available" || !result.anchorStatementId) return null;
  return result.snapshots[result.anchorStatementId] ?? null;
}

export function baselineCandidate(
  result: BalanceSheetReportResult,
  kind: string | null,
): BalanceSheetBaselineCandidate | null {
  if (!kind) return null;
  return result.comparison?.candidates.find((candidate) => candidate.kind === kind) ?? null;
}

export function buildBalanceSheetView(
  result: BalanceSheetReportResult,
  comparisonKind: string | null,
): BalanceSheetView | null {
  const anchor = anchorSnapshot(result);
  if (!anchor) return null;
  const candidate = baselineCandidate(result, comparisonKind);
  const baseline =
    candidate?.state === "available" && candidate.statementId
      ? result.snapshots[candidate.statementId]
      : undefined;
  const baselineRows = comparisonKind
    ? new Map(baseline ? allRows(baseline).map((row) => [row.rowId, row]) : [])
    : undefined;
  const changes = candidate && baseline ? candidate.rowChanges : comparisonKind ? {} : undefined;
  const find = (id: string) => anchor.sections.find((section) => section.sectionId === id)!;
  const statement = anchor.statement;
  const metadata = [
    `تاریخ: ${jalaliDisplay(statement.jalaliPeriodEnd)}`,
    statement.periodMonths > 0
      ? `گزارش ${toPersianDigits(String(statement.periodMonths))} ماهه`
      : "",
    // Owner instruction 2026-11-05: the standalone basis is the default and is not labelled; only consolidated is.
    statement.isComposing ? "تلفیقی" : "",
    statement.isAudited ? BALANCE_SHEET_AUDITED : BALANCE_SHEET_UNAUDITED,
    statement.isRepresented ? BALANCE_SHEET_RESTATED : "",
    statement.jalaliPublishedAt
      ? `تاریخ انتشار: ${jalaliDisplay(statement.jalaliPublishedAt)}`
      : "",
    "میلیون ریال",
  ].filter(Boolean);
  const company =
    result.company.name && result.company.name !== result.company.symbol
      ? `${result.company.name} (${result.company.symbol})`
      : result.company.symbol;
  const ratioChange = candidate?.ratioChange;
  return {
    title: result.title,
    companyLine: company,
    symbol: result.company.symbol,
    metadata,
    anchor: badge(anchor),
    templateId: result.templateId ?? "",
    assets: side(find("assets"), baselineRows, changes),
    liabilities: side(find("liabilities"), baselineRows, changes),
    equity: side(find("equity"), baselineRows, changes),
    finalAssets: toViewRow(
      anchor.finalTotals.totalAssets,
      "final-assets",
      false,
      baselineRows,
      changes?.total_assets,
    ),
    finalLiabilitiesAndEquity: toViewRow(
      anchor.finalTotals.totalLiabilitiesAndEquity,
      "final-lne",
      false,
      baselineRows,
      changes?.total_liabilities_and_equity,
    ),
    ratio: {
      anchor: formatRatio(anchor.ratio.value),
      baseline: comparisonKind
        ? formatRatio(baseline ? (ratioChange?.baseline ?? baseline.ratio.value) : null)
        : undefined,
      pp: comparisonKind
        ? formatPercentagePoints(baseline ? (ratioChange?.ppDifference ?? null) : null)
        : undefined,
      negative: (anchor.ratio.value ?? 0) < 0,
    },
    notices: [...snapshotNotices(anchor), ...(baseline ? snapshotNotices(baseline) : [])],
    analytical: anchor.analyticalItems.map((row) =>
      toViewRow(row, `analytical-${row.rowId}`, false),
    ),
    comparison:
      comparisonKind && candidate
        ? {
            kind: candidate.kind,
            label: baselineLabel(candidate),
            available: Boolean(baseline),
            unavailableText: baseline
              ? undefined
              : `مبنای مقایسه «${baselineLabel(candidate)}» در دسترس نیست: ${BASELINE_REASON_TEXT[candidate.reason ?? ""] ?? candidate.reason ?? "نامشخص"}.`,
            alternatives: candidate.alternatives
              .map((kind) => result.comparison?.candidates.find((item) => item.kind === kind))
              .filter((item): item is BalanceSheetBaselineCandidate => Boolean(item))
              .map(baselineLabel),
            baseline: baseline ? badge(baseline) : undefined,
          }
        : undefined,
  };
}

export interface BalanceSheetChartDatum {
  label: string;
  liabilities: number | null;
  equity: number | null;
  ratio: number | null;
  totalAssets: number | null;
  stackRepresentsAssets: boolean;
  negativeEquity: boolean;
  r2Difference: number | null;
  warning: boolean;
  gapReason: string | null;
}

/** Chart values are read from the stored snapshots, oldest first (no arithmetic). */
export function balanceSheetChartData(result: BalanceSheetReportResult): BalanceSheetChartDatum[] {
  return result.chart.points.map((point) => {
    const snapshot = point.statementId ? result.snapshots[point.statementId] : undefined;
    const sectionTotal = (id: string) => {
      const cell = snapshot?.sections.find((section) => section.sectionId === id)?.total?.cell;
      return cell && cell.state === "available" ? cell.value : null;
    };
    const assets = snapshot?.finalTotals.totalAssets.cell;
    return {
      label: jalaliDisplay(point.jalaliPeriodEnd),
      liabilities: sectionTotal("liabilities"),
      equity: sectionTotal("equity"),
      ratio: snapshot?.ratio.value ?? null,
      totalAssets: assets && assets.state === "available" ? assets.value : null,
      stackRepresentsAssets: point.stackRepresentsAssets,
      negativeEquity: point.negativeEquity,
      r2Difference: point.r2Difference ?? null,
      warning: point.r2State === "discrepancy",
      gapReason: point.gapReason ?? null,
    };
  });
}

/** Renderable when the typed result is present; non-available outcomes show their typed message. */
export function isRenderableBalanceSheet(
  result: BalanceSheetReportResult | null | undefined,
): result is BalanceSheetReportResult {
  return Boolean(result && result.contractVersion);
}
