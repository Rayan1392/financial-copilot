import type { PsVisualizationResult } from "@/lib/chat.functions";
import { toPersianDigits } from "@/lib/format/persian";
import { getPsGaugeMarkerAngles } from "./ps-gauge-geometry";

const COLORS = ["#00b900", "#45ed4d", "#9cf59f", "#ffaaaa", "#ff6268", "#f00000"];

function point(angle: number, radius: number) {
  const radians = ((180 - angle) * Math.PI) / 180;
  return [150 + radius * Math.cos(radians), 142 - radius * Math.sin(radians)] as const;
}

function path(start: number, end: number) {
  const [x1, y1] = point(start, 124),
    [x2, y2] = point(end, 124);
  const [ix2, iy2] = point(end, 67),
    [ix1, iy1] = point(start, 67);
  return `M${x1} ${y1}A124 124 0 0 0 ${x2} ${y2}L${ix2} ${iy2}A67 67 0 0 1 ${ix1} ${iy1}Z`;
}

export function PsGauge({
  data,
  compact = false,
}: {
  data: PsVisualizationResult;
  compact?: boolean;
}) {
  if (data.gaugeBands.length === 0) return null;
  const { forward: forwardAngle, ttm: ttmAngle } = getPsGaugeMarkerAngles(data);
  const forwardOuter = forwardAngle === undefined ? undefined : point(forwardAngle, 121);
  const forwardInner = forwardAngle === undefined ? undefined : point(forwardAngle, 96);
  const ttm = data.ttmPs.value;
  const ttmNeedle = ttmAngle === undefined ? undefined : point(ttmAngle, 112);
  return (
    <section
      className={
        compact
          ? "w-full shrink-0 rounded-lg border border-hairline bg-surface/90 p-1 text-foreground"
          : "w-full max-w-[360px] rounded-xl border border-hairline bg-surface/30 p-3 text-foreground"
      }
      dir="rtl"
      aria-label="P/S gauge"
    >
      <svg
        viewBox="0 0 300 205"
        className="w-full"
        role="img"
        aria-label={`P/S ${data.companySymbol}`}
      >
        {data.gaugeBands.map((band, index) => {
          const label = point(band.endAngleDegrees, 137);
          const percentage = point((band.startAngleDegrees + band.endAngleDegrees) / 2, 91);
          return (
            <g key={band.order}>
              <path
                d={path(band.startAngleDegrees, band.endAngleDegrees)}
                fill={COLORS[index] ?? COLORS[0]}
              />
              <text
                x={label[0]}
                y={label[1]}
                textAnchor="middle"
                fontSize="11"
                fontWeight="700"
                fill="currentColor"
                stroke="var(--background)"
                strokeWidth="2"
                paintOrder="stroke"
              >
                {toPersianDigits(band.upperBoundary.toFixed(2))}
              </text>
              <text
                x={percentage[0]}
                y={percentage[1]}
                textAnchor="middle"
                fontSize="10"
                fontWeight="700"
                fill="currentColor"
                stroke="var(--background)"
                strokeWidth="2"
                paintOrder="stroke"
              >
                {toPersianDigits(`${band.displayPercentage.toFixed(1)}%`)}
              </text>
            </g>
          );
        })}
        {ttmNeedle && (
          <line
            x1="150"
            y1="142"
            x2={ttmNeedle[0]}
            y2={ttmNeedle[1]}
            stroke="black"
            strokeWidth="2.5"
          />
        )}
        {forwardOuter && forwardInner && (
          <line
            x1={forwardInner[0]}
            y1={forwardInner[1]}
            x2={forwardOuter[0]}
            y2={forwardOuter[1]}
            stroke="#2563eb"
            strokeWidth="3"
            strokeDasharray="6 4"
          />
        )}
        <circle cx="150" cy="142" r="5" fill="black" />
      </svg>
      {!compact && <div className="mx-auto w-fit rounded border border-hairline px-3 py-1 text-xs">
        آخرین: {ttm === undefined ? "—" : toPersianDigits(ttm.toFixed(2))}
      </div>}
      {!compact && (
        <div className="mt-3 rounded border border-hairline px-3 py-2 text-center text-sm">
          {data.companyName ?? data.companySymbol}
        </div>
      )}
      {!compact && <div className="mt-3 flex justify-center gap-8 text-xs text-muted-foreground" dir="ltr">
        <span>
          PS Forward ({data.forwardPs.value === undefined ? "—" : data.forwardPs.value.toFixed(2)}){" "}
          <b className="text-blue-600">---</b>
        </span>
        <span>
          TTM ({ttm === undefined ? "—" : ttm.toFixed(2)}) <b>━━</b>
        </span>
      </div>}
    </section>
  );
}
