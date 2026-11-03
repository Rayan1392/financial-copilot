import type { MarketIndexObservation } from "@/lib/market-view.functions";
import { formatNumber } from "@/lib/format/persian";
import { SignedChange } from "@/components/mobile/signed-change";

/** One named index (design §6): name, value in tabular figures, and the signed change with a direction glyph. */
export function IndexCard({ index }: { index: MarketIndexObservation }) {
  return (
    <li className="min-w-0 rounded-2xl bg-surface p-3 ring-1 ring-hairline">
      <p className="truncate text-[13px] text-muted-foreground">{index.name || index.symbol}</p>
      <p className="mt-1 truncate text-xl font-semibold tabular-nums text-foreground">
        {index.value == null ? "–" : formatNumber(index.value)}
      </p>
      <SignedChange value={index.changePercent} className="mt-0.5 text-sm" />
    </li>
  );
}
