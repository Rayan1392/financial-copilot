import { Download } from "lucide-react";
import { useEffect, useState } from "react";
import { buildFinancialCopilotApiUrl } from "../../integrations/financial-copilot/api-url";
import { QUARTERLY_CHANGE_COLUMN_HEADER } from "./quarterly-financial-table-constants";
import { resolveQuarterlyChangeColors } from "./quarterly-financial-table-colors";
import {
  quarterlyPeriodHeaderLines,
  type QuarterlyFinancialPeriod,
} from "./quarterly-financial-table-header";
import { downloadQuarterlyFinancialTableImage } from "./quarterly-financial-table-image";
import { QuarterlyFinancialChart } from "./quarterly-financial-chart";
import type { QuarterlyFinancialReportTableResult } from "@/lib/chat.functions";

export interface QuarterlyFinancialTableRow {
  rowId?: string;
  label: string;
  sourceItemId: number | null;
  values: string[];
  grossMargin: boolean;
  bankingMargin?: boolean;
  qoqChange?: number | null;
  chartValues?: Array<number | null>;
  cellEvidence?: Array<{ state: string; reason?: string | null; unit?: string; sourceReportIds?: string[]; sourceLineItemIds?: string[]; normalizationPolicy?: string; operandValues?: Array<number | null> }>;
}

export interface QuarterlyFinancialTableModel {
  templateId?: string;
  title: string;
  symbol: string;
  summary: string;
  quarters: QuarterlyFinancialPeriod[];
  rows: QuarterlyFinancialTableRow[];
  provenanceNotes?: string[];
}

const QUARTERLY_CELL_STATE_NAMES = [
  "Available",
  "MissingObservation",
  "IncompatibleObservation",
  "ConflictingObservation",
  "UnsupportedFiscalPeriod",
  "MissingPredecessor",
  "CalculationUnavailable",
] as const;

function quarterlyCellStateName(state: string | number | undefined): string {
  if (typeof state === "number") return QUARTERLY_CELL_STATE_NAMES[state] ?? `Unknown(${state})`;
  return state ?? "MissingObservation";
}

function isQuarterlyCellAvailable(state: string | number | undefined): boolean {
  return state === "Available" || state === 0;
}

export function quarterlyFinancialTableModelFromTypedResult(
  result: QuarterlyFinancialReportTableResult,
): QuarterlyFinancialTableModel {
  const periodsNewestFirst = result.periodsNewestFirst ?? [];
  const periods = [...periodsNewestFirst].reverse();
  const quarters = periods.map((period, sourceIndex) => {
    const report = result.selectedReports?.find((item) => item.periodEnd === period.end);
    const date = report?.periodEnd
      ? new Intl.DateTimeFormat("fa-IR-u-ca-persian", { day: "numeric", month: "long", year: "numeric", timeZone: "Asia/Tehran" }).format(new Date(`${report.periodEnd}T12:00:00`))
      : `سال مالی ${period.fiscalYear}`;
    const names = ["", "اول", "دوم", "سوم", "چهارم"];
    const quarterName = `فصل ${names[period.quarter]}`;
    return { label: `${quarterName}<br>منتهی به<br>${date}`, quarterName, dateLabel: date,
      year: period.fiscalYear, quarter: period.quarter, sourceIndex };
  });
  const latestLabel = periodsNewestFirst[0]?.label;
  const amountFormatter = new Intl.NumberFormat("fa-IR-u-nu-arabext", { maximumFractionDigits: 2 });
  const percentFormatter = new Intl.NumberFormat("fa-IR-u-nu-arabext", { minimumFractionDigits: 1, maximumFractionDigits: 1 });
  const rows = [...result.rows].sort((a, b) => a.order - b.order).map((row) => {
    const bankingMargin = row.rowId === "banking_gross_margin" || row.rowId === "banking_fee_margin" ||
      row.policy === "DerivedBankingMargin";
    const cells = periods.map((period) => row.cells[period.label]);
    const comparison = latestLabel
      ? result.comparisons.find((item) => item.rowId === row.rowId && item.targetPeriod.label === latestLabel)
      : undefined;
    const qoqChange = bankingMargin
      ? comparison?.percentagePointChange ?? null
      : comparison?.relativeChangePercent ?? null;
    const omitUnchangedShareCountChange = row.sourceItemId === 153 && qoqChange === 0;
    const values = cells.map((cell) => {
      if (!cell || !isQuarterlyCellAvailable(cell.state) || cell.value == null) return "—";
      if (cell.value === 0 && cell.renderZeroAsDash) return "-";
      const formatted = bankingMargin ? percentFormatter.format(cell.value) : amountFormatter.format(Math.abs(cell.value));
      return cell.value < 0 && !bankingMargin ? `(${formatted})` : formatted;
    });
    const changeDisplay = qoqChange == null || omitUnchangedShareCountChange
      ? "-"
      : `${new Intl.NumberFormat("fa-IR-u-nu-arabext", { minimumFractionDigits: 1, maximumFractionDigits: 1 }).format(qoqChange)}٪`;
    values.push(changeDisplay);
    return {
      rowId: row.rowId,
      label: row.labelFa,
      sourceItemId: row.sourceItemId ?? null,
      values,
      chartValues: cells.map((cell) => isQuarterlyCellAvailable(cell?.state) ? cell?.value ?? null : null),
      grossMargin: bankingMargin || row.rowId === "gross_margin" || row.policy === "DerivedGrossMargin",
      bankingMargin,
      qoqChange,
      cellEvidence: cells.map((cell) => ({ state: quarterlyCellStateName(cell?.state), reason: cell?.reason,
        unit: cell?.unit, sourceReportIds: cell?.sourceReportIds, sourceLineItemIds: cell?.sourceLineItemIds,
        normalizationPolicy: cell?.normalizationPolicy, operandValues: cell?.operandValues,
        comparableEvidence: cell?.comparableEvidence })),
    };
  });
  const investmentSourceTitle = result.rows.find((row) => row.rowId === "source_61")?.sourceCatalogTitleFa;
  const epsRow = result.rows.find((row) => row.rowId === "source_160");
  const epsNotes = result.templateId === "BankingIncomeStatement" ? periodsNewestFirst.flatMap((period) => {
    const evidence = epsRow?.cells[period.label]?.comparableEvidence;
    if (!evidence) return [];
    const amount = (value: number | null | undefined) => value == null ? "ناموجود" : amountFormatter.format(value);
    const quarterName = ["", "اول", "دوم", "سوم", "چهارم"][period.quarter] ?? "";
    const periodName = `سال مالی ${amount(period.fiscalYear)}، فصل ${quarterName}`;
    return [`سود هر سهم بر اساس آخرین سرمایه، ${periodName}: سود مستقل ${amount(evidence.standaloneAttributableProfitMillionRials)} میلیون ریال و تعداد سهام ${amount(evidence.shareBasisThousand)} هزار سهم.`];
  }) : [];
  return {
    templateId: result.templateId,
    title: `گزارش فصلی ${result.companyName ? `${result.companyName} (${result.companySymbol})` : result.companySymbol}`,
    symbol: result.companySymbol,
    summary: "مبالغ: میلیون ریال · درصدها از مقادیر مستقل فصلی محاسبه شده‌اند",
    quarters,
    rows,
    provenanceNotes: result.templateId === "BankingIncomeStatement"
      ? [...(investmentSourceTitle ? [`عنوان منبع سرمایه‌گذاری: ${investmentSourceTitle}`] : []), ...epsNotes]
      : undefined,
  };
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
  const headerParts = label.split(/<br\s*\/?>/i).map((part) => part.trim());
  if (headerParts.length >= 3 && /^فصل\s+(?:اول|دوم|سوم|چهارم)$/.test(headerParts[0])) {
    return {
      label,
      quarterName: headerParts[0],
      dateLabel: headerParts[2],
      sourceIndex,
    };
  }
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

function chartNumericValue(value: string): number | null {
  // The backend uses a short dash for an available zero and an em dash for unavailable.
  return value.trim() === "-" ? 0 : numericValue(value);
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

// Feature 144's typed result is rendered in this fixed 15-row order. Markdown does not carry
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
      cells.length >= 3 &&
      /شرح/.test(cells[0]) &&
      cells.some(isChangeHeader) &&
      cells.filter((cell) => quarterInfo(cell, -1)).length >= 1
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
  if (quarters.length < 1 || quarters.length > 12) return null;
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
        chartValues: values.slice(0, quarters.length).map(chartNumericValue),
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
  const afterLines = lines.slice(endIndex);
  const provenanceNotes = afterLines
    .filter((line) => line.startsWith("مبنای EPS مقایسه‌ای:"))
    .map((line) => line.trim());
  const after = afterLines.filter((line) => !line.startsWith("مبنای EPS مقایسه‌ای:")).join("\n").trim();
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
    after,
    model: {
      title: toPersianDigits(displayTitle),
      symbol: toPersianDigits(symbol),
      summary,
      quarters: quarters.map((quarter) => ({ ...quarter, label: toPersianDigits(quarter.label) })),
      rows: rows.map((row) => ({ ...row, label: toPersianDigits(row.label) })),
      provenanceNotes: provenanceNotes.map(toPersianDigits),
    },
  };
}

export function QuarterlyFinancialTable({ model }: { model: QuarterlyFinancialTableModel }) {
  const [exporting, setExporting] = useState(false);
  const [showChart, setShowChart] = useState<boolean | null>(null);
  useEffect(() => {
    const controller = new AbortController();
    void fetch(buildFinancialCopilotApiUrl("/api/ui/v1/config", import.meta.env.VITE_FINANCIAL_COPILOT_API_BASE_URL), {
      signal: controller.signal,
    })
      .then(async (response) => {
        if (!response.ok) throw new Error("UI configuration unavailable");
        const config = await response.json() as { quarterlyFinancialTable?: { showChart?: boolean } };
        if (!controller.signal.aborted) setShowChart(config.quarterlyFinancialTable?.showChart === true);
      })
      .catch(() => { if (!controller.signal.aborted) setShowChart(false); });
    return () => controller.abort();
  }, []);
  const download = async () => {
    setExporting(true);
    try {
      await downloadQuarterlyFinancialTableImage(model, showChart === true || model.templateId === "BankingIncomeStatement");
    } finally {
      setExporting(false);
    }
  };

  return (
    <section
      className="my-3 w-full min-w-0 max-w-full overflow-hidden rounded-xl border border-amber-200 bg-[#fffdf8] shadow-sm"
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
            disabled={exporting || showChart === null}
            className="inline-flex shrink-0 items-center gap-1 rounded-lg bg-white/15 px-2 py-1.5 text-[10px] font-semibold transition hover:bg-white/25 disabled:opacity-60 sm:text-xs"
            aria-label="دانلود تصویر جدول"
          >
            <Download className="size-3.5" />
            {exporting ? "در حال ساخت" : showChart === null ? "آماده‌سازی" : "دانلود تصویر"}
          </button>
        </div>
      </header>
      {(showChart === true || model.templateId === "BankingIncomeStatement") && <QuarterlyFinancialChart model={model} />}
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
                aria-label={quarterlyPeriodHeaderLines(quarter).join(" ")}
                className="border border-[#b8d9d5] px-0.5 py-1.5 text-center font-semibold leading-tight"
              >
                {quarterlyPeriodHeaderLines(quarter).map((line, lineIndex) => (
                  <span
                    key={`${quarter.sourceIndex}-${lineIndex}`}
                    className={`block whitespace-normal break-words ${lineIndex === 0 ? "text-[9px] font-bold sm:text-[11px]" : lineIndex === 1 ? "text-[7px] font-medium sm:text-[9px]" : "text-[8px] font-semibold sm:text-[10px]"}`}
                  >
                    {line}
                  </span>
                ))}
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
              className={row.grossMargin || row.bankingMargin ? "text-[11px] font-bold sm:text-[13px]" : ""}
            >
              <th
                className={`border border-amber-200 px-1 py-1.5 text-right ${row.grossMargin || row.bankingMargin ? "bg-yellow-200 font-extrabold text-amber-950" : "bg-[#ffedd5] font-semibold text-slate-800"}`}
              >
                {row.label}
              </th>
              {row.values.slice(0, model.quarters.length).map((value, index) => (
                <td
                  key={index}
                  className={`border border-slate-200 px-0.5 py-1.5 text-center tabular-nums ${row.grossMargin || row.bankingMargin ? "bg-yellow-200 font-bold text-amber-950" : value.startsWith("(") ? "bg-rose-100 text-rose-900" : "bg-white text-slate-800"}`}
                >
                  {(row.grossMargin || row.bankingMargin) && value !== "-" && value !== "—"
                    ? `${value}${String.fromCharCode(0x066a)}`
                    : value}
                </td>
              ))}
              <td
                className={`border border-amber-200 px-0.5 py-1.5 text-center font-bold tabular-nums ${row.grossMargin || row.bankingMargin ? grossMarginChangeClass(resolveQuarterlyChangeColors(row.sourceItemId, row.qoqChange ?? numericValue(row.values.at(-1) ?? "-")).tone) : resolveQuarterlyChangeColors(row.sourceItemId, row.qoqChange ?? numericValue(row.values.at(-1) ?? "-")).className}`}
              >
                {row.values.at(-1)}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {model.provenanceNotes?.length ? (
        <details className="border-t border-amber-200 px-2 py-2 text-[10px] leading-5 text-slate-700 sm:text-xs">
          <summary className="cursor-pointer font-semibold text-teal-900">{model.templateId === "BankingIncomeStatement" ? "توضیحات گزارش و محاسبه" : "روش و مبنای EPS مقایسه‌ای"}</summary>
          <ul className="mt-1 list-disc space-y-1 pr-5">
            {model.provenanceNotes.map((note, index) => <li key={index}>{note}</li>)}
          </ul>
        </details>
      ) : null}
    </section>
  );
}
