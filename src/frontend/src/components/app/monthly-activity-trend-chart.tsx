import {
  Bar,
  CartesianGrid,
  ComposedChart,
  Legend,
  LabelList,
  Line,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { useEffect, useState } from "react";
import type { MonthlyActivityTrendResult, PsVisualizationResult } from "@/lib/chat.functions";
import { toPersianDigits } from "@/lib/format/persian";
import { createMonthlyTrendChartCardViewModel } from "@/components/app/monthly-activity-trend-chart-view-model";
import { downloadMonthlyTrendChartImage } from "@/components/app/monthly-activity-trend-chart-image";
import { PsGauge } from "@/components/app/ps-gauge";

interface Props {
  data: MonthlyActivityTrendResult;
  psVisualization?: PsVisualizationResult;
}

function formatAmount(value: number | null | undefined): string {
  if (value == null) return "—";
  return toPersianDigits(value.toLocaleString("en", { maximumFractionDigits: 1 }));
}

function formatBarAmount(value: number | string | null | undefined): string {
  if (value == null || value === "") return "";

  const numericValue = typeof value === "number" ? value : Number(value);
  if (!Number.isFinite(numericValue)) return "";

  return toPersianDigits(
    Math.round(numericValue).toLocaleString("en-US", {
      maximumFractionDigits: 0,
    }),
  );
}

function formatAxisAmount(value: number): string {
  return toPersianDigits(
    value.toLocaleString("en", {
      minimumFractionDigits: 0,
      maximumFractionDigits: 3,
    }),
  );
}

function chooseGaugeSide(
  points: Array<{ currentYear: number | null; previousYear: number | null; average: number | null }>,
): "left" | "center" | "right" {
  if (points.length < 3) return "right";
  const maximum = Math.max(
    ...points.flatMap((point) => [point.currentYear, point.previousYear, point.average])
      .filter((value): value is number => value !== null),
    1,
  );
  const occupancy = (side: Array<{ currentYear: number | null; previousYear: number | null; average: number | null }>) =>
    side.reduce((total, point) => {
      const tallest = Math.max(point.currentYear ?? 0, point.previousYear ?? 0, point.average ?? 0);
      return total + Math.pow(tallest / maximum, 2);
    }, 0);
  const zoneSize = Math.ceil(points.length / 3);
  const zones = [
    { name: "left" as const, points: points.slice(0, zoneSize) },
    { name: "center" as const, points: points.slice(zoneSize, zoneSize * 2) },
    { name: "right" as const, points: points.slice(zoneSize * 2) },
  ];
  return zones.reduce((leastOccupied, zone) =>
    occupancy(zone.points) < occupancy(leastOccupied.points) ? zone : leastOccupied,
  ).name;
}

interface TooltipPayloadEntry {
  name: string;
  value: number | null;
  color: string;
}

function CustomTooltip({
  active,
  payload,
  label,
  unit,
}: {
  active?: boolean;
  payload?: TooltipPayloadEntry[];
  label?: string;
  unit: string;
}) {
  if (!active || !payload?.length) return null;

  return (
    <div
      className="rounded-xl bg-surface ring-1 ring-hairline px-3 py-2 text-xs space-y-1 shadow-lg"
      dir="rtl"
    >
      <p className="font-medium text-foreground">{label}</p>
      {payload.map((entry) => (
        // eslint-disable-next-line react/forbid-dom-props
        <p key={entry.name} style={{ color: entry.color }}>
          {entry.name}:{" "}
          {entry.value != null ? `${formatAmount(entry.value)} ${unit}` : "گزارش نشده"}
        </p>
      ))}
    </div>
  );
}

function useInteractiveChartTheme() {
  const [theme, setTheme] = useState<"light" | "dark">("dark");

  useEffect(() => {
    const root = document.documentElement;
    const update = () => setTheme(root.classList.contains("light") ? "light" : "dark");
    update();
    const observer = new MutationObserver(update);
    observer.observe(root, { attributes: true, attributeFilter: ["class"] });
    return () => observer.disconnect();
  }, []);

  return theme;
}

export function MonthlyActivityTrendChart({ data, psVisualization }: Props) {
  const theme = useInteractiveChartTheme();
  const viewModel = createMonthlyTrendChartCardViewModel(data, theme);
  const [isDownloading, setIsDownloading] = useState(false);
  const [downloadError, setDownloadError] = useState<string | null>(null);
  const chartData = viewModel.points.map((point) => ({
    label: point.fiscalMonthLabel,
    currentYear: point.currentYear,
    previousYear: point.previousYear,
    average: point.average,
  }));
  const gaugeSide = chooseGaugeSide(chartData);
  const hasChartData = viewModel.points.some(
    (point) => point.currentYear !== null || point.previousYear !== null || point.average !== null,
  );

  async function downloadImage() {
    setIsDownloading(true);
    setDownloadError(null);
    try {
      const theme = document.documentElement.classList.contains("light") ? "light" : "dark";
      await downloadMonthlyTrendChartImage(createMonthlyTrendChartCardViewModel(data, theme), psVisualization);
    } catch {
      setDownloadError("دریافت تصویر نمودار با خطا مواجه شد. لطفاً دوباره تلاش کنید.");
    } finally {
      setIsDownloading(false);
    }
  }

  return (
    <div className="rounded-2xl ring-1 ring-hairline bg-surface/40 p-4 space-y-3" dir="rtl">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="space-y-0.5">
          <h3 className="text-sm font-medium text-foreground">
            {viewModel.title} {viewModel.companyLabel}
          </h3>
          <p className="text-[11px] text-muted-foreground">{`واحد: ${viewModel.unitLabel}`}</p>
        </div>
        {hasChartData && (
          <button
            type="button"
            onClick={downloadImage}
            disabled={isDownloading}
            aria-busy={isDownloading}
            className="shrink-0 rounded-md border border-hairline bg-surface px-2.5 py-1.5 text-xs text-muted-foreground transition-colors hover:bg-accent hover:text-foreground disabled:cursor-wait disabled:opacity-60"
          >
            {isDownloading ? "در حال آماده‌سازی…" : "دانلود تصویر"}
          </button>
        )}
      </div>

      <div className="relative">
        <div className="h-64 min-w-0 sm:h-72">
          <ResponsiveContainer width="100%" height="100%">
            <ComposedChart data={chartData} margin={{ top: 20, right: 8, left: 4, bottom: 12 }}>
              <CartesianGrid strokeDasharray="3 3" stroke={viewModel.palette.grid} />
              <XAxis
                dataKey="label"
                tick={{ fontSize: 10, fill: viewModel.palette.mutedForeground, textAnchor: "end" }}
                tickLine={false}
                axisLine={false}
                angle={-45}
                interval={0}
                tickMargin={10}
                height={56}
              />
              <YAxis
                tick={{ fontSize: 10, fill: viewModel.palette.mutedForeground }}
                tickLine={false}
                axisLine={false}
                width={76}
                tickFormatter={formatAxisAmount}
                label={{
                  value: data.unitLabelFa || "میلیارد تومان",
                  angle: -90,
                  position: "insideLeft",
                  offset: 12,
                  fill: viewModel.palette.mutedForeground,
                  fontSize: 11,
                }}
              />
              <Tooltip content={<CustomTooltip unit={data.unitLabelFa} />} />
              <Legend
                wrapperStyle={{ fontSize: 11, paddingTop: 8 }}
                formatter={(value: string) => (
                  <span style={{ color: viewModel.palette.mutedForeground }}>{value}</span>
                )}
              />
              <Bar
                dataKey="previousYear"
                name={viewModel.previousYearLegend}
                fill={viewModel.palette.previousYear}
                radius={[3, 3, 0, 0]}
                maxBarSize={22}
                connectNulls={false}
              >
                <LabelList
                  dataKey="previousYear"
                  position="top"
                  formatter={formatBarAmount}
                  fill={viewModel.palette.mutedForeground}
                  fontSize={10}
                  className="hidden sm:block"
                />
              </Bar>
              <Bar
                dataKey="currentYear"
                name={viewModel.currentYearLegend}
                fill={viewModel.palette.currentYear}
                radius={[3, 3, 0, 0]}
                maxBarSize={22}
                connectNulls={false}
              >
                <LabelList
                  dataKey="currentYear"
                  position="top"
                  formatter={formatBarAmount}
                  fill={viewModel.palette.mutedForeground}
                  fontSize={10}
                  className="hidden sm:block"
                />
              </Bar>
              <Line
                type="monotone"
                dataKey="average"
                name={viewModel.averageLegend}
                stroke={viewModel.palette.average}
                strokeWidth={2}
                dot={false}
                connectNulls
              />
            </ComposedChart>
          </ResponsiveContainer>
        </div>
        {psVisualization && (
          <div
            className={`pointer-events-none absolute top-1 z-10 w-[150px] ${
              gaugeSide === "right"
                ? "right-2"
                : gaugeSide === "left"
                  ? "left-2"
                  : "left-1/2 -translate-x-1/2"
            }`}
          >
            <PsGauge data={psVisualization} compact />
          </div>
        )}
      </div>

      {viewModel.explanationLines.length > 0 && (
        <div className="flex flex-col gap-1">
          {viewModel.explanationLines.map((line, index) => (
            <p key={index} className="text-[11px] text-muted-foreground">
              {line.beforeValue}
              {line.valueLabel && (
                <bdi
                  dir="ltr"
                  className={
                    line.tone === "positive"
                      ? "font-semibold text-emerald-500 dark:text-emerald-400"
                      : line.tone === "negative"
                        ? "font-semibold text-rose-600 dark:text-rose-400"
                        : undefined
                  }
                >
                  {line.valueLabel}
                </bdi>
              )}
              {line.afterValue}
            </p>
          ))}
        </div>
      )}

      {downloadError && (
        <p role="alert" className="text-[11px] text-rose">
          {downloadError}
        </p>
      )}
    </div>
  );
}
