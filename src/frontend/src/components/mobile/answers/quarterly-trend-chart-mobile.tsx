import { useCallback, useState } from "react";
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
import type { QuarterlyFinancialTableModel } from "@/components/app/quarterly-financial-table";
import {
  BANKING_CHART_SERIES,
  QUARTERLY_CHART_SERIES,
  quarterlyChartPointsChronologically,
  quarterlyChartSeriesRows,
  type QuarterlyChartSeriesKey,
} from "@/components/app/quarterly-financial-chart-data";
import { quarterlyPeriodHeaderLines } from "@/components/app/quarterly-financial-table-header";
import { useInteractiveChartTheme } from "@/components/app/use-chart-theme";
import { toPersianDigits } from "@/lib/format/persian";
import { cn } from "@/lib/utils";
import { columnIndexFromTap } from "./chart-tap";
import {
  formatQuarterlyCell,
  isMissingQuarterlyCell,
  shortPeriodLabel,
} from "./quarterly-report-mobile-model";
import {
  amountAxisScale,
  defaultSelectedPeriod,
  periodTickLines,
  QUARTERLY_TREND_SERIES_COLORS,
} from "./quarterly-trend-chart-mobile-model";

const AXIS_FONT_SIZE = 12;
/** Rough glyph width of a 12 px digit, used to size each value axis to its longest tick. */
const AXIS_DIGIT_WIDTH = 7.5;
const CHART_MARGIN = { top: 8, right: 4, left: 4, bottom: 4 };
/**
 * Narrowest period column that still fits the two-line «فصل ۲» / «۱۴۰۵» label (about 34 px at 12 px) with a gap.
 * Narrower columns (many periods, or the smallest phones) get angled labels like the monthly chart.
 */
const MIN_UPRIGHT_COLUMN = 38;
/** Before the chart is measured, assume up to five periods fit upright at phone width. */
const UNMEASURED_UPRIGHT_TICKS = 5;
const PERCENT = "٪";

const axisNumber = new Intl.NumberFormat("fa-IR-u-nu-arabext", { maximumFractionDigits: 1 });

function axisWidth(labels: string[]): number {
  return Math.max(28, Math.max(0, ...labels.map((label) => label.length)) * AXIS_DIGIT_WIDTH + 10);
}

/**
 * Recharts draws round ("nice") ticks, so from 10 upwards the tick labels have no fraction even when the data does;
 * sizing the axis from the raw value would waste the width the period columns need.
 */
function tickEstimate(value: number): number {
  return Math.abs(value) >= 10 ? Math.round(value) : value;
}

interface SeriesView {
  key: QuarterlyChartSeriesKey;
  label: string;
  unit: string;
  percent: boolean;
  color: string;
}

/**
 * Quarterly trend chart for phones (Feature 153), opened from «نمودار روند» in the phone quarterly report. Same
 * series and values as the desktop chart, on the themed surface: it fits the screen width instead of scrolling, the
 * amount axis is scaled to short ticks, and a tap on a period (or «فصل قبل»/«فصل بعد») shows that period's exact
 * table values in a panel below the plot instead of a tooltip over it.
 */
export function QuarterlyTrendChartMobile({ model }: { model: QuarterlyFinancialTableModel }) {
  const theme = useInteractiveChartTheme();
  const points = quarterlyChartPointsChronologically(model);
  const banking = model.templateId === "BankingIncomeStatement";
  const seriesRows = quarterlyChartSeriesRows(model);
  const colors = QUARTERLY_TREND_SERIES_COLORS[theme];
  // Only series whose line item is in the report; a missing row has nothing to plot or inspect.
  const series: SeriesView[] = (banking ? BANKING_CHART_SERIES : QUARTERLY_CHART_SERIES)
    .filter((item) => seriesRows[item.key])
    .map((item) => ({
      key: item.key,
      label: item.label,
      unit: item.unit,
      percent: item.unit === PERCENT,
      color: colors[item.key],
    }));
  const amountSeries = series.filter((item) => !item.percent);
  const percentSeries = series.filter((item) => item.percent);
  const [selected, setSelected] = useState(() =>
    defaultSelectedPeriod(
      points,
      series.map((item) => item.key),
    ),
  );
  const [chartWidth, setChartWidth] = useState(0);
  const measureChart = useCallback((element: HTMLDivElement | null) => {
    if (!element) return;
    const observer = new ResizeObserver(([entry]) => setChartWidth(entry.contentRect.width));
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  const axes = (() => {
    const values = (keys: SeriesView[]) =>
      points.flatMap((point) =>
        keys.map((item) => point[item.key]).filter((value): value is number => value !== null),
      );
    const amounts = values(amountSeries);
    const percents = values(percentSeries);
    const scale = amountAxisScale(Math.max(0, ...amounts.map(Math.abs)));
    const amountTick = (value: number) => axisNumber.format(value / scale.divisor);
    const percentTick = (value: number) => `${axisNumber.format(value)}${PERCENT}`;
    const extremes = (list: number[]) =>
      list.length ? [Math.min(...list), Math.max(...list)] : [];
    return {
      scale,
      amountTick,
      percentTick,
      amountWidth: amountSeries.length
        ? axisWidth(
            extremes(amounts).map((value) =>
              amountTick(tickEstimate(value / scale.divisor) * scale.divisor),
            ),
          )
        : 0,
      percentWidth: percentSeries.length
        ? axisWidth(extremes(percents).map((value) => percentTick(tickEstimate(value))))
        : 0,
    };
  })();

  if (!points.length || !series.length) return null;

  const selectedIndex = Math.min(Math.max(selected, 0), points.length - 1);
  const plot = {
    left: CHART_MARGIN.left + axes.percentWidth,
    right: CHART_MARGIN.right + axes.amountWidth,
  };
  const upright =
    chartWidth > 0
      ? (chartWidth - plot.left - plot.right) / points.length >= MIN_UPRIGHT_COLUMN
      : points.length <= UNMEASURED_UPRIGHT_TICKS;
  const tickLines = points.map((point, index) =>
    periodTickLines(model.quarters[index], point.shortLabel),
  );
  const periodName = (index: number) => {
    const quarter = model.quarters[index];
    return shortPeriodLabel(quarter, quarterlyPeriodHeaderLines(quarter));
  };
  const periodDetail = (index: number) => {
    const lines = quarterlyPeriodHeaderLines(model.quarters[index]);
    const detail = lines.length === 3 ? `${lines[1]} ${lines[2]}` : lines.join(" ");
    return detail === periodName(index) ? null : detail;
  };
  // Exactly the table cell text the cards show, with the unit for amounts.
  const valueText = (item: SeriesView, index: number) => {
    const row = seriesRows[item.key];
    const raw = row?.values[index];
    const missing = !row || isMissingQuarterlyCell(raw);
    const text = row ? formatQuarterlyCell(row, raw) : "—";
    return {
      missing,
      text: missing || item.percent || text === "-" ? text : `${text} ${item.unit}`,
    };
  };
  const title = banking ? "روند درآمد و سود فصلی بانک" : "روند فروش و سود فصلی";
  const axisNotes = [
    amountSeries.length ? `محور راست: مبالغ به ${axes.scale.unitLabel}` : null,
    percentSeries.length ? `محور چپ: ${banking ? "حاشیه‌ها" : "مارجین ناخالص"} به درصد` : null,
  ].filter((note): note is string => note !== null);
  const dimmed = (index: number) => (index === selectedIndex ? 1 : 0.45);

  return (
    <div className="space-y-3 bg-surface p-3">
      <div className="space-y-1">
        <h4 className="text-sm font-semibold leading-6 text-foreground">{title}</h4>
        {axisNotes.map((note) => (
          <p key={note} className="text-xs leading-5 text-muted-foreground">
            {note}
          </p>
        ))}
      </div>

      <ul
        className="flex flex-wrap gap-x-4 gap-y-1 text-[13px] leading-6 text-foreground"
        aria-label="راهنمای نمودار"
      >
        {series.map((item) => (
          <li key={item.key} className="flex items-center gap-1.5">
            <SeriesSwatch color={item.color} line={item.percent} />
            {item.label}
          </li>
        ))}
      </ul>

      {/*
        A tap anywhere in a period's column selects it; vertical scrolling still passes through. The plot bleeds into
        the side padding so each period column gets the width its label needs at 320 px.
      */}
      <div
        ref={measureChart}
        className={cn(
          "-mx-3 min-w-0 cursor-pointer touch-pan-y",
          upright ? "h-64" : "h-72",
          "[&_.recharts-cartesian-axis-tick-value]:fill-muted-foreground [&_.recharts-cartesian-grid_line]:stroke-muted-foreground/20",
        )}
        dir="ltr"
        role="img"
        aria-label={`${title}. برای دیدن مقدار دقیق هر فصل، روی آن بزنید یا از دکمه‌های فصل قبل و بعد استفاده کنید.`}
        onClick={(event) => {
          const box = event.currentTarget.getBoundingClientRect();
          const index = columnIndexFromTap(
            event.clientX - box.left,
            box.width,
            plot,
            points.length,
          );
          if (index !== null) setSelected(index);
        }}
      >
        <ResponsiveContainer width="100%" height="100%">
          <ComposedChart data={points} margin={CHART_MARGIN} barCategoryGap="16%" barGap={1}>
            <CartesianGrid strokeDasharray="3 3" vertical={false} />
            <XAxis
              dataKey="shortLabel"
              interval={0}
              tickLine={false}
              axisLine={false}
              height={upright ? 40 : 84}
              tick={(props: { x: number; y: number; index: number }) => (
                <PeriodTick
                  {...props}
                  lines={tickLines[props.index] ?? ["", ""]}
                  upright={upright}
                  selected={props.index === selectedIndex}
                />
              )}
            />
            {amountSeries.length > 0 && (
              <YAxis
                yAxisId="amount"
                orientation="right"
                width={axes.amountWidth}
                tickCount={4}
                tickLine={false}
                axisLine={false}
                tickFormatter={axes.amountTick}
                tick={{ fontSize: AXIS_FONT_SIZE }}
              />
            )}
            {percentSeries.length > 0 && (
              <YAxis
                yAxisId="percent"
                orientation="left"
                width={axes.percentWidth}
                tickCount={4}
                tickLine={false}
                axisLine={false}
                tickFormatter={axes.percentTick}
                tick={{ fontSize: AXIS_FONT_SIZE }}
              />
            )}
            {amountSeries.map((item) => (
              <Bar
                key={item.key}
                yAxisId="amount"
                dataKey={item.key}
                name={item.label}
                fill={item.color}
                radius={[2, 2, 0, 0]}
                isAnimationActive={false}
              >
                {points.map((_, index) => (
                  <Cell key={index} fillOpacity={dimmed(index)} />
                ))}
              </Bar>
            ))}
            {percentSeries.map((item) => (
              <Line
                key={item.key}
                yAxisId="percent"
                type="monotone"
                dataKey={item.key}
                name={item.label}
                stroke={item.color}
                strokeWidth={2.5}
                connectNulls={false}
                activeDot={false}
                isAnimationActive={false}
                dot={(props: { cx?: number; cy?: number; index: number }) => (
                  <circle
                    key={props.index}
                    cx={props.cx}
                    cy={props.cy}
                    r={
                      props.cx == null || props.cy == null
                        ? 0
                        : props.index === selectedIndex
                          ? 5
                          : 3
                    }
                    fill={item.color}
                    className="stroke-surface"
                    strokeWidth={props.index === selectedIndex ? 2 : 1}
                  />
                )}
              />
            ))}
          </ComposedChart>
        </ResponsiveContainer>
      </div>

      <div className="rounded-xl bg-background/40 p-3 ring-1 ring-hairline">
        <div aria-live="polite">
          <p className="text-sm font-semibold text-foreground">
            {periodName(selectedIndex)}
            <span className="font-normal text-muted-foreground">
              {" "}
              · فصل {toPersianDigits(selectedIndex + 1)} از {toPersianDigits(points.length)}
            </span>
          </p>
          {periodDetail(selectedIndex) && (
            <p className="text-xs leading-6 text-muted-foreground">{periodDetail(selectedIndex)}</p>
          )}
          <dl className="mt-2 space-y-1.5 text-sm">
            {series.map((item) => {
              const value = valueText(item, selectedIndex);
              return (
                <div
                  key={item.key}
                  className="flex flex-wrap items-baseline justify-between gap-x-3"
                >
                  <dt className="flex min-w-0 items-center gap-2 text-muted-foreground">
                    <SeriesSwatch color={item.color} line={item.percent} />
                    <span className="min-w-0">{item.label}</span>
                  </dt>
                  <dd
                    className={cn(
                      "ms-auto shrink-0 tabular-nums",
                      value.missing ? "text-muted-foreground" : "font-semibold text-foreground",
                    )}
                  >
                    <bdi>{value.text}</bdi>
                    {value.missing && <span className="sr-only">ناموجود</span>}
                  </dd>
                </div>
              );
            })}
          </dl>
        </div>
        <div className="mt-2 grid grid-cols-2 gap-2">
          <button
            type="button"
            onClick={() => setSelected(selectedIndex - 1)}
            disabled={selectedIndex === 0}
            className="min-h-11 rounded-xl bg-surface-2 text-sm font-medium text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40 disabled:opacity-40"
          >
            فصل قبل
          </button>
          <button
            type="button"
            onClick={() => setSelected(selectedIndex + 1)}
            disabled={selectedIndex >= points.length - 1}
            className="min-h-11 rounded-xl bg-surface-2 text-sm font-medium text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40 disabled:opacity-40"
          >
            فصل بعد
          </button>
        </div>
      </div>

      <table className="sr-only">
        <caption>مقادیر نمودار {title}، مبالغ به میلیون ریال و حاشیه‌ها به درصد</caption>
        <thead>
          <tr>
            <th scope="col">فصل</th>
            {series.map((item) => (
              <th key={item.key} scope="col">
                {item.label}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {points.map((point, index) => (
            <tr key={index}>
              <th scope="row">{point.label}</th>
              {series.map((item) => {
                const value = valueText(item, index);
                return <td key={item.key}>{value.missing ? "ناموجود" : value.text}</td>;
              })}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function SeriesSwatch({ color, line }: { color: string; line: boolean }) {
  return line ? (
    <span aria-hidden="true" className="relative inline-flex h-2.5 w-4 shrink-0 items-center">
      <span className="h-0.5 w-full rounded-full" style={{ backgroundColor: color }} />
      <span
        className="absolute left-1/2 size-2 -translate-x-1/2 rounded-full"
        style={{ backgroundColor: color }}
      />
    </span>
  ) : (
    <span
      aria-hidden="true"
      className="size-2.5 shrink-0 rounded-sm"
      style={{ backgroundColor: color }}
    />
  );
}

function PeriodTick({
  x,
  y,
  lines,
  upright,
  selected,
}: {
  x: number;
  y: number;
  lines: [string, string];
  upright: boolean;
  selected: boolean;
}) {
  const className = selected ? "fill-foreground font-semibold" : "fill-muted-foreground";
  if (!upright) {
    return (
      <text
        x={x}
        y={y + 6}
        transform={`rotate(-60, ${x}, ${y + 6})`}
        textAnchor="end"
        fontSize={AXIS_FONT_SIZE}
        className={className}
      >
        {lines.filter(Boolean).join(" ")}
      </text>
    );
  }
  return (
    <text x={x} y={y + 4} textAnchor="middle" fontSize={AXIS_FONT_SIZE} className={className}>
      <tspan x={x} dy="0.9em">
        {lines[0]}
      </tspan>
      <tspan x={x} dy="1.3em">
        {lines[1]}
      </tspan>
    </text>
  );
}
