import { Download } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { useAnswerDensity } from "../answer-density";
import { BalanceSheetFundingChart } from "./balance-sheet-funding-chart";
import { BalanceSheetReportCompact } from "./balance-sheet-report-compact";
import { downloadBalanceSheetReportImage } from "./balance-sheet-report-image";
import {
  BALANCE_SHEET_COMPARISON_TWO_SIDED_MIN_WIDTH,
  BALANCE_SHEET_WIDE_MIN_WIDTH,
  BASELINE_REASON_TEXT,
  baselineLabel,
  buildBalanceSheetView,
} from "./balance-sheet-report-model";
import type { BalanceSheetReportResult } from "./balance-sheet-report-types";
import { BalanceSheetReportWide } from "./balance-sheet-report-wide";

export type BalanceSheetLayout =
  | "compact"
  | "wide"
  | "wide-comparison-two-sided"
  | "wide-comparison-stacked";

/** Layout choice by density and container width (design §7). */
export function chooseBalanceSheetLayout(
  compact: boolean,
  containerWidth: number,
  comparison: boolean,
): BalanceSheetLayout {
  if (compact || containerWidth < BALANCE_SHEET_WIDE_MIN_WIDTH) return "compact";
  if (!comparison) return "wide";
  return containerWidth >= BALANCE_SHEET_COMPARISON_TWO_SIDED_MIN_WIDTH
    ? "wide-comparison-two-sided"
    : "wide-comparison-stacked";
}

function useContainerWidth() {
  const ref = useRef<HTMLElement | null>(null);
  const [width, setWidth] = useState<number>(() =>
    typeof window === "undefined" ? 1280 : window.innerWidth,
  );
  useEffect(() => {
    const element = ref.current;
    if (!element) return;
    const measure = () => {
      if (element.clientWidth > 0) setWidth(element.clientWidth);
    };
    measure();
    if (typeof ResizeObserver === "undefined") return;
    const observer = new ResizeObserver(measure);
    observer.observe(element);
    return () => observer.disconnect();
  }, []);
  return { ref, width };
}

export function BalanceSheetBaselineSelector({
  result,
  selected,
  onSelect,
}: {
  result: BalanceSheetReportResult;
  selected: string;
  onSelect: (kind: string) => void;
}) {
  const candidates = result.comparison?.candidates ?? [];
  return (
    <div
      className="flex flex-wrap gap-1.5"
      role="radiogroup"
      aria-label="مبنای مقایسه"
      data-testid="balance-sheet-baseline-selector"
    >
      {candidates.map((candidate) => {
        const unavailable = candidate.state !== "available";
        const reason = unavailable
          ? (BASELINE_REASON_TEXT[candidate.reason ?? ""] ?? candidate.reason ?? "")
          : "";
        return (
          <button
            key={candidate.kind}
            type="button"
            role="radio"
            aria-checked={selected === candidate.kind}
            disabled={unavailable && selected !== candidate.kind}
            title={unavailable ? `در دسترس نیست: ${reason}` : undefined}
            onClick={() => onSelect(candidate.kind)}
            className={`min-h-9 rounded-lg border px-2.5 py-1 text-[11px] leading-4 transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-teal-600 ${
              selected === candidate.kind
                ? "border-[#164e63] bg-[#164e63] text-white"
                : "border-slate-300 bg-white text-slate-800 hover:bg-slate-50"
            } ${unavailable ? "opacity-60" : ""}`}
          >
            {baselineLabel(candidate)}
            {unavailable && (
              <span className="block text-[10px]">در دسترس نیست{reason ? `: ${reason}` : ""}</span>
            )}
          </button>
        );
      })}
    </div>
  );
}

/**
 * Feature 154 report card. «مقایسه» and the baseline selector only switch local view state over the standard
 * baselines already loaded in the response: no AI request and no Billing operation (PD-24, AC-C01).
 */
export function BalanceSheetReport({ result }: { result: BalanceSheetReportResult }) {
  const compact = useAnswerDensity() === "compact";
  const { ref, width } = useContainerWidth();
  const initialKind = result.comparison?.selectedBaselineKind ?? "fiscal_year_end";
  const [comparisonOpen, setComparisonOpen] = useState(result.requestedView === "comparison");
  const [kind, setKind] = useState(initialKind);
  const [exporting, setExporting] = useState(false);
  const comparisonKind = comparisonOpen ? kind : null;
  const view = buildBalanceSheetView(result, comparisonKind);

  if (!view) {
    return (
      <section
        className="my-3 rounded-xl border border-amber-200 bg-[#fffdf8] p-3 text-sm text-slate-800"
        dir="rtl"
        data-testid="balance-sheet-outcome"
      >
        <h3 className="mb-1 font-bold text-[#164e63]">{result.title}</h3>
        <p className="leading-6">{result.outcomeMessage ?? "گزارش ترازنامه در دسترس نیست."}</p>
      </section>
    );
  }

  const layout = chooseBalanceSheetLayout(compact, width, comparisonOpen);
  const download = async () => {
    setExporting(true);
    try {
      await downloadBalanceSheetReportImage(result, comparisonKind);
    } finally {
      setExporting(false);
    }
  };
  const chart = <BalanceSheetFundingChart result={result} compact={layout === "compact"} />;
  return (
    <section
      ref={ref}
      className="my-3 w-full min-w-0 max-w-full overflow-hidden rounded-xl border border-amber-200 bg-[#fffdf8] text-slate-900 shadow-sm"
      dir="rtl"
      data-testid="balance-sheet-report"
      data-layout={layout}
      data-template={view.templateId}
    >
      <header className="bg-gradient-to-l from-[#164e63] via-[#155e75] to-[#0f766e] px-3 py-3 text-white sm:px-4">
        <div className="flex flex-wrap items-start justify-between gap-2">
          <div className="min-w-0">
            <h3 className="text-sm font-bold sm:text-base">
              {view.title} — {view.companyLine}
            </h3>
            <p className="mt-1 text-[10px] leading-5 text-cyan-50 sm:text-xs">
              {view.metadata.join(" | ")}
            </p>
          </div>
          <div className="flex shrink-0 gap-1.5">
            <button
              type="button"
              onClick={() => setComparisonOpen((value) => !value)}
              aria-pressed={comparisonOpen}
              className="inline-flex min-h-8 items-center rounded-lg bg-white/15 px-2.5 py-1.5 text-[11px] font-semibold transition hover:bg-white/25 sm:text-xs"
            >
              {comparisonOpen ? "بازگشت به گزارش" : "مقایسه"}
            </button>
            <button
              type="button"
              onClick={() => void download()}
              disabled={exporting}
              className="inline-flex min-h-8 items-center gap-1 rounded-lg bg-white/15 px-2 py-1.5 text-[11px] font-semibold transition hover:bg-white/25 disabled:opacity-60 sm:text-xs"
              aria-label="دانلود تصویر ترازنامه"
            >
              <Download className="size-3.5" />
              {exporting ? "در حال ساخت" : "دانلود تصویر"}
            </button>
          </div>
        </div>
      </header>
      {layout !== "compact" && chart}
      {comparisonOpen && (
        <div
          className="space-y-2 border-b border-amber-200 bg-white px-3 py-2"
          data-testid="balance-sheet-baseline"
        >
          <BalanceSheetBaselineSelector result={result} selected={kind} onSelect={setKind} />
          <p className="text-[12px] font-semibold text-[#164e63]">
            مبنای مقایسه: {view.comparison?.label ?? "—"}
          </p>
          {view.comparison?.unavailableText && (
            <p className="text-[12px] text-amber-900" role="status">
              {view.comparison.unavailableText}
              {view.comparison.alternatives.length > 0 &&
                ` مبناهای در دسترس: ${view.comparison.alternatives.join("، ")}.`}
            </p>
          )}
        </div>
      )}
      {layout === "compact" ? (
        <BalanceSheetReportCompact view={view} comparison={comparisonOpen} />
      ) : (
        <BalanceSheetReportWide
          view={view}
          comparison={comparisonOpen}
          twoSided={layout !== "wide-comparison-stacked"}
        />
      )}
      {view.notices.length > 0 && (
        <ul
          className="space-y-1 border-t border-amber-200 bg-amber-50 px-3 py-2 text-[12px] text-amber-950"
          data-testid="balance-sheet-notices"
        >
          {view.notices.map((notice) => (
            <li key={notice}>⚠ {notice}</li>
          ))}
        </ul>
      )}
      {view.analytical.length > 0 && (
        <div
          className="flex flex-wrap gap-x-4 gap-y-1 border-t border-amber-200 bg-white px-3 py-2 text-[12px] text-slate-700"
          data-testid="balance-sheet-analytical"
        >
          {view.analytical.map((row) => (
            <span key={row.key}>
              {row.label}:{" "}
              <span dir="ltr" className="whitespace-nowrap tabular-nums">
                {row.value}
              </span>
            </span>
          ))}
        </div>
      )}
      {layout === "compact" && chart}
    </section>
  );
}
