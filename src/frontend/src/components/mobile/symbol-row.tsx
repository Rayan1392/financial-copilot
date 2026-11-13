import { ChevronIcon } from "@/components/mobile/icons";
import { SignedChange } from "@/components/mobile/signed-change";
import { StaleChip } from "@/components/mobile/stale-chip";
import { formatNumber } from "@/lib/format/persian";

/**
 * One symbol in a list (market movers, watchlist): symbol and company name at the start, latest price and the
 * change badge at the end. The whole row is one button that opens the symbol sheet.
 */
export function SymbolRow({
  symbol,
  companyName,
  price,
  changePercent,
  stale,
  onSelect,
}: {
  symbol: string;
  companyName?: string | null;
  price: number | null | undefined;
  changePercent: number | null | undefined;
  /** True when this quote is behind the rest of the list. */
  stale?: boolean;
  onSelect: () => void;
}) {
  return (
    <li className="border-b border-hairline last:border-b-0">
      <button
        type="button"
        onClick={onSelect}
        className="flex min-h-16 w-full items-center gap-3 px-4 py-2.5 text-right active:bg-surface-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-emerald/40"
      >
        <span className="min-w-0 flex-1">
          <span className="flex items-center gap-2">
            <bdi className="text-m-body font-bold leading-7 text-foreground">{symbol}</bdi>
            {stale && <StaleChip />}
          </span>
          {companyName && (
            <span className="block truncate text-m-sub text-muted-foreground">{companyName}</span>
          )}
        </span>
        <span className="flex shrink-0 flex-col items-end gap-1">
          <span className="text-m-num font-semibold tabular-nums text-foreground">
            {price == null ? "–" : formatNumber(price)}
          </span>
          <SignedChange value={changePercent} variant="badge" className="text-m-sub" />
        </span>
        <ChevronIcon className="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
      </button>
    </li>
  );
}
