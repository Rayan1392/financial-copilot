import { useQuery } from "@tanstack/react-query";
import { getMarketSummary, getWatchlist } from "@/lib/market-view.functions";
import type { MarketMover } from "@/lib/market-view.functions";
import { formatNumber, formatPercent, relativeTime } from "@/lib/format/persian";

function MarketMoverList({
  title,
  movers,
  positive,
}: {
  title: string;
  movers: MarketMover[];
  positive: boolean;
}) {
  if (movers.length === 0) return null;

  return (
    <section className="min-w-0">
      <h4 className={`mb-2 text-[11px] font-medium ${positive ? "text-emerald" : "text-rose"}`}>
        {title}
      </h4>
      <div className="space-y-2">
        {movers.slice(0, 3).map((mover) => (
          <div key={mover.symbol} className="flex items-center justify-between gap-2 text-xs">
            <div className="min-w-0">
              <div className="truncate text-foreground" title={mover.name}>
                {mover.name || mover.symbol}
                {mover.isStale ? " *" : ""}
              </div>
              <div className="mt-0.5 text-[10px] text-muted-foreground">
                {formatNumber(mover.latestPrice)}
              </div>
            </div>
            <span className={positive ? "shrink-0 text-emerald" : "shrink-0 text-rose"}>
              {formatPercent(mover.changePercent)}
            </span>
          </div>
        ))}
      </div>
    </section>
  );
}

export function ContextPanel() {
  const { data, isLoading, isError } = useQuery({
    queryKey: ["market-summary"],
    queryFn: getMarketSummary,
    retry: false,
    throwOnError: false,
    refetchOnWindowFocus: false,
  });
  const {
    data: watchlist,
    isLoading: watchlistLoading,
    isError: watchlistError,
  } = useQuery({
    queryKey: ["watchlist"],
    queryFn: getWatchlist,
    retry: false,
    throwOnError: false,
    refetchOnWindowFocus: false,
  });

  return (
    <aside className="hidden h-screen w-80 flex-shrink-0 flex-col border-r border-hairline bg-surface/50 lg:flex">
      <div className="flex h-full min-h-0 flex-col space-y-8 overflow-hidden p-6">
        <section className="shrink-0">
          <h3 className="text-xs font-semibold text-muted-foreground uppercase tracking-widest mb-4">
            وضعیت کلی بازار
          </h3>
          {isLoading && <p className="text-xs text-muted-foreground">در حال بارگذاری...</p>}
          {isError && <p className="text-xs text-rose">داده بازار در دسترس نیست.</p>}
          {!isLoading && !isError && data?.indices.length === 0 && (
            <p className="text-xs text-muted-foreground">داده شاخص در دسترس نیست.</p>
          )}
          <div className="space-y-4">
            {data?.indices.slice(0, 2).map((index) => (
              <div key={index.symbol} className="flex justify-between items-end">
                <div>
                  <div className="text-[11px] text-muted-foreground">
                    {index.name || index.symbol}
                  </div>
                  <div className="text-lg mt-1 text-foreground">
                    {index.value == null ? "-" : formatNumber(index.value)}
                  </div>
                </div>
                <div
                  className={`text-sm ${index.changePercent == null ? "text-muted-foreground" : index.changePercent >= 0 ? "text-emerald" : "text-rose"}`}
                >
                  {index.changePercent == null ? "-" : formatPercent(index.changePercent)}
                </div>
              </div>
            ))}
          </div>
          {data && (data.topGainers.length > 0 || data.topLosers.length > 0) && (
            <div className="mt-5 grid grid-cols-2 gap-3 border-t border-hairline pt-4">
              <MarketMoverList title="بیشترین رشد" movers={data.topGainers} positive />
              <MarketMoverList title="بیشترین افت" movers={data.topLosers} positive={false} />
            </div>
          )}
          {data?.asOf && (
            <p className="mt-3 text-[10px] text-muted-foreground">
              بروزرسانی {relativeTime(data.asOf)}
            </p>
          )}
        </section>
        <section className="flex min-h-0 flex-1 flex-col">
          <h3 className="text-xs font-semibold text-muted-foreground uppercase tracking-widest mb-4">
            دیده‌بان
          </h3>
          {watchlistLoading && (
            <p className="text-xs text-muted-foreground">در حال بارگذاری دیده‌بان...</p>
          )}
          {watchlistError && <p className="text-xs text-rose">داده دیده‌بان در دسترس نیست.</p>}
          {!watchlistLoading && !watchlistError && watchlist?.symbols.length === 0 && (
            <p className="text-xs text-muted-foreground">دیده‌بان خالی است.</p>
          )}
          <div className="min-h-0 flex-1 space-y-1.5 overflow-y-auto pr-1 scrollbar-thin">
            {watchlist?.symbols.map((quote) => (
              <div
                key={quote.symbol}
                className="flex items-center justify-between gap-2 p-2 rounded-lg"
              >
                <div>
                  <div className="text-sm text-foreground">
                    {quote.symbol}
                    {quote.isStale ? " *" : ""}
                  </div>
                  <div className="mt-1 text-[11px] text-muted-foreground">
                    {quote.latestPrice == null ? "-" : formatNumber(quote.latestPrice)}
                  </div>
                </div>
                <span
                  className={`text-xs ${quote.changePercent == null ? "text-muted-foreground" : quote.changePercent >= 0 ? "text-emerald" : "text-rose"}`}
                >
                  {quote.changePercent == null ? "-" : formatPercent(quote.changePercent)}
                </span>
              </div>
            ))}
          </div>
          {watchlist?.asOf && (
            <p className="mt-2 text-[10px] text-muted-foreground">
              بروزرسانی {relativeTime(watchlist.asOf)}
            </p>
          )}
          {watchlist?.symbols.some((quote) => quote.isStale) && (
            <p className="text-[10px] text-gold mt-2">* داده قدیمی</p>
          )}
        </section>
      </div>
    </aside>
  );
}
