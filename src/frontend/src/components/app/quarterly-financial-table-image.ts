import type { QuarterlyFinancialTableModel } from "./quarterly-financial-table";
import { quarterlyPeriodHeaderLines } from "./quarterly-financial-table-header";
import { QUARTERLY_CHANGE_COLUMN_HEADER } from "./quarterly-financial-table-constants";
import { resolveQuarterlyChangeColors } from "./quarterly-financial-table-colors";

const WIDTH = 1200;

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

export async function downloadQuarterlyFinancialTableImage(model: QuarterlyFinancialTableModel) {
  const scale = Math.max(2, window.devicePixelRatio || 1);
  const rowHeight = 54;
  const headerHeight = 104;
  const summaryHeight = 54;
  const height = PAD * 2 + headerHeight + summaryHeight + 64 + rowHeight * model.rows.length;
  const canvas = document.createElement("canvas");
  canvas.width = WIDTH * scale;
  canvas.height = height * scale;
  const context = canvas.getContext("2d");
  if (!context) throw new Error("Canvas is unavailable.");
  context.scale(scale, scale);
  context.direction = "rtl";
  context.fillStyle = "#fffdf8";
  context.fillRect(0, 0, WIDTH, height);

  context.fillStyle = COLORS.header;
  context.fillRect(PAD, PAD, WIDTH - PAD * 2, headerHeight);
  context.fillStyle = "#ffffff";
  context.font = '700 34px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
  rtlText(context, model.title, WIDTH - PAD - 24, PAD + 46, WIDTH - PAD * 2 - 48);
  context.fillStyle = COLORS.muted;
  context.font = '400 20px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
  const summaryLines = wrap(context, model.summary, WIDTH - PAD * 2 - 48).slice(0, 2);
  summaryLines.forEach((line, index) =>
    rtlText(context, line, WIDTH - PAD - 24, PAD + 78 + index * 26),
  );

  const top = PAD + headerHeight + summaryHeight;
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
    context.fillRect(left, top, cellWidth, 64);
    context.strokeStyle = COLORS.border;
    context.strokeRect(left, top, cellWidth, 64);
    context.fillStyle = COLORS.header;
    const lines =
      headerLines.length === 1
        ? wrap(context, headerLines[0], cellWidth - 12).slice(0, 2)
        : headerLines;
    lines.forEach((line, lineIndex) => {
      const baseline = top + 27 + lineIndex * 24;
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
    const y = top + 64 + rowIndex * rowHeight;
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

  const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, "image/png"));
  if (!blob) throw new Error("Image generation failed.");
  const date = toPersianDigits(
    new Intl.DateTimeFormat("fa-IR-u-ca-persian", {
      timeZone: "Asia/Tehran",
      year: "numeric",
      month: "2-digit",
      day: "2-digit",
    })
      .format(new Date())
      .replaceAll("/", "-"),
  );
  const link = document.createElement("a");
  const url = URL.createObjectURL(blob);
  link.href = url;
  const safeSymbol = model.symbol
    .replace(/[<>:"\\|?*]/g, "")
    .replaceAll("/", "")
    .trim()
    .replace(/[-\s]+/g, "-");
  link.download = `\u062c\u062f\u0648\u0644-\u0645\u0627\u0644\u06cc-\u0641\u0635\u0644\u06cc${safeSymbol ? `-${safeSymbol}` : ""}-${date}.png`;
  link.click();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}
