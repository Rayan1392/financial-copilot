/**
 * Column index under a tap, from its x position in the chart box (left-to-right, like the category axis). Taps on a
 * value axis or outside the plot return null. Used by the phone charts, which select a column on tap instead of
 * showing a floating tooltip.
 */
export function columnIndexFromTap(
  x: number,
  chartWidth: number,
  plot: { left: number; right: number },
  columnCount: number,
): number | null {
  const plotWidth = chartWidth - plot.left - plot.right;
  if (columnCount <= 0 || plotWidth <= 0 || x < plot.left || x > chartWidth - plot.right)
    return null;
  return Math.min(columnCount - 1, Math.floor(((x - plot.left) / plotWidth) * columnCount));
}
