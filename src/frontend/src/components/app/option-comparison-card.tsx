import { useId, useState, type ReactNode } from "react";
import {
  OPTION_ADVICE_NOTE,
  OPTION_BOOK_NOT_CURRENT_NOTE,
  OPTION_HIDE_EMPTY_ROWS,
  OPTION_HIGHLIGHT_TEXT,
  OPTION_NO_FILLABLE_ROWS,
  OPTION_ROW_STATE_TEXT,
  OPTION_SHOW_EMPTY_ROWS,
  OPTION_THEORETICAL_NOTE,
  OPTION_UNKNOWN_CLASS_NOTE,
  OPTION_VIEW_NOTE,
  OPTION_VIEW_TAB,
  OPTION_VIEW_TITLE,
  OPTION_VOLUME_NOTE,
  formatOptionCount,
  formatOptionDate,
  formatOptionDateTime,
  formatOptionDistance,
  formatOptionMoney,
  formatOptionPercent,
  formatOptionRatio,
  initialOptionExpiry,
  initialOptionView,
  isEmptyOptionRow,
  optionFeesText,
  optionViewsWithRows,
  type OptionComparison,
  type OptionComparisonRow,
  type OptionComparisonView,
} from "@/lib/chat/option-comparison";
import { toPersianDigits } from "@/lib/format/persian";
import { cn } from "@/lib/utils";

/*
 * Feature 168 option comparison card: calls bought for exercise, puts, and covered calls of one underlying, per
 * expiry. The server owns every figure; this file lays them out. One component serves desktop chat and the phone
 * workspace: the table scrolls sideways inside the card on narrow screens. Highlights are text labels, so nothing
 * relies on colour alone, and the card compares without ordering contracts by "quality".
 */

interface Column {
  key: string;
  header: string;
  cell: (row: OptionComparisonRow) => ReactNode;
}

const symbolColumn: Column = {
  key: "symbol",
  header: "نماد",
  cell: (row) => <span className="font-medium text-foreground">{toPersianDigits(row.symbol)}</span>,
};
const strikeColumn: Column = {
  key: "strike",
  header: "قیمت اعمال",
  cell: (row) => formatOptionMoney(row.strikePrice),
};
const volumeColumn: Column = {
  key: "volume",
  header: "حجم",
  cell: (row) => formatOptionCount(row.availableVolume),
};
const theoreticalColumn: Column = {
  key: "theoretical",
  header: "قیمت تئوریک",
  cell: (row) => formatOptionMoney(row.theoreticalPrice),
};
const impliedVolatilityColumn: Column = {
  key: "implied-volatility",
  header: "نوسان ضمنی",
  cell: (row) => formatOptionPercent(row.impliedVolatility, 0),
};

const VIEW_COLUMNS: Record<OptionComparisonView, Column[]> = {
  Call: [
    symbolColumn,
    strikeColumn,
    { key: "ask", header: "عرضه", cell: (row) => formatOptionMoney(row.fillPrice) },
    {
      key: "cost",
      header: "هزینهٔ مؤثر",
      cell: (row) => formatOptionMoney(row.effectivePurchaseCost),
    },
    {
      key: "break-even",
      header: "نسبت به قیمت سهم",
      cell: (row) => formatOptionDistance(row.breakEvenDistance),
    },
    { key: "time-value", header: "ارزش زمانی", cell: (row) => formatOptionMoney(row.timeValue) },
    {
      key: "implied-rate",
      header: "نرخ ضمنی سالانه",
      cell: (row) => formatOptionPercent(row.impliedAnnualRate, 0),
    },
    { key: "leverage", header: "اهرم", cell: (row) => formatOptionRatio(row.leverage) },
    volumeColumn,
  ],
  Put: [
    symbolColumn,
    strikeColumn,
    { key: "ask", header: "عرضه", cell: (row) => formatOptionMoney(row.fillPrice) },
    {
      key: "sale",
      header: "قیمت فروش مؤثر",
      cell: (row) => formatOptionMoney(row.effectiveSalePrice),
    },
    {
      key: "distance",
      header: "نسبت به قیمت سهم",
      cell: (row) => formatOptionDistance(row.distanceFromMarket),
    },
    {
      key: "protection",
      header: "هزینهٔ پوشش",
      cell: (row) => formatOptionPercent(row.protectionCost),
    },
    volumeColumn,
  ],
  CoveredCall: [
    symbolColumn,
    strikeColumn,
    { key: "bid", header: "تقاضا", cell: (row) => formatOptionMoney(row.fillPrice) },
    {
      key: "yield-exercised",
      header: "بازده در صورت اعمال",
      cell: (row) => formatOptionPercent(row.yieldIfExercised),
    },
    {
      key: "yield-exercised-annual",
      header: "سالانه",
      cell: (row) => formatOptionPercent(row.yieldIfExercisedAnnualized, 0),
    },
    {
      key: "yield-not-exercised",
      header: "بازده بدون اعمال",
      cell: (row) => formatOptionPercent(row.yieldIfNotExercised),
    },
    volumeColumn,
  ],
};

const NOT_CURRENT_COLUMNS: Column[] = [
  symbolColumn,
  strikeColumn,
  { key: "last", header: "آخرین معامله", cell: (row) => formatOptionMoney(row.lastPrice) },
  {
    key: "open-interest",
    header: "موقعیت باز",
    cell: (row) => formatOptionCount(row.openInterest),
  },
];

function Tabs<T extends string>({
  label,
  items,
  selected,
  onSelect,
}: {
  label: string;
  items: { value: T; text: string }[];
  selected: T;
  onSelect: (value: T) => void;
}) {
  return (
    <div role="tablist" aria-label={label} className="flex flex-wrap gap-1.5">
      {items.map((item) => (
        <button
          key={item.value}
          type="button"
          role="tab"
          aria-selected={item.value === selected}
          onClick={() => onSelect(item.value)}
          className={cn(
            "rounded-full border px-3 py-1 text-xs transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
            item.value === selected
              ? "border-foreground/30 bg-foreground/10 font-medium text-foreground"
              : "border-border text-muted-foreground hover:text-foreground",
          )}
        >
          {item.text}
        </button>
      ))}
    </div>
  );
}

export function OptionComparisonCard({ data }: { data: OptionComparison }) {
  const titleId = useId();
  const views = optionViewsWithRows(data);
  const [viewChoice, setViewChoice] = useState<OptionComparisonView | null>(() =>
    initialOptionView(data),
  );
  const [expiryChoice, setExpiryChoice] = useState<string | null>(null);
  const [showEmpty, setShowEmpty] = useState(false);

  const view = views.find((item) => item.view === viewChoice) ?? views[0];
  if (!view) return null;

  const expiries = view.expiries.filter((expiry) => expiry.rows.length > 0);
  const expiry =
    expiries.find((item) => item.expiryDate === expiryChoice) ??
    expiries.find((item) => item.expiryDate === initialOptionExpiry(view)) ??
    expiries[0];
  if (!expiry) return null;

  const bookIsCurrent = data.orderBookIsCurrent;
  const hasTheoretical = bookIsCurrent && expiry.rows.some((row) => row.theoreticalPrice != null);
  const columns = bookIsCurrent
    ? [
        ...VIEW_COLUMNS[view.view],
        ...(hasTheoretical ? [theoreticalColumn, impliedVolatilityColumn] : []),
      ]
    : NOT_CURRENT_COLUMNS;
  const visibleRows = expiry.rows.filter((row) => !isEmptyOptionRow(row, bookIsCurrent));
  const emptyRows = expiry.rows.filter((row) => isEmptyOptionRow(row, bookIsCurrent));
  const rows = showEmpty ? expiry.rows : visibleRows;
  const dataTime = formatOptionDateTime(data.orderBookObservedAt);
  const oneContractAtExpiry =
    view.view === "Call" && visibleRows[0]?.cashAtExpiry != null
      ? visibleRows[0].cashAtExpiry / data.quantityInContracts
      : null;

  return (
    <section
      aria-labelledby={titleId}
      data-testid="option-comparison-card"
      className="w-full rounded-2xl border border-border bg-card p-4 text-card-foreground sm:p-5"
    >
      <header className="flex flex-wrap items-baseline justify-between gap-2">
        <h3 id={titleId} className="text-sm font-semibold text-foreground">
          {OPTION_VIEW_TITLE[view.view]} — {data.underlyingSymbol}
        </h3>
        <p className="text-xs text-muted-foreground">
          قیمت سهم:{" "}
          <span className="text-foreground">{formatOptionMoney(data.underlyingPrice)}</span> ریال (
          {formatOptionDate(data.underlyingTradingDate)})
        </p>
      </header>

      <div className="mt-3 flex flex-col gap-2">
        {views.length > 1 && (
          <Tabs
            label="نوع مقایسه"
            items={views.map((item) => ({ value: item.view, text: OPTION_VIEW_TAB[item.view] }))}
            selected={view.view}
            onSelect={(value) => {
              setViewChoice(value);
              setExpiryChoice(null);
              setShowEmpty(false);
            }}
          />
        )}
        {expiries.length > 1 && (
          <Tabs
            label="سررسید"
            items={expiries.map((item) => ({
              value: item.expiryDate,
              text: `${formatOptionDate(item.expiryDate)} (${toPersianDigits(item.daysToExpiry)} روز)`,
            }))}
            selected={expiry.expiryDate}
            onSelect={(value) => {
              setExpiryChoice(value);
              setShowEmpty(false);
            }}
          />
        )}
        <p className="text-xs text-muted-foreground">
          سررسید {formatOptionDate(expiry.expiryDate)} ({toPersianDigits(expiry.daysToExpiry)} روز)،
          برای {toPersianDigits(data.quantityInContracts)} قرارداد
        </p>
      </div>

      {!bookIsCurrent && (
        <p
          role="status"
          className="mt-3 rounded-lg border border-border bg-muted/40 px-3 py-2 text-xs text-foreground"
        >
          {OPTION_BOOK_NOT_CURRENT_NOTE}
        </p>
      )}

      {rows.length === 0 ? (
        <p className="mt-3 text-sm text-muted-foreground">{OPTION_NO_FILLABLE_ROWS}</p>
      ) : (
        <div className="mt-3 overflow-x-auto">
          <table className="w-full min-w-[34rem] border-collapse text-start text-xs">
            <thead>
              <tr className="border-b border-border text-muted-foreground">
                {columns.map((column) => (
                  <th key={column.key} scope="col" className="px-2 py-2 text-start font-medium">
                    {column.header}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => {
                const stateText = bookIsCurrent ? OPTION_ROW_STATE_TEXT[row.state] : "";
                return (
                  <tr
                    key={row.symbol}
                    data-nearest-to-money={row.isNearestToMoney || undefined}
                    data-requested={row.isRequestedContract || undefined}
                    className={cn(
                      "border-b border-border/60 text-foreground/90 last:border-b-0",
                      row.isNearestToMoney && "bg-foreground/[0.04]",
                      row.isRequestedContract &&
                        "outline outline-1 -outline-offset-1 outline-foreground/40",
                      stateText && "text-muted-foreground",
                    )}
                  >
                    {columns.map((column, index) => (
                      <td key={column.key} className="px-2 py-2 tabular-nums">
                        {column.cell(row)}
                        {index === 0 && (
                          <span className="ms-2 inline-flex flex-wrap gap-1 align-middle">
                            {row.isNearestToMoney && (
                              <span className="rounded border border-border px-1 text-[10px] text-muted-foreground">
                                نزدیک به قیمت سهم
                              </span>
                            )}
                            {row.highlights.map((code) => (
                              <span
                                key={code}
                                className="rounded border border-foreground/30 px-1 text-[10px] font-medium text-foreground"
                              >
                                {OPTION_HIGHLIGHT_TEXT[code] ?? code}
                              </span>
                            ))}
                            {stateText && (
                              <span className="rounded border border-border px-1 text-[10px]">
                                {stateText}
                                {row.state === "NotFillable" &&
                                  ` (${formatOptionCount(row.availableVolume)})`}
                              </span>
                            )}
                          </span>
                        )}
                      </td>
                    ))}
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      {emptyRows.length > 0 && (
        <button
          type="button"
          aria-expanded={showEmpty}
          onClick={() => setShowEmpty((value) => !value)}
          className="mt-2 text-xs text-muted-foreground underline underline-offset-4 hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
        >
          {showEmpty ? OPTION_HIDE_EMPTY_ROWS : OPTION_SHOW_EMPTY_ROWS} (
          {toPersianDigits(emptyRows.length)})
        </button>
      )}

      <footer className="mt-4 space-y-1.5 border-t border-border pt-3 text-xs leading-6 text-muted-foreground">
        <p>{OPTION_VIEW_NOTE[view.view]}</p>
        {oneContractAtExpiry != null && (
          <p>
            مبلغ لازم در سررسید برای اعمال یک قرارداد {toPersianDigits(visibleRows[0].symbol)}:{" "}
            <span className="text-foreground">{formatOptionMoney(oneContractAtExpiry)}</span> ریال
          </p>
        )}
        <p>{OPTION_VOLUME_NOTE}</p>
        {data.underlyingClass === "Unknown" && view.view !== "Call" && (
          <p>{OPTION_UNKNOWN_CLASS_NOTE}</p>
        )}
        {hasTheoretical && data.theoreticalInputs && (
          <p>
            {OPTION_THEORETICAL_NOTE} نرخ بدون ریسک{" "}
            {formatOptionPercent(data.theoreticalInputs.riskFreeAnnualRate)}، نوسان تاریخی{" "}
            {formatOptionPercent(data.theoreticalInputs.historicalVolatility)}.
          </p>
        )}
        <p>{optionFeesText(data, view.view)}</p>
        {dataTime && <p>زمان عمق سفارش: {dataTime}</p>}
        <p>{OPTION_ADVICE_NOTE}</p>
      </footer>
    </section>
  );
}
