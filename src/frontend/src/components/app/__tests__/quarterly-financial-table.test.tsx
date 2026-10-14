import { render } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import {
  parseQuarterlyFinancialTable,
  QuarterlyFinancialTable,
} from "../quarterly-financial-table";

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
  ["سود (زیان) خالص عملیات در حال تداوم", "100", "100"],
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
      279,
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
    expect(qoq(13).textContent).toBe("۴۵٫۹٪");
    expect(qoq(13).className).toContain("bg-emerald-100");
  });

  it("keeps zero, opposite-sign, unavailable, and share-count special cases neutral", () => {
    const content = markdownTable({
      1: ["100", "100"],
      4: ["100", "-100"],
      5: ["100", "—"],
      15: ["200", "200"],
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
    expect(qoq(15).textContent).toBe("");
    expect(qoq(15).className).toContain("bg-white");
  });

  it("leaves row IDs unset when the complete Feature 144 row contract is absent", () => {
    const partial = parseQuarterlyFinancialTable(
      markdownTable().split("\n").slice(0, 7).join("\n"),
    );
    expect(partial?.model.rows.every((row) => row.sourceItemId === null)).toBe(true);
  });
});
