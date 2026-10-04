import {
  Bar,
  CartesianGrid,
  ComposedChart,
  LabelList,
  Line,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { useState } from "react";
import type { MonthlyProductTrendResult } from "@/lib/chat.functions";
import { downloadMonthlyProductTrendChartImage } from "@/components/app/monthly-product-trend-chart-image";
import {
  createMonthlyProductTrendChartData,
  formatProductTrendNumber,
  formatProductTrendRate,
  formatProductTrendRateLabel,
  isRateVisible,
  PRODUCT_TREND_PANEL_LABELS,
  productTrendQuantitySeries,
  productTrendSeries,
  type MonthlyProductTrendChartPoint,
} from "@/components/app/monthly-product-trend-chart-model";

function formatAxisNumber(value: number): string {
  return formatProductTrendNumber(value);
}

function productCompanyLabel(data: MonthlyProductTrendResult): string {
  const symbol = data.companySymbol?.trim();
  const companyText = data.companyText?.trim();
  return symbol || companyText || "";
}

function productChartTitle(data: MonthlyProductTrendResult): string {
  const product = data.productTitle?.trim() || "محصول";
  const company = productCompanyLabel(data);
  return company ? `روند فروش ${product} ${company}` : `روند فروش ${product}`;
}

export interface ProductTooltipPayloadEntry {
  payload?: MonthlyProductTrendChartPoint;
}

export function ProductTooltip({
  active,
  label,
  payload,
  points,
}: {
  active?: boolean;
  label?: string | number;
  payload?: ProductTooltipPayloadEntry[];
  points: MonthlyProductTrendChartPoint[];
}) {
  const point = points.find((item) => item.label === String(label)) ?? payload?.[0]?.payload;
  if (!active || !point) return null;

  return (
    <div
      className="rounded-xl bg-surface ring-1 ring-hairline px-3 py-2 text-xs space-y-1 shadow-lg"
      dir="rtl"
    >
      <p className="font-medium text-foreground">دوره: {point.label}</p>
      <p>
        تولید: {formatProductTrendNumber(point.production)} {point.unit}
      </p>
      <p>
        مقدار فروش: {formatProductTrendNumber(point.quantity)} {point.unit}
      </p>
      <p>مبلغ فروش: {formatProductTrendNumber(point.value)} میلیارد تومان</p>
      {isRateVisible(point.rate) && (
        <p>
          نرخ فروش: {formatProductTrendRate(point.rate)} تومان/{point.unit}
        </p>
      )}
    </div>
  );
}

interface RatePointLabelProps {
  x?: number;
  y?: number;
  value?: number | string | null;
  index?: number;
}

function RatePointLabel({ x, y, value, index = 0 }: RatePointLabelProps) {
  if (value == null || value === "") return null;
  const numericValue = typeof value === "number" ? value : Number(value);
  const label = formatProductTrendRateLabel("ValidRate", numericValue);
  if (x == null || y == null || label == null) return null;

  // Alternate two vertical offsets to keep neighboring labels from colliding.
  const offset = 12 + (index % 2) * 14;
  const labelY = Math.max(22, y - offset);
  return (
    <text
      x={x}
      y={labelY}
      textAnchor="middle"
      fill="#b45309"
      fontSize={9}
      fontWeight={600}
      stroke="var(--surface, #ffffff)"
      strokeWidth={3}
      paintOrder="stroke"
    >
      {label}
    </text>
  );
}

/** Product-only chart: sales-value bars and the calculated product-rate line. */
export function MonthlyProductTrendChart({ data }: { data: MonthlyProductTrendResult }) {
  const [isDownloading, setIsDownloading] = useState(false);
  if (data.resolutionState !== "Resolved") {
    return (
      <div className="rounded-2xl ring-1 ring-hairline bg-surface/40 p-4 text-sm" dir="rtl">
        {data.message ?? "محصول درخواستی یافت نشد."}
      </div>
    );
  }

  const chartData = createMonthlyProductTrendChartData(data);
  const title = productChartTitle(data);
  const unit = data.productUnit ?? "واحد محصول";
  const series = productTrendSeries(data.productUnit);
  const quantitySeries = productTrendQuantitySeries();

  async function downloadImage() {
    setIsDownloading(true);
    try {
      await downloadMonthlyProductTrendChartImage(data);
    } finally {
      setIsDownloading(false);
    }
  }

  return (
    <section
      data-testid="monthly-product-trend-chart"
      className="rounded-2xl ring-1 ring-hairline bg-surface/40 p-3 space-y-2"
      dir="rtl"
      aria-label={title}
    >
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <h3 className="text-xs leading-5 font-medium text-foreground">{title}</h3>
          <p className="text-[10px] leading-4 text-muted-foreground">
            مبلغ فروش به میلیارد تومان و نرخ فروش به تومان/{unit}
          </p>
        </div>
        <button
          type="button"
          onClick={downloadImage}
          disabled={isDownloading}
          aria-busy={isDownloading}
          className="shrink-0 rounded-md border border-hairline bg-surface px-2.5 py-1.5 text-xs text-muted-foreground transition-colors hover:bg-accent hover:text-foreground disabled:cursor-wait disabled:opacity-60"
        >
          {isDownloading ? "در حال آماده‌سازی…" : "دانلود تصویر"}
        </button>
      </div>

      <ul
        aria-label="راهنمای نمودار"
        className="flex flex-wrap items-center gap-x-4 gap-y-1 text-[11px] leading-4 text-muted-foreground"
      >
        <li className="inline-flex items-center gap-1.5">
          <svg width="16" height="10" viewBox="0 0 16 10" aria-hidden="true">
            <rect x="3" width="10" height="10" rx="1" fill={series.sales.color} />
          </svg>
          {series.sales.label}
        </li>
        <li className="inline-flex items-center gap-1.5">
          <svg width="16" height="10" viewBox="0 0 16 10" aria-hidden="true">
            <path d="M0 5H16" stroke={series.rate.color} strokeWidth="2" />
            <circle cx="8" cy="5" r="2" fill={series.rate.color} />
          </svg>
          {series.rate.label}
        </li>
      </ul>

      <div
        data-testid="product-sales-rate-panel"
        aria-label="Ù…Ø¨Ù„Øº ÙØ±ÙˆØ´ Ùˆ Ù†Ø±Ø® ÙØ±ÙˆØ´"
        className="space-y-1"
        dir="rtl"
        role="group"
        {...{ "aria-label": PRODUCT_TREND_PANEL_LABELS.main }}
      >
        <h4 aria-hidden="true" className="hidden">
          Ù…Ø¨Ù„Øº ÙØ±ÙˆØ´ Ùˆ Ù†Ø±Ø® ÙØ±ÙˆØ´
        </h4>
        <h4 className="px-1 text-[11px] font-medium text-muted-foreground">
          {PRODUCT_TREND_PANEL_LABELS.main}
        </h4>
        <div className="h-60 min-w-0">
          <ResponsiveContainer width="100%" height="100%">
            <ComposedChart
              syncId="monthly-product-trend"
              data={chartData}
              margin={{ top: 30, right: 8, left: 8, bottom: 0 }}
            >
              <CartesianGrid strokeDasharray="3 3" />
              <XAxis dataKey="label" hide />
              <YAxis
                yAxisId="rate"
                orientation="left"
                tickFormatter={formatProductTrendRate}
                width={76}
                tick={{ fontSize: 10 }}
                label={{
                  value: series.rate.label,
                  angle: -90,
                  position: "insideLeft",
                  offset: 0,
                  fontSize: 10,
                }}
              />
              <YAxis
                yAxisId="value"
                orientation="right"
                tickFormatter={formatAxisNumber}
                width={54}
                tick={{ fontSize: 10 }}
                label={{
                  value: "مبلغ فروش (میلیارد تومان)",
                  angle: 90,
                  position: "insideRight",
                  offset: 0,
                  fontSize: 10,
                }}
              />
              <Tooltip content={<ProductTooltip points={chartData} />} />
              <Bar
                yAxisId="value"
                dataKey="value"
                name={series.sales.label}
                fill={series.sales.color}
                radius={[3, 3, 0, 0]}
                maxBarSize={28}
              >
                <LabelList
                  dataKey="value"
                  position="top"
                  formatter={(value: number | string | null | undefined) => {
                    const numericValue = Number(value);
                    return Number.isFinite(numericValue)
                      ? formatProductTrendNumber(numericValue)
                      : "";
                  }}
                  fontSize={9}
                />
              </Bar>
              <Line
                yAxisId="rate"
                type="monotone"
                dataKey="rate"
                name={series.rate.label}
                stroke={series.rate.color}
                strokeWidth={2}
                connectNulls={false}
                dot={{ r: 3 }}
              >
                <LabelList dataKey="rate" content={<RatePointLabel />} />
              </Line>
            </ComposedChart>
          </ResponsiveContainer>
        </div>
      </div>
      <div
        data-testid="product-quantity-panel"
        {...{ "aria-label": PRODUCT_TREND_PANEL_LABELS.quantity }}
        aria-label="Ù…Ù‚Ø¯Ø§Ø± ØªÙˆÙ„ÛŒØ¯ Ùˆ ÙØ±ÙˆØ´"
        className="space-y-1"
        dir="rtl"
        role="group"
      >
        <div className="flex flex-wrap items-center justify-between gap-2 px-1">
          <h4 aria-hidden="true" className="hidden">
            Ù…Ù‚Ø¯Ø§Ø± ØªÙˆÙ„ÛŒØ¯ Ùˆ Ù…Ù‚Ø¯Ø§Ø± ÙØ±ÙˆØ´
          </h4>
          <h4 className="text-[11px] font-medium text-muted-foreground">
            {PRODUCT_TREND_PANEL_LABELS.quantity}
          </h4>
          <ul
            aria-label="Ù„ÛŒØ³Øª Ø±Ø§Ù‡Ù†Ù…Ø§ÛŒ Ù…Ù‚Ø§Ø¯ÛŒØ±"
            className="flex flex-wrap items-center gap-x-3 text-[10px] text-muted-foreground"
          >
            {([quantitySeries.production, quantitySeries.quantity] as const).map((entry) => (
              <li className="inline-flex items-center gap-1" key={entry.label}>
                <svg width="16" height="10" viewBox="0 0 16 10" aria-hidden="true">
                  <path d="M0 5H16" stroke={entry.color} strokeWidth="2" />
                  <circle cx="8" cy="5" r="2" fill={entry.color} />
                </svg>
                {entry.label}
              </li>
            ))}
          </ul>
        </div>
        <div className="h-52 min-w-0">
          <ResponsiveContainer width="100%" height="100%">
            <ComposedChart
              syncId="monthly-product-trend"
              data={chartData}
              margin={{ top: 8, right: 8, left: 8, bottom: 22 }}
            >
              <CartesianGrid strokeDasharray="3 3" />
              <XAxis
                dataKey="label"
                tick={{ fontSize: 10 }}
                angle={-35}
                height={42}
                interval="preserveStartEnd"
              />
              <YAxis
                tickFormatter={formatAxisNumber}
                width={64}
                tick={{ fontSize: 10 }}
                label={{
                  value: `${unit}`,
                  angle: -90,
                  position: "insideLeft",
                  offset: 0,
                  fontSize: 10,
                }}
              />
              <Line
                type="monotone"
                dataKey="production"
                name={quantitySeries.production.label}
                stroke={quantitySeries.production.color}
                strokeWidth={2}
                connectNulls={false}
                dot={{ r: 2 }}
                isAnimationActive={false}
              />
              <Line
                type="monotone"
                dataKey="quantity"
                name={quantitySeries.quantity.label}
                stroke={quantitySeries.quantity.color}
                strokeWidth={2}
                connectNulls={false}
                dot={{ r: 2 }}
                isAnimationActive={false}
              />
            </ComposedChart>
          </ResponsiveContainer>
        </div>
      </div>
    </section>
  );
}
