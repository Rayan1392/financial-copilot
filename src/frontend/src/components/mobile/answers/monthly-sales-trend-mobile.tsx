import { useId, useMemo, useState } from "react";
import {
  Bar,
  CartesianGrid,
  Cell,
  ComposedChart,
  Line,
  ResponsiveContainer,
  XAxis,
  YAxis,
} from "recharts";
import { Download } from "lucide-react";
import type { MonthlyActivityTrendResult, PsVisualizationResult } from "@/lib/chat.functions";
import { toPersianDigits } from "@/lib/format/persian";
import {
  createMonthlyTrendChartCardViewModel,
  formatMonthlyTrendAmount,
} from "@/components/app/monthly-activity-trend-chart-view-model";
import { defaultSelectedMonth, sameMonthChangePercent } from "./monthly-sales-trend-mobile-model";
import { columnIndexFromTap } from "./chart-tap";
import { downloadMonthlyTrendChartImage } from "@/components/app/monthly-activity-trend-chart-image";
import { PsGauge } from "@/components/app/ps-gauge";
import { useInteractiveChartTheme } from "@/components/app/use-chart-theme";
import { SignedChange } from "@/components/mobile/signed-change";
import { cn } from "@/lib/utils";

interface Props {
  data: MonthlyActivityTrendResult;
  psVisualization?: PsVisualizationResult;
}

const AXIS_FONT_SIZE = 12;
const CHART_MARGIN = { top: 8, right: 4, left: 0, bottom: 4 };
/** Rough glyph width of a 12 px digit, used to size the value axis to its longest tick. */
const AXIS_DIGIT_WIDTH = 7.5;

function axisAmount(value: number): string {
  return toPersianDigits(Math.round(value).toLocaleString("en-US"));
}

/**
 * Monthly sales trend for phones (Feature 153). Same data as the desktop card, laid out for 320–390 px:
 * a full-text legend, a chart sized to its numbers, and a tap-to-select detail panel below the plot instead of a
 * floating tooltip, so a finger never covers the bars it is reading.
 */
export function MonthlySalesTrendMobile({ data, psVisualization }: Props) {
  const theme = useInteractiveChartTheme();
  const viewModel = createMonthlyTrendChartCardViewModel(data, theme);
  const points = viewModel.points;
  const [selected, setSelected] = useState(() => defaultSelectedMonth(points));
  const [isDownloading, setIsDownloading] = useState(false);
  const [downloadError, setDownloadError] = useState<string | null>(null);
  const titleId = useId();
  const hasChartData = points.some(
    (point) => point.currentYear !== null || point.previousYear !== null || point.average !== null,
  );
  const chartData = points.map((point) => ({
    label: point.fiscalMonthLabel,
    currentYear: point.currentYear,
    previousYear: point.previousYear,
    average: point.average,
  }));
  const axisWidth = useMemo(() => {
    const maximum = Math.max(
      0,
      ...points.flatMap((point) => [
        point.currentYear ?? 0,
        point.previousYear ?? 0,
        point.average ?? 0,
      ]),
    );
    return Math.max(32, axisAmount(maximum).length * AXIS_DIGIT_WIDTH + 10);
  }, [points]);

  const selectedIndex = Math.min(Math.max(selected, 0), Math.max(points.length - 1, 0));
  const point = points[selectedIndex];
  const source = data.chartPoints[selectedIndex];
  const yearLabel = (year: number | null | undefined, fallback: string) =>
    year ? toPersianDigits(String(year)) : fallback;
  const change = point ? sameMonthChangePercent(point) : null;

  async function downloadImage() {
    setIsDownloading(true);
    setDownloadError(null);
    try {
      await downloadMonthlyTrendChartImage(
        createMonthlyTrendChartCardViewModel(data, theme),
        psVisualization,
      );
    } catch {
      setDownloadError("دریافت تصویر نمودار با خطا مواجه شد. لطفاً دوباره تلاش کنید.");
    } finally {
      setIsDownloading(false);
    }
  }

  const amountWithUnit = (value: number | null, missing: string) =>
    value === null ? missing : `${formatMonthlyTrendAmount(value)} ${viewModel.unitLabel}`;

  return (
    <section
      dir="rtl"
      aria-labelledby={titleId}
      className="space-y-4 rounded-2xl bg-surface/40 p-3 ring-1 ring-hairline"
    >
      <header className="space-y-2">
        <h3 id={titleId} className="text-[15px] font-semibold leading-7 text-foreground">
          {viewModel.title} <span className="break-words">{viewModel.companyLabel}</span>
        </h3>
        <div className="flex flex-wrap items-center gap-2">
          <span className="rounded-full bg-surface-2 px-2.5 py-1 text-xs text-muted-foreground">
            واحد: {viewModel.unitLabel}
          </span>
          {hasChartData && (
            <button
              type="button"
              onClick={downloadImage}
              disabled={isDownloading}
              aria-busy={isDownloading}
              className="ms-auto inline-flex min-h-11 items-center gap-1.5 rounded-xl px-3 text-sm text-muted-foreground ring-1 ring-hairline active:bg-surface-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40 disabled:cursor-wait disabled:opacity-60"
            >
              <Download className="size-4" aria-hidden="true" />
              {isDownloading ? "در حال آماده‌سازی…" : "دانلود تصویر"}
            </button>
          )}
        </div>
      </header>

      {hasChartData ? (
        <>
          <p className="text-xs leading-6 text-muted-foreground">
            عدد کنار هر سال، مجموع فروش ماه‌های گزارش‌شده آن سال است.
          </p>
          <ul
            className="space-y-1.5 text-[13px] leading-6 text-foreground"
            aria-label="راهنمای نمودار"
          >
            {[
              {
                label: viewModel.previousYearLegend,
                color: viewModel.palette.previousYear,
                kind: "bar",
              },
              {
                label: viewModel.currentYearLegend,
                color: viewModel.palette.currentYear,
                kind: "bar",
              },
              { label: viewModel.averageLegend, color: viewModel.palette.average, kind: "line" },
            ].map((item) => (
              <li key={item.label} className="flex items-start gap-2">
                <span
                  aria-hidden="true"
                  className={cn(
                    "mt-2 shrink-0",
                    item.kind === "bar" ? "size-3 rounded-sm" : "h-0.5 w-4 rounded-full",
                  )}
                  style={{ backgroundColor: item.color }}
                />
                <span className="min-w-0">{item.label}</span>
              </li>
            ))}
          </ul>

          {/* A tap anywhere in a month's column selects it; vertical scrolling still passes through. */}
          <div
            className="h-60 w-full min-w-0 cursor-pointer touch-pan-y"
            dir="ltr"
            role="img"
            onClick={(event) => {
              const box = event.currentTarget.getBoundingClientRect();
              const index = columnIndexFromTap(
                event.clientX - box.left,
                box.width,
                { left: CHART_MARGIN.left + axisWidth, right: CHART_MARGIN.right },
                points.length,
              );
              if (index !== null) setSelected(index);
            }}
            aria-label={`نمودار ${viewModel.title} ${viewModel.companyLabel}. برای دیدن مقدار دقیق هر ماه، روی آن بزنید یا از دکمه‌های ماه قبل و بعد استفاده کنید.`}
          >
            <ResponsiveContainer width="100%" height="100%">
              <ComposedChart data={chartData} margin={CHART_MARGIN} barCategoryGap="18%" barGap={1}>
                <CartesianGrid
                  strokeDasharray="3 3"
                  stroke={viewModel.palette.grid}
                  vertical={false}
                />
                <XAxis
                  dataKey="label"
                  interval={0}
                  angle={-60}
                  height={62}
                  tickMargin={6}
                  tickLine={false}
                  axisLine={false}
                  tick={{
                    fontSize: AXIS_FONT_SIZE,
                    fill: viewModel.palette.mutedForeground,
                    textAnchor: "end",
                  }}
                />
                <YAxis
                  width={axisWidth}
                  tickCount={4}
                  tickLine={false}
                  axisLine={false}
                  tickFormatter={axisAmount}
                  tick={{ fontSize: AXIS_FONT_SIZE, fill: viewModel.palette.mutedForeground }}
                />
                <Bar
                  dataKey="previousYear"
                  name={viewModel.previousYearLegend}
                  fill={viewModel.palette.previousYear}
                  radius={[2, 2, 0, 0]}
                  isAnimationActive={false}
                >
                  {chartData.map((_, index) => (
                    <Cell key={index} fillOpacity={index === selectedIndex ? 1 : 0.45} />
                  ))}
                </Bar>
                <Bar
                  dataKey="currentYear"
                  name={viewModel.currentYearLegend}
                  fill={viewModel.palette.currentYear}
                  radius={[2, 2, 0, 0]}
                  isAnimationActive={false}
                >
                  {chartData.map((_, index) => (
                    <Cell key={index} fillOpacity={index === selectedIndex ? 1 : 0.45} />
                  ))}
                </Bar>
                <Line
                  type="monotone"
                  dataKey="average"
                  name={viewModel.averageLegend}
                  stroke={viewModel.palette.average}
                  strokeWidth={2}
                  dot={false}
                  activeDot={false}
                  connectNulls
                  isAnimationActive={false}
                />
              </ComposedChart>
            </ResponsiveContainer>
          </div>

          {point && (
            <div className="rounded-xl bg-surface p-3 ring-1 ring-hairline">
              <div aria-live="polite" className="min-h-[148px]">
                <p className="text-sm font-semibold text-foreground">
                  {point.fiscalMonthLabel}
                  <span className="font-normal text-muted-foreground">
                    {" "}
                    · ماه {toPersianDigits(String(selectedIndex + 1))} از{" "}
                    {toPersianDigits(String(points.length))}
                  </span>
                </p>
                <dl className="mt-2 space-y-1.5 text-sm">
                  <DetailRow
                    color={viewModel.palette.currentYear}
                    term={`${point.fiscalMonthLabel} ${yearLabel(source?.currentFiscalYear, "سال جاری")}`}
                    value={amountWithUnit(point.currentYear, "گزارش نشده")}
                    missing={point.currentYear === null}
                  />
                  <DetailRow
                    color={viewModel.palette.previousYear}
                    term={`${point.fiscalMonthLabel} ${yearLabel(source?.previousFiscalYear, "سال قبل")}`}
                    value={amountWithUnit(point.previousYear, "گزارش نشده")}
                    missing={point.previousYear === null}
                  />
                  <DetailRow
                    color={viewModel.palette.average}
                    term={viewModel.averageLegend}
                    value={amountWithUnit(point.average, "–")}
                    missing={point.average === null}
                  />
                  {change !== null && (
                    <div className="flex items-baseline justify-between gap-3 border-t border-hairline pt-1.5">
                      <dt className="text-muted-foreground">تغییر نسبت به ماه مشابه سال قبل</dt>
                      <dd>
                        <SignedChange value={change} />
                      </dd>
                    </div>
                  )}
                </dl>
              </div>
              <div className="mt-2 grid grid-cols-2 gap-2">
                <button
                  type="button"
                  onClick={() => setSelected(selectedIndex - 1)}
                  disabled={selectedIndex === 0}
                  className="min-h-11 rounded-xl bg-surface-2 text-sm font-medium text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40 disabled:opacity-40"
                >
                  ماه قبل
                </button>
                <button
                  type="button"
                  onClick={() => setSelected(selectedIndex + 1)}
                  disabled={selectedIndex >= points.length - 1}
                  className="min-h-11 rounded-xl bg-surface-2 text-sm font-medium text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40 disabled:opacity-40"
                >
                  ماه بعد
                </button>
              </div>
            </div>
          )}

          <table className="sr-only">
            <caption>
              {viewModel.title} {viewModel.companyLabel}، واحد {viewModel.unitLabel}
            </caption>
            <thead>
              <tr>
                <th scope="col">ماه</th>
                <th scope="col">{viewModel.currentYearLegend}</th>
                <th scope="col">{viewModel.previousYearLegend}</th>
                <th scope="col">{viewModel.averageLegend}</th>
              </tr>
            </thead>
            <tbody>
              {points.map((item) => (
                <tr key={item.fiscalMonthIndex}>
                  <th scope="row">{item.fiscalMonthLabel}</th>
                  <td>{amountWithUnit(item.currentYear, "گزارش نشده")}</td>
                  <td>{amountWithUnit(item.previousYear, "گزارش نشده")}</td>
                  <td>{amountWithUnit(item.average, "ناموجود")}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      ) : (
        <p className="rounded-xl bg-surface p-3 text-sm text-muted-foreground">
          داده‌ای برای رسم نمودار گزارش نشده است.
        </p>
      )}

      {psVisualization && psVisualization.gaugeBands.length > 0 && (
        <div className="mx-auto w-full max-w-[240px]">
          <PsGauge data={psVisualization} compact />
        </div>
      )}

      {viewModel.explanationLines.length > 0 && (
        <ul className="space-y-1.5 text-[13px] leading-6 text-muted-foreground">
          {viewModel.explanationLines.map((line, index) => (
            <li key={index}>
              {line.beforeValue}
              {line.valueLabel && (
                <bdi
                  dir="ltr"
                  className={cn(
                    "mx-1 font-semibold",
                    line.tone === "positive" && "text-emerald",
                    line.tone === "negative" && "mobile-danger-text",
                  )}
                >
                  {line.valueLabel}
                </bdi>
              )}
              {line.afterValue}
            </li>
          ))}
        </ul>
      )}

      <p className="text-xs leading-6 text-muted-foreground">
        {viewModel.sourceContext} · {viewModel.calculationContext}
      </p>

      {downloadError && (
        <p role="alert" className="text-sm mobile-danger-text">
          {downloadError}
        </p>
      )}
    </section>
  );
}

function DetailRow({
  color,
  term,
  value,
  missing,
}: {
  color: string;
  term: string;
  value: string;
  missing: boolean;
}) {
  return (
    <div className="flex items-baseline justify-between gap-3">
      <dt className="flex min-w-0 items-center gap-2 text-muted-foreground">
        <span
          aria-hidden="true"
          className="size-2.5 shrink-0 rounded-sm"
          style={{ backgroundColor: color }}
        />
        <span className="min-w-0">{term}</span>
      </dt>
      <dd
        className={cn(
          "shrink-0 tabular-nums",
          missing ? "text-muted-foreground" : "font-semibold text-foreground",
        )}
      >
        {value}
      </dd>
    </div>
  );
}
