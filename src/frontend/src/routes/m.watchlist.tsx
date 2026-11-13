import { createFileRoute } from "@tanstack/react-router";
import { useMemo, useState } from "react";
import { MobileScreen } from "@/components/mobile/mobile-screen";
import { EmptyState, stateActionClass } from "@/components/mobile/empty-state";
import { ErrorState } from "@/components/mobile/error-state";
import { AddIcon, NavWatchlistIcon } from "@/components/mobile/icons";
import { SymbolRow } from "@/components/mobile/symbol-row";
import { RefreshButton } from "@/components/mobile/refresh-button";
import { SymbolSheet, type SymbolSheetSubject } from "@/components/mobile/symbol-sheet";
import { AddSymbolSheet } from "@/components/mobile/add-symbol-sheet";
import { Skeleton } from "@/components/ui/skeleton";
import { relativeTime, toPersianDigits } from "@/lib/format/persian";
import { useOpenNewChat } from "@/lib/chat/new-chat-intent";
import { useWatchlistWithNames } from "@/lib/workspace/queries";
import { isLaggingQuote, marketSessionStatus } from "@/lib/market/market-session";

export const Route = createFileRoute("/m/watchlist")({
  head: () => ({ meta: [{ title: "دیده‌بان — ساپیو" }] }),
  component: MobileWatchlist,
});

const ADD_BUTTON =
  "flex min-h-11 shrink-0 items-center gap-1 rounded-xl px-3 text-m-title font-bold text-emerald active:bg-emerald-soft/60 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40";

function MobileWatchlist() {
  const watchlist = useWatchlistWithNames({ freshness: true });
  const { ask, askAbout } = useOpenNewChat();
  const [subject, setSubject] = useState<SymbolSheetSubject | null>(null);
  const [adding, setAdding] = useState(false);
  const rows = watchlist.join?.rows ?? [];
  // A day without trading gets one line; the chip is kept for rows that are really behind.
  const session = marketSessionStatus(rows);
  const followedIds = useMemo(
    () => new Set(watchlist.followed.map((item) => item.externalCompanyId)),
    [watchlist.followed],
  );

  return (
    <MobileScreen title="دیده‌بان">
      <div className="px-4 pb-6 pt-3">
        <div className="mb-2 flex items-center gap-2">
          <p className="min-w-0 flex-1 truncate text-m-sub text-muted-foreground">
            {rows.length > 0 && `${toPersianDigits(rows.length)} نماد`}
            {rows.length > 0 && watchlist.asOf && ` · ${relativeTime(watchlist.asOf)}`}
          </p>
          <RefreshButton
            label="به‌روزرسانی دیده‌بان"
            onRefresh={() => watchlist.refetch()}
            busy={watchlist.isFetching}
          />
          <button type="button" onClick={() => setAdding(true)} className={ADD_BUTTON}>
            <AddIcon weight="bold" className="size-5" aria-hidden="true" />
            افزودن
          </button>
        </div>
        {rows.length > 0 && session.label && (
          <p role="status" className="mb-2 text-m-sub text-muted-foreground">
            {session.label}
          </p>
        )}

        {watchlist.isLoading ? (
          <div className="space-y-2" aria-busy="true" aria-label="در حال بارگذاری دیده‌بان">
            {Array.from({ length: 5 }, (_, index) => (
              <Skeleton key={index} className="h-[60px] rounded-xl" />
            ))}
          </div>
        ) : watchlist.isError && !watchlist.join ? (
          <div className="-mx-4">
            <ErrorState message="دیده‌بان در دسترس نیست" onRetry={() => void watchlist.refetch()} />
          </div>
        ) : rows.length === 0 ? (
          <EmptyState
            icon={NavWatchlistIcon}
            title="هنوز نمادی ندارید"
            action={
              <button type="button" onClick={() => setAdding(true)} className={stateActionClass}>
                <AddIcon weight="bold" className="size-5" aria-hidden="true" />
                افزودن نماد
              </button>
            }
          />
        ) : (
          <ul className="overflow-hidden rounded-2xl bg-surface ring-1 ring-hairline">
            {rows.map((row) => (
              <SymbolRow
                key={row.symbol}
                symbol={row.symbol}
                companyName={row.companyName}
                price={row.latestPrice}
                changePercent={row.changePercent}
                stale={isLaggingQuote(row, session)}
                onSelect={() =>
                  setSubject({
                    symbol: row.symbol,
                    companyName: row.companyName,
                    price: row.latestPrice,
                    changePercent: row.changePercent,
                    asOf: row.asOf,
                    externalCompanyId: row.externalCompanyId,
                    followed: true,
                  })
                }
              />
            ))}
          </ul>
        )}
      </div>
      <SymbolSheet
        subject={subject}
        onOpenChange={(open) => !open && setSubject(null)}
        onAsk={ask}
        onAskOther={askAbout}
      />
      <AddSymbolSheet open={adding} onOpenChange={setAdding} followedIds={followedIds} />
    </MobileScreen>
  );
}
