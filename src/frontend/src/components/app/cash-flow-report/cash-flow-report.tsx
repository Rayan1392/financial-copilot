import { useMemo, useState } from "react";
import { Download } from "lucide-react";
import {
  Bar,
  CartesianGrid,
  ComposedChart,
  Legend,
  Line,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { useAnswerDensity } from "@/components/app/answer-density";
import { downloadCashFlowReportImage } from "./cash-flow-report-image";
import {
  cashFlowReasonText,
  cashFlowStateLabel,
  formatCashFlowAmount,
  formatCashFlowPercent,
} from "./cash-flow-report-model";
import type { CashFlowReportResult, CashFlowReportRow } from "./cash-flow-report-types";

const amount = new Intl.NumberFormat("fa-IR-u-nu-arabext", { maximumFractionDigits: 0 });
const multiple = new Intl.NumberFormat("fa-IR-u-nu-arabext", {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});
const unavailable = "—";
const HEADLINE_METRICS = [
  ["operating_cash_flow", "جریان نقد عملیاتی"],
  ["investing_cash_flow", "جریان نقد سرمایه‌گذاری"],
  ["financing_cash_flow", "جریان نقد تأمین مالی"],
  ["net_change", "تغییر خالص وجه نقد"],
  ["closing_cash", "مانده پایان دوره"],
] as const;

function uniqueMessages(messages: string[]) {
  return [...new Set(messages.map((message) => message.trim()).filter(Boolean))];
}

function distinctWarnings(result: CashFlowReportResult) {
  const groups = new Map<
    string,
    { code: string; severity: string; message: string; periods: Set<string> }
  >();
  for (const warning of result.warnings ?? []) {
    const key = JSON.stringify([warning.code, warning.severity, warning.message]);
    const group = groups.get(key) ?? {
      code: warning.code,
      severity: warning.severity,
      message: warning.message,
      periods: new Set<string>(),
    };
    const period = warning.columnId
      ? result.columns.find((column) => column.columnId === warning.columnId)?.label
      : undefined;
    if (period) group.periods.add(period);
    groups.set(key, group);
  }
  return [...groups.values()].map((group) => ({
    ...group,
    message: group.periods.size
      ? `${group.message} (${[...group.periods].join("، ")})`
      : group.message,
  }));
}

function formatKpiUnit(unit: string) {
  if (unit === "million_irr") return "";
  if (unit === "multiple") return "برابر";
  if (unit === "percent") return "٪";
  return "واحد دیگر";
}

function formatKpiValue(value: number | null | undefined, state: string, unit: string) {
  if (value == null || ["unavailable", "not_reported", "not_applicable"].includes(state))
    return unavailable;
  if (unit === "percent") return formatCashFlowPercent(value);
  if (unit === "multiple") return multiple.format(value);
  return formatCashFlowAmount(value, state);
}

function changeFor(result: CashFlowReportResult, row: CashFlowReportRow) {
  return result.changes?.rows?.[row.rowId];
}

function comparisonLabel(result: CashFlowReportResult) {
  const baseline = result.columns.find(
    (column) => column.columnId === result.changes?.baseColumnId,
  );
  return baseline ? `تغییر نسبت به ${baseline.label}` : "تغییر نسبت به دورهٔ مبنا";
}

function reportMetadataLabel(result: CashFlowReportResult) {
  const metadata = result.metadata;
  if (!metadata) return `مبالغ به میلیون ریال`;
  const facts = [metadata.viewLabel, metadata.basisLabel];
  if (metadata.jalaliPeriodEnd) facts.push(`پایان دوره ${metadata.jalaliPeriodEnd}`);
  if (metadata.fiscalYear) facts.push(`سال مالی ${amount.format(metadata.fiscalYear)}`);
  if (metadata.isAudited != null) facts.push(metadata.isAudited ? "حسابرسی‌شده" : "حسابرسی‌نشده");
  if (metadata.isRepresented) facts.push("ارائهٔ مجدد");
  if (metadata.jalaliPublishedAt) facts.push(`انتشار ${metadata.jalaliPublishedAt}`);
  if (metadata.providerName) facts.push(`منبع ${metadata.providerName}`);
  facts.push(`واحد مبالغ: ${metadata.unit}`);
  return facts.join(" · ");
}

function validationCheckLabel(checkId: string) {
  return (
    (
      {
        R1: "تطبیق جمع جریان‌های نقدی",
        R2: "تطبیق مانده وجه نقد",
        V3: "کنترل متقابل جریان عملیاتی",
        C1: "پیوستگی ماندهٔ آغاز سال مالی",
        C2: "پیوستگی ماندهٔ ابتدای فصل",
      } as Record<string, string>
    )[checkId] ?? "کنترل حسابداری"
  );
}

function coverageSectionLabel(sectionId: string) {
  return (
    (
      { operating: "عملیاتی", investing: "سرمایه‌گذاری", financing: "تأمین مالی" } as Record<
        string,
        string
      >
    )[sectionId] ?? "گزارش"
  );
}

function CashFlowValidationSummary({ result }: { result: CashFlowReportResult }) {
  const anchorColumn =
    result.columns.find((column) =>
      column.statementIds?.includes(result.anchorStatementId ?? ""),
    ) ?? result.columns.at(-1);
  const coverage = (result.verification.detailCoverage ?? []).filter(
    (item) => item.columnId === anchorColumn?.columnId,
  );
  const checks = (result.verification.checks ?? []).filter(
    (item) => item.columnId === anchorColumn?.columnId,
  );
  const overallCoverage = coverage.some((item) => item.status === "inconsistent_source_data")
    ? "inconsistent_source_data"
    : coverage.some((item) => item.status === "insufficient_detail")
      ? "insufficient_detail"
      : coverage.some((item) => item.status === "documented_difference")
        ? "documented_difference"
        : coverage.length > 0 && coverage.every((item) => item.status === "complete")
          ? "complete"
          : "unverified";
  return (
    <section
      className="border-b border-slate-200 px-4 py-2 text-xs dark:border-slate-700"
      aria-label="اعتبارسنجی حسابداری"
    >
      <div className="flex flex-wrap gap-x-6 gap-y-1">
        <p>
          جمع جریان‌ها:{" "}
          <strong>
            {cashFlowStateLabel(
              result.verification.mainTotals?.status ?? result.verification.status,
            )}
          </strong>
        </p>
        <p>
          مانده وجه نقد:{" "}
          <strong>
            {cashFlowStateLabel(result.verification.cashBalance?.status ?? "unverified")}
          </strong>
        </p>
        <p>
          پوشش ریزاقلام: <strong>{cashFlowStateLabel(overallCoverage)}</strong>
        </p>
      </div>
      <details className="mt-1">
        <summary className="min-h-8 cursor-pointer font-semibold text-[#164e63] underline underline-offset-2 dark:text-teal-200">
          جزئیات تطبیق، پوشش ریزاقلام و رد منبع
        </summary>
        <div className="space-y-2 pb-2">
          {coverage.map((item) => (
            <div
              key={`${item.columnId}:${item.sectionId}`}
              className="border-t border-slate-200 pt-2 dark:border-slate-700"
            >
              <p className="font-semibold">
                {coverageSectionLabel(item.sectionId)}: {cashFlowStateLabel(item.status)}
              </p>
              <p className="mt-1 tabular-nums">
                جمع کدال: {formatCashFlowAmount(item.reportedSubtotal)} · ریزاقلام تأییدشده:{" "}
                {formatCashFlowAmount(item.verifiedDetailTotal)} · اختلاف:{" "}
                {formatCashFlowAmount(item.difference)}
              </p>
              <p>
                تعداد ریزاقلام تأییدشده: {amount.format(item.verifiedDetailCount)} · اقلام
                تخصیص‌نیافته: {amount.format(item.unclassifiedItemCount)}
              </p>
              {item.unclassifiedItems.length > 0 && (
                <ul className="mt-1 list-inside list-disc">
                  {item.unclassifiedItems.map((source) => (
                    <li key={source.sourceItemId}>
                      {source.label || `قلم منبع ${amount.format(source.sourceItemId)}`} ·{" "}
                      {formatCashFlowAmount(source.value)}
                    </li>
                  ))}
                </ul>
              )}
            </div>
          ))}
          {checks.map((check) => (
            <p
              key={`${check.checkId}:${check.columnId}`}
              className="border-t border-slate-200 pt-2 dark:border-slate-700"
            >
              {validationCheckLabel(check.checkId)}: {cashFlowStateLabel(check.state)}
              {check.expected != null && check.actual != null && (
                <>
                  {" "}
                  · مورد انتظار {formatCashFlowAmount(check.expected)} · گزارش‌شده{" "}
                  {formatCashFlowAmount(check.actual)} · اختلاف{" "}
                  {formatCashFlowAmount(check.difference)}
                </>
              )}
            </p>
          ))}
          <p className="text-muted-foreground">
            تطبیق جمع‌های کل و ماندهٔ وجه نقد، به‌تنهایی اثبات‌کنندهٔ کامل‌بودن ریزاقلام نیست.
          </p>
        </div>
      </details>
    </section>
  );
}

function formulaLabel(formula?: string | null) {
  if (formula === "subtotal - displayed_details") return "جمع کدال منهای مجموع ریزاقلام تأییدشده";
  if (formula === "current_ytd - previous_ytd")
    return "مبلغ تجمعی دورهٔ جاری منهای مبلغ تجمعی دورهٔ قبل";
  return formula ? "فرمول محاسبه‌شده بر پایهٔ مبالغ گزارش" : "";
}

function CashFlowRowDisclosure({
  row,
  result,
}: {
  row: CashFlowReportRow;
  result: CashFlowReportResult;
}) {
  if (row.kind !== "residual") return null;
  const traceColumn =
    result.changes?.compareColumnId ??
    result.columns.find((column) => column.statementIds?.includes(result.anchorStatementId ?? ""))
      ?.columnId ??
    result.columns.at(-1)?.columnId;
  const trace = traceColumn ? row.cells[traceColumn]?.provenance : undefined;
  return (
    <details className="mt-1 text-[10px] font-normal text-slate-600 dark:text-slate-300">
      <summary className="min-h-7 cursor-pointer underline underline-offset-2">
        روش محاسبه و منبع
      </summary>
      <p>{row.note}</p>
      {trace && (
        <div className="mt-1 space-y-0.5" dir="rtl">
          <p>فرمول: {formulaLabel(trace.formula)}</p>
          <p>
            شناسهٔ اقلام منبع:{" "}
            {(trace.sourceItemIds ?? []).map((id) => amount.format(id)).join("، ") || "—"}
          </p>
          <p dir="ltr" className="break-all">
            شناسهٔ ردیف‌ها: {(trace.lineItemIds ?? []).join("، ") || "—"}
          </p>
          <p dir="ltr" className="break-all">
            شناسهٔ صورت‌ها: {(trace.statementIds ?? []).join("، ") || "—"}
          </p>
        </div>
      )}
    </details>
  );
}

export function CashFlowReport({ result }: { result: CashFlowReportResult }) {
  const compact = useAnswerDensity() === "compact";
  const [flowsOnly, setFlowsOnly] = useState(false);
  const [chartOpen, setChartOpen] = useState(!compact);
  const [openSections, setOpenSections] = useState<Record<string, boolean>>({ operating: true });
  const columns = result.columns;
  const kpis = result.kpis;
  const latestColumn =
    columns.find((column) => column.statementIds?.includes(result.anchorStatementId ?? "")) ??
    columns.at(-1);
  const headlineKpis = HEADLINE_METRICS.map(([metricId, fallbackLabel]) => {
    const metric = kpis.find(
      (item) => item.metricId === metricId && item.columnId === latestColumn?.columnId,
    );
    return { metricId, label: metric?.label ?? fallbackLabel, metric };
  }).filter(
    ({ metric }) =>
      metric?.value != null &&
      !["unavailable", "not_reported", "not_applicable"].includes(metric.state),
  );
  const headlineMetricIds: Set<string> = new Set(HEADLINE_METRICS.map(([metricId]) => metricId));
  const secondaryKpis = kpis.filter(
    (item) =>
      item.columnId === latestColumn?.columnId &&
      !headlineMetricIds.has(item.metricId) &&
      item.value != null &&
      !["unavailable", "not_reported", "not_applicable"].includes(item.state),
  );
  const unavailableMetricNotes = [
    ...new Set(
      kpis
        .filter(
          (item) =>
            item.columnId === latestColumn?.columnId &&
            item.value == null &&
            [
              "capex_mapping_unverified",
              "capex_component_missing",
              "unexpected_capex_sign",
              "mixed_sign_change",
            ].includes(item.reason ?? ""),
        )
        .map((item) => cashFlowReasonText(item.reason)),
    ),
  ];
  const warnings = distinctWarnings(result);
  const warningMessages = new Set((result.warnings ?? []).map((warning) => warning.message));
  const notices = uniqueMessages(result.notices ?? []).filter(
    (notice) => !warningMessages.has(notice),
  );
  const chartRows = useMemo(() => {
    const ids = [
      ["operating_cash_flow", "item_128"],
      ["investing_cash_flow", "item_121"],
      ["financing_cash_flow", "item_123"],
      ["net_change", "item_124"],
    ] as const;
    return columns.map((column) => {
      const point: Record<string, string | number | null> = { period: column.label };
      for (const [id, rowId] of ids) {
        const row = result.sections
          .flatMap((section) => section.rows)
          .find((item) => item.rowId === rowId);
        point[id] = row?.cells?.[column.columnId]?.value ?? null;
      }
      return point;
    });
  }, [columns, result.sections]);

  if (result.outcome !== "available") {
    return (
      <section
        className="rounded-xl border border-slate-200 bg-white p-4 text-sm text-slate-700 dark:border-slate-700 dark:bg-slate-900 dark:text-slate-200"
        dir="rtl"
        data-testid="cash-flow-report-empty"
      >
        <h3 className="font-bold text-[#164e63]">صورت جریان وجوه نقد {result.company.symbol}</h3>
        <p className="mt-2">
          {result.message || cashFlowStateLabel(result.outcomeReason || result.outcome)}
        </p>
        {uniqueMessages(result.notices ?? []).map((notice) => (
          <p key={notice} className="mt-1 text-xs text-muted-foreground">
            {notice}
          </p>
        ))}
      </section>
    );
  }

  return (
    <section
      className="overflow-hidden rounded-xl border border-slate-200 bg-white text-slate-900 shadow-sm dark:border-slate-700 dark:bg-slate-900 dark:text-slate-100"
      dir="rtl"
      data-testid="cash-flow-report"
    >
      <header className="bg-[#164e63] px-4 py-3 text-white sm:px-5">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div>
            <h3 className="text-base font-bold sm:text-lg">
              صورت جریان وجوه نقد {result.company.symbol}
            </h3>
            <p className="mt-1 text-xs text-teal-50">
              {result.company.name ?? "گزارش چنددوره‌ای"} · {reportMetadataLabel(result)}
            </p>
          </div>
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={() => void downloadCashFlowReportImage(result)}
              className="inline-flex min-h-9 items-center gap-1.5 rounded-lg border border-white/40 px-2.5 text-xs hover:bg-white/10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-white"
              aria-label="دانلود تصویر گزارش جریان وجوه نقد"
            >
              <Download aria-hidden="true" size={14} /> تصویر
            </button>
          </div>
        </div>
      </header>

      <CashFlowValidationSummary result={result} />

      {result.analysis?.facts?.length ? (
        <div className="border-b border-slate-200 bg-teal-50 px-4 py-3 text-sm dark:border-slate-700 dark:bg-teal-950/30">
          <h4 className="mb-1 font-bold">جمع‌بندی تحلیلی ساپیو</h4>
          {result.analysis.facts.map((fact) => (
            <p key={fact.factId} className="leading-6">
              {fact.text}
            </p>
          ))}
        </div>
      ) : null}

      {warnings.length > 0 && (
        <div
          className={`border-b px-4 py-2 text-xs ${result.verification.status === "failed" ? "border-rose-200 bg-rose-50 text-rose-950 dark:border-rose-900 dark:bg-rose-950/30 dark:text-rose-100" : "border-amber-200 bg-amber-50 text-amber-950 dark:border-amber-900 dark:bg-amber-950/30 dark:text-amber-100"}`}
          role={result.verification.status === "failed" ? "alert" : "status"}
        >
          <div className="flex flex-wrap items-center justify-between gap-x-3 gap-y-1">
            <p className="font-medium">
              {result.verification.status === "failed"
                ? "تطبیق جمع‌های گزارش نیازمند بررسی است."
                : `${warnings.length} هشدار یکتا برای این گزارش ثبت شده است.`}
            </p>
            {warnings.length === 1 ? (
              <p>{warnings[0].message}</p>
            ) : (
              <details className="max-w-full">
                <summary className="min-h-8 cursor-pointer font-semibold underline underline-offset-2">
                  مشاهده جزئیات
                </summary>
                <ul className="mt-1 list-inside list-disc space-y-1">
                  {warnings.map((warning) => (
                    <li key={`${warning.code}:${warning.severity}:${warning.message}`}>
                      {warning.message}
                    </li>
                  ))}
                </ul>
              </details>
            )}
          </div>
        </div>
      )}

      {headlineKpis.length > 0 && latestColumn && (
        <dl
          className="grid grid-cols-2 border-b border-slate-200 px-4 py-2 dark:border-slate-700 sm:grid-cols-3 xl:grid-cols-5"
          aria-label={`خلاصه جریان نقد ${latestColumn.label}`}
          data-testid="cash-flow-headline-summary"
        >
          {headlineKpis.map(({ metricId, label, metric }) => (
            <div
              key={metricId}
              className="min-w-0 border-s border-slate-200 py-2 ps-3 pe-2 first:border-s-0 dark:border-slate-700"
              title={metric?.reason ? cashFlowReasonText(metric.reason) : undefined}
            >
              <dt className="text-[11px] leading-5 text-slate-600 dark:text-slate-300">{label}</dt>
              <dd
                className={`tabular-nums font-bold ${metric?.value != null && metric.value < 0 ? "text-rose-800 dark:text-rose-300" : "text-slate-900 dark:text-slate-100"}`}
              >
                {formatCashFlowAmount(metric?.value, metric?.state)}
              </dd>
            </div>
          ))}
        </dl>
      )}

      <div className="border-b border-slate-200 dark:border-slate-700">
        <button
          type="button"
          aria-expanded={chartOpen}
          onClick={() => setChartOpen((value) => !value)}
          className="flex min-h-11 w-full items-center justify-between px-4 text-sm font-semibold hover:bg-slate-50 dark:hover:bg-slate-800"
        >
          روند جریان نقد <span aria-hidden="true">{chartOpen ? "−" : "+"}</span>
        </button>
        {chartOpen && chartRows.length > 0 && (
          <div
            className="h-64 px-2 pb-3"
            role="img"
            aria-label="نمودار روند جریان نقد عملیاتی، سرمایه‌گذاری، تأمین مالی و تغییر خالص وجه نقد"
          >
            <ResponsiveContainer width="100%" height="100%">
              <ComposedChart data={chartRows} margin={{ top: 10, right: 8, left: 4, bottom: 5 }}>
                <CartesianGrid stroke="#e2e8f0" strokeDasharray="3 3" />
                <XAxis dataKey="period" tick={{ fontSize: 10 }} />
                <YAxis
                  tick={{ fontSize: 10 }}
                  tickFormatter={(value: number) => amount.format(value)}
                />
                <Tooltip
                  formatter={(value) =>
                    typeof value === "number" ? amount.format(value) : unavailable
                  }
                />
                <Legend
                  verticalAlign="top"
                  height={30}
                  wrapperStyle={{ direction: "rtl", fontSize: 12 }}
                />
                <Bar dataKey="operating_cash_flow" name="عملیاتی" fill="#0f766e" />
                <Bar dataKey="investing_cash_flow" name="سرمایه‌گذاری" fill="#f59e0b" />
                <Bar dataKey="financing_cash_flow" name="تأمین مالی" fill="#0e7490" />
                <Line
                  dataKey="net_change"
                  name="تغییر خالص وجه نقد"
                  stroke="#7c3aed"
                  strokeWidth={2}
                />
              </ComposedChart>
            </ResponsiveContainer>
          </div>
        )}
      </div>

      {result.waterfall && (
        <>
          <div className="flex justify-end border-b border-slate-200 px-4 py-2 dark:border-slate-700">
            <button
              type="button"
              className="min-h-9 rounded-lg border border-slate-300 px-3 text-xs font-medium hover:bg-slate-50 dark:border-slate-600 dark:hover:bg-slate-800"
              onClick={() => setFlowsOnly((value) => !value)}
              aria-pressed={flowsOnly}
            >
              {flowsOnly ? "از مانده ابتدای دوره" : "فقط جریان‌ها"}
            </button>
          </div>
          <CashFlowWaterfall
            steps={flowsOnly ? result.waterfall.flowSteps : result.waterfall.steps}
          />
        </>
      )}

      {!compact && (
        <div
          className="hidden px-3 pb-2 lg:block"
          role="region"
          aria-label="جدول صورت جریان وجوه نقد برای پنج فصل مستقل"
          data-testid="cash-flow-wide-table"
        >
          <table className="w-full table-fixed border-collapse text-right text-[11px] text-slate-800 lg:text-xs dark:text-slate-100">
            <colgroup>
              <col style={{ width: columns.length >= 5 ? "21%" : "30%" }} />
              {columns.map((column) => (
                <col
                  key={column.columnId}
                  style={{
                    width: `${(100 - (columns.length >= 5 ? 21 : 30) - (result.changes ? 18 : 0)) / Math.max(columns.length, 1)}%`,
                  }}
                />
              ))}
              {result.changes && <col style={{ width: "18%" }} />}
            </colgroup>
            <thead>
              <tr className="border-y border-[#c7e5e2] bg-[#d9efed] text-[#164e63]">
                <th className="px-2 py-2 text-right sm:px-3">شرح</th>
                {columns.map((column) => (
                  <th
                    key={column.columnId}
                    className="px-1.5 py-2 text-center font-semibold sm:px-2"
                  >
                    <span className="block">{column.label || column.jalaliPeriodEnd}</span>
                  </th>
                ))}
                {result.changes && (
                  <th className="px-1.5 py-2 text-center font-semibold sm:px-2">
                    {comparisonLabel(result)}
                  </th>
                )}
              </tr>
            </thead>
            <tbody>
              {result.sections.map((section) => (
                <CashFlowWideSection key={section.sectionId} section={section} result={result} />
              ))}
            </tbody>
          </table>
        </div>
      )}
      <div className={compact ? "block" : "lg:hidden"} data-testid="cash-flow-mobile-sections">
        <CashFlowCompactSections
          sections={result.sections}
          result={result}
          openSections={openSections}
          onToggle={(sectionId) =>
            setOpenSections((state) => ({
              ...state,
              [sectionId]: !(state[sectionId] ?? sectionId === "operating"),
            }))
          }
        />
      </div>

      {secondaryKpis.length > 0 && (
        <details
          className="border-t border-slate-200 px-4 py-2 dark:border-slate-700"
          data-testid="cash-flow-secondary-indicators"
        >
          <summary className="min-h-9 cursor-pointer text-xs font-semibold text-[#164e63] dark:text-teal-200">
            شاخص‌های تکمیلی {latestColumn ? `· ${latestColumn.label}` : ""}
          </summary>
          <dl className="divide-y divide-slate-100 text-xs dark:divide-slate-800">
            {secondaryKpis.map((kpi) => (
              <div
                key={`${kpi.metricId}:${kpi.columnId}`}
                className="flex flex-wrap justify-between gap-x-4 gap-y-1 py-2"
              >
                <dt className="text-slate-600 dark:text-slate-300">{kpi.label}</dt>
                <dd className="tabular-nums">
                  {formatKpiValue(kpi.value, kpi.state, kpi.unit)}
                  {formatKpiUnit(kpi.unit) && ` ${formatKpiUnit(kpi.unit)}`}
                  {kpi.reason && (
                    <span className="ms-2 text-muted-foreground">
                      {cashFlowReasonText(kpi.reason)}
                    </span>
                  )}
                </dd>
              </div>
            ))}
          </dl>
        </details>
      )}

      {unavailableMetricNotes.length > 0 && (
        <div
          className="space-y-1 border-t border-slate-200 px-4 py-2 text-xs text-muted-foreground dark:border-slate-700"
          data-testid="cash-flow-metric-notes"
        >
          {unavailableMetricNotes.map((note) => (
            <p key={note}>{note}</p>
          ))}
        </div>
      )}

      {notices.length > 0 && (
        <footer className="space-y-1 border-t border-slate-200 px-4 py-3 text-[11px] text-muted-foreground dark:border-slate-700">
          {notices.map((notice) => (
            <p key={notice}>{notice}</p>
          ))}
        </footer>
      )}
    </section>
  );
}

function CashFlowCompactSections({
  sections,
  result,
  openSections,
  onToggle,
}: {
  sections: CashFlowReportResult["sections"];
  result: CashFlowReportResult;
  openSections: Record<string, boolean>;
  onToggle: (sectionId: string) => void;
}) {
  return (
    <div className="divide-y divide-slate-200 px-4 dark:divide-slate-700">
      {sections.map((section) => {
        const opened = openSections[section.sectionId] ?? section.sectionId === "operating";
        return (
          <section key={section.sectionId}>
            <button
              type="button"
              aria-expanded={opened}
              onClick={() => onToggle(section.sectionId)}
              className="flex min-h-11 w-full items-center justify-between py-2 text-right text-sm font-semibold text-[#164e63] dark:text-teal-200"
            >
              {section.label}
              <span aria-hidden="true">{opened ? "−" : "+"}</span>
            </button>
            {opened &&
              section.rows.map((row) => (
                <CashFlowCompactRow key={row.rowId} row={row} result={result} />
              ))}
          </section>
        );
      })}
    </div>
  );
}

function CashFlowWideSection({
  section,
  result,
}: {
  section: CashFlowReportResult["sections"][number];
  result: CashFlowReportResult;
}) {
  const compareLabel = comparisonLabel(result);
  return (
    <>
      <tr>
        <th
          colSpan={result.columns.length + 1 + (result.changes ? 1 : 0)}
          className="border-b border-amber-200 bg-[#d9efed] px-3 py-2 text-right font-bold text-[#164e63] dark:bg-teal-950 dark:text-teal-100"
        >
          {section.label}
        </th>
      </tr>
      {section.rows.map((row) => {
        const change = changeFor(result, row);
        const important = ["total", "primary-total", "reconciliation"].includes(row.kind);
        return (
          <tr
            key={row.rowId}
            className={`border-b border-slate-200 dark:border-slate-700 ${important ? "bg-amber-50 font-semibold text-amber-950 dark:bg-amber-950/20 dark:text-amber-100" : ""}`}
          >
            <th scope="row" className="px-2 py-2 text-right font-medium leading-5 sm:px-3">
              {row.label}
              {row.note && (
                <span className="mt-0.5 block text-[10px] font-normal text-muted-foreground">
                  {row.note}
                </span>
              )}
              <CashFlowRowDisclosure row={row} result={result} />
            </th>
            {result.columns.map((column) => {
              const cell = row.cells?.[column.columnId];
              return (
                <td
                  key={column.columnId}
                  className={`px-1.5 py-2 text-center tabular-nums sm:px-2 ${cell?.value != null && cell.value < 0 ? "text-rose-800 dark:text-rose-300" : ""}`}
                  title={cell?.reason ? cashFlowReasonText(cell.reason) : undefined}
                >
                  <span className="block whitespace-nowrap">
                    {formatCashFlowAmount(cell?.value, cell?.state)}
                  </span>
                </td>
              );
            })}
            {result.changes && (
              <td
                className="px-1.5 py-2 text-center tabular-nums sm:px-2"
                aria-label={compareLabel}
              >
                <CashFlowChangeLine change={change} />
              </td>
            )}
          </tr>
        );
      })}
    </>
  );
}

function CashFlowChangeLine({ change }: { change: ReturnType<typeof changeFor> }) {
  return (
    <span
      data-testid="cash-flow-change-line"
      className="mt-0.5 block whitespace-nowrap text-[9px] font-normal text-slate-500 dark:text-slate-400 lg:text-[10px]"
      title={change?.reason ? cashFlowReasonText(change.reason) : "مقایسه با دورهٔ مبنا"}
    >
      {formatCashFlowAmount(change?.amount)} · {formatCashFlowPercent(change?.percent)}
    </span>
  );
}

function CashFlowCompactRow({
  row,
  result,
}: {
  row: CashFlowReportRow;
  result: CashFlowReportResult;
}) {
  const columns = [...result.columns].reverse();
  const change = changeFor(result, row);
  return (
    <article className="border-t border-slate-100 py-2.5 first:border-0 dark:border-slate-800">
      <p className="text-xs font-medium leading-5">{row.label}</p>
      {row.note && <p className="mt-0.5 text-[10px] text-muted-foreground">{row.note}</p>}
      <CashFlowRowDisclosure row={row} result={result} />
      <dl className="mt-1 divide-y divide-slate-100 text-[11px] dark:divide-slate-800">
        {columns.map((column) => {
          const cell = row.cells?.[column.columnId];
          return (
            <div
              key={column.columnId}
              className="flex min-h-8 items-center justify-between gap-3 py-1"
            >
              <dt className="text-slate-500 dark:text-slate-400">{column.label}</dt>
              <dd
                className={`whitespace-nowrap text-left tabular-nums ${cell?.value != null && cell.value < 0 ? "text-rose-800 dark:text-rose-300" : ""}`}
              >
                {formatCashFlowAmount(cell?.value, cell?.state)}
              </dd>
            </div>
          );
        })}
      </dl>
      {columns.length > 0 && (
        <p
          className="mt-1 text-[10px] text-slate-500 dark:text-slate-400"
          title={change?.reason ? cashFlowReasonText(change.reason) : undefined}
        >
          {comparisonLabel(result)}: {formatCashFlowAmount(change?.amount)} ·{" "}
          {formatCashFlowPercent(change?.percent)}
        </p>
      )}
    </article>
  );
}
function CashFlowWaterfall({
  steps,
}: {
  steps: NonNullable<CashFlowReportResult["waterfall"]>["steps"];
}) {
  if (steps.length === 0) return null;
  const min = Math.min(0, ...steps.flatMap((step) => [step.from, step.to]));
  const max = Math.max(0, ...steps.flatMap((step) => [step.from, step.to]));
  const span = max - min || 1;
  return (
    <div
      className="grid gap-2 bg-slate-50 px-4 py-3 text-xs dark:bg-slate-800/50"
      role="img"
      aria-label="Ù†Ù…ÙˆØ¯Ø§Ø± ØªØ±Ø§Ú©Ù…ÛŒ Ø¬Ø±ÛŒØ§Ù† ÙˆØ¬Ù‡ Ù†Ù‚Ø¯"
    >
      {steps.map((step) => {
        const low = Math.min(step.from, step.to);
        const high = Math.max(step.from, step.to);
        const right = ((max - high) / span) * 100;
        const width = Math.max(0.7, ((high - low) / span) * 100);
        return (
          <div
            key={step.stepId}
            className="grid min-w-0 grid-cols-[minmax(0,1fr)_minmax(2rem,2fr)_max-content] items-center gap-2"
          >
            <span className="min-w-0 truncate">{step.label}</span>
            <span
              className="relative h-3 rounded bg-slate-200 dark:bg-slate-700"
              aria-hidden="true"
            >
              <span
                className={`absolute top-0 h-3 rounded ${step.value < 0 ? "bg-rose-500" : "bg-teal-600"}`}
                style={{ right: `${right}%`, width: `${width}%` }}
              />
            </span>
            <span className="whitespace-nowrap text-left tabular-nums">
              {formatCashFlowAmount(step.value)}
            </span>
          </div>
        );
      })}
    </div>
  );
}
