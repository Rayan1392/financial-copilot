import { Download } from "lucide-react";
import { useState } from "react";
import { QUARTERLY_CHANGE_COLUMN_HEADER } from "./quarterly-financial-table-constants";
import { resolveQuarterlyChangeColors } from "./quarterly-financial-table-colors";
import {
  quarterlyPeriodHeaderLines,
  type QuarterlyFinancialPeriod,
} from "./quarterly-financial-table-header";
import { downloadQuarterlyFinancialTableImage } from "./quarterly-financial-table-image";

export interface QuarterlyFinancialTableRow {
  label: string;
  sourceItemId: number | null;
  values: string[];
  grossMargin: boolean;
}

export interface QuarterlyFinancialTableModel {
  title: string;
  symbol: string;
  summary: string;
  quarters: QuarterlyFinancialPeriod[];
  rows: QuarterlyFinancialTableRow[];
}

const DIGITS = "۰۱۲۳۴۵۶۷۸۹٠١٢٣٤٥٦٧٨٩";
function normalizeDigits(value: string): string {
  return value.replace(/[۰-۹٠-٩]/g, (digit) => {
    const index = DIGITS.indexOf(digit);
    return String(index < 10 ? index : index - 10);
  });
}

function toPersianDigits(value: string): string {
  return value.replace(/[0-9\u0660-\u0669\u06f0-\u06f9]/g, (digit) => {
    const code = digit.charCodeAt(0);
    const numericValue =
      code >= 0x06f0 ? code - 0x06f0 : code >= 0x0660 ? code - 0x0660 : code - 48;
    return String.fromCharCode(0x06f0 + numericValue);
  });
}

function splitRow(line: string): string[] {
  return line
    .trim()
    .replace(/^\||\|$/g, "")
    .split("|")
    .map((cell) => cell.trim());
}

function quarterInfo(label: string, sourceIndex: number) {
  const normalized = normalizeDigits(label);
  if (
    /^دوره\s+منتهی\s+به\s+\d{1,2}\s+(?:فروردین|اردیبهشت|خرداد|تیر|مرداد|شهریور|مهر|آبان|آذر|دی|بهمن|اسفند)\s+1[34]\d{2}$/.test(
      normalized,
    )
  ) {
    return { label, sourceIndex };
  }
  const year = normalized.match(/(?:سال\s*مالی\s*)?(13\d{2}|14\d{2})/);
  const quarter = normalized.match(/(?:فصل|ربع)\s*([1-4])/);
  return year && quarter
    ? { label, year: Number(year[1]), quarter: Number(quarter[1]), sourceIndex }
    : null;
}

function numericValue(value: string): number | null {
  const normalized = normalizeDigits(value)
    .replace(/[\u066c,\s]/g, "")
    .replace(/\u066b/g, ".")
    .replace(/[\u2212\u2013]/g, "-");
  if (!/\d/.test(normalized)) return null;
  const paren = normalized.match(/^\(([+-]?[\d.]+)\)$/);
  const parsed = Number(paren ? `-${paren[1]}` : normalized.replace(/[^\d.+-]/g, ""));
  return Number.isFinite(parsed) ? parsed : null;
}

function displayFinancialValue(value: string): string {
  const parsed = numericValue(value);
  if (parsed === 0) return "-";
  if (parsed == null) return toPersianDigits(value.trim() || "-");
  const absolute = new Intl.NumberFormat("fa-IR-u-nu-arabext", { maximumFractionDigits: 2 }).format(
    Math.abs(parsed),
  );
  return parsed < 0 ? `(${absolute})` : absolute;
}

function quarterChange(
  latest: string,
  previous: string,
): { value: number; signMismatch: boolean } | null {
  const latestValue = numericValue(latest);
  const previousValue = numericValue(previous);
  if (latestValue == null || previousValue == null || previousValue === 0) return null;
  if (latestValue !== 0 && Math.sign(latestValue) !== Math.sign(previousValue)) {
    return { value: 0, signMismatch: true };
  }
  return {
    value: (Math.abs(latestValue) / Math.abs(previousValue) - 1) * 100,
    signMismatch: false,
  };
}

function isShareCountRow(label: string) {
  return /\u062a\u0639\u062f\u0627\u062f\s*\u0633\u0647\u0627\u0645\s*\u0634\u0631\u06a9\u062a/.test(
    label,
  );
}

function rowIsGrossMargin(label: string) {
  return /\u062d\u0627\u0634\u06cc\u0647\s*\u0633\u0648\u062f\s*\u0646\u0627\u062e\u0627\u0644\u0635|\u0645\u0627\u0631[\u062c\u0698]\u06cc\u0646\s*\u0646\u0627\u062e\u0627\u0644\u0635/.test(
    label,
  );
}

function formatPercent(value: number, fractionDigits: number): string {
  const formatted = new Intl.NumberFormat("fa-IR-u-nu-arabext", {
    minimumFractionDigits: fractionDigits,
    maximumFractionDigits: fractionDigits,
    useGrouping: false,
  }).format(value);
  return `${formatted}${String.fromCharCode(0x066a)}`;
}

function grossMarginChangeClass(
  tone: ReturnType<typeof resolveQuarterlyChangeColors>["tone"],
): string {
  const text =
    tone === "positive"
      ? "text-emerald-900"
      : tone === "negative"
        ? "text-rose-900"
        : "text-slate-700";
  return `bg-yellow-200 ${text}`;
}

function formatGrossMargin(value: string): string {
  const parsed = numericValue(value);
  if (parsed == null) return toPersianDigits(value.trim() || "-");
  return new Intl.NumberFormat("fa-IR-u-nu-arabext", {
    minimumFractionDigits: 1,
    maximumFractionDigits: 1,
    useGrouping: false,
  }).format(parsed);
}

function simplifiedSummary(summary: string): string {
  return toPersianDigits(
    summary
      .split(/[\u061b;]/)
      .filter((item) => !/\u062a\u0639\u062f\u0627\u062f\s*\u0633\u0647\u0627\u0645/.test(item))
      .join("\u061b")
      .trim(),
  );
}

function isChangeHeader(value: string) {
  return /تغییر|درصد تغییر|فصل.?به.?فصل|qoq/i.test(value);
}

// Feature 144's typed result is rendered in this fixed 16-row order. Markdown does not carry
// row metadata, so attach stable source IDs only when the complete contracted row set is present.
const FEATURE_144_SOURCE_ITEM_IDS: ReadonlyArray<number | null> = [
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
];

export function parseQuarterlyFinancialTable(
  content: string,
): { before: string; after: string; model: QuarterlyFinancialTableModel } | null {
  const lines = content.split("\n");
  const headerIndex = lines.findIndex((line) => {
    if (!line.trim().startsWith("|")) return false;
    const cells = splitRow(line);
    return (
      cells.length >= 7 &&
      /شرح/.test(cells[0]) &&
      cells.some(isChangeHeader) &&
      cells.filter((cell) => quarterInfo(cell, -1)).length >= 5
    );
  });
  if (headerIndex < 0 || !lines[headerIndex + 1]?.trim().startsWith("|")) return null;

  const header = splitRow(lines[headerIndex]);
  const quarters = header
    .map((label, sourceIndex) => quarterInfo(label, sourceIndex))
    .filter((period): period is NonNullable<typeof period> => period !== null);
  if (quarters.some((period) => period.year == null)) {
    // New backend tables are emitted newest first, including any fallback fiscal labels.
    quarters.sort((a, b) => b.sourceIndex - a.sourceIndex);
  } else {
    quarters.sort((a, b) => a.year! - b.year! || a.quarter! - b.quarter!);
  }
  if (quarters.length < 5) return null;
  const changeIndex = header.findIndex(isChangeHeader);
  const rows: QuarterlyFinancialTableRow[] = [];
  let endIndex = headerIndex + 2;
  while (endIndex < lines.length && lines[endIndex].trim().startsWith("|")) {
    const cells = splitRow(lines[endIndex]);
    if (cells.length >= header.length && !cells[0].replace(/[:-]/g, "").trim()) {
      endIndex++;
      continue;
    }
    if (cells.length >= header.length) {
      const label = cells[0];
      const values = quarters.map((period) => cells[period.sourceIndex] ?? "-");
      const latest = values.at(-1) ?? "-";
      const previous = values.at(-2) ?? "-";
      const change = quarterChange(latest, previous);
      const shareCount = isShareCountRow(cells[0]);
      const changeDisplay =
        changeIndex < 0 || change == null
          ? "-"
          : change.signMismatch || (change.value === 0 && shareCount)
            ? ""
            : change.value === 0
              ? String.fromCharCode(0x06f0)
              : formatPercent(change.value, 1);
      values.push(changeDisplay);
      const grossMargin = rowIsGrossMargin(label);
      rows.push({
        label,
        sourceItemId: null,
        values: values.map((value, index) =>
          index === values.length - 1
            ? value
            : grossMargin
              ? formatGrossMargin(value)
              : displayFinancialValue(value),
        ),
        grossMargin,
      });
    }
    endIndex++;
  }

  if (rows.length === FEATURE_144_SOURCE_ITEM_IDS.length) {
    rows.forEach((row, index) => {
      row.sourceItemId = FEATURE_144_SOURCE_ITEM_IDS[index];
    });
  }

  const before = lines.slice(0, headerIndex).join("\n").trim();
  const title = before.match(/^#{1,6}\s*(.+)$/m)?.[1]?.trim() || "گزارش مالی فصلی";
  const summaryRaw =
    before
      .split("\n")
      .find((line) => /مبالغ:|واحد|EPS:|تعداد سهام:/.test(line))
      ?.replace(/^#+\s*/, "")
      .trim() || "مبالغ: میلیون ریال";
  const summary = simplifiedSummary(summaryRaw);
  const symbolSuffix = /[\uff08(]([^()\uff08\uff09]+)[\uff09)]\s*$/;
  const parenthesizedSymbol = title.match(symbolSuffix)?.[1]?.trim() ?? "";
  const titleName = title.replace(symbolSuffix, "").replace(/^جدول گزارش فصلی\s*/, "").trim();
  const symbol = parenthesizedSymbol || (/^جدول گزارش فصلی\s/.test(title) ? titleName : "");
  const displayTitle = parenthesizedSymbol && titleName === parenthesizedSymbol
    ? title.replace(/\s*[\uff08(][^()\uff08\uff09]+[\uff09)]\s*$/, "")
    : title;
  return {
    before: before
      .replace(/^#{1,6}\s*.+(?:\n|$)/m, "")
      .replace(summaryRaw, "")
      .trim(),
    after: lines.slice(endIndex).join("\n").trim(),
    model: {
      title: toPersianDigits(displayTitle),
      symbol: toPersianDigits(symbol),
      summary,
      quarters: quarters.map((quarter) => ({ ...quarter, label: toPersianDigits(quarter.label) })),
      rows: rows.map((row) => ({ ...row, label: toPersianDigits(row.label) })),
    },
  };
}

export function QuarterlyFinancialTable({ model }: { model: QuarterlyFinancialTableModel }) {
  const [exporting, setExporting] = useState(false);
  const download = async () => {
    setExporting(true);
    try {
      await downloadQuarterlyFinancialTableImage(model);
    } finally {
      setExporting(false);
    }
  };

  return (
    <section
      className="my-3 w-full overflow-hidden rounded-xl border border-amber-200 bg-[#fffdf8] shadow-sm"
      dir="rtl"
    >
      <header className="bg-gradient-to-l from-[#164e63] via-[#155e75] to-[#0f766e] px-3 py-3 text-white sm:px-4">
        <div className="flex items-start justify-between gap-2">
          <div className="min-w-0">
            <h3 className="truncate text-sm font-bold sm:text-base">{model.title}</h3>
            <p className="mt-1 text-[10px] leading-5 text-cyan-50 sm:text-xs">{model.summary}</p>
          </div>
          <button
            type="button"
            onClick={download}
            disabled={exporting}
            className="inline-flex shrink-0 items-center gap-1 rounded-lg bg-white/15 px-2 py-1.5 text-[10px] font-semibold transition hover:bg-white/25 disabled:opacity-60 sm:text-xs"
            aria-label="دانلود تصویر جدول"
          >
            <Download className="size-3.5" />
            {exporting ? "در حال ساخت" : "دانلود تصویر"}
          </button>
        </div>
      </header>
      <table
        className="w-full table-fixed border-collapse text-[9px] leading-4 sm:text-[11px] sm:leading-5"
        dir="rtl"
      >
        <colgroup>
          <col style={{ width: "22%" }} />
          {model.quarters.map((quarter) => (
            <col key={quarter.sourceIndex} style={{ width: `${62 / model.quarters.length}%` }} />
          ))}
          <col style={{ width: "16%" }} />
        </colgroup>
        <thead>
          <tr className="bg-[#d9efed] text-[#164e63]">
            <th className="border border-[#b8d9d5] px-1 py-2 text-right font-bold">شرح</th>
            {model.quarters.map((quarter) => (
              <th
                key={quarter.sourceIndex}
                aria-label={quarter.label}
                className="border border-[#b8d9d5] px-0.5 py-1.5 text-center font-semibold"
              >
                {quarterlyPeriodHeaderLines(quarter)[0]}
                <br />
                {quarterlyPeriodHeaderLines(quarter)[1]}
              </th>
            ))}
            <th className="border border-[#b8d9d5] bg-[#c7e5e1] px-0.5 py-1.5 text-center font-bold">
              {QUARTERLY_CHANGE_COLUMN_HEADER}
            </th>
          </tr>
        </thead>
        <tbody>
          {model.rows.map((row, rowIndex) => (
            <tr
              key={`${row.label}-${rowIndex}`}
              className={row.grossMargin ? "text-[11px] font-bold sm:text-[13px]" : ""}
            >
              <th
                className={`border border-amber-200 px-1 py-1.5 text-right ${row.grossMargin ? "bg-yellow-200 font-extrabold text-amber-950" : "bg-[#ffedd5] font-semibold text-slate-800"}`}
              >
                {row.label}
              </th>
              {row.values.slice(0, model.quarters.length).map((value, index) => (
                <td
                  key={index}
                  className={`border border-slate-200 px-0.5 py-1.5 text-center tabular-nums ${row.grossMargin ? "bg-yellow-200 font-bold text-amber-950" : value.startsWith("(") ? "bg-rose-100 text-rose-900" : "bg-white text-slate-800"}`}
                >
                  {row.grossMargin && value !== "-"
                    ? `${value}${String.fromCharCode(0x066a)}`
                    : value}
                </td>
              ))}
              <td
                className={`border border-amber-200 px-0.5 py-1.5 text-center font-bold tabular-nums ${row.grossMargin ? grossMarginChangeClass(resolveQuarterlyChangeColors(row.sourceItemId, numericValue(row.values.at(-1) ?? "-")).tone) : resolveQuarterlyChangeColors(row.sourceItemId, numericValue(row.values.at(-1) ?? "-")).className}`}
              >
                {row.values.at(-1)}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
}
