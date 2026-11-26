import { createFileRoute, useRouter } from "@tanstack/react-router";
import { useEffect, useState } from "react";
import { MobileScreen } from "@/components/mobile/mobile-screen";
import { MobileComposer } from "@/components/mobile/mobile-composer";
import { ChatErrorCard } from "@/components/mobile/chat-error-card";
import { MobileUserBubble, PendingAnswer } from "@/components/mobile/pending-exchange";
import { SymbolSheet, type SymbolSheetSubject } from "@/components/mobile/symbol-sheet";
import { InstallCard } from "@/components/mobile/install-card";
import { MobileStarterList } from "@/components/mobile/mobile-starter-list";
import { SignedChange } from "@/components/mobile/signed-change";
import { ReleaseVersionLink } from "@/components/release-notes/release-version-link";
import { homeStarterPrompts } from "@/lib/chat/starter-prompts";
import { useChatCapabilityAvailability } from "@/lib/chat/use-capability-availability";
import { formatNumber } from "@/lib/format/persian";
import { useStartChat } from "@/lib/chat/use-start-chat";
import { useNewChatIntent, useOpenNewChat } from "@/lib/chat/new-chat-intent";
import { restoresDraftImmediately } from "@/lib/chat/send-recovery";
import { useWatchlistWithNames } from "@/lib/workspace/queries";
import { useOnlineStatus } from "@/hooks/use-online-status";
import { markAnswered } from "@/lib/pwa/install";

export const Route = createFileRoute("/m/")({
  head: () => ({ meta: [{ title: "گفتگو — ساپیو" }] }),
  component: MobileNewChat,
});

const WATCHLIST_STRIP_LIMIT = 8;

function MobileNewChat() {
  const router = useRouter();
  const online = useOnlineStatus();
  const [draft, setDraft] = useState("");
  const [sheetSubject, setSheetSubject] = useState<SymbolSheetSubject | null>(null);
  const { askAbout } = useOpenNewChat();
  const watchlist = useWatchlistWithNames({ freshness: true });
  const { valuationGaugeCards } = useChatCapabilityAvailability();
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
  // The fixed starter cards; watchlist personalization is switched off in starter-prompts.ts. An optional card (the
  // valuation gauges) is shown only while the API reports its capability available.
  const starters = homeStarterPrompts(watchlist.followed, { valuationGaugeCards }).prompts;
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
          <MobileStarterList
            prompts={starters}
            disabled={startChat.isPending || !online}
            onSelect={submit}
          />
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
                      <span className="text-m-sub tabular-nums font-fanum text-foreground">
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
