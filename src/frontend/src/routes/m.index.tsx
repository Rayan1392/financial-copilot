import { createFileRoute, useRouter } from "@tanstack/react-router";
import { useEffect, useState } from "react";
import { MobileScreen } from "@/components/mobile/mobile-screen";
import { MobileComposer } from "@/components/mobile/mobile-composer";
import { ChatErrorCard } from "@/components/mobile/chat-error-card";
import { MobileUserBubble, PendingAnswer } from "@/components/mobile/pending-exchange";
import { SymbolSheet, type SymbolSheetSubject } from "@/components/mobile/symbol-sheet";
import { InstallCard } from "@/components/mobile/install-card";
import { IconTile } from "@/components/mobile/icon-tile";
import {
  BalanceSheetIcon,
  MonthlySalesIcon,
  ProductTrendIcon,
  QuarterlyReportIcon,
  RankingIcon,
  SalesMixIcon,
  type MobileIcon,
} from "@/components/mobile/icons";
import { SignedChange } from "@/components/mobile/signed-change";
import { ReleaseVersionLink } from "@/components/release-notes/release-version-link";
import { homeStarterPrompts, type StarterPromptId } from "@/lib/chat/starter-prompts";
import { formatNumber } from "@/lib/format/persian";
import { useStartChat } from "@/lib/chat/use-start-chat";
import { useNewChatIntent, useOpenNewChat } from "@/lib/chat/new-chat-intent";
import { restoresDraftImmediately } from "@/lib/chat/send-recovery";
import { useWatchlistWithNames } from "@/lib/workspace/queries";
import { useOnlineStatus } from "@/hooks/use-online-status";
import { markAnswered } from "@/lib/pwa/install";
import { cn } from "@/lib/utils";

export const Route = createFileRoute("/m/")({
  head: () => ({ meta: [{ title: "گفتگو — ساپیو" }] }),
  component: MobileNewChat,
});

/** Phone icon of each starter capability; the shared prompt list carries the desktop icons. */
const STARTER_ICONS: Record<StarterPromptId, MobileIcon> = {
  "sales-mix": SalesMixIcon,
  "monthly-sales": MonthlySalesIcon,
  "product-trend": ProductTrendIcon,
  ranking: RankingIcon,
  "quarterly-report": QuarterlyReportIcon,
  "balance-sheet": BalanceSheetIcon,
};
const WATCHLIST_STRIP_LIMIT = 8;

function MobileNewChat() {
  const router = useRouter();
  const online = useOnlineStatus();
  const [draft, setDraft] = useState("");
  const [sheetSubject, setSheetSubject] = useState<SymbolSheetSubject | null>(null);
  const { askAbout } = useOpenNewChat();
  const watchlist = useWatchlistWithNames({ freshness: true });
  const startChat = useStartChat({
    navigateToThread: (threadId) => {
      markAnswered();
      void router.navigate({ to: "/m/c/$threadId", params: { threadId }, replace: true });
    },
  });
  // Consume a router-state intent once per history entry, then clear it so Back never re-submits a question.
  useNewChatIntent("/m", ({ submitQuestion, prefillQuestion, newChat }) => {
    if (newChat) {
      startChat.clearFailure();
      setDraft("");
    }
    if (prefillQuestion) setDraft(prefillQuestion);
    if (submitQuestion) submit(submitQuestion);
  });

  // A failed first message goes straight back into the composer (spec AC-06a); it is never lost.
  useEffect(() => {
    if (startChat.failure && restoresDraftImmediately(startChat.failure.recovery)) {
      setDraft((current) => (current.trim() ? current : startChat.failure!.draft));
    }
  }, [startChat.failure]);

  function submit(text: string) {
    setDraft("");
    startChat.submit(text);
  }

  const failure = startChat.failure;
  const exchangeText = startChat.pendingMessage ?? failure?.draft ?? null;
  // The fixed six cards; watchlist personalization is switched off in starter-prompts.ts.
  const starters = homeStarterPrompts(watchlist.followed).prompts;
  const symbols = watchlist.join?.rows ?? [];

  return (
    <MobileScreen
      title="ساپیو"
      hideNewChat
      showTabBar={!startChat.isPending}
      footer={
        <MobileComposer
          value={draft}
          onChange={setDraft}
          onSubmit={submit}
          pending={startChat.isPending}
          online={online}
        />
      }
    >
      {exchangeText !== null ? (
        <div className="space-y-5 py-5">
          <MobileUserBubble
            text={exchangeText}
            uncertain={failure?.recovery.kind === "indeterminate"}
          />
          {startChat.isPending && <PendingAnswer />}
          {failure && <ChatErrorCard failure={failure} recovery={failure.recovery} />}
        </div>
      ) : (
        <div className="mx-auto max-w-xl px-4 py-4">
          <h2 className="mb-3 text-m-head font-bold text-foreground">چه چیزی را بررسی کنیم؟</h2>
          <ul data-starters className="flex flex-col gap-2.5">
            {starters.map((prompt) => {
              const statement = prompt.tone === "statement";
              return (
                <li key={prompt.id}>
                  {/* The question is shown in full: the cards teach how to phrase a question. */}
                  <button
                    type="button"
                    onClick={() => submit(prompt.text)}
                    disabled={startChat.isPending || !online}
                    className={cn(
                      "flex min-h-[72px] w-full items-center gap-3 rounded-2xl bg-surface bg-linear-to-l to-transparent to-65% px-3.5 py-2.5 text-right ring-1 ring-hairline transition-colors active:bg-surface-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40 disabled:opacity-50",
                      statement ? "from-gold/10" : "from-emerald/10",
                    )}
                  >
                    <IconTile
                      icon={STARTER_ICONS[prompt.id]}
                      tone={statement ? "gold" : "emerald"}
                      className="size-11"
                    />
                    <span className="min-w-0 flex-1">
                      <span
                        className={cn(
                          "block text-m-cap font-bold",
                          statement ? "mobile-gold-text" : "text-emerald",
                        )}
                      >
                        {prompt.topic}
                      </span>
                      <span className="block text-balance text-m-sub font-medium leading-7 text-foreground">
                        {prompt.text}
                      </span>
                    </span>
                  </button>
                </li>
              );
            })}
          </ul>
          {symbols.length > 0 && (
            <section aria-labelledby="my-symbols" className="mt-5">
              <h3 id="my-symbols" className="mb-2 text-m-title font-bold text-muted-foreground">
                دیده‌بان من
              </h3>
              <ul className="relative -mx-4 flex gap-2.5 overflow-x-auto px-4 pb-1 scrollbar-hide">
                {symbols.slice(0, WATCHLIST_STRIP_LIMIT).map((row) => (
                  <li key={row.symbol} className="shrink-0">
                    <button
                      type="button"
                      onClick={() =>
                        setSheetSubject({
                          symbol: row.symbol,
                          companyName: row.companyName,
                          price: row.latestPrice,
                          changePercent: row.changePercent,
                          asOf: row.asOf,
                          externalCompanyId: row.externalCompanyId,
                          followed: true,
                        })
                      }
                      className="flex min-h-[92px] min-w-[128px] flex-col items-start gap-0.5 rounded-2xl bg-surface px-3.5 py-3 text-right ring-1 ring-hairline active:bg-surface-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-emerald/40"
                    >
                      <bdi className="text-m-body font-bold leading-7 text-foreground">
                        {row.symbol}
                      </bdi>
                      <span className="text-m-sub tabular-nums text-foreground">
                        {row.latestPrice == null ? "–" : formatNumber(row.latestPrice)}
                      </span>
                      <SignedChange value={row.changePercent} className="text-m-sub" />
                    </button>
                  </li>
                ))}
              </ul>
            </section>
          )}
          <InstallCard />
          <div className="mt-6 flex justify-center">
            <ReleaseVersionLink className="text-m-cap" />
          </div>
        </div>
      )}
      <SymbolSheet
        subject={sheetSubject}
        onOpenChange={(open) => !open && setSheetSubject(null)}
        onAsk={submit}
        onAskOther={(symbol) => {
          setSheetSubject(null);
          askAbout(symbol);
        }}
      />
    </MobileScreen>
  );
}
