import { Link } from "@tanstack/react-router";
import { Plus, Activity, Eye, ChevronLeft, ChevronRight } from "lucide-react";
import { formatNumber, formatPercent, relativeTime } from "@/lib/format/persian";
import { useMarketSummary, useWatchlist } from "@/lib/workspace/queries";

export function ContextPanel({ collapsed, onToggle }: { collapsed: boolean; onToggle: () => void }) {
  const { data, isLoading, isError } = useMarketSummary();
  const {
    data: watchlist,
    isLoading: watchlistLoading,
    isError: watchlistError,
  } = useWatchlist();

  if (collapsed) {
    return (
      <aside className="hidden h-screen w-[4.5rem] shrink-0 flex-col items-center border-r border-hairline bg-surface/70 py-4 lg:flex" aria-label="نوار اطلاعات بازار">
        <button type="button" onClick={onToggle} className="mb-5 flex size-10 items-center justify-center rounded-xl text-muted-foreground transition hover:bg-white/5 hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/50" aria-label="باز کردن پنل بازار و دیده‌بان" title="باز کردن پنل بازار و دیده‌بان">
          <ChevronRight className="size-5" />
        </button>
        <button type="button" onClick={onToggle} className="mb-2 flex size-11 items-center justify-center rounded-xl text-emerald transition hover:bg-emerald-soft/60 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/50" aria-label="وضعیت کلی بازار؛ باز کردن پنل" title="وضعیت کلی بازار">
          <Activity className="size-5" />
        </button>
        <button type="button" onClick={onToggle} className="flex size-11 items-center justify-center rounded-xl text-muted-foreground transition hover:bg-white/5 hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/50" aria-label={`دیده‌بان، ${watchlist?.symbols.length ?? 0} نماد؛ باز کردن پنل`} title={`دیده‌بان (${watchlist?.symbols.length ?? 0} نماد)`}>
          <span className="relative"><Eye className="size-5" />{(watchlist?.symbols.length ?? 0) > 0 && <span className="absolute -right-2 -top-2 flex size-4 items-center justify-center rounded-full bg-emerald px-1 text-[9px] font-semibold text-primary-foreground">{Math.min(watchlist?.symbols.length ?? 0, 9)}</span>}</span>
        </button>
      </aside>
    );
  }

  return (
    <aside className="hidden h-screen w-80 flex-shrink-0 flex-col border-r border-hairline bg-surface/50 transition-[width] duration-200 lg:flex">
      <div className="flex h-full min-h-0 flex-col space-y-8 overflow-hidden p-6">
        <section className="shrink-0">
          <div className="mb-4 flex items-center justify-between gap-2">
            <h3 className="text-xs font-semibold text-muted-foreground uppercase tracking-widest">وضعیت کلی بازار</h3>
            <button type="button" onClick={onToggle} className="flex size-8 shrink-0 items-center justify-center rounded-lg text-muted-foreground transition hover:bg-white/5 hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/50" aria-label="جمع کردن پنل بازار و دیده‌بان" title="جمع کردن پنل بازار و دیده‌بان"><ChevronLeft className="size-4" /></button>
          </div>
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
        <section className="flex min-h-0 flex-1 flex-col">
          <div className="mb-3 flex items-center justify-between gap-2">
            <h3 className="text-xs font-semibold text-muted-foreground uppercase tracking-widest">
              دیده‌بان
            </h3>
            <Link
              to="/followed-symbols"
              hash="symbol-search"
              className="inline-flex shrink-0 items-center gap-1 rounded-md px-2 py-1 text-[11px] font-medium text-emerald transition hover:bg-emerald-soft/70 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40"
            >
              <Plus className="size-3.5" aria-hidden="true" />
              افزودن نماد
            </Link>
          </div>
          {watchlistLoading && (
            <p className="text-xs text-muted-foreground">در حال بارگذاری دیده‌بان...</p>
          )}
          {watchlistError && (
            <p className="text-xs text-rose">داده دیده‌بان در دسترس نیست.</p>
          )}
          {!watchlistLoading && !watchlistError && watchlist?.symbols.length === 0 && (
            <div className="mb-2 rounded-lg border border-dashed border-hairline bg-background/40 px-3 py-3">
              <p className="text-xs leading-6 text-muted-foreground">
                قیمت و تغییرات نمادهایی که دنبال می‌کنید، اینجا نمایش داده می‌شود. برای شروع، «افزودن نماد» را انتخاب کنید.
              </p>
            </div>
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
