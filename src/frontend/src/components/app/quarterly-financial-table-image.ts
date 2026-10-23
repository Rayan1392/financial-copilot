import type { QuarterlyFinancialTableModel } from "./quarterly-financial-table";
import { quarterlyPeriodHeaderLines } from "./quarterly-financial-table-header";
import { QUARTERLY_CHANGE_COLUMN_HEADER } from "./quarterly-financial-table-constants";
import { resolveQuarterlyChangeColors } from "./quarterly-financial-table-colors";
import { QUARTERLY_CHART_SERIES, quarterlyChartPointsChronologically } from "./quarterly-financial-chart-data";

const WIDTH = 1200;
const BRAND_HEIGHT = 100;
const CHART_HEIGHT = 350;
const FOOTER_HEIGHT = 80;

function parseDisplayNumber(value: string): number {
  const normalized = value
    .replace(/[\u06f0-\u06f9\u0660-\u0669]/g, (digit) =>
      String(digit.charCodeAt(0) - (digit >= "\u06f0" ? 0x06f0 : 0x0660)),
    )
    .replace(/[\u200e\u200f\u061c\s]/g, "")
    .replace(/[\u066c,\u066a%]/g, "")
    .replace(/[\u2212\u2013]/g, "-")
    .replace(/[()]/g, "")
    .replace("\u066b", ".");
  const parsed = Number(normalized);
  return Number.isFinite(parsed) ? parsed : 0;
}

function toPersianDigits(value: string): string {
  return value.replace(/[0-9\u0660-\u0669\u06f0-\u06f9]/g, (digit) => {
    const code = digit.charCodeAt(0);
    const numericValue =
      code >= 0x06f0 ? code - 0x06f0 : code >= 0x0660 ? code - 0x0660 : code - 48;
    return String.fromCharCode(0x06f0 + numericValue);
  });
}
const PAD = 36;
const COLORS = {
  ink: "#1f2937",
  muted: "#fff7ed",
  header: "#164e63",
  headerLight: "#d9efed",
  changeHeader: "#c7e5e1",
  label: "#ffedd5",
  border: "#e7d5bd",
  white: "#ffffff",
  negative: "#ffe4e6",
  positive: "#d1fae5",
  gross: "#fef08a",
};

function rtlText(
  context: CanvasRenderingContext2D,
  text: string,
  x: number,
  y: number,
  maxWidth?: number,
) {
  context.direction = "rtl";
  context.textAlign = "right";
  context.fillText(text, x, y, maxWidth);
}

function wrap(context: CanvasRenderingContext2D, text: string, maxWidth: number): string[] {
  const words = text.split(/\s+/);
  const lines: string[] = [];
  let line = "";
  for (const word of words) {
    const candidate = line ? `${line} ${word}` : word;
    if (line && context.measureText(candidate).width > maxWidth) {
      lines.push(line);
      line = word;
    } else line = candidate;
  }
  if (line) lines.push(line);
  return lines;
}

function exportDate(): string {
  const parts = new Intl.DateTimeFormat("fa-IR-u-ca-persian", {
    timeZone: "Asia/Tehran", year: "numeric", month: "2-digit", day: "2-digit",
  }).formatToParts(new Date());
  const part = (type: "year" | "month" | "day") => parts.find((item) => item.type === type)?.value ?? "";
  return toPersianDigits(`${part("year")}/${part("month")}/${part("day")}`);
}

function drawChart(context: CanvasRenderingContext2D, model: QuarterlyFinancialTableModel, top: number) {
  const points = quarterlyChartPointsChronologically(model);
  const left = PAD + 52;
  const right = WIDTH - PAD - 56;
  const plotTop = top + 98;
  const plotBottom = top + CHART_HEIGHT - 56;
  const plotHeight = plotBottom - plotTop;
  const values = points.flatMap((point) => [point.sales, point.grossProfit, point.netProfit])
    .filter((value): value is number => value !== null);
  const margins = points.map((point) => point.grossMargin)
    .filter((value): value is number => value !== null);
  const amountMin = Math.min(0, ...values);
  const rawAmountMax = Math.max(1, ...values);
  const rawAmountStep = rawAmountMax / 4;
  const amountMagnitude = 10 ** Math.floor(Math.log10(rawAmountStep));
  const amountStep = Math.ceil(rawAmountStep / amountMagnitude) * amountMagnitude;
  const amountMax = amountStep * 4;
  const marginMin = Math.min(0, ...margins);
  const marginMax = Math.max(1, ...margins);
  const amountY = (value: number) => plotBottom - (value - amountMin) / (amountMax - amountMin) * plotHeight;
  const marginY = (value: number) => plotBottom - (value - marginMin) / (marginMax - marginMin) * plotHeight;
  const baseline = amountY(0);
  const groupWidth = (right - left) / Math.max(points.length, 1);
  const barWidth = Math.min(25, groupWidth / 5);
  const formatter = new Intl.NumberFormat("fa-IR-u-nu-arabext", { maximumFractionDigits: 2 });

  context.fillStyle = "#ffffff";
  context.fillRect(PAD, top, WIDTH - PAD * 2, CHART_HEIGHT - 14);
  context.strokeStyle = "#e2e8f0";
  context.strokeRect(PAD, top, WIDTH - PAD * 2, CHART_HEIGHT - 14);
  context.fillStyle = COLORS.ink;
  context.font = '700 20px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
  rtlText(context, "روند فروش و سود فصلی", WIDTH - PAD - 18, top + 30);
  context.fillStyle = "#64748b";
  context.font = '400 14px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
  rtlText(context, "مبالغ: میلیون ریال | مارجین ناخالص: درصد", WIDTH - PAD - 18, top + 54);
  let legendX = WIDTH - PAD - 22;
  QUARTERLY_CHART_SERIES.forEach((series) => {
    context.fillStyle = series.color;
    context.fillRect(legendX - 11, top + 68, 11, 11);
    context.fillStyle = COLORS.ink;
    context.font = '400 13px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
    rtlText(context, series.label, legendX - 17, top + 78);
    legendX -= context.measureText(series.label).width + 46;
  });

  context.strokeStyle = "#cbd5e1";
  context.beginPath();
  context.moveTo(left, baseline);
  context.lineTo(right, baseline);
  context.stroke();
  for (let tick = 0; tick <= 4; tick++) {
    const y = plotBottom - tick / 4 * plotHeight;
    context.fillStyle = "#64748b";
    context.font = '400 11px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
    context.direction = "ltr";
    context.textAlign = "right";
    context.fillText(formatter.format(amountMin + tick / 4 * (amountMax - amountMin)), left - 8, y + 4);
    context.textAlign = "left";
    context.fillText(`${formatter.format(marginMin + tick / 4 * (marginMax - marginMin))}٪`, right + 8, y + 4);
  }
  points.forEach((point, index) => {
    const center = left + groupWidth * (index + 0.5);
    (["sales", "grossProfit", "netProfit"] as const).forEach((key, seriesIndex) => {
      const value = point[key];
      if (value === null) return;
      const y = amountY(value);
      context.fillStyle = QUARTERLY_CHART_SERIES[seriesIndex].color;
      context.fillRect(center + (seriesIndex - 1.5) * barWidth, Math.min(y, baseline), barWidth - 2, Math.max(1, Math.abs(baseline - y)));
    });
    context.fillStyle = COLORS.ink;
    context.font = '400 11px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
    context.direction = "rtl";
    context.textAlign = "center";
    context.fillText(point.shortLabel, center, plotBottom + 22, groupWidth - 4);
  });
  context.strokeStyle = QUARTERLY_CHART_SERIES[3].color;
  context.fillStyle = QUARTERLY_CHART_SERIES[3].color;
  context.lineWidth = 3;
  const runs: { x: number; y: number }[][] = [[]];
  points.forEach((point, index) => {
    if (point.grossMargin === null) { runs.push([]); return; }
    runs[runs.length - 1].push({ x: left + groupWidth * (index + 0.5), y: marginY(point.grossMargin) });
  });
  runs.filter((run) => run.length >= 2).forEach((run) => strokeMonotoneCurve(context, run));
  runs.flat().forEach((current) => {
    context.beginPath();
    context.arc(current.x, current.y, 4, 0, Math.PI * 2);
    context.fill();
  });
  context.lineWidth = 1;
}

// Monotone cubic interpolation (same shape as Recharts/d3 "monotone"): smooth through every point without overshooting.
function strokeMonotoneCurve(context: CanvasRenderingContext2D, run: { x: number; y: number }[]) {
  const slopes = run.slice(1).map((point, index) => (point.y - run[index].y) / (point.x - run[index].x));
  const tangents = run.map((_, index) => {
    if (index === 0) return slopes[0];
    if (index === run.length - 1) return slopes[slopes.length - 1];
    const h0 = run[index].x - run[index - 1].x;
    const h1 = run[index + 1].x - run[index].x;
    const s0 = slopes[index - 1];
    const s1 = slopes[index];
    const blended = (s0 * h1 + s1 * h0) / (h0 + h1);
    return (Math.sign(s0) + Math.sign(s1)) * Math.min(Math.abs(s0), Math.abs(s1), 0.5 * Math.abs(blended)) || 0;
  });
  context.beginPath();
  context.moveTo(run[0].x, run[0].y);
  for (let index = 0; index < run.length - 1; index += 1) {
    const from = run[index];
    const to = run[index + 1];
    const third = (to.x - from.x) / 3;
    context.bezierCurveTo(from.x + third, from.y + tangents[index] * third, to.x - third, to.y - tangents[index + 1] * third, to.x, to.y);
  }
  context.stroke();
}

export async function downloadQuarterlyFinancialTableImage(model: QuarterlyFinancialTableModel, showChart = false) {
  const scale = Math.max(2, window.devicePixelRatio || 1);
  const rowHeight = 54;
  const headerHeight = 104;
  const periodHeaderHeight = 78;
  const summaryHeight = 54;
  const height = PAD * 2 + BRAND_HEIGHT + headerHeight + summaryHeight + (showChart ? CHART_HEIGHT : 0)
    + periodHeaderHeight + rowHeight * model.rows.length + FOOTER_HEIGHT;
  const canvas = document.createElement("canvas");
  canvas.width = WIDTH * scale;
  canvas.height = height * scale;
  const context = canvas.getContext("2d");
  if (!context) throw new Error("Canvas is unavailable.");
  context.scale(scale, scale);
  context.direction = "rtl";
  context.fillStyle = "#fffdf8";
  context.fillRect(0, 0, WIDTH, height);

  const date = exportDate();
  context.fillStyle = "#0f766e";
  context.font = '700 30px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
  rtlText(context, "ساپیو", WIDTH - PAD, PAD + 31);
  context.fillStyle = "#475569";
  context.font = '400 16px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
  rtlText(context, "دستیار هوشمند بازار سرمایه", WIDTH - PAD, PAD + 57);
  rtlText(context, `تاریخ دریافت: \u2066${date}\u2069`, WIDTH - PAD, PAD + 81);

  const headerTop = PAD + BRAND_HEIGHT;
  context.fillStyle = COLORS.header;
  context.fillRect(PAD, headerTop, WIDTH - PAD * 2, headerHeight);
  context.fillStyle = "#ffffff";
  context.font = '700 34px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
  rtlText(context, model.title, WIDTH - PAD - 24, headerTop + 46, WIDTH - PAD * 2 - 48);
  context.fillStyle = COLORS.muted;
  context.font = '400 20px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
  const summaryLines = wrap(context, model.summary, WIDTH - PAD * 2 - 48).slice(0, 2);
  summaryLines.forEach((line, index) =>
    rtlText(context, line, WIDTH - PAD - 24, headerTop + 78 + index * 26),
  );

  const chartTop = headerTop + headerHeight + summaryHeight;
  if (showChart) drawChart(context, model, chartTop);
  const top = chartTop + (showChart ? CHART_HEIGHT : 0);
  const colCount = model.quarters.length + 2;
  const usableWidth = WIDTH - PAD * 2;
  const widths = [
    usableWidth * 0.22,
    ...model.quarters.map(() => usableWidth * (0.62 / model.quarters.length)),
    usableWidth * 0.16,
  ];
  const headers = [
    ["شرح"],
    ...model.quarters.map(quarterlyPeriodHeaderLines),
    [QUARTERLY_CHANGE_COLUMN_HEADER],
  ];
  let right = WIDTH - PAD;
  context.font = '600 18px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
  headers.forEach((headerLines, index) => {
    const cellWidth = widths[index];
    const left = right - cellWidth;
    context.fillStyle = index === colCount - 1 ? COLORS.changeHeader : COLORS.headerLight;
    context.fillRect(left, top, cellWidth, periodHeaderHeight);
    context.strokeStyle = COLORS.border;
    context.strokeRect(left, top, cellWidth, periodHeaderHeight);
    context.fillStyle = COLORS.header;
    const lines =
      headerLines.length === 1
        ? wrap(context, headerLines[0], cellWidth - 12).slice(0, 2)
        : headerLines;
    lines.forEach((line, lineIndex) => {
      context.fillStyle = COLORS.header;
      context.font =
        headerLines.length >= 3 && lineIndex === 0
          ? '700 19px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif'
          : headerLines.length >= 3 && lineIndex === 1
            ? '400 13px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif'
            : headerLines.length >= 3
              ? '600 15px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif'
              : '600 18px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
      const baseline = headerLines.length >= 3 ? top + 20 + lineIndex * 19 : top + 27 + lineIndex * 24;
      if (index === 0) {
        rtlText(context, line, right - 6, baseline, cellWidth - 12);
      } else {
        context.direction = "rtl";
        context.textAlign = "center";
        context.fillText(line, left + cellWidth / 2, baseline, cellWidth - 12);
      }
    });
    right = left;
  });

  model.rows.forEach((row, rowIndex) => {
    const y = top + periodHeaderHeight + rowIndex * rowHeight;
    const cells = [row.label, ...row.values];
    right = WIDTH - PAD;
    cells.forEach((value, columnIndex) => {
      const cellWidth = widths[columnIndex];
      const left = right - cellWidth;
      const isChange = columnIndex === colCount - 1;
      const parsedChange = isChange ? parseDisplayNumber(value) : 0;
      const changeColors = isChange
        ? resolveQuarterlyChangeColors(row.sourceItemId, parsedChange)
        : null;
      context.fillStyle = row.grossMargin
        ? COLORS.gross
        : columnIndex === 0
          ? COLORS.label
          : isChange
            ? changeColors!.backgroundColor
            : row.grossMargin
              ? COLORS.gross
              : value.startsWith("(")
                ? COLORS.negative
                : COLORS.white;
      context.fillRect(left, y, cellWidth, rowHeight);
      context.strokeStyle = COLORS.border;
      context.strokeRect(left, y, cellWidth, rowHeight);
      context.fillStyle = changeColors?.textColor ?? (row.grossMargin ? "#78350f" : COLORS.ink);
      context.font = `${row.grossMargin ? "700 20" : columnIndex === 0 ? "600 17" : "400 17"}px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif`;
      const displayValue =
        row.grossMargin && columnIndex > 0 && !isChange && value !== "-"
          ? `${value}${String.fromCharCode(0x066a)}`
          : value;
      const textLines = wrap(context, displayValue, cellWidth - 12).slice(0, 2);
      textLines.forEach((line, lineIndex) =>
        rtlText(context, line, right - 6, y + 22 + lineIndex * 19, cellWidth - 12),
      );
      right = left;
    });
  });

  const footerTop = top + periodHeaderHeight + rowHeight * model.rows.length + 25;
  context.strokeStyle = "#cbd5e1";
  context.beginPath();
  context.moveTo(PAD, footerTop - 10);
  context.lineTo(WIDTH - PAD, footerTop - 10);
  context.stroke();
  context.fillStyle = COLORS.header;
  context.font = '600 16px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
  rtlText(context, "گزارش مالی فصلی | ساپیو - دستیار هوشمند بازار", WIDTH - PAD, footerTop + 15);
  context.fillStyle = "#64748b";
  context.font = '400 14px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
  rtlText(context, "بر پایه داده‌های گزارش‌شده", WIDTH - PAD, footerTop + 39);

  const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, "image/png"));
  if (!blob) throw new Error("Image generation failed.");
  const link = document.createElement("a");
  const url = URL.createObjectURL(blob);
  link.href = url;
  const safeSymbol = model.symbol
    .replace(/[<>:"\\|?*]/g, "")
    .replaceAll("/", "")
    .trim()
    .replace(/[-\s]+/g, "-");
  link.download = `\u062c\u062f\u0648\u0644-\u0645\u0627\u0644\u06cc-\u0641\u0635\u0644\u06cc${safeSymbol ? `-${safeSymbol}` : ""}-${date.replaceAll("/", "-")}.png`;
  link.click();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}
