import {
  Bar,
  BarChart,
  CartesianGrid,
  Line,
  ComposedChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
  Legend,
} from "recharts";
import type { MonthlyActivityTrendResult, MonthlyActivityTrendChartPoint } from "@/lib/chat.functions";
import { toPersianDigits } from "@/lib/format/persian";

interface Props {
  data: MonthlyActivityTrendResult;
}

const COLORS = {
  currentYear: "#10b981",   // emerald
  previousYear: "#6366f1",  // indigo
  average: "#f59e0b",       // amber
};

function formatAmount(value: number | null | undefined): string {
  if (value == null) return "—";
  return toPersianDigits(
    value.toLocaleString("en", { maximumFractionDigits: 1 }),
  );
}

function buildChartData(points: MonthlyActivityTrendChartPoint[]) {
  return points.map((p) => ({
    label: p.fiscalMonthNameFa,
    currentYear: p.isCurrentYearReported ? (p.currentFiscalYearSalesAmount ?? null) : null,
    previousYear: p.isPreviousYearReported ? (p.previousFiscalYearSalesAmount ?? null) : null,
    average: p.average12MonthSalesAmount ?? null,
  }));
}

function buildTitle(data: MonthlyActivityTrendResult): string {
  const name = data.companyName ?? data.companySymbol;
  return `روند فروش ماهانه ${name}`;
}

function buildLegendLabels(points: MonthlyActivityTrendChartPoint[]) {
  const currentYear = points.find((p) => p.currentFiscalYear != null)?.currentFiscalYear;
  const previousYear = points.find((p) => p.previousFiscalYear != null)?.previousFiscalYear;
  return {
    currentYear: currentYear ? toPersianDigits(String(currentYear)) : "سال جاری",
    previousYear: previousYear ? toPersianDigits(String(previousYear)) : "سال قبل",
    average: "میانگین ۱۲ ماهه",
  };
}

interface TooltipPayloadEntry {
  name: string;
  value: number | null;
  color: string;
}

function CustomTooltip({
  active,
  payload,
  label,
  unit,
}: {
  active?: boolean;
  payload?: TooltipPayloadEntry[];
  label?: string;
  unit: string;
}) {
  if (!active || !payload?.length) return null;
  return (
    <div
      className="rounded-xl bg-surface ring-1 ring-hairline px-3 py-2 text-xs space-y-1 shadow-lg"
      dir="rtl"
    >
      <p className="font-medium text-foreground">{label}</p>
      {payload.map((entry) => (
        // eslint-disable-next-line react/forbid-dom-props
        <p key={entry.name} style={{ color: entry.color }}>
          {entry.name}:{" "}
          {entry.value != null ? `${formatAmount(entry.value)} ${unit}` : "گزارش نشده"}
        </p>
      ))}
    </div>
  );
}

export function MonthlyActivityTrendChart({ data }: Props) {
  const chartData = buildChartData(data.chartPoints);
  const labels = buildLegendLabels(data.chartPoints);
  const title = buildTitle(data);
  const hasMissingNotes = data.missingDataPoints.length > 0;

  return (
    <div className="rounded-2xl ring-1 ring-hairline bg-surface/40 p-4 space-y-3" dir="rtl">
      <div className="space-y-0.5">
        <h3 className="text-sm font-medium text-foreground">{title}</h3>
        <p className="text-[11px] text-muted-foreground">{`واحد: ${data.unitLabelFa}`}</p>
      </div>

      <div className="w-full h-72">
        <ResponsiveContainer width="100%" height="100%">
          <ComposedChart data={chartData} margin={{ top: 4, right: 8, left: 8, bottom: 4 }}>
            <CartesianGrid strokeDasharray="3 3" stroke="rgba(255,255,255,0.07)" />
            <XAxis
              dataKey="label"
              tick={{ fontSize: 10, fill: "hsl(var(--muted-foreground))", textAnchor: "end" }}
              tickLine={false}
              axisLine={false}
              angle={-45}
              interval={0}
              height={48}
            />
            <YAxis
              tick={{ fontSize: 10, fill: "hsl(var(--muted-foreground))" }}
              tickLine={false}
              axisLine={false}
              width={80}
              tickFormatter={(v: number) => toPersianDigits(v.toFixed(1))}
              label={{
                value: "میلیارد تومان",
                angle: -90,
                position: "insideLeft",
                offset: 12,
                className: "chart-axis-label",
              }}
            />
            <Tooltip
              content={
                <CustomTooltip unit={data.unitLabelFa} />
              }
            />
            <Legend
              wrapperStyle={{ fontSize: 11, paddingTop: 8 }}
              formatter={(value: string) => (
                <span style={{ color: "hsl(var(--muted-foreground))" }}>{value}</span>
              )}
            />
            <Bar
              dataKey="previousYear"
              name={labels.previousYear}
              fill={COLORS.previousYear}
              radius={[3, 3, 0, 0]}
              maxBarSize={28}
              connectNulls={false}
            />
            <Bar
              dataKey="currentYear"
              name={labels.currentYear}
              fill={COLORS.currentYear}
              radius={[3, 3, 0, 0]}
              maxBarSize={28}
              connectNulls={false}
            />
            <Line
              type="monotone"
              dataKey="average"
              name={labels.average}
              stroke={COLORS.average}
              strokeWidth={2}
              dot={false}
              connectNulls
            />
          </ComposedChart>
        </ResponsiveContainer>
      </div>

      {data.insights.length > 0 && (
        <div className="flex flex-col gap-1">
          {data.insights.map((insight, i) => (
            <p key={i} className="text-[11px] text-muted-foreground">
              {insight.textFa}
            </p>
          ))}
        </div>
      )}

      {hasMissingNotes && (
        <div className="text-[10px] text-muted-foreground/70 space-y-0.5">
          {data.missingDataPoints.map((pt, i) => (
            <p key={i}>
              ⚠ {pt.reasonFa}
            </p>
          ))}
        </div>
      )}
    </div>
  );
}
