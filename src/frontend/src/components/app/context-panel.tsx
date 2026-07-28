import { useQuery } from "@tanstack/react-query";
import { useServerFn } from "@tanstack/react-start";
import { getMarketSummary, getWatchlist } from "@/lib/market-view.functions";
import { formatNumber, formatPercent, relativeTime } from "@/lib/format/persian";

export function ContextPanel() {
  const fetchSummary = useServerFn(getMarketSummary);
  const fetchWatchlist = useServerFn(getWatchlist);
  const { data, isLoading, isError } = useQuery({
    queryKey: ["market-summary"],
    queryFn: () => fetchSummary(),
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
    queryFn: () => fetchWatchlist(),
    retry: false,
    throwOnError: false,
    refetchOnWindowFocus: false,
  });

  return (
    <aside className="w-80 flex-shrink-0 border-r border-hairline bg-surface/50 flex-col hidden lg:flex">
      <div className="p-6 space-y-8 overflow-y-auto scrollbar-thin">
        <section>
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
          {data?.asOf && (
            <p className="mt-3 text-[10px] text-muted-foreground">
              بروزرسانی {relativeTime(data.asOf)}
            </p>
          )}
        </section>
        <section>
          <h3 className="text-xs font-semibold text-muted-foreground uppercase tracking-widest mb-4">
            دیده‌بان
          </h3>
          {watchlistLoading && (
            <p className="text-xs text-muted-foreground">در حال بارگذاری دیده‌بان...</p>
          )}
          {watchlistError && (
            <p className="text-xs text-rose">داده دیده‌بان در دسترس نیست.</p>
          )}
          {!watchlistLoading && !watchlistError && watchlist?.symbols.length === 0 && (
            <p className="text-xs text-muted-foreground">دیده‌بان خالی است.</p>
          )}
          <div className="max-h-72 space-y-1.5 overflow-y-auto pr-1 scrollbar-thin">
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
        <section className="text-xs text-muted-foreground space-y-2">
          <p>ورود پول حقیقی: در دسترس نیست</p>
          <p>صنایع فعال: در دسترس نیست</p>
        </section>
      </div>
    </aside>
  );
}
