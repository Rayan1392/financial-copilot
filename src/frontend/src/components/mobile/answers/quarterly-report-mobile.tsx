import { useId, useState } from "react";
import { ChevronDown, Download } from "lucide-react";
import type {
  QuarterlyFinancialTableModel,
  QuarterlyFinancialTableRow,
} from "@/components/app/quarterly-financial-table";
import { QUARTERLY_CHANGE_COLUMN_HEADER } from "@/components/app/quarterly-financial-table-constants";
import { resolveQuarterlyChangeColors } from "@/components/app/quarterly-financial-table-colors";
import { quarterlyPeriodHeaderLines } from "@/components/app/quarterly-financial-table-header";
import { cn } from "@/lib/utils";
import {
  barWidthsPercent,
  formatQuarterlyCell,
  isMissingQuarterlyCell,
  periodsNewestFirst,
  shortPeriodLabel,
} from "./quarterly-report-mobile-model";
import { QuarterlyTrendChartMobile } from "./quarterly-trend-chart-mobile";

interface Props {
  model: QuarterlyFinancialTableModel;
  /** Numeric QoQ change per row (same order as `model.rows`), used only for the badge tone and glyph. */
  changes: Array<number | null>;
  showChart: boolean;
  chartConfigLoading: boolean;
  exporting: boolean;
  onDownload: () => void;
}

type View = "cards" | "table";

const TONE_CLASS = {
  positive: "bg-emerald-soft text-emerald",
  negative: "bg-rose/10 mobile-danger-text",
  neutral: "bg-surface-2 text-muted-foreground",
} as const;

function periodText(model: QuarterlyFinancialTableModel, index: number): string {
  const quarter = model.quarters[index];
  return shortPeriodLabel(quarter, quarterlyPeriodHeaderLines(quarter));
}

/** Full period definitions, once: short card label → exact end date. */
function PeriodList({ model, order }: { model: QuarterlyFinancialTableModel; order: number[] }) {
  return (
    <dl
      aria-label="دوره‌های گزارش"
      className="space-y-0.5 rounded-xl bg-surface px-3 py-2 text-[13px] leading-6 ring-1 ring-hairline"
    >
      {order.map((index) => {
        const lines = quarterlyPeriodHeaderLines(model.quarters[index]);
        const short = periodText(model, index);
        // Typed results give [quarter, «منتهی به», end date]; other labels are shown whole.
        const detail = lines.length === 3 ? `${lines[1]} ${lines[2]}` : lines.join(" ");
        return (
          <div key={index} className="flex flex-wrap justify-between gap-x-3">
            <dt className="font-medium text-foreground">{short}</dt>
            {detail !== short && <dd className="text-muted-foreground">{detail}</dd>}
          </div>
        );
      })}
    </dl>
  );
}

/**
 * Quarterly report for phones (Feature 153). The desktop table squeezes every period into 320 px at 7–9 px text;
 * here each line item is a card showing the latest period, its change from the previous quarter, and the previous
 * period, and it expands to every period with proportional bars. A pinned-column table is one tap away for
 * comparing many items side by side; only that table scrolls horizontally, inside its own region.
 */
export function QuarterlyReportMobile({
  model,
  changes,
  showChart,
  chartConfigLoading,
  exporting,
  onDownload,
}: Props) {
  const [view, setView] = useState<View>("cards");
  const titleId = useId();
  const order = periodsNewestFirst(model.quarters.length);

  return (
    <section
      dir="rtl"
      aria-labelledby={titleId}
      className="space-y-3 rounded-2xl bg-surface/40 p-3 ring-1 ring-hairline"
    >
      <header className="space-y-2">
        <h3
          id={titleId}
          className="break-words text-[15px] font-semibold leading-7 text-foreground"
        >
          {model.title}
        </h3>
        <p className="text-[13px] leading-6 text-muted-foreground">{model.summary}</p>
        <div className="flex flex-wrap items-center gap-2">
          <div
            role="group"
            aria-label="نحوه نمایش گزارش"
            className="inline-flex rounded-xl bg-surface-2 p-1"
          >
            {(
              [
                ["cards", "کارت‌ها"],
                ["table", "جدول"],
              ] as const
            ).map(([value, label]) => (
              <button
                key={value}
                type="button"
                aria-pressed={view === value}
                onClick={() => setView(value)}
                className={cn(
                  "min-h-11 min-w-[4.5rem] rounded-lg px-3 text-sm font-medium focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40",
                  view === value
                    ? "bg-background text-foreground shadow-sm"
                    : "text-muted-foreground",
                )}
              >
                {label}
              </button>
            ))}
          </div>
          <button
            type="button"
            onClick={onDownload}
            disabled={exporting || chartConfigLoading}
            aria-label="دانلود تصویر جدول"
            className="ms-auto inline-flex min-h-11 items-center gap-1.5 rounded-xl px-3 text-sm text-muted-foreground ring-1 ring-hairline active:bg-surface-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40 disabled:opacity-60"
          >
            <Download className="size-4" aria-hidden="true" />
            {exporting ? "در حال ساخت" : chartConfigLoading ? "آماده‌سازی" : "دانلود تصویر"}
          </button>
        </div>
      </header>

      {showChart && (
        <details className="group overflow-hidden rounded-xl ring-1 ring-hairline">
          <summary className="flex min-h-11 cursor-pointer list-none items-center justify-between gap-2 bg-surface px-3 text-sm font-medium text-foreground">
            نمودار روند
            <ChevronDown
              className="size-4 transition-transform group-open:rotate-180"
              aria-hidden="true"
            />
          </summary>
          <QuarterlyTrendChartMobile model={model} />
        </details>
      )}

      {view === "cards" && <PeriodList model={model} order={order} />}

      {view === "cards" ? (
        <ul className="space-y-2.5" aria-label="اقلام گزارش فصلی">
          {model.rows.map((row, rowIndex) => (
            <MetricCard
              key={`${row.rowId ?? row.label}-${rowIndex}`}
              model={model}
              row={row}
              change={changes[rowIndex] ?? null}
              order={order}
            />
          ))}
        </ul>
      ) : (
        <ComparisonTable model={model} changes={changes} order={order} />
      )}

      {model.provenanceNotes?.length ? (
        <details className="rounded-xl bg-surface p-3 text-[13px] leading-6 text-muted-foreground ring-1 ring-hairline">
          <summary className="min-h-6 cursor-pointer font-semibold text-foreground">
            {model.templateId === "BankingIncomeStatement"
              ? "توضیحات گزارش و محاسبه"
              : "روش و مبنای EPS مقایسه‌ای"}
          </summary>
          <ul className="mt-2 list-disc space-y-1 pr-5">
            {model.provenanceNotes.map((note, index) => (
              <li key={index}>{note}</li>
            ))}
          </ul>
        </details>
      ) : null}
    </section>
  );
}

function ChangeBadge({
  row,
  change,
  label,
}: {
  row: QuarterlyFinancialTableRow;
  change: number | null;
  label: string;
}) {
  const text = row.values.at(-1) ?? "-";
  const tone = resolveQuarterlyChangeColors(row.sourceItemId, change).tone;
  const glyph =
    change == null || change === 0 || text.trim() === "" || text === "-"
      ? ""
      : change > 0
        ? "▲"
        : "▼";
  const shown = text.trim() === "" ? "–" : text;
  return (
    <span
      className={cn(
        "inline-flex shrink-0 items-center gap-1 rounded-full px-2 py-0.5 text-[13px] font-semibold tabular-nums",
        TONE_CLASS[tone],
      )}
    >
      {/* A plain span cannot carry aria-label, so screen readers get the full phrase from hidden text. */}
      <span className="sr-only">
        {label}: {shown === "–" || shown === "-" ? "قابل محاسبه نیست" : shown}
      </span>
      {glyph && <span aria-hidden="true">{glyph}</span>}
      <bdi aria-hidden="true">{shown}</bdi>
    </span>
  );
}

function CellValue({
  row,
  value,
  className,
}: {
  row: QuarterlyFinancialTableRow;
  value: string;
  className?: string;
}) {
  const missing = isMissingQuarterlyCell(value);
  return (
    <span className={cn("tabular-nums", missing && "text-muted-foreground", className)}>
      <bdi>{formatQuarterlyCell(row, value)}</bdi>
      {missing && <span className="sr-only">ناموجود</span>}
    </span>
  );
}

function MetricCard({
  model,
  row,
  change,
  order,
}: {
  model: QuarterlyFinancialTableModel;
  row: QuarterlyFinancialTableRow;
  change: number | null;
  order: number[];
}) {
  const [expanded, setExpanded] = useState(false);
  const listId = useId();
  const [latest, previous] = order;
  const margin = row.grossMargin || row.bankingMargin;
  const widths = barWidthsPercent(row.chartValues ?? []);

  return (
    <li
      className={cn("rounded-xl bg-surface p-3 ring-1", margin ? "ring-gold/50" : "ring-hairline")}
    >
      <div className="flex items-start justify-between gap-3">
        <h4
          className={cn(
            "min-w-0 text-sm leading-6 text-foreground",
            margin ? "font-bold" : "font-semibold",
          )}
        >
          {row.label}
        </h4>
        {order.length > 1 && (
          <ChangeBadge row={row} change={change} label={QUARTERLY_CHANGE_COLUMN_HEADER} />
        )}
      </div>

      <dl className="mt-2 space-y-1">
        <div className="flex items-baseline justify-between gap-3">
          <dt className="min-w-0 text-[13px] leading-6 text-muted-foreground">
            {periodText(model, latest)}
          </dt>
          <dd className="shrink-0 text-base font-semibold text-foreground">
            <CellValue row={row} value={row.values[latest]} />
          </dd>
        </div>
        {previous !== undefined && (
          <div className="flex items-baseline justify-between gap-3">
            <dt className="min-w-0 text-[13px] leading-6 text-muted-foreground">
              {periodText(model, previous)}
            </dt>
            <dd className="shrink-0 text-sm text-foreground">
              <CellValue row={row} value={row.values[previous]} />
            </dd>
          </div>
        )}
      </dl>

      {order.length > 2 && (
        <>
          <button
            type="button"
            aria-expanded={expanded}
            aria-controls={listId}
            onClick={() => setExpanded((value) => !value)}
            className="mt-1 inline-flex min-h-11 items-center gap-1 rounded-lg text-[13px] font-medium text-emerald focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40"
          >
            {expanded ? "بستن" : `همه فصل‌ها (${order.length.toLocaleString("fa-IR")})`}
            <ChevronDown
              className={cn("size-4 transition-transform", expanded && "rotate-180")}
              aria-hidden="true"
            />
          </button>
          {expanded && (
            <ol id={listId} className="space-y-2 border-t border-hairline pt-2">
              {order.map((index) => {
                const numeric = row.chartValues?.[index] ?? null;
                return (
                  <li key={index} className="space-y-1">
                    <div className="flex items-baseline justify-between gap-3 text-[13px] leading-6">
                      <span className="min-w-0 text-muted-foreground">
                        {periodText(model, index)}
                      </span>
                      <CellValue
                        row={row}
                        value={row.values[index]}
                        className="shrink-0 font-medium text-foreground"
                      />
                    </div>
                    <div
                      aria-hidden="true"
                      className="h-1.5 w-full overflow-hidden rounded-full bg-surface-2"
                    >
                      <div
                        className={cn(
                          "h-full rounded-full",
                          numeric !== null && numeric < 0
                            ? "bg-rose"
                            : margin
                              ? "bg-gold"
                              : "bg-emerald/70",
                        )}
                        style={{ width: `${widths[index] ?? 0}%` }}
                      />
                    </div>
                  </li>
                );
              })}
            </ol>
          )}
        </>
      )}
    </li>
  );
}

function ComparisonTable({
  model,
  changes,
  order,
}: {
  model: QuarterlyFinancialTableModel;
  changes: Array<number | null>;
  order: number[];
}) {
  return (
    <div className="space-y-1.5">
      <p className="text-xs text-muted-foreground" aria-hidden="true">
        ستون شرح ثابت است؛ برای دیدن همه فصل‌ها جدول را افقی بکشید.
      </p>
      <div
        role="region"
        aria-label="جدول کامل گزارش فصلی؛ برای دیدن همه فصل‌ها افقی بکشید"
        tabIndex={0}
        className="overflow-x-auto overscroll-x-contain rounded-xl ring-1 ring-hairline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40"
      >
        <table className="min-w-max border-separate border-spacing-0 text-[13px] leading-6">
          <thead>
            <tr className="bg-surface-2 text-foreground">
              <th
                scope="col"
                className="sticky right-0 z-10 w-32 min-w-32 border-b border-l border-hairline bg-surface-2 px-2 py-2 text-right font-semibold"
              >
                شرح
              </th>
              <th
                scope="col"
                className="w-24 min-w-24 border-b border-hairline px-2 py-2 text-center font-semibold"
              >
                {QUARTERLY_CHANGE_COLUMN_HEADER}
              </th>
              {order.map((index) => (
                <th
                  key={index}
                  scope="col"
                  className="w-28 min-w-28 border-b border-hairline px-2 py-2 text-center font-semibold"
                >
                  {quarterlyPeriodHeaderLines(model.quarters[index]).map((line, lineIndex) => (
                    <span
                      key={lineIndex}
                      className={cn(
                        "block",
                        lineIndex === 1 && "font-normal text-muted-foreground",
                      )}
                    >
                      {line}
                    </span>
                  ))}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {model.rows.map((row, rowIndex) => {
              const margin = row.grossMargin || row.bankingMargin;
              return (
                <tr
                  key={`${row.rowId ?? row.label}-${rowIndex}`}
                  className={margin ? "bg-gold/10" : undefined}
                >
                  <th
                    scope="row"
                    className={cn(
                      "sticky right-0 z-10 w-32 min-w-32 border-b border-l border-hairline px-2 py-2 text-right align-top leading-6",
                      margin ? "bg-surface font-bold" : "bg-surface font-medium",
                    )}
                  >
                    {row.label}
                  </th>
                  <td className="border-b border-hairline px-2 py-2 text-center align-top">
                    {model.quarters.length > 1 ? (
                      <ChangeBadge
                        row={row}
                        change={changes[rowIndex] ?? null}
                        label={QUARTERLY_CHANGE_COLUMN_HEADER}
                      />
                    ) : (
                      "–"
                    )}
                  </td>
                  {order.map((index) => (
                    <td
                      key={index}
                      className="border-b border-hairline px-2 py-2 text-center align-top text-foreground"
                    >
                      <CellValue row={row} value={row.values[index]} />
                    </td>
                  ))}
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}
