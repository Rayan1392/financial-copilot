import { Link } from "@tanstack/react-router";
import { Plus, Activity, Eye, ChevronLeft, ChevronRight } from "lucide-react";
import { formatNumber, formatPercent, relativeTime } from "@/lib/format/persian";
import { useMarketSummary, useWatchlist } from "@/lib/workspace/queries";
import { ChangePill, RailHoverCard } from "@/components/app/rail-hover-card";

function MoverRow({ label, symbol, change }: { label: string; symbol: string; change: number }) {
  return (
    <div className="flex items-center justify-between gap-2">
      <p className="min-w-0 truncate text-xs">
        <span className="text-muted-foreground">{label}: </span>
        <span className="font-semibold">{symbol}</span>
      </p>
      <ChangePill value={change} label={formatPercent(change)} />
    </div>
  );
}

export function ContextPanel({
  collapsed,
  onToggle,
}: {
  collapsed: boolean;
  onToggle: () => void;
}) {
  const { data, isLoading, isError } = useMarketSummary();
  const { data: watchlist, isLoading: watchlistLoading, isError: watchlistError } = useWatchlist();
  const indices = data?.indices.slice(0, 2) ?? [];
  const leadChange = indices[0]?.changePercent;
  const marketTone =
    leadChange == null
      ? null
      : leadChange >= 0
        ? { label: "بازار مثبت", className: "bg-emerald-soft/70 text-emerald" }
        : { label: "بازار منفی", className: "bg-rose/10 text-rose" };
  const topGainer = data?.topGainers?.[0];
  const topLoser = data?.topLosers?.[0];
  const symbols = watchlist?.symbols ?? [];
  const symbolCount = symbols.length;
  const breadth = {
    up: symbols.filter((quote) => (quote.changePercent ?? 0) > 0).length,
    down: symbols.filter((quote) => (quote.changePercent ?? 0) < 0).length,
    flat: symbols.filter((quote) => !quote.changePercent).length,
  };
  const topMovers = [...symbols]
    .sort((a, b) => Math.abs(b.changePercent ?? -1) - Math.abs(a.changePercent ?? -1))
    .slice(0, 5);

  if (collapsed) {
    return (
      <aside
        className="hidden h-screen w-[4.5rem] shrink-0 flex-col items-center border-r border-hairline bg-surface/70 py-4 lg:flex"
        aria-label="نوار اطلاعات بازار"
      >
        <button
          type="button"
          onClick={onToggle}
          className="mb-5 flex size-10 items-center justify-center rounded-xl text-muted-foreground transition hover:bg-white/5 hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/50"
          aria-label="باز کردن پنل بازار و دیده‌بان"
          title="باز کردن پنل بازار و دیده‌بان"
        >
          <ChevronRight className="size-5" />
        </button>
        <div className="mb-2">
          <RailHoverCard
            id="compact-market-tooltip"
            opensTo="right"
            align="start"
            icon={<Activity className="size-4" />}
            title="وضعیت کلی بازار"
            subtitle="شاخص‌های اصلی بورس"
            badge={
              marketTone && (
                <span
                  className={`shrink-0 rounded-full px-2 py-0.5 text-[10px] font-semibold ${marketTone.className}`}
                >
                  {marketTone.label}
                </span>
              )
            }
            trigger={
              <button
                type="button"
                onClick={onToggle}
                className="flex size-11 items-center justify-center rounded-xl text-emerald transition hover:bg-emerald-soft/60 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/50"
                aria-label="وضعیت کلی بازار؛ برای باز کردن پنل کلیک کنید"
                aria-describedby="compact-market-tooltip"
              >
                <Activity className="size-5" />
              </button>
            }
            footer={
              data?.asOf
                ? `به‌روزرسانی ${relativeTime(data.asOf)} · برای جزئیات کلیک کنید`
                : "برای جزئیات کلیک کنید"
            }
          >
            {isLoading && (
              <p className="rounded-lg bg-background/70 px-3 py-2.5 text-xs leading-6 text-muted-foreground">
                در حال دریافت وضعیت بازار…
              </p>
            )}
            {isError && (
              <p className="rounded-lg bg-rose/10 px-3 py-2.5 text-xs leading-6 text-rose">
                داده بازار در دسترس نیست.
              </p>
            )}
            {!isLoading && !isError && indices.length === 0 && (
              <p className="rounded-lg bg-background/70 px-3 py-2.5 text-xs leading-6 text-muted-foreground">
                داده شاخص در دسترس نیست.
              </p>
            )}
            {indices.length > 0 && (
              <div className="space-y-2">
                {indices.map((index) => (
                  <div
                    key={index.symbol}
                    className="flex items-end justify-between gap-2 rounded-xl bg-background/70 px-3 py-2.5"
                  >
                    <div className="min-w-0">
                      <p className="truncate text-[11px] text-muted-foreground">
                        {index.name || index.symbol}
                      </p>
                      <p className="mt-1 text-lg font-bold leading-none tracking-tight tabular-nums">
                        {index.value == null ? "-" : formatNumber(index.value)}
                      </p>
                    </div>
                    <ChangePill
                      value={index.changePercent}
                      label={index.changePercent == null ? "-" : formatPercent(index.changePercent)}
                    />
                  </div>
                ))}
              </div>
            )}
            {(topGainer || topLoser) && (
              <div className="mt-2 space-y-1.5 rounded-xl border border-hairline px-3 py-2.5">
                {topGainer && (
                  <MoverRow
                    label="بیشترین رشد"
                    symbol={topGainer.symbol}
                    change={topGainer.changePercent}
                  />
                )}
                {topLoser && (
                  <MoverRow
                    label="بیشترین افت"
                    symbol={topLoser.symbol}
                    change={topLoser.changePercent}
                  />
                )}
              </div>
            )}
          </RailHoverCard>
        </div>
        <RailHoverCard
          id="compact-watchlist-tooltip"
          opensTo="right"
          align="start"
          icon={<Eye className="size-4" />}
          title="دیده‌بان"
          subtitle={
            symbolCount > 0
              ? `${formatNumber(symbolCount)} نماد دنبال‌شده`
              : "هنوز نمادی دنبال نمی‌کنید"
          }
          trigger={
            <button
              type="button"
              onClick={onToggle}
              className="flex size-11 items-center justify-center rounded-xl text-muted-foreground transition hover:bg-white/5 hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/50"
              aria-label={`دیده‌بان، ${formatNumber(symbolCount)} نماد؛ برای باز کردن پنل کلیک کنید`}
              aria-describedby="compact-watchlist-tooltip"
            >
              <span className="relative">
                <Eye className="size-5" />
                {symbolCount > 0 && (
                  <span className="absolute -right-2 -top-2 flex size-4 items-center justify-center rounded-full bg-emerald px-1 text-[9px] font-semibold text-primary-foreground">
                    {formatNumber(Math.min(symbolCount, 9))}
                  </span>
                )}
              </span>
            </button>
          }
          footer={
            watchlist?.asOf
              ? `به‌روزرسانی ${relativeTime(watchlist.asOf)} · برای مشاهده همه کلیک کنید`
              : undefined
          }
        >
          {watchlistLoading && (
            <p className="rounded-lg bg-background/70 px-3 py-2.5 text-xs leading-6 text-muted-foreground">
              در حال دریافت دیده‌بان…
            </p>
          )}
          {watchlistError && (
            <p className="rounded-lg bg-rose/10 px-3 py-2.5 text-xs leading-6 text-rose">
              داده دیده‌بان در دسترس نیست.
            </p>
          )}
          {!watchlistLoading && !watchlistError && symbolCount === 0 && (
            <p className="rounded-lg border border-dashed border-hairline bg-background/40 px-3 py-2.5 text-xs leading-6 text-muted-foreground">
              قیمت و تغییرات نمادهایی که دنبال می‌کنید اینجا خلاصه می‌شود. پنل را باز کنید و «افزودن
              نماد» را بزنید.
            </p>
          )}
          {symbolCount > 0 && (
            <>
              <div className="rounded-xl bg-background/70 px-3 py-2.5">
                <div
                  className="flex h-1.5 w-full overflow-hidden rounded-full bg-muted"
                  role="img"
                  aria-label={`${formatNumber(breadth.up)} نماد مثبت، ${formatNumber(breadth.down)} نماد منفی، ${formatNumber(breadth.flat)} نماد بدون تغییر`}
                >
                  <span
                    className="h-full bg-emerald"
                    style={{ width: `${(breadth.up / symbolCount) * 100}%` }}
                  />
                  <span
                    className="h-full bg-muted-foreground/30"
                    style={{ width: `${(breadth.flat / symbolCount) * 100}%` }}
                  />
                  <span
                    className="h-full bg-rose"
                    style={{ width: `${(breadth.down / symbolCount) * 100}%` }}
                  />
                </div>
                <div className="mt-2 flex items-center justify-between text-[11px] text-muted-foreground">
                  <span>
                    <span className="font-semibold text-emerald tabular-nums">
                      {formatNumber(breadth.up)}
                    </span>{" "}
                    مثبت
                  </span>
                  <span>
                    <span className="font-semibold tabular-nums">{formatNumber(breadth.flat)}</span>{" "}
                    بدون تغییر
                  </span>
                  <span>
                    <span className="font-semibold text-rose tabular-nums">
                      {formatNumber(breadth.down)}
                    </span>{" "}
                    منفی
                  </span>
                </div>
              </div>
              <p className="mb-1.5 mt-3 text-[11px] font-medium text-muted-foreground">
                بیشترین تغییر
              </p>
              <div className="space-y-1">
                {topMovers.map((quote) => (
                  <div
                    key={quote.symbol}
                    className="flex items-center justify-between gap-2 rounded-lg px-1 py-1"
                  >
                    <div className="min-w-0">
                      <p className="truncate text-sm font-semibold">
                        {quote.symbol}
                        {quote.isStale && <span className="text-gold"> *</span>}
                      </p>
                      <p className="text-[11px] text-muted-foreground tabular-nums">
                        {quote.latestPrice == null ? "-" : formatNumber(quote.latestPrice)}
                      </p>
                    </div>
                    <ChangePill
                      value={quote.changePercent}
                      label={quote.changePercent == null ? "-" : formatPercent(quote.changePercent)}
                    />
                  </div>
                ))}
              </div>
              {symbolCount > topMovers.length && (
                <p className="mt-1.5 text-[11px] text-muted-foreground">
                  و {formatNumber(symbolCount - topMovers.length)} نماد دیگر
                </p>
              )}
              {watchlist?.symbols.some((quote) => quote.isStale) && (
                <p className="mt-1.5 text-[10px] text-gold">* داده قدیمی</p>
              )}
            </>
          )}
        </RailHoverCard>
      </aside>
    );
  }

  return (
    <aside className="hidden h-screen w-80 flex-shrink-0 flex-col border-r border-hairline bg-surface/50 transition-[width] duration-200 lg:flex">
      <div className="flex h-full min-h-0 flex-col space-y-8 overflow-hidden p-6">
        <section className="shrink-0">
          <div className="mb-4 flex items-center justify-between gap-2">
            <h3 className="text-xs font-semibold text-muted-foreground uppercase tracking-widest">
              وضعیت کلی بازار
            </h3>
            <button
              type="button"
              onClick={onToggle}
              className="flex size-8 shrink-0 items-center justify-center rounded-lg text-muted-foreground transition hover:bg-white/5 hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/50"
              aria-label="جمع کردن پنل بازار و دیده‌بان"
              title="جمع کردن پنل بازار و دیده‌بان"
            >
              <ChevronLeft className="size-4" />
            </button>
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
          {watchlistError && <p className="text-xs text-rose">داده دیده‌بان در دسترس نیست.</p>}
          {!watchlistLoading && !watchlistError && watchlist?.symbols.length === 0 && (
            <div className="mb-2 rounded-lg border border-dashed border-hairline bg-background/40 px-3 py-3">
              <p className="text-xs leading-6 text-muted-foreground">
                قیمت و تغییرات نمادهایی که دنبال می‌کنید، اینجا نمایش داده می‌شود. برای شروع،
                «افزودن نماد» را انتخاب کنید.
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
