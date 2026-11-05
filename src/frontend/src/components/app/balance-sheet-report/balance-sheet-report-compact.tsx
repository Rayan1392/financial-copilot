import { ChevronDown } from "lucide-react";
import { useId, useState } from "react";
import {
  BALANCE_SHEET_CHANGE_HEADER,
  BALANCE_SHEET_RATIO_LABEL,
  statementBadgeText,
  type BalanceSheetView,
  type BalanceSheetViewRow,
  type BalanceSheetViewSide,
} from "./balance-sheet-report-model";

function Value({ row }: { row: BalanceSheetViewRow }) {
  return (
    <span
      className={`whitespace-nowrap tabular-nums ${row.negative ? "rounded bg-rose-100 px-1 text-rose-900" : ""}`}
      dir="ltr"
      aria-label={row.missing ? "داده موجود نیست" : undefined}
    >
      {row.value}
      {row.negative && <span className="sr-only"> منفی</span>}
    </span>
  );
}

function Item({
  row,
  view,
  comparison,
}: {
  row: BalanceSheetViewRow;
  view: BalanceSheetView;
  comparison: boolean;
}) {
  if (row.heading)
    return (
      <li className="bg-[#d9efed] px-3 py-1.5 text-[12px] font-bold text-[#164e63]">{row.label}</li>
    );
  const strong = row.kind === "subtotal" || row.kind === "total";
  return (
    <li
      className={`border-b border-amber-100 px-3 py-2 ${strong ? "bg-amber-50 font-semibold" : ""}`}
      data-row-id={row.rowId}
    >
      <div className="flex items-start justify-between gap-3">
        <span className={`min-w-0 break-words text-[13px] leading-5 ${row.indent ? "ps-3" : ""}`}>
          {row.label}
        </span>
        <Value row={row} />
      </div>
      {comparison && (
        <p className="mt-0.5 text-[11px] leading-5 text-slate-600">
          مانده در {view.comparison?.baseline?.date ?? "—"}:{" "}
          <span dir="ltr" className="whitespace-nowrap tabular-nums">
            {row.baselineValue}
          </span>
          {" · "}
          {BALANCE_SHEET_CHANGE_HEADER}:{" "}
          <span dir="ltr" className="whitespace-nowrap tabular-nums">
            {row.percent}
          </span>
          <span className="sr-only">
            ؛ تغییر مبلغ: {row.amountChange} میلیون ریال
            {row.changeNote ? ` — ${row.changeNote}` : ""}
          </span>
        </p>
      )}
    </li>
  );
}

function Section({
  side,
  total,
  view,
  comparison,
}: {
  side: BalanceSheetViewSide;
  total: BalanceSheetViewRow;
  view: BalanceSheetView;
  comparison: boolean;
}) {
  const [open, setOpen] = useState(false);
  const id = useId();
  const rows = side.total ? side.rows : side.rows;
  return (
    <div className="border-b border-amber-200" data-section={side.sectionId}>
      <button
        type="button"
        aria-expanded={open}
        aria-controls={id}
        onClick={() => setOpen((value) => !value)}
        className="flex min-h-11 w-full items-center justify-between gap-3 bg-[#164e63] px-3 py-2 text-right text-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-amber-300"
      >
        <span className="flex min-w-0 items-center gap-2">
          <ChevronDown
            aria-hidden="true"
            className={`size-4 shrink-0 transition-transform ${open ? "rotate-180" : ""}`}
          />
          <span className="min-w-0 break-words text-[13px] font-bold">{side.label}</span>
        </span>
        <span className="flex flex-col items-end">
          <span className="whitespace-nowrap text-[13px] font-bold tabular-nums" dir="ltr">
            {total.value}
          </span>
          {comparison && (
            <span className="whitespace-nowrap text-[10px] text-cyan-100" dir="ltr">
              {total.percent}
            </span>
          )}
        </span>
      </button>
      <ul id={id} hidden={!open} className="bg-white">
        <li className="px-3 py-1 text-[10px] text-slate-500">{side.caption}</li>
        {rows.map((row) => (
          <Item key={row.key} row={row} view={view} comparison={comparison} />
        ))}
        <Item row={total} view={view} comparison={comparison} />
      </ul>
    </div>
  );
}

/** Mobile layout (spec §9.3/§9.4): «شرح | مانده», collapsible sections; total L&E and the ratio always visible. */
export function BalanceSheetReportCompact({
  view,
  comparison,
}: {
  view: BalanceSheetView;
  comparison: boolean;
}) {
  return (
    <div className="text-slate-900" data-layout="compact">
      <div className="flex justify-between bg-[#d9efed] px-3 py-1 text-[11px] font-bold text-[#164e63]">
        <span>شرح</span>
        <span className="text-left">
          {comparison ? `مانده در ${view.anchor.date}` : "مانده"}
          {comparison && (
            <span
              className="block text-[10px] font-normal text-slate-600"
              data-testid="balance-sheet-anchor-badge"
            >
              {statementBadgeText(view.anchor)}
            </span>
          )}
        </span>
      </div>
      {comparison && view.comparison?.baseline && (
        // Each dated balance carries its own status (spec §8.3 item 1, §8.5): the baseline line that
        // the items' secondary «مانده در [تاریخ مبنا]» refers to.
        <p
          className="flex justify-between gap-3 border-b border-amber-100 bg-[#f1f8f7] px-3 py-1 text-[11px] text-[#164e63]"
          data-testid="balance-sheet-baseline-badge"
        >
          <span className="font-bold">مانده در {view.comparison.baseline.date} (مبنا)</span>
          <span className="text-slate-600">{statementBadgeText(view.comparison.baseline)}</span>
        </p>
      )}
      <Section side={view.assets} total={view.finalAssets} view={view} comparison={comparison} />
      <Section
        side={view.liabilities}
        total={view.liabilities.total ?? view.finalLiabilitiesAndEquity}
        view={view}
        comparison={comparison}
      />
      <Section
        side={view.equity}
        total={view.equity.total ?? view.finalLiabilitiesAndEquity}
        view={view}
        comparison={comparison}
      />
      <ul>
        <Item
          row={{ ...view.finalLiabilitiesAndEquity, kind: "total" }}
          view={view}
          comparison={comparison}
        />
      </ul>
      <div
        className="bg-yellow-200 px-3 py-2 font-bold text-amber-950"
        data-testid="balance-sheet-ratio-row"
      >
        <p className="text-[13px] font-extrabold leading-5">{BALANCE_SHEET_RATIO_LABEL}</p>
        <p className="mt-1 flex flex-wrap gap-x-3 text-[13px] tabular-nums">
          <span>
            {comparison ? `${view.anchor.date}: ` : ""}
            <span dir="ltr">{view.ratio.anchor}</span>
          </span>
          {comparison && (
            <span>
              {view.comparison?.baseline?.date ?? "—"}: <span dir="ltr">{view.ratio.baseline}</span>
            </span>
          )}
          {comparison && (
            <span>
              تغییر: <span dir="rtl">{view.ratio.pp}</span>
            </span>
          )}
        </p>
      </div>
    </div>
  );
}
