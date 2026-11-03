// Representative analytical answers for the Feature 153 phone checks. They follow the API contracts
// (MonthlyActivityTrendResult, QuarterlyFinancialReportTableResult) and include the awkward cases:
// a long company name, unreported months, insights and missing-data notes, missing quarterly cells,
// negative values, a long line-item label, and the gross-margin row.

const MONTHS = [
  "فروردین",
  "اردیبهشت",
  "خرداد",
  "تیر",
  "مرداد",
  "شهریور",
  "مهر",
  "آبان",
  "آذر",
  "دی",
  "بهمن",
  "اسفند",
];
const PREVIOUS = [
  41250.4, 38990.1, 45210.7, 47120.2, 39875.9, 52345.6, 49870.3, 51230.8, 47650.1, 55120.9, 58340.2,
  61205.5,
];
const CURRENT = [48760.2, 45120.8, 52990.4, 55340.7, 47210.3, 61890.6, 58430.9];

export const monthlySalesTrendResult = {
  companySymbol: "فولاد",
  companyName: "شرکت فولاد مبارکه اصفهان (سهامی عام)",
  latestReportYear: 1405,
  latestReportMonth: 7,
  unitLabelFa: "میلیارد ریال",
  latestMonthlySalesAmount: CURRENT.at(-1),
  sameMonthPreviousYearSalesAmount: PREVIOUS[6],
  average12MonthSalesAmount: 53210.4,
  salesAmountYoYGrowthPercent: 17.16,
  salesVsAverage12MonthPercent: 9.81,
  chartPoints: MONTHS.map((name, index) => ({
    fiscalMonthIndex: index + 1,
    fiscalMonthNameFa: name,
    previousFiscalYear: 1404,
    previousFiscalYearSalesAmount: PREVIOUS[index],
    currentFiscalYear: 1405,
    currentFiscalYearSalesAmount: CURRENT[index],
    average12MonthSalesAmount: index < 3 ? undefined : 46000 + index * 900.25,
    isCurrentYearReported: index < CURRENT.length,
    isPreviousYearReported: index !== 4,
  })),
  insights: [
    { kind: "YoYGrowth", textFa: "فروش مهر ۱۴۰۵ نسبت به مهر ۱۴۰۴ 17.2% رشد داشته است." },
    { kind: "VsAverage12Month", textFa: "فروش این ماه 9.8% بالاتر از میانگین ۱۲ ماهه است." },
  ],
  missingDataPoints: [{ year: 1404, month: 5, reasonFa: "گزارش مرداد ۱۴۰۴ منتشر نشده است." }],
  sourceProviderName: "نوآوران امین",
  calculatedAtUtc: "2026-11-02T09:30:00Z",
};

const PERIODS = [
  { fiscalYear: 1405, quarter: 2, start: "2026-06-22", end: "2026-09-22", label: "1405 Q2" },
  { fiscalYear: 1405, quarter: 1, start: "2026-03-21", end: "2026-06-21", label: "1405 Q1" },
  { fiscalYear: 1404, quarter: 4, start: "2025-12-22", end: "2026-03-20", label: "1404 Q4" },
  { fiscalYear: 1404, quarter: 3, start: "2025-09-23", end: "2025-12-21", label: "1404 Q3" },
  { fiscalYear: 1404, quarter: 2, start: "2025-06-22", end: "2025-09-22", label: "1404 Q2" },
];

// Values newest first, in million rials (percent for the margin row). null = not reported.
const ROWS = [
  ["source_15", 15, "فروش", [184523110, 162004780, 171230455, 149870220, 138450900]],
  [
    "source_1",
    1,
    "بهای تمام‌شده کالای فروش‌رفته",
    [-121430220, -110230540, -118760330, -101230870, -96540120],
  ],
  ["source_139", 139, "سود (زیان) ناخالص", [63092890, 51774240, 52470125, 48639350, 41910780]],
  ["gross_margin", null, "حاشیه سود ناخالص", [34.19, 31.96, 30.64, 32.45, 30.27], "percent"],
  [
    "source_5",
    5,
    "هزینه‌های عمومی، اداری و تشکیلاتی",
    [-8540220, -7980110, null, -7120450, -6890330],
  ],
  [
    "source_39",
    39,
    "خالص سایر درآمدها (هزینه‌ها)ی عملیاتی",
    [1230450, -450120, 980330, 0, 1120540],
  ],
  ["source_140", 140, "سود (زیان) عملیاتی", [55783120, 43344010, 45720340, 41518900, 36141000]],
  ["source_12", 12, "هزینه‌های مالی", [-3420110, -3980220, -4120330, -3870440, -3650550]],
  [
    "source_40",
    40,
    "خالص سایر درآمدها و هزینه‌های غیرعملیاتی",
    [2310450, 1890330, null, 1450220, 1320110],
  ],
  [
    "source_142",
    142,
    "سود (زیان) عملیات در حال تداوم قبل از مالیات",
    [54673460, 41254120, 43120450, 39098680, 33810560],
  ],
  ["source_334", 334, "مالیات", [-10934690, -8250820, -8624090, -7819730, -6762110]],
  [
    "source_278",
    278,
    "سود (زیان) خالص عملیات متوقف‌شده پس از اثر مالیاتی در دوره جاری",
    [0, 0, 0, 0, 0],
  ],
  ["source_143", 143, "سود (زیان) خالص", [43738770, 33003300, 34496360, 31278950, 27048450]],
  ["source_160", 160, "سود هر سهم پس از کسر مالیات", [146, 110, 115, 104, 90]],
  [
    "source_153",
    153,
    "تعداد سهام شرکت (هزار سهم)",
    [300000000, 300000000, 300000000, 300000000, 300000000],
  ],
];

function changePercent(latest, previous) {
  if (latest == null || previous == null || previous === 0) return null;
  return (Math.abs(latest) / Math.abs(previous) - 1) * 100;
}

export const quarterlyReportResult = {
  templateId: "GeneralIncomeStatement",
  companySymbol: "فولاد",
  companyName: "فولاد مبارکه اصفهان",
  status: "Available",
  periodsNewestFirst: PERIODS,
  selectedReports: PERIODS.map((period) => ({ periodEnd: period.end, periodType: "ThreeMonths" })),
  rows: ROWS.map(([rowId, sourceItemId, labelFa, values, unit = "million_rial"], order) => ({
    rowId,
    order: order + 1,
    labelFa,
    sourceItemId,
    policy: rowId === "gross_margin" ? "DerivedGrossMargin" : "CumulativeFlow",
    unit,
    cells: Object.fromEntries(
      PERIODS.map((period, index) => [
        period.label,
        {
          value: values[index],
          state: values[index] === null ? "MissingObservation" : "Available",
          unit,
          renderZeroAsDash: values[index] === 0,
        },
      ]),
    ),
  })),
  comparisons: ROWS.map(([rowId, , , values]) => ({
    rowId,
    targetPeriod: { label: PERIODS[0].label },
    relativeChangePercent: changePercent(values[0], values[1]),
    percentagePointChange: rowId === "gross_margin" ? values[0] - values[1] : null,
    absoluteChange: values[0] != null && values[1] != null ? values[0] - values[1] : null,
    state: "Available",
  })),
};
