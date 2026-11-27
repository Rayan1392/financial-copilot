import { useMemo, useState } from "react";
import { Command as CommandPrimitive } from "cmdk";
import { Search } from "lucide-react";
import { Command, CommandGroup, CommandItem, CommandList } from "@/components/ui/command";
import { STARTER_SYMBOL_SLOT, type SymbolStarterPrompt } from "@/lib/chat/starter-prompts";
import { isRightsIssueSymbol } from "@/lib/market/instrument-kind";
import { useSymbolSearch } from "@/lib/market/use-symbol-search";

interface PickerRow {
  /** cmdk item value; the group prefix keeps a symbol that is both followed and the example apart. */
  value: string;
  symbol: string;
  companyName?: string;
}

const ROW_CLASS =
  "min-h-12 cursor-pointer gap-3 rounded-xl px-3 text-right data-[selected=true]:bg-surface-2 data-[selected=true]:text-foreground";
const STATUS_CLASS = "px-4 py-6 text-center text-sm text-muted-foreground";

/**
 * The content of the starter cards' symbol picker (change «starter-cards-symbol-picker»), hosted by a dialog on
 * desktop and a bottom sheet on the phone. Before typing it offers the user's watchlist symbols and the card's
 * example symbol; typing searches every symbol. Choosing a row calls `onPick`; sending is the host's job.
 */
export function StarterSymbolPicker({
  prompt,
  watchlistSymbols,
  onPick,
}: {
  prompt: SymbolStarterPrompt;
  watchlistSymbols: readonly string[];
  onPick: (symbol: string) => void;
}) {
  const [query, setQuery] = useState("");
  const [highlighted, setHighlighted] = useState("");
  const typed = query.trim();
  const { term, results } = useSymbolSearch(query);

  const watchlistRows = useMemo<PickerRow[]>(
    () => watchlistSymbols.map((symbol) => ({ value: `watchlist:${symbol}`, symbol })),
    [watchlistSymbols],
  );
  const exampleRow: PickerRow = {
    value: `example:${prompt.exampleSymbol}`,
    symbol: prompt.exampleSymbol,
  };
  // A rights issue has no statements or monthly sales of its own, so no card can answer for it.
  const foundRows = useMemo<PickerRow[]>(
    () =>
      (results.data ?? [])
        .filter((item) => !isRightsIssueSymbol(item.symbolCode))
        .map((item) => ({
          value: `found:${item.symbolCode}`,
          symbol: item.symbolCode,
          companyName: item.companyName,
        })),
    [results.data],
  );
  const searching = typed.length > 0 && (term !== typed || results.isLoading);
  const shownRows =
    typed.length === 0 ? [...watchlistRows, exampleRow] : searching ? [] : foundRows;
  const highlightedSymbol = shownRows.find((row) => row.value === highlighted)?.symbol;

  const renderRow = (row: PickerRow) => (
    <CommandItem
      key={row.value}
      value={row.value}
      onSelect={() => onPick(row.symbol)}
      className={ROW_CLASS}
    >
      <bdi className="text-sm font-bold text-foreground">{row.symbol}</bdi>
      {row.companyName && (
        <span className="min-w-0 flex-1 truncate text-xs text-muted-foreground">
          {row.companyName}
        </span>
      )}
    </CommandItem>
  );

  return (
    <Command
      dir="rtl"
      shouldFilter={false}
      value={highlighted}
      onValueChange={setHighlighted}
      label="انتخاب نماد"
      className="bg-transparent text-right"
    >
      <div className="mx-2 flex items-center gap-2 rounded-xl bg-surface px-3 ring-1 ring-inset ring-hairline focus-within:ring-2 focus-within:ring-emerald/40">
        <Search className="size-5 shrink-0 text-muted-foreground" aria-hidden="true" />
        {/* Autofocus is acceptable here: the user opened the picker to type. */}
        <CommandPrimitive.Input
          autoFocus
          value={query}
          onValueChange={setQuery}
          maxLength={100}
          placeholder="نماد یا نام شرکت…"
          className="min-h-12 min-w-0 flex-1 bg-transparent text-base outline-none placeholder:text-muted-foreground"
        />
      </div>
      <p
        className="px-4 pb-1 pt-3 text-xs leading-6 text-muted-foreground"
        aria-live="polite"
        data-testid="starter-picker-preview"
      >
        پرسشی که ارسال می‌شود:{" "}
        <bdi className="font-medium text-foreground">
          «{prompt.forSymbol(highlightedSymbol ?? `‹${STARTER_SYMBOL_SLOT}›`)}»
        </bdi>
      </p>
      <CommandList className="max-h-[min(22rem,55dvh)] px-1 pb-2 scrollbar-thin">
        {typed.length === 0 ? (
          <>
            {watchlistRows.length > 0 && (
              <CommandGroup heading="دیده‌بان شما">{watchlistRows.map(renderRow)}</CommandGroup>
            )}
            <CommandGroup heading="نمونه">{renderRow(exampleRow)}</CommandGroup>
          </>
        ) : searching ? (
          <p role="status" className={STATUS_CLASS}>
            در حال جست‌وجو…
          </p>
        ) : results.isError ? (
          <div role="alert" className={STATUS_CLASS}>
            <p>جست‌وجو ناموفق بود</p>
            <button
              type="button"
              onClick={() => void results.refetch()}
              className="mt-2 min-h-11 rounded-xl bg-surface px-4 font-medium text-foreground ring-1 ring-hairline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40"
            >
              تلاش مجدد
            </button>
          </div>
        ) : foundRows.length === 0 ? (
          <p role="status" className={STATUS_CLASS}>
            نمادی یافت نشد
          </p>
        ) : (
          <CommandGroup heading="نتایج">{foundRows.map(renderRow)}</CommandGroup>
        )}
      </CommandList>
    </Command>
  );
}
