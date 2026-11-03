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
