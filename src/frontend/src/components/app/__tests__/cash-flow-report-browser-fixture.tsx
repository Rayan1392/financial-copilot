import { createRoot } from "react-dom/client";
import fixtures from "./fixtures/cash-flow-161.json";
import { AnswerDensityContext } from "../answer-density";
import { CashFlowReport } from "../cash-flow-report/cash-flow-report";
import type { CashFlowReportResult } from "../cash-flow-report/cash-flow-report-types";
import "../../../styles.css";

const params = new URLSearchParams(location.search);
const fixtureName = params.get("fixture") ?? "quarterly";
const density = params.get("density") === "compact" ? "compact" : "default";
const source = fixtures.quarterly as unknown as CashFlowReportResult;
const visualPeriods = [520_000_000, 480_000_000, 610_000_000, 530_000_000, 460_000_000];
const investingPeriods = [-180_000_000, -220_000_000, -310_000_000, -450_000_000, -380_000_000];
const financingPeriods = [-90_000_000, -140_000_000, -180_000_000, -140_000_000, -160_000_000];
const netPeriods = [250_000_000, 120_000_000, 120_000_000, -60_000_000, -80_000_000];
const closingPeriods = [3_250_000_000, 3_375_000_000, 3_490_000_000, 3_440_000_000, 3_370_000_000];

function makeRow(rowId: string, label: string, kind: string, values: Array<number | null>) {
  return {
    rowId,
    label,
    kind,
    sourceItemIds: [],
    cells: Object.fromEntries(
      source.columns.map((column, index) => [
        column.columnId,
        { value: values[index], state: values[index] == null ? "unavailable" : "available" },
      ]),
    ),
  };
}

const visualResult: CashFlowReportResult = {
  ...source,
  company: {
    symbol: "نمونه",
    name: "داده‌های ساختگی برای بازبینی رابط",
    providerName: "VisualFixture",
  },
  columns: source.columns.map((column) => ({ ...column, state: "available" })),
  sections: [
    {
      sectionId: "operating",
      label: "فعالیت‌های عملیاتی",
      rows: [makeRow("item_128", "جریان نقد عملیاتی", "primary-total", visualPeriods)],
    },
    {
      sectionId: "investing",
      label: "فعالیت‌های سرمایه‌گذاری",
      rows: [makeRow("item_121", "جریان نقد سرمایه‌گذاری", "subtotal", investingPeriods)],
    },
    {
      sectionId: "financing",
      label: "فعالیت‌های تأمین مالی",
      rows: [makeRow("item_123", "جریان نقد تأمین مالی", "subtotal", financingPeriods)],
    },
    {
      sectionId: "reconciliation",
      label: "تطبیق وجه نقد",
      rows: [
        makeRow("item_124", "خالص افزایش (کاهش) در وجه نقد", "reconciliation", netPeriods),
        makeRow(
          "item_33",
          "مانده وجه نقد در آغاز دوره",
          "balance",
          [3_000_000_000, 3_250_000_000, 3_375_000_000, 3_490_000_000, 3_440_000_000],
        ),
        makeRow(
          "item_35",
          "اثر تغییرات نرخ ارز",
          "detail",
          [0, 5_000_000, -5_000_000, 10_000_000, 10_000_000],
        ),
        makeRow("item_125", "مانده وجه نقد در پایان دوره", "reconciliation", closingPeriods),
      ],
    },
  ],
  kpis: [
    ["operating_cash_flow", "جریان نقد عملیاتی", visualPeriods[4], "million_irr"],
    ["investing_cash_flow", "جریان نقد سرمایه‌گذاری", investingPeriods[4], "million_irr"],
    ["financing_cash_flow", "جریان نقد تأمین مالی", financingPeriods[4], "million_irr"],
    ["net_change", "تغییر خالص وجه نقد", netPeriods[4], "million_irr"],
    ["closing_cash", "مانده پایان دوره", closingPeriods[4], "million_irr"],
    ["free_cash_flow", "جریان نقد آزاد", 130_000_000, "million_irr"],
    ["capex_coverage", "پوشش مخارج سرمایه‌ای", 1.36, "multiple"],
    ["ocf_to_revenue", "جریان نقد عملیاتی به فروش", 21.4, "percent"],
  ].map(([metricId, label, value, unit]) => ({
    metricId: String(metricId),
    label: String(label),
    value: Number(value),
    unit: String(unit),
    columnId: "q5",
    state: "available",
    formula: "visual fixture",
  })),
  changes: {
    baseColumnId: "q4",
    compareColumnId: "q5",
    rows: {
      item_128: { amount: -70_000_000, percent: -13.21, state: "available" },
      item_121: {
        amount: 70_000_000,
        percent: null,
        state: "available",
        reason: "non_positive_baseline",
      },
      item_123: {
        amount: -20_000_000,
        percent: null,
        state: "available",
        reason: "non_positive_baseline",
      },
      item_124: {
        amount: -20_000_000,
        percent: null,
        state: "available",
        reason: "non_positive_baseline",
      },
      item_33: { amount: -50_000_000, percent: -1.43, state: "available" },
      item_35: { amount: 0, percent: 0, state: "available" },
      item_125: { amount: -70_000_000, percent: -2.03, state: "available" },
    },
  },
  verification: { status: "verified_with_warnings", checks: [] },
  warnings: [
    {
      code: "comparison",
      severity: "warning",
      columnId: "q4",
      message: "درصد مبناهای منفی محاسبه نشده است.",
    },
    {
      code: "comparison",
      severity: "warning",
      columnId: "q5",
      message: "درصد مبناهای منفی محاسبه نشده است.",
    },
  ],
  notices: ["داده‌های این صفحه ساختگی است و صرفاً برای بررسی چیدمان استفاده می‌شود."],
};

const fixture =
  fixtureName === "visual" ? visualResult : fixtures[fixtureName as keyof typeof fixtures];
const result = fixture as unknown as CashFlowReportResult;

createRoot(document.getElementById("root")!).render(
  <AnswerDensityContext.Provider value={density}>
    <main className={density === "compact" ? "px-2 py-3" : "mx-auto max-w-[1600px] p-4"}>
      <CashFlowReport result={result} />
    </main>
  </AnswerDensityContext.Provider>,
);
