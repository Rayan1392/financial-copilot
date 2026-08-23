import type { PsVisualizationResult } from "@/lib/chat.functions";
import type { MonthlyTrendChartCardViewModel } from "@/components/app/monthly-activity-trend-chart-view-model";
import { getPsGaugeMarkerAngles } from "@/components/app/ps-gauge-geometry";
import { toPersianDigits } from "@/lib/format/persian";

const WIDTH = 1800;
const PADDING = 90;
const PLOT_TOP = 230;
const PLOT_HEIGHT = 680;

/** Renders only the canonical trend-card view model; it never requests or derives financial data. */
export async function downloadMonthlyTrendChartImage(
  viewModel: MonthlyTrendChartCardViewModel,
  psVisualization?: PsVisualizationResult,
) {
  const explanationHeight = Math.max(140, viewModel.explanationLines.length * 44 + 90);
  const height = PLOT_TOP + PLOT_HEIGHT + explanationHeight + PADDING;
  const canvas = document.createElement("canvas");
  // Use a high-resolution backing canvas so downloaded labels and one-pixel details remain sharp
  // when the PNG is previewed or downscaled.
  const scale = Math.max(2, window.devicePixelRatio || 1);
  canvas.width = WIDTH * scale;
  canvas.height = height * scale;
  const context = canvas.getContext("2d");
  if (!context) throw new Error("Canvas is unavailable.");
  context.scale(scale, scale);
  context.imageSmoothingEnabled = true;

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
  context.lineWidth = 2;
  for (let tick = 0; tick <= 4; tick++) {
    const y = PLOT_TOP + (PLOT_HEIGHT * tick) / 4;
    context.beginPath(); context.moveTo(plotLeft, y); context.lineTo(plotRight, y); context.stroke();
  }

  points.forEach((point, index) => {
    const center = plotLeft + groupWidth * (index + 0.5);
    const barWidth = Math.min(30, groupWidth * 0.25);
    drawBar(context, center - barWidth - 5, point.previousYear, point.previousYearValueLabel, palette.previousYear, palette.foreground, maximum, plotBottom);
    drawBar(context, center + 5, point.currentYear, point.currentYearValueLabel, palette.currentYear, palette.foreground, maximum, plotBottom);
    context.fillStyle = palette.foreground;
    context.font = '600 22px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
    context.textAlign = "center";
    drawRtlText(context, point.fiscalMonthLabel, center, plotBottom + 38);
  });

  drawAverageLine(context, points, plotLeft, groupWidth, maximum, plotBottom, palette.average);
  // Draw the gauge after the plot so bars/grid lines cannot cover it in the exported image.
  if (psVisualization) {
    drawCompactPsGauge(
      context,
      psVisualization,
      WIDTH - PADDING - 315,
      PLOT_TOP + 25,
      300,
      palette.foreground,
      palette.surface,
    );
  }
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
  context.font = '600 22px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
  const lines = viewModel.explanationLines.length > 0
    ? viewModel.explanationLines
    : [
        {
          beforeValue: "دادهٔ گم‌شده‌ای گزارش نشده است.",
          valueLabel: null,
          afterValue: "",
          tone: "neutral" as const,
        },
      ];
  lines.forEach((line, index) => drawExplanationLine(
    context,
    line,
    WIDTH - PADDING,
    explanationTop + 42 * (index + 1),
    viewModel,
  ));

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

function drawCompactPsGauge(
  context: CanvasRenderingContext2D,
  data: PsVisualizationResult,
  left: number,
  top: number,
  width: number,
  foreground: string,
  background: string,
) {
  const height = width * 0.68;
  const cx = left + width / 2;
  const cy = top + height * 0.82;
  const outer = width * 0.44;
  const inner = width * 0.24;
  const colors = ["#00b900", "#45ed4d", "#9cf59f", "#ffaaaa", "#ff6268", "#f00000"];

  data.gaugeBands.forEach((band, index) => {
    context.beginPath();
    for (let step = 0; step <= 12; step++) {
      const angle = band.startAngleDegrees + ((band.endAngleDegrees - band.startAngleDegrees) * step) / 12;
      const radians = Math.PI - (angle * Math.PI) / 180;
      const x = cx + outer * Math.cos(radians);
      const y = cy - outer * Math.sin(radians);
      if (step === 0) context.moveTo(x, y); else context.lineTo(x, y);
    }
    for (let step = 12; step >= 0; step--) {
      const angle = band.startAngleDegrees + ((band.endAngleDegrees - band.startAngleDegrees) * step) / 12;
      const radians = Math.PI - (angle * Math.PI) / 180;
      context.lineTo(cx + inner * Math.cos(radians), cy - inner * Math.sin(radians));
    }
    context.closePath();
    context.fillStyle = colors[index] ?? colors[0];
    context.fill();

    const labelAngle = band.endAngleDegrees;
    const labelRadians = Math.PI - (labelAngle * Math.PI) / 180;
    const labelRadius = outer + 25;
    context.textAlign = "center";
    context.font = '700 18px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
    context.fillStyle = foreground;
    context.strokeStyle = background;
    context.lineWidth = 5;
    context.strokeText(
      toPersianDigits(band.upperBoundary.toFixed(2)),
      cx + labelRadius * Math.cos(labelRadians),
      cy - labelRadius * Math.sin(labelRadians),
    );
    context.fillText(
      toPersianDigits(band.upperBoundary.toFixed(2)),
      cx + labelRadius * Math.cos(labelRadians),
      cy - labelRadius * Math.sin(labelRadians),
    );

    const percentageAngle = (band.startAngleDegrees + band.endAngleDegrees) / 2;
    const percentageRadians = Math.PI - (percentageAngle * Math.PI) / 180;
    const percentageRadius = (outer + inner) / 2;
    context.font = '700 16px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
    context.strokeText(
      toPersianDigits(`${band.displayPercentage.toFixed(1)}%`),
      cx + percentageRadius * Math.cos(percentageRadians),
      cy - percentageRadius * Math.sin(percentageRadians) + 6,
    );
    context.fillText(
      toPersianDigits(`${band.displayPercentage.toFixed(1)}%`),
      cx + percentageRadius * Math.cos(percentageRadians),
      cy - percentageRadius * Math.sin(percentageRadians) + 6,
    );
  });

  const { ttm } = getPsGaugeMarkerAngles(data);
  if (ttm !== undefined) {
    const radians = Math.PI - (ttm * Math.PI) / 180;
    context.beginPath();
    context.moveTo(cx, cy);
    context.lineTo(cx + outer * 0.86 * Math.cos(radians), cy - outer * 0.86 * Math.sin(radians));
    context.strokeStyle = "#000000";
    context.lineWidth = 5;
    context.stroke();
  }
  context.beginPath(); context.arc(cx, cy, 7, 0, Math.PI * 2);
  context.fillStyle = "#000000"; context.fill();
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
    context.fillStyle = viewModel.palette.foreground; context.textAlign = "right";
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
  context.font = '600 22px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
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

function drawExplanationLine(
  context: CanvasRenderingContext2D,
  line: MonthlyTrendChartCardViewModel["explanationLines"][number],
  right: number,
  y: number,
  viewModel: MonthlyTrendChartCardViewModel,
) {
  context.textAlign = "right";
  context.fillStyle = viewModel.palette.mutedForeground;
  drawRtlText(context, line.beforeValue, right, y);
  let cursor = right - context.measureText(line.beforeValue).width;
  if (line.valueLabel) {
    context.save();
    context.direction = "ltr";
    context.textAlign = "right";
    context.fillStyle = line.tone === "positive"
      ? viewModel.palette.positive
      : line.tone === "negative"
        ? viewModel.palette.negative
        : viewModel.palette.mutedForeground;
    context.fillText(line.valueLabel, cursor, y);
    cursor -= context.measureText(line.valueLabel).width;
    context.restore();
  }
  context.fillStyle = viewModel.palette.mutedForeground;
  drawRtlText(context, line.afterValue, cursor, y);
}
