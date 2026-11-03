import { createFileRoute } from "@tanstack/react-router";
import { useMemo, useState } from "react";
import { ChevronLeft, Plus } from "lucide-react";
import { MobileScreen } from "@/components/mobile/mobile-screen";
import { SignedChange } from "@/components/mobile/signed-change";
import { StaleChip } from "@/components/mobile/stale-chip";
import { RefreshButton } from "@/components/mobile/refresh-button";
import { SymbolSheet, type SymbolSheetSubject } from "@/components/mobile/symbol-sheet";
import { AddSymbolSheet } from "@/components/mobile/add-symbol-sheet";
import { Skeleton } from "@/components/ui/skeleton";
import { formatNumber, relativeTime, toPersianDigits } from "@/lib/format/persian";
import { useOpenNewChat } from "@/lib/chat/new-chat-intent";
import { useWatchlistWithNames } from "@/lib/workspace/queries";

export const Route = createFileRoute("/m/watchlist")({
  head: () => ({ meta: [{ title: "دیده‌بان — ساپیو" }] }),
  component: MobileWatchlist,
});

const ADD_BUTTON =
  "flex min-h-11 shrink-0 items-center gap-1 rounded-xl px-3 text-sm font-medium text-emerald active:bg-emerald-soft/60 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40";

function MobileWatchlist() {
  const watchlist = useWatchlistWithNames({ freshness: true });
  const { ask, askAbout } = useOpenNewChat();
  const [subject, setSubject] = useState<SymbolSheetSubject | null>(null);
  const [adding, setAdding] = useState(false);
  const rows = watchlist.join?.rows ?? [];
  const followedIds = useMemo(
    () => new Set(watchlist.followed.map((item) => item.externalCompanyId)),
    [watchlist.followed],
  );

  return (
    <MobileScreen title="دیده‌بان">
      <div className="px-4 pb-6 pt-3">
        <div className="mb-2 flex items-center gap-2">
          <p className="min-w-0 flex-1 truncate text-xs text-muted-foreground">
            {rows.length > 0 && `${toPersianDigits(rows.length)} نماد`}
            {rows.length > 0 && watchlist.asOf && ` · به‌روزرسانی ${relativeTime(watchlist.asOf)}`}
          </p>
          <RefreshButton
            label="به‌روزرسانی دیده‌بان"
            onRefresh={() => watchlist.refetch()}
            busy={watchlist.isFetching}
          />
          <button type="button" onClick={() => setAdding(true)} className={ADD_BUTTON}>
            <Plus className="size-4" aria-hidden="true" />
            افزودن
          </button>
        </div>

        {watchlist.isLoading ? (
          <div className="space-y-2" aria-busy="true" aria-label="در حال بارگذاری دیده‌بان">
            {Array.from({ length: 5 }, (_, index) => (
              <Skeleton key={index} className="h-[60px] rounded-xl" />
            ))}
          </div>
        ) : watchlist.isError && !watchlist.join ? (
          <div role="alert" className="rounded-2xl bg-surface p-4 text-sm ring-1 ring-hairline">
            <p>دیده‌بان در دسترس نیست</p>
            <button
              type="button"
              onClick={() => void watchlist.refetch()}
              className="mt-2 min-h-11 rounded-xl bg-surface-2 px-4 font-medium focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40"
            >
              تلاش مجدد
            </button>
          </div>
        ) : rows.length === 0 ? (
          <div className="rounded-2xl border border-dashed border-hairline p-5 text-center">
            <p className="text-sm leading-7 text-muted-foreground">
              هنوز نمادی دنبال نمی‌کنید. نمادهای مورد علاقه را اضافه کنید تا قیمت و تغییراتشان اینجا
              نمایش داده شود.
            </p>
            <button
              type="button"
              onClick={() => setAdding(true)}
              className="mx-auto mt-3 flex min-h-11 items-center gap-1 rounded-xl bg-emerald px-4 text-sm font-medium text-primary-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40"
            >
              <Plus className="size-4" aria-hidden="true" />
              افزودن نماد
            </button>
          </div>
        ) : (
          <ul className="overflow-hidden rounded-2xl bg-surface ring-1 ring-hairline">
            {rows.map((row) => (
              <li key={row.symbol} className="border-b border-hairline last:border-b-0">
                <button
                  type="button"
                  onClick={() =>
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
                  className="flex min-h-[60px] w-full items-center gap-3 px-4 py-2 text-right active:bg-surface-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-emerald/40"
                >
                  <span className="min-w-0 flex-1">
                    <span className="flex items-center gap-2">
                      <bdi className="font-semibold text-foreground">{row.symbol}</bdi>
                      {row.isStale && <StaleChip />}
                    </span>
                    {row.companyName && (
                      <span className="block truncate text-xs text-muted-foreground">
                        {row.companyName}
                      </span>
                    )}
                  </span>
                  <span className="flex flex-col items-end text-sm">
                    <span className="tabular-nums text-foreground">
                      {row.latestPrice == null ? "–" : formatNumber(row.latestPrice)}
                    </span>
                    <SignedChange value={row.changePercent} />
                  </span>
                  <ChevronLeft
                    className="size-4 shrink-0 text-muted-foreground"
                    aria-hidden="true"
                  />
                </button>
              </li>
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
