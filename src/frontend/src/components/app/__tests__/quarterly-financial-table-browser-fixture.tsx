import { createRoot } from "react-dom/client";
import { parseQuarterlyFinancialTable, QuarterlyFinancialTable, quarterlyFinancialTableModelFromTypedResult } from "../quarterly-financial-table";
import "../../../styles.css";

const quarters = [
  "\u0641\u0635\u0644 \u0633\u0648\u0645<br>\u0645\u0646\u062a\u0647\u06cc \u0628\u0647<br>\u06f3\u06f1 \u0634\u0647\u0631\u06cc\u0648\u0631 \u06f1\u06f4\u06f0\u06f5",
  "\u0641\u0635\u0644 \u062f\u0648\u0645<br>\u0645\u0646\u062a\u0647\u06cc \u0628\u0647<br>\u06f3\u06f1 \u062e\u0631\u062f\u0627\u062f \u06f1\u06f4\u06f0\u06f5",
  "\u0641\u0635\u0644 \u0627\u0648\u0644<br>\u0645\u0646\u062a\u0647\u06cc \u0628\u0647<br>\u06f2\u06f9 \u0627\u0633\u0641\u0646\u062f \u06f1\u06f4\u06f0\u06f4",
  "\u0641\u0635\u0644 \u0633\u0648\u0645<br>\u0645\u0646\u062a\u0647\u06cc \u0628\u0647<br>\u06f3\u06f0 \u0622\u0630\u0631 \u06f1\u06f4\u06f0\u06f4",
  "\u0641\u0635\u0644 \u062f\u0648\u0645<br>\u0645\u0646\u062a\u0647\u06cc \u0628\u0647<br>\u06f3\u06f1 \u0634\u0647\u0631\u06cc\u0648\u0631 \u06f1\u06f4\u06f0\u06f4",
];

const rows: Array<[string, string[]]> = [
  ["فروش", ["۱٬۰۰۰", "۱٬۲۰۰", "۱٬۴۰۰", "۱٬۶۰۰", "۱٬۸۰۰"]],
  ["بهای تمام‌شده کالای فروش‌رفته", ["(۶۰۰)", "(۷۰۰)", "(۸۰۰)", "(۹۰۰)", "(۱٬۰۰۰)"]],
  ["سود (زیان) ناخالص", ["۴۰۰", "۵۰۰", "۶۰۰", "۷۰۰", "۸۰۰"]],
  ["مارجین ناخالص", ["۴۰", "۴۱٫۷", "۴۲٫۹", "۴۳٫۸", "۴۴٫۴"]],
  ["هزینه‌های عمومی، اداری و تشکیلاتی", ["(۱۰۰)", "(۱۲۰)", "(۱۴۰)", "(۱۶۰)", "(۱۸۰)"]],
  ["خالص سایر درآمدها (هزینه‌ها)ی عملیاتی", ["۲۰", "۲۵", "۳۰", "۳۵", "۴۰"]],
  ["سود (زیان) عملیاتی", ["۳۲۰", "۴۰۵", "۴۹۰", "۵۷۵", "۶۶۰"]],
  ["هزینه‌های مالی", ["(۳۰)", "(۳۵)", "(۴۰)", "(۴۵)", "(۵۰)"]],
  ["خالص سایر درآمدها و هزینه‌های غیرعملیاتی", ["۱۰", "۱۲", "۱۴", "۱۶", "۱۸"]],
  ["سود (زیان) عملیات در حال تداوم قبل از مالیات", ["۳۰۰", "۳۸۲", "۴۶۴", "۵۴۶", "۶۲۸"]],
  ["مالیات", ["(۶۰)", "(۷۵)", "(۹۰)", "(۱۰۵)", "(۱۲۰)"]],
  ["سود (زیان) عملیات متوقف‌شده پس از اثر مالیاتی", ["۰", "۰", "۰", "۰", "۰"]],
  ["سود (زیان) خالص", ["۲۴۰", "۳۰۷", "۳۷۴", "۴۴۱", "۵۰۸"]],
  ["سود هر سهم پس از کسر مالیات", ["۱۲۰", "۱۵۳", "۱۸۷", "۲۲۰", "۲۵۴"]],
  ["تعداد سهام شرکت (هزار سهم)", ["۲٬۰۰۰", "۲٬۰۰۰", "۲٬۰۰۰", "۲٬۰۰۰", "۲٬۰۰۰"]],
];

const markdown = [
  "### جدول گزارش فصلی (شاراک)",
  "واحد مبالغ: میلیون ریال؛ EPS: ریال به‌ازای هر سهم",
  `| شرح | ${quarters.join(" | ")} | درصد تغییر به فصل قبل |`,
  "|---|---:|---:|---:|---:|---:|---:|",
  ...rows.map(([label, values]) => `| ${label} | ${values.toReversed().join(" | ")} | - |`),
].join("\n");

const parsed = parseQuarterlyFinancialTable(markdown);
if (!parsed) throw new Error("Quarterly browser fixture did not parse.");

const banking = new URLSearchParams(location.search).has("banking");
const bankPeriods = [
  { fiscalYear: 1405, quarter: 2, start: "2026-06-22", end: "2026-09-22", label: "1405 Q2" },
  { fiscalYear: 1405, quarter: 1, start: "2026-03-21", end: "2026-06-21", label: "1405 Q1" },
  { fiscalYear: 1404, quarter: 4, start: "2025-12-22", end: "2026-03-20", label: "1404 Q4" },
  { fiscalYear: 1404, quarter: 3, start: "2025-09-23", end: "2025-12-21", label: "1404 Q3" },
  { fiscalYear: 1404, quarter: 2, start: "2025-06-22", end: "2025-09-22", label: "1404 Q2" },
];
const bankItems: Array<[string, number | null, string, number[]]> = [
  ["source_304", 304, "درآمد تسهیلات اعطایی و سپرده‌گذاری", [1000, 1100, 1200, 1300, 1400]],
  ["source_305", 305, "هزینه سود سپرده‌ها", [-400, -420, -430, -450, -470]],
  ["source_306", 306, "خالص درآمد تسهیلات و سپرده‌گذاری", [600, 680, 770, 850, 930]],
  ["banking_gross_margin", null, "مارجین ناخالص عملیات بانکی", [60, 61.8, 64.2, 65.4, 66.4]],
  ["source_26", 26, "درآمد کارمزد", [100, 110, 120, 130, 140]],
  ["source_7", 7, "هزینه کارمزد", [-20, -22, -24, -26, -28]],
  ["source_307", 307, "خالص درآمد کارمزد", [80, 88, 96, 104, 112]],
  ["banking_fee_margin", null, "حاشیه سود کارمزد", [80, 80, 80, 80, 80]],
  ["source_61", 61, "خالص سود (زیان) سرمایه‌گذاری‌ها", [10, 12, 14, 16, 18]],
  ["source_27", 27, "نتیجه معاملات ارزی", [1, -2, 3, -4, 5]],
  ["source_194", 194, "سایر درآمدها و هزینه‌های عملیاتی", [20, 30, 40, 50, 60]],
  ["banking_operating_income", null, "خالص درآمدهای عملیاتی", [711, 808, 923, 1016, 1125]],
  ["source_40", 40, "سایر درآمدها و هزینه‌های غیرعملیاتی", [8, 9, 10, 11, 12]],
  ["source_5", 5, "هزینه‌های عمومی و اداری", [-50, -51, -52, -53, -54]],
  ["source_6", 6, "هزینه مطالبات مشکوک‌الوصول", [-5, -6, -7, -8, -9]],
  ["source_12", 12, "هزینه‌های مالی", [-10, -11, -12, -13, -14]],
  ["source_142", 142, "سود (زیان) قبل از کسر مالیات", [634, 699, 806, 890, 988]],
  ["source_13", 13, "مالیات", [-100, -110, -120, -130, -140]],
  ["source_278", 278, "سود (زیان) خالص عملیات متوقف‌شده", [0, 0, 0, 0, 0]],
  ["source_143", 143, "سود (زیان) خالص", [534, 589, 686, 760, 848]],
  ["source_160", 160, "سود هر سهم بر اساس آخرین سرمایه", [100, 110, 120, 130, 140]],
  ["source_153", 153, "تعداد سهام شرکت (هزار سهم)", [3000, 3000, 3000, 3000, 3000]],
];
const bankResult = {
  templateId: "BankingIncomeStatement", companySymbol: "وبملت", companyName: "بانک ملت", status: "Available",
  periodsNewestFirst: bankPeriods,
  selectedReports: bankPeriods.map((period) => ({ periodEnd: period.end })),
  rows: bankItems.map(([rowId, sourceItemId, labelFa, values], index) => ({
    rowId, sourceItemId, order: index + 1, labelFa,
    policy: rowId.includes("margin") ? "DerivedBankingMargin" : rowId === "banking_operating_income" ? "DerivedBankingOperatingIncome" : "CumulativeFlow",
    unit: rowId.includes("margin") ? "percent" : "million_rial",
    cells: Object.fromEntries(bankPeriods.map((period, i) => [period.label, {
      value: values[4 - i], state: "Available", unit: rowId.includes("margin") ? "percent" : "million_rial",
      renderZeroAsDash: values[4 - i] === 0, sourceReportIds: [], sourceLineItemIds: [], operandValues: [],
    }])),
  })),
  comparisons: [],
};
const modelToRender = banking ? quarterlyFinancialTableModelFromTypedResult(bankResult) : parsed.model;
createRoot(document.getElementById("root")!).render(<QuarterlyFinancialTable model={modelToRender} />);
