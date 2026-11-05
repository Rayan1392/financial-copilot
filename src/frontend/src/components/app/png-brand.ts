/**
 * Sapio PNG brand helpers shared by the Feature 144 quarterly table and the Feature 154 balance-sheet exports.
 * Extracted unchanged from quarterly-financial-table-image.ts (Feature 154 T19).
 */
export const PNG_FONT = 'Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
export const PNG_BRAND_HEIGHT = 100;
export const PNG_FOOTER_HEIGHT = 80;
export const PNG_HEADER_COLOR = "#164e63";

export function toPersianDigits(value: string): string {
  return value.replace(/[0-9\u0660-\u0669\u06f0-\u06f9]/g, (digit) => {
    const code = digit.charCodeAt(0);
    const numericValue =
      code >= 0x06f0 ? code - 0x06f0 : code >= 0x0660 ? code - 0x0660 : code - 48;
    return String.fromCharCode(0x06f0 + numericValue);
  });
}

export function rtlText(
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

export function wrap(context: CanvasRenderingContext2D, text: string, maxWidth: number): string[] {
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

export function exportDate(): string {
  const parts = new Intl.DateTimeFormat("fa-IR-u-ca-persian", {
    timeZone: "Asia/Tehran",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).formatToParts(new Date());
  const part = (type: "year" | "month" | "day") =>
    parts.find((item) => item.type === type)?.value ?? "";
  return toPersianDigits(`${part("year")}/${part("month")}/${part("day")}`);
}

/** The Sapio brand block at the top-right of every exported image. */
export function drawBrandHeader(
  context: CanvasRenderingContext2D,
  width: number,
  pad: number,
  date: string,
) {
  context.fillStyle = "#0f766e";
  context.font = `700 30px ${PNG_FONT}`;
  rtlText(context, "ساپیو", width - pad, pad + 31);
  context.fillStyle = "#475569";
  context.font = `400 16px ${PNG_FONT}`;
  rtlText(context, "دستیار هوشمند بازار سرمایه", width - pad, pad + 57);
  rtlText(context, `تاریخ دریافت: \u2066${date}\u2069`, width - pad, pad + 81);
}

/** The footer rule, report name and data note. */
export function drawBrandFooter(
  context: CanvasRenderingContext2D,
  width: number,
  pad: number,
  footerTop: number,
  reportName: string,
) {
  context.strokeStyle = "#cbd5e1";
  context.beginPath();
  context.moveTo(pad, footerTop - 10);
  context.lineTo(width - pad, footerTop - 10);
  context.stroke();
  context.fillStyle = PNG_HEADER_COLOR;
  context.font = `600 16px ${PNG_FONT}`;
  rtlText(context, `${reportName} | ساپیو - دستیار هوشمند بازار`, width - pad, footerTop + 15);
  context.fillStyle = "#64748b";
  context.font = `400 14px ${PNG_FONT}`;
  rtlText(context, "بر پایه داده‌های گزارش‌شده", width - pad, footerTop + 39);
}

export function safeFileSymbol(symbol: string): string {
  return symbol
    .replace(/[<>:"\\|?*]/g, "")
    .replaceAll("/", "")
    .trim()
    .replace(/[-\s]+/g, "-");
}

export async function downloadCanvasPng(canvas: HTMLCanvasElement, fileName: string) {
  const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, "image/png"));
  if (!blob) throw new Error("Image generation failed.");
  const link = document.createElement("a");
  const url = URL.createObjectURL(blob);
  link.href = url;
  link.download = fileName;
  link.click();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}
