import { render } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import {
  parseQuarterlyFinancialTable,
  QuarterlyFinancialTable,
  quarterlyFinancialTableModelFromTypedResult,
} from "../quarterly-financial-table";

vi.mock("../quarterly-financial-chart", () => ({ QuarterlyFinancialChart: () => null }));

const rows = [
  ["فروش", "1275", "1000"],
  ["بهای تمام‌شده کالای فروش‌رفته", "1301", "1000"],
  ["سود (زیان) ناخالص", "1200", "1000"],
  ["مارجین ناخالص", "30", "30"],
  ["هزینه‌های عمومی، اداری و تشکیلاتی", "1111", "1000"],
  ["خالص سایر درآمدها (هزینه‌ها)ی عملیاتی", "100", "100"],
  ["سود (زیان) عملیاتی", "100", "100"],
  ["هزینه‌های مالی", "850", "1000"],
  ["خالص سایر درآمدها و هزینه‌های غیرعملیاتی", "100", "100"],
  ["سود (زیان) عملیات در حال تداوم قبل از مالیات", "100", "100"],
  ["مالیات", "100", "100"],
  ["سود (زیان) عملیات متوقف‌شده پس از اثر مالیاتی", "100", "100"],
  ["سود (زیان) خالص", "1459", "1000"],
  ["سود هر سهم پس از کسر مالیات", "100", "100"],
  ["تعداد سهام شرکت (هزار سهم)", "200", "200"],
];

function markdownTable(overrides: Record<number, [latest: string, previous: string]> = {}): string {
  const header =
    "| شرح | سال مالی 1405، فصل 2 | سال مالی 1405، فصل 1 | سال مالی 1404، فصل 4 | سال مالی 1404، فصل 3 | سال مالی 1404، فصل 2 | درصد تغییر به فصل قبل |";
  const body = rows.map(([label, latest, previous], index) => {
    const [latestValue, previousValue] = overrides[index] ?? [latest, previous];
    return `| ${label} | ${latestValue} | ${previousValue} | 100 | 100 | 100 | ignored |`;
  });
  return [
    "### جدول گزارش فصلی چخزر",
    "",
    header,
    "|---|---:|---:|---:|---:|---:|---:|",
    ...body,
  ].join("\n");
}

describe("quarterly financial table QoQ rendering", () => {
  it("adapts the typed banking result by stable row ID and excludes unsupported rows", () => {
    const ids = [304, 305, 306, null, 26, 7, 307, null, 61, 27, 40, 5, 6, 12, 142, 13, 278, 143, 160, 153];
    const labels = ["درآمد تسهیلات", "هزینه سود سپرده", "خالص درآمد تسهیلات", "مارجین ناخالص عملیات بانکی", "درآمد کارمزد", "هزینه کارمزد", "خالص درآمد کارمزد", "حاشیه سود کارمزد", "سود سرمایه‌گذاری", "معاملات ارزی", "غیرعملیاتی", "اداری", "مطالبات", "مالی", "قبل مالیات", "مالیات", "متوقف‌شده", "سود خالص", "سود هر سهم بر اساس آخرین سرمایه", "تعداد سهام"];
    const rowIds = ids.map((id, i) => id == null ? i === 3 ? "banking_gross_margin" : "banking_fee_margin" : `source_${id}`);
    const makeCell = (value: number | null, unit = "million_rial") => ({ value, unit, state: value == null ? 1 : 0, renderZeroAsDash: value === 0 });
    const result = {
      templateId: "BankingIncomeStatement", companySymbol: "وبملت", companyName: "بانک ملت", status: "Available",
      periodsNewestFirst: [
        { fiscalYear: 1405, quarter: 2, start: "2026-06-22", end: "2026-09-22", label: "1405 Q2" },
        { fiscalYear: 1405, quarter: 1, start: "2026-03-21", end: "2026-06-21", label: "1405 Q1" },
      ],
      selectedReports: [{ periodEnd: "2026-09-22", periodType: "SixMonths" }, { periodEnd: "2026-06-21", periodType: "ThreeMonths" }],
      rows: rowIds.map((rowId, order) => ({ rowId, order: order + 1, labelFa: labels[order], sourceItemId: ids[order], policy: ids[order] == null ? 4 : 0, unit: order === 3 || order === 7 ? "percent" : "million_rial", cells: { "1405 Q1": makeCell(order === 0 ? 100 : 0, order === 3 || order === 7 ? "percent" : "million_rial"), "1405 Q2": makeCell(order === 0 ? 120 : 0, order === 3 || order === 7 ? "percent" : "million_rial") } })),
      comparisons: [
        { rowId: "source_153", targetPeriod: { label: "1405 Q2" }, relativeChangePercent: 0, percentagePointChange: null, absoluteChange: 0, state: "Available" },
        { rowId: "source_304", targetPeriod: { label: "1405 Q2" }, relativeChangePercent: 0, percentagePointChange: null, absoluteChange: 0, state: "Available" },
        { rowId: "banking_gross_margin", targetPeriod: { label: "1405 Q2" }, relativeChangePercent: null, percentagePointChange: 14.7, absoluteChange: 14.7, state: "Available" },
        { rowId: "banking_fee_margin", targetPeriod: { label: "1405 Q2" }, relativeChangePercent: null, percentagePointChange: 14.7, absoluteChange: 14.7, state: "Available" },
      ],
    };
    const model = quarterlyFinancialTableModelFromTypedResult(result);
    expect(model.templateId).toBe("BankingIncomeStatement");
    expect(model.title).toBe(`گزارش فصلی ${result.companyName} (${result.companySymbol})`);
    expect(model.rows).toHaveLength(20);
    expect(model.rows.map((row) => row.rowId)).toEqual(rowIds);
    expect(model.rows.filter((row) => row.bankingMargin)).toHaveLength(2);
    expect(model.rows.map((row) => row.label)).not.toContain("سود عملیاتی خالص");
    expect(model.quarters.map((period) => period.quarter)).toEqual([1, 2]);
    expect(model.rows[0]?.values.slice(0, 2)).toEqual([
      new Intl.NumberFormat("fa-IR-u-nu-arabext").format(100),
      new Intl.NumberFormat("fa-IR-u-nu-arabext").format(120),
    ]);
    expect(model.rows[0]?.chartValues).toEqual([100, 120]);
    expect(model.rows[0]?.cellEvidence?.map((cell) => cell.state)).toEqual(["Available", "Available"]);
    expect(model.rows.find((row) => row.sourceItemId === 153)?.values.at(-1)).toBe("-");
    expect(model.rows.find((row) => row.sourceItemId === 304)?.values.at(-1)).toBe(
      `${new Intl.NumberFormat("fa-IR-u-nu-arabext", { minimumFractionDigits: 1, maximumFractionDigits: 1 }).format(0)}٪`,
    );
    const marginChange = `${new Intl.NumberFormat("fa-IR-u-nu-arabext", { minimumFractionDigits: 1, maximumFractionDigits: 1 }).format(14.7)}٪`;
    expect(model.rows.find((row) => row.rowId === "banking_gross_margin")?.values.at(-1)).toBe(marginChange);
    expect(model.rows.find((row) => row.rowId === "banking_fee_margin")?.values.at(-1)).toBe(marginChange);
    const { container } = render(<QuarterlyFinancialTable model={model} />);
    const tableRows = Array.from(container.querySelectorAll<HTMLTableRowElement>("tbody tr"));
    for (const index of [3, 7]) {
      expect(Array.from(tableRows[index]!.cells).every((cell) => cell.className.includes("bg-yellow-200"))).toBe(true);
    }
  });

  it("renders shared three-line fiscal-quarter labels from the selected report", () => {
    const fiscalHeaders = [
      "فصل اول<br>منتهی به<br>۲۹ اسفند ۱۴۰۴",
      "فصل دوم<br>منتهی به<br>۳۱ خرداد ۱۴۰۵",
      "فصل سوم<br>منتهی به<br>۳۱ شهریور ۱۴۰۵",
      "فصل چهارم<br>منتهی به<br>۳۰ آذر ۱۴۰۵",
      "فصل اول<br>منتهی به<br>۲۹ اسفند ۱۴۰۵",
    ];
    const content = markdownTable().split("\n");
    content[2] = `| شرح | ${fiscalHeaders.toReversed().join(" | ")} | درصد تغییر به فصل قبل |`;
    const parsed = parseQuarterlyFinancialTable(content.join("\n"));
    expect(parsed).not.toBeNull();

    const { container } = render(<QuarterlyFinancialTable model={parsed!.model} />);
    const headers = Array.from(container.querySelectorAll("thead th"));
    expect(headers.map((cell) => cell.textContent)).toEqual([
      "شرح",
      "فصل اولمنتهی به۲۹ اسفند ۱۴۰۴",
      "فصل دوممنتهی به۳۱ خرداد ۱۴۰۵",
      "فصل سوممنتهی به۳۱ شهریور ۱۴۰۵",
      "فصل چهارممنتهی به۳۰ آذر ۱۴۰۵",
      "فصل اولمنتهی به۲۹ اسفند ۱۴۰۵",
      "درصد تغییر به فصل قبل",
    ]);
    expect(headers[3]?.querySelectorAll("span")).toHaveLength(3);
    expect(headers[3]?.querySelector("span")?.className).toContain("font-bold");
    expect(headers[3]?.getAttribute("aria-label")).toBe("فصل سوم منتهی به ۳۱ شهریور ۱۴۰۵");
  });

  it("renders selected PeriodEnd headers in RTL order and keeps the fiscal fallback", () => {
    const dateHeaders = [
      "دوره منتهی به ۳۱ شهریور ۱۴۰۵",
      "دوره منتهی به ۳۱ خرداد ۱۴۰۵",
      "دوره منتهی به ۲۹ اسفند ۱۴۰۴",
      "دوره منتهی به ۳۰ آذر ۱۴۰۴",
      "سال مالی ۱۴۰۴، فصل ۲",
    ];
    const markdown = markdownTable().replace(
      /^\| شرح \|.*$/m,
      `| شرح | ${dateHeaders.join(" | ")} | درصد تغییر به فصل قبل |`,
    );
    const parsed = parseQuarterlyFinancialTable(markdown);
    expect(parsed).not.toBeNull();
    const { container } = render(<QuarterlyFinancialTable model={parsed!.model} />);
    const headers = Array.from(container.querySelectorAll("thead th"), (cell) => cell.textContent);
    expect(headers).toEqual([
      "شرح",
      "سال مالی ۱۴۰۴فصل ۲",
      "دوره منتهی به۳۰ آذر ۱۴۰۴",
      "دوره منتهی به۲۹ اسفند ۱۴۰۴",
      "دوره منتهی به۳۱ خرداد ۱۴۰۵",
      "دوره منتهی به۳۱ شهریور ۱۴۰۵",
      "درصد تغییر به فصل قبل",
    ]);
    expect(parsed!.model.rows[0].values.slice(0, 5)).toEqual([
      "۱۰۰",
      "۱۰۰",
      "۱۰۰",
      "۱٬۰۰۰",
      "۱٬۲۷۵",
    ]);
  });

  it("highlights every gross-margin cell without changing percentage values", () => {
    const parsed = parseQuarterlyFinancialTable(markdownTable({ 3: ["17.7", "26"] }))!;
    const { container } = render(<QuarterlyFinancialTable model={parsed.model} />);
    const gross = container.querySelectorAll("tbody tr")[3] as HTMLTableRowElement;
    expect(gross.className).toContain("text-[11px]");
    expect(gross.className).toContain("font-bold");
    expect(Array.from(gross.cells).every((cell) => cell.className.includes("bg-yellow-200"))).toBe(
      true,
    );
    expect(gross.cells[4].textContent).toBe("۲۶٫۰٪");
    expect(gross.cells[5].textContent).toBe("۱۷٫۷٪");
    expect(gross.cells[6].textContent).toBe("\u200e−۳۱٫۹٪");
    expect(gross.cells[6].className).toContain("text-rose-900");
  });

  it("uses SourceItemIds for the agreed expense rows and preserves computed percentages", () => {
    const parsed = parseQuarterlyFinancialTable(markdownTable());
    expect(parsed).not.toBeNull();
    const model = parsed!.model;
    expect(model.rows.map((row) => row.sourceItemId)).toEqual([
      15,
      1,
      139,
      null,
      5,
      39,
      140,
      12,
      40,
      142,
      334,
      278,
      143,
      160,
      153,
    ]);

    const { container } = render(<QuarterlyFinancialTable model={model} />);
    const tableRows = Array.from(container.querySelectorAll<HTMLTableRowElement>("tbody tr"));
    const qoq = (index: number) => tableRows[index]!.cells[tableRows[index]!.cells.length - 1]!;

    expect(qoq(0).textContent).toBe("۲۷٫۵٪");
    expect(qoq(0).className).toContain("bg-emerald-100");
    expect(qoq(1).textContent).toBe("۳۰٫۱٪");
    expect(qoq(1).className).toContain("bg-rose-100");
    expect(qoq(4).textContent).toBe("۱۱٫۱٪");
    expect(qoq(4).className).toContain("bg-rose-100");
    expect(qoq(7).textContent).toBe("\u200e−۱۵٫۰٪");
    expect(qoq(7).className).toContain("bg-emerald-100");
    expect(qoq(12).textContent).toBe("۴۵٫۹٪");
    expect(qoq(12).className).toContain("bg-emerald-100");
  });

  it("keeps zero, opposite-sign, unavailable, and share-count special cases neutral", () => {
    const content = markdownTable({
      1: ["100", "100"],
      4: ["100", "-100"],
      5: ["100", "—"],
      14: ["200", "200"],
    });
    const parsed = parseQuarterlyFinancialTable(content)!;
    const { container } = render(<QuarterlyFinancialTable model={parsed.model} />);
    const tableRows = Array.from(container.querySelectorAll<HTMLTableRowElement>("tbody tr"));
    const qoq = (index: number) => tableRows[index]!.cells[tableRows[index]!.cells.length - 1]!;

    expect(qoq(1).textContent).toBe("۰");
    expect(qoq(1).className).toContain("bg-white");
    expect(qoq(4).textContent).toBe("");
    expect(qoq(4).className).toContain("bg-white");
    expect(qoq(5).textContent).toBe("-");
    expect(qoq(5).className).toContain("bg-white");
    expect(qoq(14).textContent).toBe("");
    expect(qoq(14).className).toContain("bg-white");
  });

  it("leaves row IDs unset when the complete Feature 144 row contract is absent", () => {
    const partial = parseQuarterlyFinancialTable(
      markdownTable().split("\n").slice(0, 7).join("\n"),
    );
    expect(partial?.model.rows.every((row) => row.sourceItemId === null)).toBe(true);
  });

  it("keeps compact comparable EPS provenance available to the table details", () => {
    const note = "مبنای EPS مقایسه‌ای: 1404 Q2: سود قابل انتساب 2,097,274 میلیون ریال ÷ 47,616,343 هزار سهم؛ FY1405 Q2، گزارش 553578؛ مقدار مشتق‌شده 44 ریال با گردکردن به نزدیک‌ترین ریال و نیم‌به‌بالا؛ سهام گزارش‌شده پایان دوره است و میانگین وزنی محسوب نمی‌شود؛ EPS تجمعی منبع 371 منهای 121.";
    const parsed = parseQuarterlyFinancialTable(`${markdownTable()}\n${note}`)!;
    expect(parsed.after).toBe("");
    expect(parsed.model.provenanceNotes?.[0]).toContain("۵۵۳۵۷۸");
    const { container } = render(<QuarterlyFinancialTable model={parsed.model} />);
    expect(container.textContent).toContain("روش و مبنای EPS مقایسه‌ای");
    expect(container.textContent).toContain("۵۵۳۵۷۸");
    expect(container.textContent).not.toContain("قلم منبع");
  });
});
