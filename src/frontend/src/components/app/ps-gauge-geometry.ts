import type { PsVisualizationResult } from "@/lib/chat.functions";

export function getPsGaugeAxis(data: Pick<PsVisualizationResult, "gaugeBands">) {
  const axisMin = data.gaugeBands[0]?.lowerBoundary;
  const axisMax = data.gaugeBands[data.gaugeBands.length - 1]?.upperBoundary;
  if (axisMin === undefined || axisMax === undefined || axisMax <= axisMin) return undefined;
  return { min: axisMin, max: axisMax };
}

/** Maps a P/S value to the shared 0..180 degree semicircle coordinate system. */
export function mapPsGaugeValueToAngle(
  value: number | undefined,
  data: Pick<
    PsVisualizationResult,
    "gaugeBands" | "providerBoundaryStart" | "providerBoundaryEnd" | "gaugeAxisMin" | "gaugeAxisMax"
  >,
) {
  if (value === undefined || data.gaugeBands.length !== 6) return undefined;
  const first = data.gaugeBands[0];
  const last = data.gaugeBands[data.gaugeBands.length - 1];
  if (first === undefined || last === undefined) return undefined;

  const start = data.providerBoundaryStart ?? first.lowerBoundary;
  const min = data.gaugeAxisMin ?? first.upperBoundary;
  const max = data.gaugeAxisMax ?? last.lowerBoundary;
  const end = data.providerBoundaryEnd ?? last.upperBoundary;
  if (!(start < min && min < max && max < end)) return undefined;

  const middleStep = (max - min) / 4;
  const lowerBoundaries = [start, min, min + middleStep, min + middleStep * 2, min + middleStep * 3, max];
  const upperBoundaries = [min, min + middleStep, min + middleStep * 2, min + middleStep * 3, max, end];
  if (value <= start) return first.startAngleDegrees;
  if (value >= end) return last.endAngleDegrees;

  const bandIndex = upperBoundaries.findIndex((upperBoundary) => value <= upperBoundary);
  if (bandIndex < 0) return undefined;
  const band = data.gaugeBands[bandIndex];
  const lowerBoundary = lowerBoundaries[bandIndex];
  const upperBoundary = upperBoundaries[bandIndex];
  if (band === undefined || upperBoundary <= lowerBoundary) return undefined;
  const fraction = (value - lowerBoundary) / (upperBoundary - lowerBoundary);
  return band.startAngleDegrees + fraction * (band.endAngleDegrees - band.startAngleDegrees);
}

export function getPsGaugeMarkerAngles(
  data: Pick<
    PsVisualizationResult,
    | "gaugeBands"
    | "ttmPs"
    | "forwardPs"
    | "needle"
    | "providerBoundaryStart"
    | "providerBoundaryEnd"
    | "gaugeAxisMin"
    | "gaugeAxisMax"
  >,
) {
  return {
    ttm: mapPsGaugeValueToAngle(data.ttmPs.value ?? data.needle?.sourceValue, data),
    forward: mapPsGaugeValueToAngle(data.forwardPs.value, data),
  };
}
