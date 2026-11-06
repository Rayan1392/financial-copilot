import { createFileRoute } from "@tanstack/react-router";
import { useState } from "react";
import { ChevronLeft } from "lucide-react";
import { MobileScreen } from "@/components/mobile/mobile-screen";
import { IndexCard } from "@/components/mobile/index-card";
import { RefreshButton } from "@/components/mobile/refresh-button";
import { SignedChange } from "@/components/mobile/signed-change";
import { StaleChip } from "@/components/mobile/stale-chip";
import { SymbolSheet, type SymbolSheetSubject } from "@/components/mobile/symbol-sheet";
import { Skeleton } from "@/components/ui/skeleton";
import type { MarketMover } from "@/lib/market-view.functions";
import { formatNumber, relativeTime } from "@/lib/format/persian";
import { useOpenNewChat } from "@/lib/chat/new-chat-intent";
import { useFollowedSymbols, useMarketSummary } from "@/lib/workspace/queries";
import {
  isLaggingQuote,
  marketSessionStatus,
  type MarketSessionStatus,
} from "@/lib/market/market-session";

export const Route = createFileRoute("/m/market")({
  head: () => ({ meta: [{ title: "بازار — ساپیو" }] }),
  component: MobileMarket,
});

function isForbidden(error: unknown): boolean {
  const message = error instanceof Error ? error.message : "";
  return /forbidden|\(403\)/i.test(message);
}

function MobileMarket() {
  const summary = useMarketSummary({ freshness: true });
  const followed = useFollowedSymbols({ freshness: true });
  const { ask, askAbout } = useOpenNewChat();
  const [subject, setSubject] = useState<SymbolSheetSubject | null>(null);
  const data = summary.data;
  // One line for a day without trading; the chip is kept for movers that are really behind.
  const session = marketSessionStatus([...(data?.topGainers ?? []), ...(data?.topLosers ?? [])]);

  function openMover(mover: MarketMover) {
    const match = followed.data?.symbols.find((item) => item.symbol === mover.symbol);
    setSubject({
      symbol: mover.symbol,
      companyName: mover.name || match?.companyName,
      price: mover.latestPrice,
      changePercent: mover.changePercent,
      asOf: mover.asOf,
      externalCompanyId: match?.externalCompanyId ?? null,
      followed: Boolean(match),
    });
  }

  return (
    <MobileScreen title="بازار">
      <div className="px-4 pb-6 pt-3">
        <div className="mb-2 flex items-center gap-2">
          <h2 className="text-[13px] font-semibold text-muted-foreground">وضعیت شاخص‌ها</h2>
          <span className="min-w-0 flex-1 truncate text-xs text-muted-foreground">
            {data?.asOf ? `به‌روزرسانی ${relativeTime(data.asOf)}` : ""}
          </span>
          <RefreshButton
            label="به‌روزرسانی داده بازار"
            onRefresh={() => summary.refetch()}
            busy={summary.isFetching}
          />
        </div>
        {session.label && (
          <p role="status" className="mb-2 text-xs text-muted-foreground">
            {session.label}
          </p>
        )}

        {summary.isLoading ? (
          <div
            className="grid grid-cols-1 gap-2 min-[360px]:grid-cols-2"
            aria-busy="true"
            aria-label="در حال بارگذاری بازار"
          >
            {Array.from({ length: 6 }, (_, index) => (
              <Skeleton key={index} className="h-24 rounded-2xl" />
            ))}
          </div>
        ) : !data ? (
          <div role="alert" className="rounded-2xl bg-surface p-4 text-sm ring-1 ring-hairline">
            <p>
              {isForbidden(summary.error)
                ? "دسترسی به داده بازار برای اشتراک شما فعال نیست"
                : "داده بازار در دسترس نیست"}
            </p>
            {!isForbidden(summary.error) && (
              <button
                type="button"
                onClick={() => void summary.refetch()}
                className="mt-2 min-h-11 rounded-xl bg-surface-2 px-4 font-medium focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40"
              >
                تلاش مجدد
              </button>
            )}
          </div>
        ) : (
          <>
            {data.indices.length === 0 ? (
              <p className="rounded-2xl bg-surface p-4 text-sm text-muted-foreground ring-1 ring-hairline">
                داده شاخص در دسترس نیست
              </p>
            ) : (
              <ul className="grid grid-cols-1 gap-2 min-[360px]:grid-cols-2">
                {data.indices.map((index) => (
                  <IndexCard key={index.symbol} index={index} />
                ))}
              </ul>
            )}
            <MoverList
              title="بیشترین رشد"
              movers={data.topGainers}
              session={session}
              onSelect={openMover}
            />
            <MoverList
              title="بیشترین افت"
              movers={data.topLosers}
              session={session}
              onSelect={openMover}
            />
          </>
        )}
      </div>
      <SymbolSheet
        subject={subject}
        onOpenChange={(open) => !open && setSubject(null)}
        onAsk={ask}
        onAskOther={askAbout}
      />
    </MobileScreen>
  );
}

function MoverList({
  title,
  movers,
  session,
  onSelect,
}: {
  title: string;
  movers: readonly MarketMover[];
  session: MarketSessionStatus;
  onSelect: (mover: MarketMover) => void;
}) {
  if (movers.length === 0) return null;
  return (
    <section className="mt-5" aria-label={title}>
      <h2 className="mb-1.5 text-[13px] font-semibold text-muted-foreground">{title}</h2>
      <ul className="overflow-hidden rounded-2xl bg-surface ring-1 ring-hairline">
        {movers.map((mover) => (
          <li key={mover.symbol} className="border-b border-hairline last:border-b-0">
            <button
              type="button"
              onClick={() => onSelect(mover)}
              className="flex min-h-[52px] w-full items-center gap-3 px-4 py-2 text-right active:bg-surface-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-emerald/40"
            >
              <span className="min-w-0 flex-1">
                <span className="flex items-center gap-2">
                  <bdi className="font-semibold text-foreground">{mover.symbol}</bdi>
                  {isLaggingQuote(mover, session) && <StaleChip />}
                </span>
                {mover.name && (
                  <span className="block truncate text-xs text-muted-foreground">{mover.name}</span>
                )}
              </span>
              <span className="flex flex-col items-end text-sm">
                <span className="tabular-nums text-foreground">
                  {formatNumber(mover.latestPrice)}
                </span>
                <SignedChange value={mover.changePercent} />
              </span>
              <ChevronLeft className="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
            </button>
          </li>
        ))}
      </ul>
    </section>
  );
}
