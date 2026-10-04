import type { MonthlyProductTrendResult } from "@/lib/chat.functions";
import { toPersianDigits } from "@/lib/format/persian";
import {
  buildMonthlyProductTrendExportFileName,
  formatProductTrendNumber,
  formatProductTrendRate,
  formatProductTrendPeriod,
  createMonthlyProductTrendChartData,
  createProductTrendQuantityAxis,
  formatProductTrendIntegerNumber,
  PRODUCT_TREND_PANEL_LABELS,
  productTrendQuantitySeries,
  productTrendSeries,
} from "@/components/app/monthly-product-trend-chart-model";

const WIDTH = 1800;
const HEIGHT = 1440;
const PADDING = 90;
const PLOT_TOP = 220;
const PLOT_HEIGHT = 390;
const QUANTITY_PLOT_TOP = 790;
const QUANTITY_PLOT_HEIGHT = 330;
const VALID_RATE_STATUSES = new Set(["ValidRate", "ValidZeroRate"]);

function productCompanyLabel(data: MonthlyProductTrendResult): string {
  return data.companySymbol?.trim() || data.companyText?.trim() || "";
}

function productTitle(data: MonthlyProductTrendResult): string {
  const product = data.productTitle?.trim() || "محصول";
  const company = productCompanyLabel(data);
  return company ? `روند فروش ${product} ${company}` : `روند فروش ${product}`;
}

function rateForPoint(point: MonthlyProductTrendResult["points"][number]): number | null {
  if (!VALID_RATE_STATUSES.has(point.rateStatus)) return null;
  return point.calculatedSaleRateToman ?? null;
}

/** Exports the typed product result directly; it never derives a company average or rate series. */
export async function downloadMonthlyProductTrendChartImage(data: MonthlyProductTrendResult) {
  if (data.resolutionState !== "Resolved") throw new Error("Product trend is not resolved.");

  const canvas = document.createElement("canvas");
  const scale = Math.max(2, window.devicePixelRatio || 1);
  canvas.width = WIDTH * scale;
  canvas.height = HEIGHT * scale;
  const context = canvas.getContext("2d");
  if (!context) throw new Error("Image generation is unavailable.");
  context.scale(scale, scale);
  context.fillStyle = "#ffffff";
  context.fillRect(0, 0, WIDTH, HEIGHT);

  const points = data.points;
  const chartData = createMonthlyProductTrendChartData(data);
  const productUnit = data.productUnit ?? "واحد محصول";
  const series = productTrendSeries(data.productUnit);
  const quantitySeries = productTrendQuantitySeries();
  const values = points
    .map((point) => point.salesValueBillionToman)
    .filter((value): value is number => value != null && Number.isFinite(value));
  const rates = points
    .map(rateForPoint)
    .filter((value): value is number => value != null && Number.isFinite(value));
  const quantityAxis = createProductTrendQuantityAxis(
    chartData.flatMap((point) => [point.production, point.quantity]),
  );
  const maxValue = Math.max(...values, 1) * 1.15;
  const maxRate = Math.max(...rates, 1) * 1.15;
  const maxQuantity = quantityAxis.max;
  const plotLeft = PADDING + 105;
  const plotRight = WIDTH - PADDING - 105;
  const plotBottom = PLOT_TOP + PLOT_HEIGHT;
  const quantityPlotBottom = QUANTITY_PLOT_TOP + QUANTITY_PLOT_HEIGHT;
  const groupWidth = (plotRight - plotLeft) / Math.max(points.length, 1);

  context.fillStyle = "#17202a";
  context.textAlign = "right";
  context.font = '700 36px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
  drawRtlText(context, productTitle(data), WIDTH - PADDING, 72);
  context.fillStyle = "#5f6b76";
  context.font = '24px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
  drawRtlText(
    context,
    `مبلغ فروش به میلیارد تومان و نرخ فروش به تومان/${productUnit}`,
    WIDTH - PADDING,
    116,
  );
  context.fillStyle = "#17202a";
  context.font = '700 25px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
  drawRtlText(context, "Ù…Ø¨Ù„Øº ÙØ±ÙˆØ´ Ùˆ Ù†Ø±Ø® ÙØ±ÙˆØ´", WIDTH - PADDING, 174);

  context.fillStyle = "#ffffff";
  context.fillRect(WIDTH - PADDING - 650, 145, 650, 42);
  context.fillStyle = "#17202a";
  drawRtlText(context, PRODUCT_TREND_PANEL_LABELS.main, WIDTH - PADDING, 174);

  context.strokeStyle = "#d7dde3";
  context.lineWidth = 2;
  for (let tick = 0; tick <= 4; tick++) {
    const ratio = 1 - tick / 4;
    const y = PLOT_TOP + (PLOT_HEIGHT * tick) / 4;
    context.beginPath();
    context.moveTo(plotLeft, y);
    context.lineTo(plotRight, y);
    context.stroke();

    context.fillStyle = "#5f6b76";
    context.font = '20px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
    context.textAlign = "right";
    drawRtlText(context, formatProductTrendRate(maxRate * ratio), plotLeft - 18, y + 7);
    context.textAlign = "left";
    drawRtlText(context, formatProductTrendNumber(maxValue * ratio), plotRight + 18, y + 7);
  }

  context.fillStyle = "#5f6b76";
  context.font = '700 22px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
  context.textAlign = "left";
  drawRtlText(context, series.rate.label, PADDING, PLOT_TOP - 28);
  context.textAlign = "right";
  drawRtlText(context, "مبلغ فروش (میلیارد تومان)", WIDTH - PADDING, PLOT_TOP - 28);

  points.forEach((point, index) => {
    const center = plotLeft + groupWidth * (index + 0.5);
    const value = point.salesValueBillionToman;
    if (value != null && Number.isFinite(value)) {
      const barWidth = Math.min(68, groupWidth * 0.44);
      const barHeight = (value / maxValue) * PLOT_HEIGHT;
      context.fillStyle = series.sales.color;
      context.fillRect(center - barWidth / 2, plotBottom - barHeight, barWidth, barHeight);
      context.fillStyle = "#17202a";
      context.font = '600 18px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
      context.textAlign = "center";
      drawRtlText(
        context,
        formatProductTrendNumber(value),
        center,
        Math.max(PLOT_TOP + 20, plotBottom - barHeight - 12),
      );
    }
  });

  drawRateLine(context, points, plotLeft, groupWidth, plotBottom, maxRate);
  context.fillStyle = "#17202a";
  context.font = '700 25px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
  context.textAlign = "right";
  drawRtlText(
    context,
    `Ù…Ù‚Ø¯Ø§Ø± ØªÙˆÙ„ÛŒØ¯ Ùˆ Ù…Ù‚Ø¯Ø§Ø± ÙØ±ÙˆØ´ (${productUnit})`,
    WIDTH - PADDING,
    710,
  );

  context.strokeStyle = "#d7dde3";
  context.lineWidth = 2;
  quantityAxis.ticks.forEach((tick, index) => {
    const ratio = index / Math.max(quantityAxis.ticks.length - 1, 1);
    const y = quantityPlotBottom - QUANTITY_PLOT_HEIGHT * ratio;
    context.beginPath();
    context.moveTo(plotLeft, y);
    context.lineTo(plotRight, y);
    context.stroke();
    context.fillStyle = "#5f6b76";
    context.font = '18px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
    context.textAlign = "right";
    drawRtlText(context, formatProductTrendIntegerNumber(tick), plotLeft - 18, y + 6);
  });

  context.fillStyle = "#ffffff";
  context.fillRect(WIDTH - PADDING - 700, 682, 700, 42);
  context.fillStyle = "#17202a";
  drawRtlText(
    context,
    `${PRODUCT_TREND_PANEL_LABELS.quantity} (${productUnit})`,
    WIDTH - PADDING,
    710,
  );

  const quantityGroupWidth = (plotRight - plotLeft) / Math.max(points.length, 1);
  drawQuantityLine(
    context,
    chartData,
    "production",
    plotLeft,
    quantityGroupWidth,
    quantityPlotBottom,
    maxQuantity,
    quantitySeries.production.color,
  );
  drawQuantityLine(
    context,
    chartData,
    "quantity",
    plotLeft,
    quantityGroupWidth,
    quantityPlotBottom,
    maxQuantity,
    quantitySeries.quantity.color,
  );
  points.forEach((point, index) => {
    const center = plotLeft + quantityGroupWidth * (index + 0.5);
    context.fillStyle = "#17202a";
    context.font = '600 19px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
    context.textAlign = "center";
    drawRtlText(
      context,
      formatProductTrendPeriod(point.fiscalLabel),
      center,
      quantityPlotBottom + 42,
    );
  });

  context.fillStyle = "#5f6b76";
  context.font = '22px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
  context.textAlign = "right";
  drawSeriesLegend(
    context,
    { sales: series.sales, rate: series.rate },
    WIDTH - PADDING,
    plotBottom + 50,
  );
  drawQuantityLegend(context, quantitySeries, WIDTH - PADDING, 1200);
  context.textAlign = "left";
  drawRtlText(context, `ساپیو — ${productCompanyLabel(data)}`, PADDING, HEIGHT - 54);

  const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, "image/png"));
  if (!blob) throw new Error("Image generation failed.");
  const link = document.createElement("a");
  link.href = URL.createObjectURL(blob);
  link.download = buildMonthlyProductTrendExportFileName(data);
  link.click();
  URL.revokeObjectURL(link.href);
}

function drawQuantityLine(
  context: CanvasRenderingContext2D,
  points: ReturnType<typeof createMonthlyProductTrendChartData>,
  metric: "production" | "quantity",
  plotLeft: number,
  groupWidth: number,
  plotBottom: number,
  maxQuantity: number,
  color: string,
) {
  context.save();
  context.strokeStyle = color;
  context.fillStyle = color;
  context.lineWidth = 5;
  let segment: Array<{ x: number; y: number }> = [];
  const drawSegment = () => {
    if (segment.length === 0) return;
    context.beginPath();
    segment.forEach((point, index) => {
      if (index === 0) context.moveTo(point.x, point.y);
      else context.lineTo(point.x, point.y);
    });
    context.stroke();
    for (const point of segment) {
      context.beginPath();
      context.arc(point.x, point.y, 6, 0, Math.PI * 2);
      context.fill();
    }
    segment = [];
  };
  points.forEach((point, index) => {
    const value = point[metric];
    if (value == null || !Number.isFinite(value)) {
      drawSegment();
      return;
    }
    segment.push({
      x: plotLeft + groupWidth * (index + 0.5),
      y: plotBottom - (value / maxQuantity) * QUANTITY_PLOT_HEIGHT,
    });
  });
  drawSegment();
  context.restore();
}

function drawQuantityLegend(
  context: CanvasRenderingContext2D,
  series: ReturnType<typeof productTrendQuantitySeries>,
  right: number,
  y: number,
) {
  context.save();
  context.font = '22px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
  context.textAlign = "right";
  let cursor = right;
  for (const entry of [series.production, series.quantity]) {
    context.strokeStyle = entry.color;
    context.fillStyle = entry.color;
    context.lineWidth = 4;
    context.beginPath();
    context.moveTo(cursor - 32, y - 8);
    context.lineTo(cursor, y - 8);
    context.stroke();
    context.beginPath();
    context.arc(cursor - 16, y - 8, 5, 0, Math.PI * 2);
    context.fill();
    context.fillStyle = "#17202a";
    drawRtlText(context, entry.label, cursor - 44, y);
    cursor -= context.measureText(entry.label).width + 100;
  }
  context.restore();
}

function drawSeriesLegend(
  context: CanvasRenderingContext2D,
  series: Pick<ReturnType<typeof productTrendSeries>, "sales" | "rate">,
  right: number,
  y: number,
) {
  context.save();
  context.font = '22px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
  context.textAlign = "right";
  let cursor = right;
  for (const [kind, entry] of Object.entries(series)) {
    context.fillStyle = entry.color;
    if (kind === "sales") {
      context.fillRect(cursor - 24, y - 18, 24, 18);
    } else {
      context.strokeStyle = entry.color;
      context.lineWidth = 4;
      context.beginPath();
      context.moveTo(cursor - 32, y - 8);
      context.lineTo(cursor, y - 8);
      context.stroke();
      context.beginPath();
      context.arc(cursor - 16, y - 8, 5, 0, Math.PI * 2);
      context.fill();
    }
    context.fillStyle = "#17202a";
    drawRtlText(context, entry.label, cursor - 44, y);
    cursor -= context.measureText(entry.label).width + 100;
  }
  context.restore();
}

function drawRateLine(
  context: CanvasRenderingContext2D,
  points: MonthlyProductTrendResult["points"],
  plotLeft: number,
  groupWidth: number,
  plotBottom: number,
  maxRate: number,
) {
  context.strokeStyle = productTrendSeries().rate.color;
  context.lineWidth = 5;
  context.fillStyle = productTrendSeries().rate.color;

  let segment: Array<{ x: number; y: number; rate: number; index: number }> = [];
  const drawSegment = () => {
    if (segment.length === 0) return;
    context.beginPath();
    segment.forEach((point, index) => {
      if (index === 0) context.moveTo(point.x, point.y);
      else context.lineTo(point.x, point.y);
    });
    context.stroke();
    segment.forEach((point) => {
      context.beginPath();
      context.arc(point.x, point.y, 7, 0, Math.PI * 2);
      context.fill();
      drawRateLabel(context, point.x, point.y, point.rate, point.index);
    });
    segment = [];
  };

  points.forEach((point, index) => {
    const rate = rateForPoint(point);
    if (rate == null || !Number.isFinite(rate)) {
      drawSegment();
      return;
    }
    const x = plotLeft + groupWidth * (index + 0.5);
    const y = plotBottom - (rate / maxRate) * PLOT_HEIGHT;
    segment.push({ x, y, rate, index });
  });
  drawSegment();
}

function drawRateLabel(
  context: CanvasRenderingContext2D,
  x: number,
  y: number,
  rate: number,
  index: number,
) {
  const labelY = Math.max(PLOT_TOP + 18, y - 18 - (index % 2) * 22);
  context.save();
  context.font = '600 18px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif';
  context.textAlign = "center";
  context.fillStyle = "#b45309";
  context.strokeStyle = "#ffffff";
  context.lineWidth = 6;
  context.direction = "ltr";
  context.strokeText(formatProductTrendRate(rate), x, labelY);
  drawRtlText(context, formatProductTrendRate(rate), x, labelY);
  context.restore();
}

function drawRtlText(context: CanvasRenderingContext2D, value: string, x: number, y: number) {
  context.save();
  const normalizedValue = toPersianDigits(value);
  const numericOnly = /^[+\-۰-۹٠-٩\s.,٬٫/]+$/u.test(normalizedValue);
  context.direction = numericOnly ? "ltr" : "rtl";
  const isolatedValue = normalizedValue.replace(
    /[-+]?\s*[0-9۰-۹٠-٩]+(?:[.,٬٫][0-9۰-۹٠-٩]+)?/gu,
    (numericRun) => `\u2066${numericRun}\u2069`,
  );
  context.fillText(numericOnly ? normalizedValue : isolatedValue, x, y);
  context.restore();
}
