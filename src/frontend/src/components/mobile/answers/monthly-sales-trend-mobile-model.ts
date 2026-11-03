import type { MonthlyTrendChartPointViewModel } from "@/components/app/monthly-activity-trend-chart-view-model";

/** The latest month with current-year sales, so the detail panel opens on the newest reported data. */
export function defaultSelectedMonth(points: readonly MonthlyTrendChartPointViewModel[]): number {
  for (let index = points.length - 1; index >= 0; index -= 1) {
    if (points[index].currentYear !== null) return index;
  }
  for (let index = points.length - 1; index >= 0; index -= 1) {
    if (points[index].previousYear !== null) return index;
  }
  return 0;
}

/** Change against the same month of the previous year, only when both months are reported. */
export function sameMonthChangePercent(point: MonthlyTrendChartPointViewModel): number | null {
  if (point.currentYear === null || point.previousYear === null || point.previousYear === 0)
    return null;
  return (point.currentYear / point.previousYear - 1) * 100;
}

/**
 * Month index under a tap, from its x position in the chart box (left-to-right, like the axis). Taps on the value
 * axis or outside the plot return null.
 */
export function monthIndexFromTap(
  x: number,
  chartWidth: number,
  plot: { left: number; right: number },
  monthCount: number,
): number | null {
  const plotWidth = chartWidth - plot.left - plot.right;
  if (monthCount <= 0 || plotWidth <= 0 || x < plot.left || x > chartWidth - plot.right)
    return null;
  return Math.min(monthCount - 1, Math.floor(((x - plot.left) / plotWidth) * monthCount));
}
