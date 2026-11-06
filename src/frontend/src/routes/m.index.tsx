import { createFileRoute, useRouter } from "@tanstack/react-router";
import { useEffect, useState } from "react";
import { ChevronLeft } from "lucide-react";
import { MobileScreen } from "@/components/mobile/mobile-screen";
import { MobileComposer } from "@/components/mobile/mobile-composer";
import { ChatErrorCard } from "@/components/mobile/chat-error-card";
import { MobileUserBubble, PendingAnswer } from "@/components/mobile/pending-exchange";
import { SymbolSheet, type SymbolSheetSubject } from "@/components/mobile/symbol-sheet";
import { InstallCard } from "@/components/mobile/install-card";
import { homeStarterPrompts } from "@/lib/chat/starter-prompts";
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

const VISIBLE_STARTERS = 4;

function MobileNewChat() {
  const router = useRouter();
  const online = useOnlineStatus();
  const [draft, setDraft] = useState("");
  const [showAllStarters, setShowAllStarters] = useState(false);
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
  const starterSet = homeStarterPrompts(watchlist.followed);
  const allStarters = starterSet.prompts;
  const starters = showAllStarters ? allStarters : allStarters.slice(0, VISIBLE_STARTERS);
  const symbols = watchlist.join?.rows ?? [];

  return (
    <MobileScreen
      title="ساپیو"
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
        <div className="mx-auto max-w-xl px-4 py-6">
          <div className="mb-5 flex items-center gap-2">
            <span aria-hidden="true" className="size-2.5 rounded-full bg-emerald" />
            <h2 className="text-base font-semibold text-foreground">چه چیزی را بررسی کنیم؟</h2>
          </div>
          <ul className="overflow-hidden rounded-2xl bg-surface ring-1 ring-hairline">
            {starters.map((prompt) => (
              <li key={prompt.text} className="border-b border-hairline last:border-b-0">
                <button
                  type="button"
                  onClick={() => submit(prompt.text)}
                  disabled={startChat.isPending || !online}
                  className="flex min-h-[52px] w-full items-center gap-3 px-4 py-2 text-right text-[15px] leading-7 text-foreground active:bg-surface-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-emerald/40 disabled:opacity-50"
                >
                  <prompt.icon className="size-4 shrink-0 text-emerald" aria-hidden="true" />
                  <span className="min-w-0 flex-1">{prompt.text}</span>
                  <ChevronLeft
                    className="size-4 shrink-0 text-muted-foreground"
                    aria-hidden="true"
                  />
                </button>
              </li>
            ))}
          </ul>
          {!showAllStarters && allStarters.length > VISIBLE_STARTERS && (
            <button
              type="button"
              onClick={() => setShowAllStarters(true)}
              className="mt-2 min-h-11 rounded-xl px-3 text-sm font-medium text-emerald focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40"
            >
              بیشتر
            </button>
          )}
          {symbols.length > 0 && (
            <section aria-labelledby="my-symbols" className="mt-6">
              <h3 id="my-symbols" className="mb-2 text-[13px] font-semibold text-muted-foreground">
                نمادهای دیده‌بان من
              </h3>
              <div className="flex flex-wrap gap-2">
                {symbols.slice(0, 8).map((row) => (
                  <button
                    key={row.symbol}
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
                    className="min-h-11 rounded-full bg-surface px-4 text-sm text-foreground ring-1 ring-hairline active:bg-surface-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40"
                  >
                    <bdi>{row.symbol}</bdi>
                  </button>
                ))}
              </div>
            </section>
          )}
          <InstallCard />
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
