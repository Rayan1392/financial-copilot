import {
  Bar,
  CartesianGrid,
  ComposedChart,
  Line,
  ReferenceDot,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import type { BalanceSheetReportResult } from "./balance-sheet-report-types";
import {
  balanceSheetChartData,
  formatBalanceAmount,
  formatRatio,
  type BalanceSheetChartDatum,
} from "./balance-sheet-report-model";

/** Neutral tokens with ≥ 3:1 non-text contrast on the chart's white surface in both themes. */
export const BALANCE_SHEET_CHART_SERIES = [
  { key: "liabilities", label: "جمع کل بدهی‌ها", color: "#2563eb", type: "bar" },
  { key: "equity", label: "جمع حقوق صاحبان سهام", color: "#0d9488", type: "bar" },
  { key: "ratio", label: "نسبت حقوق صاحبان سهام به کل دارایی‌ها", color: "#a16207", type: "line" },
] as const;

export const BALANCE_SHEET_CHART_TITLE = "روند ساختار تأمین مالی دارایی‌ها";

function TooltipContent({
  active,
  payload,
}: {
  active?: boolean;
  payload?: Array<{ payload: BalanceSheetChartDatum }>;
}) {
  if (!active || !payload?.length) return null;
  const point = payload[0].payload;
  return (
    <div
      className="rounded-lg border border-slate-200 bg-white p-2 text-[11px] text-slate-800 shadow"
      dir="rtl"
    >
      <p className="font-bold">{point.label}</p>
      {point.gapReason ? (
        <p>نسخه‌های هم‌رتبه متناقض‌اند؛ این تاریخ نمایش داده نمی‌شود.</p>
      ) : (
        <>
          <p>جمع کل دارایی‌ها (گزارش‌شده): {formatBalanceAmount(point.totalAssets)}</p>
          <p>جمع کل بدهی‌ها: {formatBalanceAmount(point.liabilities)}</p>
          <p>جمع حقوق صاحبان سهام: {formatBalanceAmount(point.equity)}</p>
          <p>نسبت حقوق صاحبان سهام به کل دارایی‌ها: {formatRatio(point.ratio)}</p>
          {point.stackRepresentsAssets && (
            <p className="text-slate-600">بدهی‌ها + حقوق صاحبان سهام = جمع کل دارایی‌ها</p>
          )}
          {point.warning && (
            <p className="font-semibold text-amber-900">
              ⚠ اختلاف تطبیق: {formatBalanceAmount(point.r2Difference)}
            </p>
          )}
        </>
      )}
    </div>
  );
}

/** Sign-aware stacked funding bars with the equity-to-assets ratio on its own percent axis (spec §10). */
const compactAmount = new Intl.NumberFormat("fa-IR-u-nu-arabext", { notation: "compact", maximumFractionDigits: 1 });

/** Two-line Jalali tick (month/day over year) so five dates fit side by side at 320 px. */
function CompactDateTick({ x, y, payload }: { x?: number; y?: number; payload?: { value: string } }) {
  const [year, month, day] = (payload?.value ?? "").split("/");
  return (
    <text x={x} y={y} textAnchor="middle" fontSize={9} fill="#334155">
      <tspan x={x} dy={11}>{month && day ? `${month}/${day}` : payload?.value}</tspan>
      {year && <tspan x={x} dy={12}>{year}</tspan>}
    </text>
  );
}

/** On phones the Jalali dates are angled and the amount axis is compact so ticks never overlap (F154-R-12). */
export function BalanceSheetFundingChart({
  result,
  compact = false,
}: {
  result: BalanceSheetReportResult;
  compact?: boolean;
}) {
  const data = balanceSheetChartData(result);
  if (!data.length) return null;
  const negativeDates = data.filter((point) => point.negativeEquity).map((point) => point.label);
  const gaps = data.filter((point) => point.gapReason).map((point) => point.label);
  return (
    <div
      className="border-b border-amber-200 bg-white px-2 py-4 sm:px-4"
      dir="rtl"
      data-testid="balance-sheet-funding-chart"
    >
      <h4 className="mb-1 text-sm font-bold text-slate-900">{BALANCE_SHEET_CHART_TITLE}</h4>
      <p className="mb-3 text-[11px] text-slate-600">مبالغ: میلیون ریال · نسبت: درصد</p>
      <div
        className="mb-3 flex flex-wrap gap-x-4 gap-y-1 text-[11px] text-slate-700"
        aria-hidden="true"
      >
        {BALANCE_SHEET_CHART_SERIES.map((item) => (
          <span key={item.key} className="inline-flex items-center gap-1.5">
            <span
              className={`inline-block size-2.5 ${item.type === "line" ? "rounded-full" : "rounded-sm"}`}
              style={{ backgroundColor: item.color }}
            />
            {item.label}
          </span>
        ))}
      </div>
      <div
        className={`${compact ? "h-72" : "h-64"} w-full`}
        dir="ltr"
        role="img"
        aria-label={`${BALANCE_SHEET_CHART_TITLE}: بدهی‌ها و حقوق صاحبان سهام به صورت ستون انباشته و نسبت حقوق صاحبان سهام به صورت خط`}
      >
        <ResponsiveContainer width="100%" height="100%">
          <ComposedChart
            data={data}
            stackOffset="sign"
            margin={compact ? { top: 12, right: 4, left: 4, bottom: 0 } : { top: 12, right: 12, left: 12, bottom: 0 }}
          >
            <CartesianGrid stroke="#e2e8f0" strokeDasharray="3 3" />
            <XAxis
              dataKey="label"
              interval={0}
              height={compact ? 40 : 32}
              tick={compact ? <CompactDateTick /> : { fontSize: 10 }}
            />
            <YAxis
              yAxisId="amount"
              orientation="right"
              tick={{ fontSize: compact ? 9 : 10 }}
              width={compact ? 44 : 72}
              tickFormatter={(value: number) =>
                compact ? compactAmount.format(value) : formatBalanceAmount(Math.round(value))
              }
            />
            <YAxis
              yAxisId="percent"
              orientation="left"
              tick={{ fontSize: compact ? 9 : 10 }}
              width={compact ? 40 : 52}
              tickFormatter={(value: number) => formatRatio(value)}
            />
            <Tooltip content={<TooltipContent />} />
            <Bar
              yAxisId="amount"
              stackId="funding"
              dataKey="liabilities"
              name={BALANCE_SHEET_CHART_SERIES[0].label}
              fill={BALANCE_SHEET_CHART_SERIES[0].color}
              maxBarSize={36}
              isAnimationActive={false}
            />
            <Bar
              yAxisId="amount"
              stackId="funding"
              dataKey="equity"
              name={BALANCE_SHEET_CHART_SERIES[1].label}
              fill={BALANCE_SHEET_CHART_SERIES[1].color}
              maxBarSize={36}
              isAnimationActive={false}
            />
            <Line
              yAxisId="percent"
              type="monotone"
              dataKey="ratio"
              name={BALANCE_SHEET_CHART_SERIES[2].label}
              stroke={BALANCE_SHEET_CHART_SERIES[2].color}
              strokeWidth={3}
              dot={{ r: 4 }}
              connectNulls={false}
              isAnimationActive={false}
              label={
                data.length === 1
                  ? {
                      position: "top",
                      formatter: (value: number) => formatRatio(value),
                      fontSize: 11,
                    }
                  : undefined
              }
            />
            {data
              .filter((point) => point.warning && point.totalAssets !== null)
              .map((point) => (
                <ReferenceDot
                  key={point.label}
                  yAxisId="amount"
                  x={point.label}
                  y={point.totalAssets!}
                  r={6}
                  fill="#f59e0b"
                  stroke="#78350f"
                />
              ))}
          </ComposedChart>
        </ResponsiveContainer>
      </div>
      {negativeDates.length > 0 && (
        <p className="mt-2 text-[11px] text-slate-700">
          در تاریخ‌های {negativeDates.join("، ")} حقوق صاحبان سهام منفی است و ارتفاع ستون برابر جمع
          کل دارایی‌ها نیست.
        </p>
      )}
      {gaps.length > 0 && (
        <p className="mt-1 text-[11px] text-slate-700">
          در تاریخ‌های {gaps.join("، ")} نسخه‌های هم‌رتبه متناقض‌اند و مقداری نمایش داده نمی‌شود.
        </p>
      )}
      <div className="sr-only">
        <table>
          <caption>{BALANCE_SHEET_CHART_TITLE}، مبالغ به میلیون ریال</caption>
          <thead>
            <tr>
              <th>تاریخ</th>
              <th>جمع کل دارایی‌ها</th>
              <th>جمع کل بدهی‌ها</th>
              <th>جمع حقوق صاحبان سهام</th>
              <th>نسبت</th>
            </tr>
          </thead>
          <tbody>
            {data.map((point) => (
              <tr key={point.label}>
                <th>{point.label}</th>
                <td>{point.gapReason ? "نسخه متناقض" : formatBalanceAmount(point.totalAssets)}</td>
                <td>{formatBalanceAmount(point.liabilities)}</td>
                <td>{formatBalanceAmount(point.equity)}</td>
                <td>{formatRatio(point.ratio)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
