import type { MonthlyTrendChartCardViewModel } from "@/components/app/monthly-activity-trend-chart-view-model";

const WIDTH = 1800;
const PADDING = 90;
const PLOT_TOP = 230;
const PLOT_HEIGHT = 680;

/** Renders only the canonical trend-card view model; it never requests or derives financial data. */
export async function downloadMonthlyTrendChartImage(viewModel: MonthlyTrendChartCardViewModel) {
  const explanationHeight = Math.max(140, viewModel.explanationLines.length * 44 + 90);
  const height = PLOT_TOP + PLOT_HEIGHT + explanationHeight + PADDING;
  const canvas = document.createElement("canvas");
  const scale = Math.max(1, window.devicePixelRatio || 1);
  canvas.width = WIDTH * scale;
  canvas.height = height * scale;
  const context = canvas.getContext("2d");
  if (!context) throw new Error("Canvas is unavailable.");
  context.scale(scale, scale);

  const { palette, points } = viewModel;
  context.fillStyle = palette.surface;
  context.fillRect(0, 0, WIDTH, height);
  context.textAlign = "right";
  context.fillStyle = palette.foreground;
  context.font = '600 34px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
  drawRtlText(context, `${viewModel.title} — ${viewModel.companyLabel}`, WIDTH - PADDING, 84);
  context.font = '24px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
  context.fillStyle = palette.mutedForeground;
  drawRtlText(context, `واحد: ${viewModel.unitLabel}`, WIDTH - PADDING, 126);

  drawLegend(context, viewModel, WIDTH - PADDING, 170);
  const values = points.flatMap((point) => [point.currentYear, point.previousYear, point.average])
    .filter((value): value is number => value !== null);
  // Keep enough headroom above the tallest bar for its value label.
  const maximum = Math.max(...values, 1) * 1.12;
  const plotLeft = PADDING + 80;
  const plotRight = WIDTH - PADDING;
  const plotBottom = PLOT_TOP + PLOT_HEIGHT;
  const groupWidth = (plotRight - plotLeft) / Math.max(points.length, 1);

  context.strokeStyle = palette.grid;
  context.lineWidth = 1;
  for (let tick = 0; tick <= 4; tick++) {
    const y = PLOT_TOP + (PLOT_HEIGHT * tick) / 4;
    context.beginPath(); context.moveTo(plotLeft, y); context.lineTo(plotRight, y); context.stroke();
  }

  points.forEach((point, index) => {
    const center = plotLeft + groupWidth * (index + 0.5);
    const barWidth = Math.min(30, groupWidth * 0.25);
    drawBar(context, center - barWidth - 5, point.previousYear, point.previousYearValueLabel, palette.previousYear, palette.foreground, maximum, plotBottom);
    drawBar(context, center + 5, point.currentYear, point.currentYearValueLabel, palette.currentYear, palette.foreground, maximum, plotBottom);
    context.fillStyle = palette.mutedForeground;
    context.font = '20px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
    context.textAlign = "center";
    drawRtlText(context, point.fiscalMonthLabel, center, plotBottom + 38);
  });

  drawAverageLine(context, points, plotLeft, groupWidth, maximum, plotBottom, palette.average);
  // Keep the watermark in the lower-left whitespace so it does not obscure the plotted data.
  context.save();
  context.textAlign = "left";
  context.fillStyle = palette.watermark;
  context.font = '700 34px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
  drawRtlText(context, "ساپیو - دستیار هوشمند بازار", PADDING, plotBottom + 148);
  context.restore();

  const explanationTop = plotBottom + 105;
  context.textAlign = "right";
  context.fillStyle = palette.foreground;
  context.font = '600 24px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
  drawRtlText(context, "توضیحات", WIDTH - PADDING, explanationTop);
  context.fillStyle = palette.mutedForeground;
  context.font = '20px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
  const lines = viewModel.explanationLines.length > 0 ? viewModel.explanationLines : ["دادهٔ گم‌شده‌ای گزارش نشده است."];
  lines.forEach((line, index) => drawRtlText(context, line, WIDTH - PADDING, explanationTop + 42 * (index + 1)));

  const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, "image/png"));
  if (!blob) throw new Error("Image generation failed.");
  const date = new Intl.DateTimeFormat("fa-IR-u-ca-persian", { timeZone: "Asia/Tehran", year: "numeric", month: "2-digit", day: "2-digit" })
    .format(new Date()).replaceAll("/", "-");
  const link = document.createElement("a");
  link.href = URL.createObjectURL(blob);
  link.download = `روند-فروش-ماهانه-${viewModel.companyLabel.replaceAll(/[\\/:*?"<>|]/g, "-")}-${date}.png`;
  link.click();
  URL.revokeObjectURL(link.href);
}

function drawLegend(context: CanvasRenderingContext2D, viewModel: MonthlyTrendChartCardViewModel, right: number, y: number) {
  const averageValueLabel = viewModel.points.find((point) => point.average !== null)?.averageValueLabel;
  const averageLabel = averageValueLabel
    ? `${viewModel.averageLegend}: ${averageValueLabel}`
    : viewModel.averageLegend;
  const entries = [[viewModel.currentYearLegend, viewModel.palette.currentYear], [viewModel.previousYearLegend, viewModel.palette.previousYear], [averageLabel, viewModel.palette.average]];
  let cursor = right;
  context.font = '20px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
  entries.forEach(([label, color]) => {
    context.fillStyle = color; context.fillRect(cursor - 18, y - 15, 18, 18);
    context.fillStyle = viewModel.palette.mutedForeground; context.textAlign = "right";
    drawRtlText(context, label, cursor - 28, y);
    cursor -= context.measureText(label).width + 95;
  });
}

function drawBar(context: CanvasRenderingContext2D, x: number, value: number | null, label: string | null, color: string, labelColor: string, maximum: number, bottom: number) {
  if (value === null || label === null) return;
  const height = (value / maximum) * PLOT_HEIGHT;
  context.fillStyle = color;
  context.fillRect(x, bottom - height, 30, height);
  context.fillStyle = labelColor;
  context.font = '17px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
  context.textAlign = "center";
  context.fillText(label, x + 15, bottom - height - 14);
}

function drawAverageLine(context: CanvasRenderingContext2D, points: MonthlyTrendChartCardViewModel["points"], left: number, groupWidth: number, maximum: number, bottom: number, color: string) {
  let started = false;
  context.strokeStyle = color; context.lineWidth = 4;
  points.forEach((point, index) => {
    if (point.average === null) { started = false; return; }
    const x = left + groupWidth * (index + 0.5);
    const y = bottom - (point.average / maximum) * PLOT_HEIGHT;
    if (!started) { context.beginPath(); context.moveTo(x, y); started = true; } else context.lineTo(x, y);
    context.stroke();
  });

}

function drawRtlText(context: CanvasRenderingContext2D, value: string, x: number, y: number) {
  context.save();
  context.direction = "rtl";
  context.fillText(`\u202B${value}\u202C`, x, y);
  context.restore();
}
