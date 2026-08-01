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
  data: Pick<PsVisualizationResult, "gaugeBands">,
) {
  const axis = getPsGaugeAxis(data);
  if (value === undefined || axis === undefined) return undefined;
  return Math.max(0, Math.min(180, ((value - axis.min) / (axis.max - axis.min)) * 180));
}

export function getPsGaugeMarkerAngles(data: Pick<PsVisualizationResult, "gaugeBands" | "ttmPs" | "forwardPs">) {
  return {
    ttm: mapPsGaugeValueToAngle(data.ttmPs.value, data),
    forward: mapPsGaugeValueToAngle(data.forwardPs.value, data),
  };
}
