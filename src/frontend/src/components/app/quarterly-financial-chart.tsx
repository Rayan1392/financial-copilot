import {
  Bar, CartesianGrid, ComposedChart, Line, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from "recharts";
import type { QuarterlyFinancialTableModel } from "./quarterly-financial-table";
import { QUARTERLY_CHART_SERIES, quarterlyChartPoints } from "./quarterly-financial-chart-data";

const number = new Intl.NumberFormat("fa-IR-u-nu-arabext", { maximumFractionDigits: 2 });

export function QuarterlyFinancialChart({ model }: { model: QuarterlyFinancialTableModel }) {
  const points = quarterlyChartPoints(model);
  if (!points.length) return null;

  return (
    <div className="border-b border-amber-200 bg-white px-2 py-4 sm:px-4" dir="rtl">
      <h4 className="mb-1 text-sm font-bold text-slate-900">روند فروش و سود فصلی</h4>
      <p className="mb-3 text-[11px] text-slate-600">مبالغ: میلیون ریال · مارجین ناخالص: درصد</p>
      <div className="mb-3 flex flex-wrap gap-x-4 gap-y-1 text-[11px] text-slate-700" aria-hidden="true">
        {QUARTERLY_CHART_SERIES.map((series) => (
          <span key={series.key} className="inline-flex items-center gap-1.5">
            <span className={`inline-block size-2.5 ${series.key === "grossMargin" ? "rounded-full" : "rounded-sm"}`} style={{ backgroundColor: series.color }} />
            {series.label}
          </span>
        ))}
      </div>
      <div className="w-0 min-w-full max-w-full overflow-x-auto" dir="rtl">
        <div style={{ width: `max(100%, ${Math.max(640, points.length * 120)}px)` }} className="h-64" role="img" aria-label="نمودار فروش، سود ناخالص، سود خالص و مارجین ناخالص بر اساس فصل">
          <ResponsiveContainer width="100%" height="100%">
            <ComposedChart data={[...points].reverse()} margin={{ top: 12, right: 12, left: 12, bottom: 0 }} barGap={2}>
              <CartesianGrid stroke="#e2e8f0" strokeDasharray="3 3" />
              <XAxis dataKey="shortLabel" interval={0} height={32} tick={{ fontSize: 10 }} />
              <YAxis yAxisId="amount" orientation="right" tick={{ fontSize: 10 }} tickFormatter={(value: number) => number.format(value)} width={62} />
              <YAxis yAxisId="percent" orientation="left" tick={{ fontSize: 10 }} tickFormatter={(value: number) => `${number.format(value)}٪`} width={48} />
              <Tooltip labelFormatter={(_, payload) => payload?.[0]?.payload?.label ?? ""} formatter={(value: number, name: string) => [`${number.format(value)} ${name === "مارجین ناخالص" ? "٪" : "میلیون ریال"}`, name]} />
              <Bar yAxisId="amount" dataKey="sales" name="فروش" fill="#3b82f6" maxBarSize={25} />
              <Bar yAxisId="amount" dataKey="grossProfit" name="سود ناخالص" fill="#06b6d4" maxBarSize={25} />
              <Bar yAxisId="amount" dataKey="netProfit" name="سود خالص" fill="#10b981" maxBarSize={25} />
              <Line type="monotone" yAxisId="percent" dataKey="grossMargin" name="مارجین ناخالص" stroke="#f59e0b" strokeWidth={3} dot={{ r: 4 }} connectNulls={false} />
            </ComposedChart>
          </ResponsiveContainer>
        </div>
      </div>
      <div className="sr-only">
      <table>
        <caption>مقادیر نمودار گزارش مالی فصلی، مبالغ به میلیون ریال و مارجین ناخالص به درصد</caption>
        <thead><tr><th>فصل</th>{QUARTERLY_CHART_SERIES.map((series) => <th key={series.key}>{series.label}</th>)}</tr></thead>
        <tbody>{points.map((point, index) => <tr key={index}>
          <th>{point.label}</th>
          {QUARTERLY_CHART_SERIES.map((series) => <td key={series.key}>{point[series.key] == null ? "ناموجود" : number.format(point[series.key]!)}</td>)}
        </tr>)}</tbody>
      </table>
      </div>
    </div>
  );
}
