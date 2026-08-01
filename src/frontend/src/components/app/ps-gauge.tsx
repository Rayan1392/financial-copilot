import type { PsVisualizationResult } from "@/lib/chat.functions";
import { toPersianDigits } from "@/lib/format/persian";

const COLORS = ["#00b900", "#45ed4d", "#9cf59f", "#ffaaaa", "#ff6268", "#f00000"];

function point(angle: number, radius: number) {
  const radians = ((180 - angle) * Math.PI) / 180;
  return [150 + radius * Math.cos(radians), 142 - radius * Math.sin(radians)] as const;
}

function path(start: number, end: number) {
  const [x1, y1] = point(start, 124), [x2, y2] = point(end, 124);
  const [ix2, iy2] = point(end, 67), [ix1, iy1] = point(start, 67);
  return `M${x1} ${y1}A124 124 0 0 0 ${x2} ${y2}L${ix2} ${iy2}A67 67 0 0 1 ${ix1} ${iy1}Z`;
}

export function PsGauge({ data }: { data: PsVisualizationResult }) {
  if (data.gaugeBands.length === 0 || !data.needle) return null;
  const needle = point(data.needle.angleDegrees, 112);
  const forward = data.forwardPs.value;
  const axisMin = data.gaugeBands[0]?.lowerBoundary;
  const axisMax = data.gaugeBands[data.gaugeBands.length - 1]?.upperBoundary;
  const forwardAngle = forward === undefined || axisMin === undefined || axisMax === undefined || axisMax <= axisMin
    ? undefined
    : Math.max(0, Math.min(180, ((forward - axisMin) / (axisMax - axisMin)) * 180));
  const forwardOuter = forwardAngle === undefined ? undefined : point(forwardAngle, 121);
  const forwardInner = forwardAngle === undefined ? undefined : point(forwardAngle, 96);
  const ttm = data.ttmPs.value;
  return (
    <section className="w-full max-w-[360px] rounded-xl border border-hairline bg-surface/30 p-3" dir="rtl" aria-label="P/S gauge">
      <svg viewBox="0 0 300 205" className="w-full" role="img" aria-label={`P/S ${data.companySymbol}`}>
        {data.gaugeBands.map((band, index) => {
          const middle = (band.startAngleDegrees + band.endAngleDegrees) / 2;
          const label = point(middle, 137), percentage = point(middle, 91);
          return <g key={band.order}>
            <path d={path(band.startAngleDegrees, band.endAngleDegrees)} fill={COLORS[index] ?? COLORS[0]} />
            <text x={label[0]} y={label[1]} textAnchor="middle" fontSize="10" fill="currentColor">{toPersianDigits(band.upperBoundary.toFixed(2))}</text>
            <text x={percentage[0]} y={percentage[1]} textAnchor="middle" fontSize="9" fill="currentColor">{toPersianDigits(`${band.displayPercentage.toFixed(2)}%`)}</text>
          </g>;
        })}
        <line x1="150" y1="142" x2={needle[0]} y2={needle[1]} stroke="black" strokeWidth="2.5" />
        {forwardOuter && forwardInner && <line x1={forwardInner[0]} y1={forwardInner[1]} x2={forwardOuter[0]} y2={forwardOuter[1]} stroke="#2563eb" strokeWidth="3" strokeDasharray="6 4" />}
        <circle cx="150" cy="142" r="5" fill="black" />
      </svg>
      <div className="mx-auto w-fit rounded border border-hairline px-3 py-1 text-xs">آخرین: {ttm === undefined ? "—" : toPersianDigits(ttm.toFixed(2))}</div>
      <div className="mt-3 rounded border border-hairline px-3 py-2 text-center text-sm">{data.companyName ?? data.companySymbol}</div>
      <div className="mt-3 flex justify-center gap-8 text-xs text-muted-foreground" dir="ltr">
        <span>PS Forward ({data.forwardPs.value === undefined ? "—" : data.forwardPs.value.toFixed(2)}) <b className="text-blue-600">---</b></span>
        <span>TTM ({ttm === undefined ? "—" : ttm.toFixed(2)}) <b>━━</b></span>
      </div>
    </section>
  );
}
