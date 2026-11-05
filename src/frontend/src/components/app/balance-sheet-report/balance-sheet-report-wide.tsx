import { useId, useState } from "react";
import {
  BALANCE_SHEET_CHANGE_HEADER,
  BALANCE_SHEET_RATIO_LABEL,
  statementBadgeText,
  type BalanceSheetStatementBadge,
  type BalanceSheetView,
  type BalanceSheetViewRow,
  type BalanceSheetViewSide,
} from "./balance-sheet-report-model";

const RATIO_ROW = "bg-yellow-200 font-bold text-amber-950";
const NEGATIVE = "bg-rose-100 text-rose-900";

function Amount({
  value,
  negative,
  missing,
}: {
  value: string;
  negative?: boolean;
  missing?: boolean;
}) {
  return (
    <span
      className="whitespace-nowrap tabular-nums"
      dir="ltr"
      aria-label={missing ? "داده موجود نیست" : undefined}
    >
      {value}
      {negative && <span className="sr-only"> منفی</span>}
    </span>
  );
}

function Direction({ direction }: { direction?: BalanceSheetViewRow["direction"] }) {
  // One neutral hue: direction never encodes favorability (spec §8.4).
  if (!direction || direction === "flat") return null;
  return (
    <span aria-hidden="true" className="ms-1 text-[10px] text-slate-500">
      {direction === "up" ? "▲" : "▼"}
    </span>
  );
}

function ChangeCell({ row }: { row: BalanceSheetViewRow }) {
  const [open, setOpen] = useState(false);
  const id = useId();
  return (
    <td className="relative whitespace-nowrap px-2 py-1.5 text-left tabular-nums text-slate-800">
      <button
        type="button"
        className="inline-flex items-center rounded px-1 hover:bg-slate-100 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-teal-600"
        aria-expanded={open}
        aria-controls={id}
        aria-label={`${BALANCE_SHEET_CHANGE_HEADER} ${row.label}: ${row.percent}؛ تغییر مبلغ: ${row.amountChange} میلیون ریال`}
        title={`تغییر مبلغ: ${row.amountChange} میلیون ریال${row.changeNote ? ` — ${row.changeNote}` : ""}`}
        onClick={() => setOpen((value) => !value)}
        onBlur={() => setOpen(false)}
      >
        <span dir="ltr">{row.percent}</span>
        <Direction direction={row.direction} />
      </button>
      <span
        id={id}
        role="status"
        className={
          open
            ? "absolute left-1 top-full z-10 mt-1 rounded-md border border-slate-200 bg-white px-2 py-1 text-[11px] text-slate-800 shadow"
            : "sr-only"
        }
      >
        تغییر مبلغ: <span dir="ltr">{row.amountChange}</span> میلیون ریال
        {row.changeNote ? ` — ${row.changeNote}` : ""}
      </span>
    </td>
  );
}

function rowClass(row: BalanceSheetViewRow): string {
  if (row.heading) return "bg-[#d9efed] font-bold text-[#164e63]";
  if (row.kind === "subtotal" || row.kind === "total") return "bg-amber-50 font-semibold";
  return "";
}

function SideRows({ rows, comparison }: { rows: BalanceSheetViewRow[]; comparison: boolean }) {
  return (
    <>
      {rows.map((row) => (
        <tr
          key={row.key}
          className={`border-b border-amber-100 ${rowClass(row)}`}
          data-row-id={row.rowId}
        >
          <th
            scope="row"
            className={`px-2 py-1.5 text-right font-[inherit] leading-5 ${row.indent ? "ps-5" : ""}`}
          >
            {row.label}
          </th>
          {row.heading ? (
            <td colSpan={comparison ? 3 : 1} />
          ) : (
            <>
              <td className={`px-2 py-1.5 text-left ${row.negative ? NEGATIVE : ""}`}>
                <Amount value={row.value} negative={row.negative} missing={row.missing} />
              </td>
              {comparison && (
                <>
                  <td className={`px-2 py-1.5 text-left ${row.baselineNegative ? NEGATIVE : ""}`}>
                    <Amount value={row.baselineValue ?? "—"} negative={row.baselineNegative} />
                  </td>
                  <ChangeCell row={row} />
                </>
              )}
            </>
          )}
        </tr>
      ))}
    </>
  );
}

function SideTable({
  side,
  view,
  comparison,
  extraRows = [],
}: {
  side: BalanceSheetViewSide;
  view: BalanceSheetView;
  comparison: boolean;
  extraRows?: BalanceSheetViewRow[];
}) {
  const rows = [...side.rows, ...(side.total ? [side.total] : []), ...extraRows];
  return (
    <table
      className="w-full table-fixed border-collapse text-[12px] text-slate-900"
      data-section={side.sectionId}
    >
      <caption className="bg-[#164e63] px-2 py-1.5 text-right text-xs font-bold text-white">
        {side.label} <span className="font-normal text-cyan-100">({side.caption})</span>
      </caption>
      <colgroup>
        <col className={comparison ? "w-[40%]" : "w-[62%]"} />
        <col />
        {comparison && (
          <>
            <col />
            <col className="w-[16%]" />
          </>
        )}
      </colgroup>
      <thead>
        <tr className="bg-[#d9efed] text-[11px] text-[#164e63]">
          <th scope="col" className="px-2 py-1 text-right">
            شرح
          </th>
          <th scope="col" className="px-2 py-1 text-left">
            {comparison ? (
              <>
                مانده در {view.anchor.date}
                <Badges statement={view.anchor} />
              </>
            ) : (
              "مبلغ"
            )}
          </th>
          {comparison && (
            <>
              <th scope="col" className="px-2 py-1 text-left">
                مانده در {view.comparison?.baseline?.date ?? "—"}
                {view.comparison?.baseline && <Badges statement={view.comparison.baseline} />}
              </th>
              <th scope="col" className="px-2 py-1 text-left">
                {BALANCE_SHEET_CHANGE_HEADER}
              </th>
            </>
          )}
        </tr>
      </thead>
      <tbody>
        <SideRows rows={rows} comparison={comparison} />
      </tbody>
    </table>
  );
}

function Badges({ statement }: { statement: BalanceSheetStatementBadge }) {
  return (
    <span className="mt-0.5 block text-[10px] font-normal text-slate-600">
      {statementBadgeText(statement)}
    </span>
  );
}

function FinalTotal({ row, comparison }: { row: BalanceSheetViewRow; comparison: boolean }) {
  // Explicit ink: the card never inherits the page foreground (dark theme) on its light surfaces (F154-R-06).
  return (
    <table
      className="w-full table-fixed border-collapse text-[12px] text-slate-900"
      data-testid="balance-sheet-final-total"
    >
      <colgroup>
        <col className={comparison ? "w-[40%]" : "w-[62%]"} />
        <col />
        {comparison && (
          <>
            <col />
            <col className="w-[16%]" />
          </>
        )}
      </colgroup>
      <tbody>
        <SideRows rows={[{ ...row, kind: "total" }]} comparison={comparison} />
      </tbody>
    </table>
  );
}

export function BalanceSheetRatioRow({
  view,
  comparison,
}: {
  view: BalanceSheetView;
  comparison: boolean;
}) {
  return (
    <div
      className={`flex flex-wrap items-center justify-between gap-2 px-3 py-2 text-sm ${RATIO_ROW}`}
      data-testid="balance-sheet-ratio-row"
    >
      <span className="font-extrabold">{BALANCE_SHEET_RATIO_LABEL}</span>
      <span className="flex flex-wrap items-center gap-x-4 gap-y-1 tabular-nums">
        <span>
          {comparison ? `${view.anchor.date}: ` : ""}
          <span dir="ltr">{view.ratio.anchor}</span>
          {view.ratio.negative && <span className="sr-only"> منفی</span>}
        </span>
        {comparison && (
          <>
            <span>
              {view.comparison?.baseline?.date ?? "—"}: <span dir="ltr">{view.ratio.baseline}</span>
            </span>
            <span>
              تغییر: <span dir="rtl">{view.ratio.pp}</span>
            </span>
          </>
        )}
      </span>
    </div>
  );
}

/** Wide two-sided layout (spec §9.1/§9.2): assets right, liabilities and equity left, aligned final totals only. */
export function BalanceSheetReportWide({
  view,
  comparison,
  twoSided,
}: {
  view: BalanceSheetView;
  comparison: boolean;
  twoSided: boolean;
}) {
  if (!twoSided) {
    return (
      <div className="space-y-3 p-2" data-layout="stacked">
        <SideTable
          side={view.assets}
          view={view}
          comparison={comparison}
          extraRows={[view.finalAssets]}
        />
        <SideTable side={view.liabilities} view={view} comparison={comparison} />
        <SideTable
          side={view.equity}
          view={view}
          comparison={comparison}
          extraRows={[view.finalLiabilitiesAndEquity]}
        />
        <BalanceSheetRatioRow view={view} comparison={comparison} />
      </div>
    );
  }
  return (
    <div className="p-2" data-layout="two-sided">
      <div className="grid grid-cols-2 items-start gap-3">
        <div data-side="assets">
          <SideTable side={view.assets} view={view} comparison={comparison} />
        </div>
        <div data-side="liabilities-and-equity" className="space-y-3">
          <SideTable side={view.liabilities} view={view} comparison={comparison} />
          <SideTable side={view.equity} view={view} comparison={comparison} />
        </div>
      </div>
      <div className="mt-2 grid grid-cols-2 gap-3" data-testid="balance-sheet-final-totals">
        <FinalTotal row={view.finalAssets} comparison={comparison} />
        <FinalTotal row={view.finalLiabilitiesAndEquity} comparison={comparison} />
      </div>
      <div className="mt-2">
        <BalanceSheetRatioRow view={view} comparison={comparison} />
      </div>
    </div>
  );
}
