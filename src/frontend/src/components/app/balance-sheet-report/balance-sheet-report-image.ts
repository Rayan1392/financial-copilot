import {
  downloadCanvasPng,
  drawBrandFooter,
  drawBrandHeader,
  exportDate,
  PNG_BRAND_HEIGHT,
  PNG_FONT,
  PNG_FOOTER_HEIGHT,
  rtlText,
  safeFileSymbol,
  wrap,
} from "../png-brand";
import {
  BALANCE_SHEET_CHART_SERIES,
  BALANCE_SHEET_CHART_TITLE,
} from "./balance-sheet-funding-chart";
import {
  BALANCE_SHEET_CHANGE_HEADER,
  BALANCE_SHEET_RATIO_LABEL,
  balanceSheetChartData,
  buildBalanceSheetView,
  formatBalanceAmount,
  formatRatio,
  statementBadgeText,
  type BalanceSheetView,
  type BalanceSheetViewRow,
  type BalanceSheetViewSide,
} from "./balance-sheet-report-model";
import type { BalanceSheetReportResult } from "./balance-sheet-report-types";

/** Initial design values (PD-25), validated in T22 with the widest وبملت amounts. */
export const BALANCE_SHEET_PNG_WIDTH = 1200;
export const BALANCE_SHEET_COMPARISON_PNG_WIDTH = 1600;
const PAD = 36;
const HEADER_HEIGHT = 104;
const CHART_HEIGHT = 350;
const ROW_HEIGHT = 44;
const CAPTION_HEIGHT = 34;
const COLUMN_HEADER_HEIGHT = 48;
/** Comparison date columns carry a second line with that balance's own audit/restatement status. */
const COMPARISON_COLUMN_HEADER_HEIGHT = 64;
const RATIO_HEIGHT = 56;
const COLORS = {
  ink: "#1f2937",
  header: "#164e63",
  headerLight: "#d9efed",
  border: "#e7d5bd",
  white: "#ffffff",
  subtotal: "#fffbeb",
  negative: "#ffe4e6",
  ratio: "#fef08a",
  ratioInk: "#78350f",
};

interface Layout {
  width: number;
  comparison: boolean;
  sideWidth: number;
  columns: number[];
}

function layoutFor(comparison: boolean): Layout {
  const width = comparison ? BALANCE_SHEET_COMPARISON_PNG_WIDTH : BALANCE_SHEET_PNG_WIDTH;
  const sideWidth = (width - PAD * 2 - 16) / 2;
  const columns = comparison ? [0.4, 0.22, 0.22, 0.16] : [0.62, 0.38];
  return { width, comparison, sideWidth, columns: columns.map((share) => share * sideWidth) };
}

function columnHeaderHeight(layout: Layout): number {
  return layout.comparison ? COMPARISON_COLUMN_HEADER_HEIGHT : COLUMN_HEADER_HEIGHT;
}

export interface BalanceSheetPngColumnHeader {
  title: string;
  /** The dated balance's own status (spec §8.3 item 1, §8.5); present only on comparison date columns. */
  badge?: string;
}

/** Column headers of each PNG side. Pure, so the comparison badges are testable without a canvas. */
export function balanceSheetPngColumnHeaders(
  view: BalanceSheetView,
  comparison: boolean,
): BalanceSheetPngColumnHeader[] {
  if (!comparison) return [{ title: "شرح" }, { title: "مبلغ" }];
  const baseline = view.comparison?.baseline;
  return [
    { title: "شرح" },
    { title: `مانده در ${view.anchor.date}`, badge: statementBadgeText(view.anchor) },
    {
      title: `مانده در ${baseline?.date ?? "—"}`,
      badge: baseline ? statementBadgeText(baseline) : undefined,
    },
    { title: BALANCE_SHEET_CHANGE_HEADER },
  ];
}

function drawHeaderCell(
  context: CanvasRenderingContext2D,
  header: BalanceSheetPngColumnHeader,
  right: number,
  top: number,
  width: number,
  height: number,
) {
  if (!header.badge) {
    drawCell(
      context,
      header.title,
      right,
      top,
      width,
      height,
      COLORS.headerLight,
      `600 14px ${PNG_FONT}`,
      COLORS.header,
      true,
    );
    return;
  }
  drawCell(
    context,
    "",
    right,
    top,
    width,
    height,
    COLORS.headerLight,
    `600 14px ${PNG_FONT}`,
    COLORS.header,
    true,
  );
  context.fillStyle = COLORS.header;
  context.font = `600 14px ${PNG_FONT}`;
  rtlText(context, header.title, right - 6, top + 24, width - 12);
  context.fillStyle = "#475569";
  context.font = `400 12px ${PNG_FONT}`;
  const lines = wrap(context, header.badge, width - 12).slice(0, 2);
  lines.forEach((line, index) =>
    rtlText(context, line, right - 6, top + 43 + index * 16, width - 12),
  );
}

function sideRows(
  side: BalanceSheetViewSide,
  extra: BalanceSheetViewRow[] = [],
): BalanceSheetViewRow[] {
  return [...side.rows, ...(side.total ? [side.total] : []), ...extra];
}

function drawCell(
  context: CanvasRenderingContext2D,
  text: string,
  right: number,
  top: number,
  width: number,
  height: number,
  fill: string,
  font: string,
  color: string,
  alignRight: boolean,
) {
  context.fillStyle = fill;
  context.fillRect(right - width, top, width, height);
  context.strokeStyle = COLORS.border;
  context.strokeRect(right - width, top, width, height);
  context.fillStyle = color;
  context.font = font;
  const lines = alignRight ? wrap(context, text, width - 12).slice(0, 2) : [text];
  const lineHeight = 19;
  const first = top + height / 2 - ((lines.length - 1) * lineHeight) / 2 + 6;
  lines.forEach((line, index) => {
    if (alignRight) rtlText(context, line, right - 6, first + index * lineHeight, width - 12);
    else {
      context.direction = "ltr";
      context.textAlign = "left";
      context.fillText(line, right - width + 6, first + index * lineHeight, width - 12);
    }
  });
}

function drawRow(
  context: CanvasRenderingContext2D,
  layout: Layout,
  row: BalanceSheetViewRow,
  right: number,
  top: number,
) {
  const strong = row.heading || row.kind === "subtotal" || row.kind === "total";
  const fill = row.heading ? COLORS.headerLight : strong ? COLORS.subtotal : COLORS.white;
  const weight = strong ? "700" : "400";
  const labelFont = `${weight} 15px ${PNG_FONT}`;
  drawCell(
    context,
    row.label,
    right,
    top,
    layout.columns[0],
    ROW_HEIGHT,
    fill,
    labelFont,
    row.heading ? COLORS.header : COLORS.ink,
    true,
  );
  if (row.heading) {
    const rest = layout.columns.slice(1).reduce((sum, value) => sum + value, 0);
    drawCell(
      context,
      "",
      right - layout.columns[0],
      top,
      rest,
      ROW_HEIGHT,
      fill,
      labelFont,
      COLORS.ink,
      true,
    );
    return;
  }
  const values = layout.comparison
    ? [row.value, row.baselineValue ?? "—", row.percent ?? "—"]
    : [row.value];
  let cellRight = right - layout.columns[0];
  values.forEach((value, index) => {
    const negative = index === 0 ? row.negative : index === 1 ? row.baselineNegative : false;
    drawCell(
      context,
      value,
      cellRight,
      top,
      layout.columns[index + 1],
      ROW_HEIGHT,
      negative ? COLORS.negative : fill,
      `${weight} 15px ${PNG_FONT}`,
      negative ? "#881337" : COLORS.ink,
      false,
    );
    cellRight -= layout.columns[index + 1];
  });
}

function drawSide(
  context: CanvasRenderingContext2D,
  layout: Layout,
  view: BalanceSheetView,
  side: BalanceSheetViewSide,
  rows: BalanceSheetViewRow[],
  right: number,
  top: number,
): number {
  context.fillStyle = COLORS.header;
  context.fillRect(right - layout.sideWidth, top, layout.sideWidth, CAPTION_HEIGHT);
  context.fillStyle = COLORS.white;
  context.font = `700 16px ${PNG_FONT}`;
  rtlText(context, `${side.label} (${side.caption})`, right - 10, top + 23);
  let y = top + CAPTION_HEIGHT;
  const headers = balanceSheetPngColumnHeaders(view, layout.comparison);
  const headerHeight = columnHeaderHeight(layout);
  let cellRight = right;
  headers.forEach((header, index) => {
    drawHeaderCell(context, header, cellRight, y, layout.columns[index], headerHeight);
    cellRight -= layout.columns[index];
  });
  y += headerHeight;
  rows.forEach((row) => {
    drawRow(context, layout, row, right, y);
    y += ROW_HEIGHT;
  });
  return y;
}

function drawChart(
  context: CanvasRenderingContext2D,
  result: BalanceSheetReportResult,
  width: number,
  top: number,
) {
  const data = balanceSheetChartData(result);
  context.fillStyle = COLORS.white;
  context.fillRect(PAD, top, width - PAD * 2, CHART_HEIGHT - 14);
  context.strokeStyle = "#e2e8f0";
  context.strokeRect(PAD, top, width - PAD * 2, CHART_HEIGHT - 14);
  context.fillStyle = COLORS.ink;
  context.font = `700 20px ${PNG_FONT}`;
  rtlText(context, BALANCE_SHEET_CHART_TITLE, width - PAD - 18, top + 30);
  context.fillStyle = "#64748b";
  context.font = `400 14px ${PNG_FONT}`;
  rtlText(context, "مبالغ: میلیون ریال | نسبت: درصد", width - PAD - 18, top + 54);
  let legendX = width - PAD - 22;
  BALANCE_SHEET_CHART_SERIES.forEach((item) => {
    context.fillStyle = item.color;
    context.fillRect(legendX - 11, top + 68, 11, 11);
    context.fillStyle = COLORS.ink;
    context.font = `400 13px ${PNG_FONT}`;
    rtlText(context, item.label, legendX - 17, top + 78);
    legendX -= context.measureText(item.label).width + 46;
  });
  if (!data.length) return;
  const left = PAD + 80;
  const right = width - PAD - 64;
  const plotTop = top + 98;
  const plotBottom = top + CHART_HEIGHT - 56;
  const positives = data.map(
    (point) => Math.max(0, point.liabilities ?? 0) + Math.max(0, point.equity ?? 0),
  );
  const negatives = data.map(
    (point) => Math.min(0, point.liabilities ?? 0) + Math.min(0, point.equity ?? 0),
  );
  const amountMax = Math.max(1, ...positives);
  const amountMin = Math.min(0, ...negatives);
  const ratios = data
    .map((point) => point.ratio)
    .filter((value): value is number => value !== null);
  const ratioMin = Math.min(0, ...ratios);
  const ratioMax = Math.max(1, ...ratios);
  const amountY = (value: number) =>
    plotBottom - ((value - amountMin) / (amountMax - amountMin)) * (plotBottom - plotTop);
  const ratioY = (value: number) =>
    plotBottom - ((value - ratioMin) / (ratioMax - ratioMin)) * (plotBottom - plotTop);
  const groupWidth = (right - left) / data.length;
  const barWidth = Math.min(48, groupWidth / 2.5);
  context.strokeStyle = "#cbd5e1";
  context.beginPath();
  context.moveTo(left, amountY(0));
  context.lineTo(right, amountY(0));
  context.stroke();
  context.font = `400 11px ${PNG_FONT}`;
  for (let tick = 0; tick <= 4; tick += 1) {
    const y = plotBottom - (tick / 4) * (plotBottom - plotTop);
    context.fillStyle = "#64748b";
    context.direction = "ltr";
    context.textAlign = "right";
    context.fillText(
      formatBalanceAmount(Math.round(amountMin + (tick / 4) * (amountMax - amountMin))),
      left - 8,
      y + 4,
    );
    context.textAlign = "left";
    context.fillText(formatRatio(ratioMin + (tick / 4) * (ratioMax - ratioMin)), right + 8, y + 4);
  }
  // Oldest on the left, newest on the right; sign-aware stacking, no segment for a missing value.
  data.forEach((point, index) => {
    const center = left + groupWidth * (index + 0.5);
    let positiveBase = 0;
    let negativeBase = 0;
    (
      [
        ["liabilities", BALANCE_SHEET_CHART_SERIES[0].color],
        ["equity", BALANCE_SHEET_CHART_SERIES[1].color],
      ] as const
    ).forEach(([key, color]) => {
      const value = point[key];
      if (value === null) return;
      const from = value >= 0 ? positiveBase : negativeBase;
      const to = from + value;
      if (value >= 0) positiveBase = to;
      else negativeBase = to;
      context.fillStyle = color;
      context.fillRect(
        center - barWidth / 2,
        Math.min(amountY(from), amountY(to)),
        barWidth,
        Math.max(1, Math.abs(amountY(to) - amountY(from))),
      );
    });
    if (point.warning && point.totalAssets !== null) {
      context.fillStyle = "#f59e0b";
      context.beginPath();
      context.arc(center, amountY(point.totalAssets), 6, 0, Math.PI * 2);
      context.fill();
    }
    context.fillStyle = COLORS.ink;
    context.font = `400 12px ${PNG_FONT}`;
    context.direction = "rtl";
    context.textAlign = "center";
    context.fillText(point.label, center, plotBottom + 22, groupWidth - 4);
  });
  context.strokeStyle = BALANCE_SHEET_CHART_SERIES[2].color;
  context.fillStyle = BALANCE_SHEET_CHART_SERIES[2].color;
  context.lineWidth = 3;
  let previous: { x: number; y: number } | null = null;
  data.forEach((point, index) => {
    if (point.ratio === null) {
      previous = null;
      return;
    }
    const current = { x: left + groupWidth * (index + 0.5), y: ratioY(point.ratio) };
    if (previous) {
      context.beginPath();
      context.moveTo(previous.x, previous.y);
      context.lineTo(current.x, current.y);
      context.stroke();
    }
    context.beginPath();
    context.arc(current.x, current.y, 4, 0, Math.PI * 2);
    context.fill();
    if (data.length === 1) {
      context.font = `600 13px ${PNG_FONT}`;
      context.textAlign = "center";
      context.fillText(formatRatio(point.ratio), current.x, current.y - 10);
    }
    previous = current;
  });
  context.lineWidth = 1;
}

/** Draws the single-date or comparison PNG: brand, header, chart above, baseline line, two-sided table, ratio, notices. */
export function renderBalanceSheetCanvas(
  result: BalanceSheetReportResult,
  comparisonKind: string | null,
  scale = 2,
): HTMLCanvasElement | null {
  const view = buildBalanceSheetView(result, comparisonKind);
  if (!view) return null;
  const comparison = Boolean(comparisonKind);
  const layout = layoutFor(comparison);
  const width = layout.width;
  const assetsRows = sideRows(view.assets);
  const leftRows = [...sideRows(view.liabilities), ...sideRows(view.equity)];
  const sidesHeight = Math.max(
    CAPTION_HEIGHT + columnHeaderHeight(layout) + assetsRows.length * ROW_HEIGHT,
    2 * (CAPTION_HEIGHT + columnHeaderHeight(layout)) + 12 + leftRows.length * ROW_HEIGHT,
  );
  const baselineHeight = comparison ? 44 : 0;
  const notices = [
    ...view.notices,
    ...(view.comparison?.unavailableText ? [view.comparison.unavailableText] : []),
  ];
  const noticeHeight = notices.length * 26 + (notices.length ? 12 : 0);
  const height =
    PAD * 2 +
    PNG_BRAND_HEIGHT +
    HEADER_HEIGHT +
    CHART_HEIGHT +
    baselineHeight +
    sidesHeight +
    12 +
    ROW_HEIGHT +
    10 +
    RATIO_HEIGHT +
    noticeHeight +
    PNG_FOOTER_HEIGHT;
  const canvas = document.createElement("canvas");
  canvas.width = width * scale;
  canvas.height = height * scale;
  const context = canvas.getContext("2d");
  if (!context) throw new Error("Canvas is unavailable.");
  context.scale(scale, scale);
  context.direction = "rtl";
  context.fillStyle = "#fffdf8";
  context.fillRect(0, 0, width, height);
  const date = exportDate();
  drawBrandHeader(context, width, PAD, date);

  const headerTop = PAD + PNG_BRAND_HEIGHT;
  context.fillStyle = COLORS.header;
  context.fillRect(PAD, headerTop, width - PAD * 2, HEADER_HEIGHT);
  context.fillStyle = COLORS.white;
  context.font = `700 30px ${PNG_FONT}`;
  rtlText(
    context,
    `${view.title} — ${view.companyLine}`,
    width - PAD - 24,
    headerTop + 44,
    width - PAD * 2 - 48,
  );
  context.fillStyle = "#fff7ed";
  context.font = `400 18px ${PNG_FONT}`;
  rtlText(
    context,
    view.metadata.join(" | "),
    width - PAD - 24,
    headerTop + 80,
    width - PAD * 2 - 48,
  );

  const chartTop = headerTop + HEADER_HEIGHT + 8;
  drawChart(context, result, width, chartTop);
  let top = chartTop + CHART_HEIGHT;
  if (comparison) {
    context.fillStyle = COLORS.header;
    context.font = `700 18px ${PNG_FONT}`;
    rtlText(
      context,
      `مبنای مقایسه: ${view.comparison?.label ?? "—"}${view.comparison?.available ? "" : " (در دسترس نیست)"}`,
      width - PAD,
      top + 26,
    );
    top += baselineHeight;
  }

  const rightSide = width - PAD;
  const leftSide = PAD + layout.sideWidth;
  drawSide(context, layout, view, view.assets, assetsRows, rightSide, top);
  const liabilitiesEnd = drawSide(
    context,
    layout,
    view,
    view.liabilities,
    sideRows(view.liabilities),
    leftSide,
    top,
  );
  drawSide(
    context,
    layout,
    view,
    view.equity,
    sideRows(view.equity),
    leftSide,
    liabilitiesEnd + 12,
  );
  top += sidesHeight + 12;
  drawRow(context, layout, { ...view.finalAssets, kind: "total" }, rightSide, top);
  drawRow(context, layout, { ...view.finalLiabilitiesAndEquity, kind: "total" }, leftSide, top);
  top += ROW_HEIGHT + 10;

  context.fillStyle = COLORS.ratio;
  context.fillRect(PAD, top, width - PAD * 2, RATIO_HEIGHT);
  context.fillStyle = COLORS.ratioInk;
  context.font = `800 20px ${PNG_FONT}`;
  rtlText(context, BALANCE_SHEET_RATIO_LABEL, width - PAD - 16, top + 35);
  const ratioText = comparison
    ? `${view.anchor.date}: ${view.ratio.anchor}   |   ${view.comparison?.baseline?.date ?? "—"}: ${view.ratio.baseline}   |   تغییر: ${view.ratio.pp}`
    : view.ratio.anchor;
  context.font = `700 20px ${PNG_FONT}`;
  context.direction = "rtl";
  context.textAlign = "left";
  context.fillText(ratioText, PAD + 16, top + 35);
  top += RATIO_HEIGHT + 12;

  context.font = `400 15px ${PNG_FONT}`;
  context.fillStyle = "#7c2d12";
  notices.forEach((notice) => {
    rtlText(context, `⚠ ${notice}`, width - PAD, top + 18, width - PAD * 2);
    top += 26;
  });
  drawBrandFooter(context, width, PAD, top + 25, "ترازنامه (صورت وضعیت مالی)");
  return canvas;
}

/** Vazirmatn weights the Feature 154 canvas draws with (header, body, labels, brand). */
export const BALANCE_SHEET_PNG_FONT_WEIGHTS = [400, 600, 700, 800] as const;
/**
 * Text whose characters span every Vazirmatn unicode-range subset the canvas draws: Arabic letters and
 * Persian digits, plus the Latin subset for punctuation and symbols («—», «·», «%», «()», «+»/«-», digits).
 * FontFaceSet.load fetches only the subsets the sample touches, so a narrower sample leaves glyphs in fallback.
 */
export const BALANCE_SHEET_PNG_FONT_SAMPLE =
  "ترازنامه ۰۱۲۳۴۵۶۷۸۹٬٫٪ — · () % + - : / | 0123456789 Sapio";
/** Upper bound on waiting for web fonts before an export; the download never waits longer. */
export const BALANCE_SHEET_PNG_FONT_TIMEOUT_MS = 3000;

/**
 * Canvas text does not wait for web fonts, so an export started right after page load can fall back to
 * another font (F154-V-08; spec §9.5 requires Vazirmatn). This waits, bounded, for the Vazirmatn faces the
 * canvas uses. It never throws and never hangs: on timeout, error or no FontFaceSet it returns false and
 * the export proceeds with the font stack's fallback.
 */
export async function ensureBalanceSheetPngFonts(
  timeoutMs: number = BALANCE_SHEET_PNG_FONT_TIMEOUT_MS,
): Promise<boolean> {
  const fonts = typeof document === "undefined" ? undefined : document.fonts;
  if (!fonts || typeof fonts.load !== "function") return false;
  const loading = (async () => {
    await Promise.all(
      BALANCE_SHEET_PNG_FONT_WEIGHTS.map((weight) =>
        fonts.load(`${weight} 16px Vazirmatn`, BALANCE_SHEET_PNG_FONT_SAMPLE),
      ),
    );
    await fonts.ready;
    return true;
  })().catch(() => false);
  let timer: ReturnType<typeof setTimeout> | undefined;
  const timeout = new Promise<boolean>((resolve) => {
    timer = setTimeout(() => resolve(false), timeoutMs);
  });
  try {
    return await Promise.race([loading, timeout]);
  } finally {
    if (timer !== undefined) clearTimeout(timer);
  }
}

export async function downloadBalanceSheetReportImage(
  result: BalanceSheetReportResult,
  comparisonKind: string | null,
) {
  await ensureBalanceSheetPngFonts();
  // A fixed 2x scale makes desktop and mobile export identical images for the same state (AC-P12).
  const canvas = renderBalanceSheetCanvas(result, comparisonKind, 2);
  if (!canvas) throw new Error("Balance sheet is unavailable.");
  const safeSymbol = safeFileSymbol(result.company.symbol);
  await downloadCanvasPng(
    canvas,
    `ترازنامه${safeSymbol ? `-${safeSymbol}` : ""}${comparisonKind ? "-مقایسه" : ""}-${exportDate().replaceAll("/", "-")}.png`,
  );
}
