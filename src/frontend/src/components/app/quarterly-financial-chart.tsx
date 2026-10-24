import {
  Bar, CartesianGrid, ComposedChart, Line, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from "recharts";
import type { QuarterlyFinancialTableModel } from "./quarterly-financial-table";
import { BANKING_CHART_SERIES, QUARTERLY_CHART_SERIES, quarterlyChartPointsChronologically } from "./quarterly-financial-chart-data";

const number = new Intl.NumberFormat("fa-IR-u-nu-arabext", { maximumFractionDigits: 2 });
const percent = new Intl.NumberFormat("fa-IR-u-nu-arabext", { minimumFractionDigits: 1, maximumFractionDigits: 1 });

export function QuarterlyFinancialChart({ model }: { model: QuarterlyFinancialTableModel }) {
  const points = quarterlyChartPointsChronologically(model);
  if (!points.length) return null;
  const banking = model.templateId === "BankingIncomeStatement";
  const series = banking ? BANKING_CHART_SERIES : QUARTERLY_CHART_SERIES;
  const chartSeries = banking
    ? BANKING_CHART_SERIES.map((item) => item.type === "bar"
      ? <Bar key={item.key} yAxisId="amount" dataKey={item.key} name={item.label} fill={item.color} maxBarSize={25} isAnimationActive={false} />
      : <Line key={item.key} type="monotone" yAxisId="percent" dataKey={item.key} name={item.label} stroke={item.color} strokeWidth={3} dot={{ r: 4 }} connectNulls={false} isAnimationActive={false} />)
    : QUARTERLY_CHART_SERIES.map((item) => item.key === "grossMargin"
      ? <Line key={item.key} type="monotone" yAxisId="percent" dataKey={item.key} name={item.label} stroke={item.color} strokeWidth={3} dot={{ r: 4 }} connectNulls={false} isAnimationActive={false} />
      : <Bar key={item.key} yAxisId="amount" dataKey={item.key} name={item.label} fill={item.color} maxBarSize={25} isAnimationActive={false} />);

  return (
    <div className="border-b border-amber-200 bg-white px-2 py-4 sm:px-4" dir="rtl">
      <h4 className="mb-1 text-sm font-bold text-slate-900">{banking ? "روند درآمد و سود فصلی بانک" : "روند فروش و سود فصلی"}</h4>
      <p className="mb-3 text-[11px] text-slate-600">{banking ? "مبالغ: میلیون ریال · حاشیه‌ها: درصد" : "مبالغ: میلیون ریال · مارجین ناخالص: درصد"}</p>
      <div className="mb-3 flex flex-wrap gap-x-4 gap-y-1 text-[11px] text-slate-700" aria-hidden="true">
        {series.map((item) => (
          <span key={item.key} className="inline-flex items-center gap-1.5">
            <span className={`inline-block size-2.5 ${banking ? item.key === "bankingGrossMargin" || item.key === "feeProfitMargin" ? "rounded-full" : "rounded-sm" : item.key === "grossMargin" ? "rounded-full" : "rounded-sm"}`} style={{ backgroundColor: item.color }} />
            {item.label}
          </span>
        ))}
      </div>
      <div className="w-0 min-w-full max-w-full overflow-x-auto" dir="ltr">
        <div style={{ width: `max(100%, ${Math.max(640, points.length * 120)}px)` }} className="h-64" role="img" aria-label={banking ? "نمودار درآمد تسهیلات، خالص درآمد و سود بانک با دو حاشیه درصدی" : "نمودار فروش، سود ناخالص، سود خالص و مارجین ناخالص بر اساس فصل"}>
          <ResponsiveContainer width="100%" height="100%">
            <ComposedChart data={points} margin={{ top: 12, right: 12, left: 12, bottom: 0 }} barGap={2}>
              <CartesianGrid stroke="#e2e8f0" strokeDasharray="3 3" />
              <XAxis dataKey="shortLabel" interval={0} height={32} tick={{ fontSize: 10 }} />
              <YAxis yAxisId="amount" orientation="right" tick={{ fontSize: 10 }} tickFormatter={(value: number) => number.format(value)} width={62} />
              <YAxis yAxisId="percent" orientation="left" tick={{ fontSize: 10 }} tickFormatter={(value: number) => `${(banking ? percent : number).format(value)}٪`} width={48} />
              <Tooltip labelFormatter={(_, payload) => payload?.[0]?.payload?.label ?? ""} formatter={(value: number, name: string) => {
                const unit = series.find((item) => item.label === name)?.unit ?? "میلیون ریال";
                return [`${unit === "٪" && banking ? percent.format(value) : number.format(value)} ${unit}`, name];
              }} />
              {chartSeries}
            </ComposedChart>
          </ResponsiveContainer>
        </div>
      </div>
      <div className="sr-only">
      <table>
        <caption>مقادیر نمودار گزارش مالی فصلی، مبالغ به میلیون ریال و مارجین ناخالص به درصد</caption>
        <thead><tr><th>فصل</th>{series.map((item) => <th key={item.key}>{item.label}</th>)}</tr></thead>
        <tbody>{points.map((point, index) => <tr key={index}>
          <th>{point.label}</th>
          {series.map((item) => {
            const value = point[item.key as keyof typeof point];
            return <td key={item.key}>{typeof value !== "number" ? "ناموجود" : number.format(value)}</td>;
          })}
        </tr>)}</tbody>
      </table>
      </div>
    </div>
  );
}
