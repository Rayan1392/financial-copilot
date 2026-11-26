import { createFileRoute, useNavigate } from "@tanstack/react-router";
import { PromptInput } from "@/components/app/prompt-input";
import { MessageList } from "@/components/app/message-list";
import { StarterCardGrid } from "@/components/app/starter-card-grid";
import { useMemo, useState } from "react";
import { useNewChatIntent } from "@/lib/chat/new-chat-intent";
import { homeStarterPrompts, starterCoverage } from "@/lib/chat/starter-prompts";
import { useChatCapabilityAvailability } from "@/lib/chat/use-capability-availability";
import { useFollowedSymbols } from "@/lib/workspace/queries";
import { ChatErrorNotice } from "@/components/app/chat-error-notice";
import { useStartChat } from "@/lib/chat/use-start-chat";
import { withPendingQuestion } from "@/lib/chat/pending-question";

export const Route = createFileRoute("/_app/chat")({
  component: NewChatPage,
});

function NewChatPage() {
  const navigate = useNavigate();
  const startChat = useStartChat({
    navigateToThread: (threadId) => navigate({ to: "/c/$threadId", params: { threadId } }),
  });
  const { submit, pendingMessage } = startChat;
  // A symbol row in the market column sends its question here: submit it, or seed the composer with it.
  const [seed, setSeed] = useState({ text: "", version: 0 });
  useNewChatIntent("/chat", ({ submitQuestion, prefillQuestion }) => {
    if (prefillQuestion)
      setSeed((current) => ({ text: prefillQuestion, version: current.version + 1 }));
    if (submitQuestion) submit(submitQuestion);
  });
  // The fixed starter cards; watchlist personalization is switched off in starter-prompts.ts. An optional card (the
  // valuation gauges) is shown only while the API reports its capability available.
  const followed = useFollowedSymbols();
  const { valuationGaugeCards } = useChatCapabilityAvailability();
  const starterSet = useMemo(
    () => homeStarterPrompts(followed.data?.symbols ?? [], { valuationGaugeCards }),
    [followed.data, valuationGaugeCards],
  );
  const starterPrompts = starterSet.prompts;
  const coverage = starterCoverage(starterPrompts);

  return (
    <>
      {/* The intro scrolls in its own region so the composer below stays pinned on short viewports
          (same structure as the thread page). m-auto centers without clipping overflowing content. */}
      <div
        className={`min-h-0 flex-1 overflow-y-auto scrollbar-thin ${pendingMessage ? "" : "flex p-8"}`}
      >
        {pendingMessage ? (
          <MessageList
            messages={withPendingQuestion([], pendingMessage)}
            loading={false}
            streaming={startChat.isPending}
            onSuggested={submit}
          />
        ) : (
          <div
            dir="rtl"
            className="m-auto max-w-3xl w-full py-6 md:py-10 text-center animate-fade-up"
          >
            <h1 className="text-2xl md:text-3xl font-bold text-foreground mb-3 text-balance">
              دستیار هوشمند بازار سرمایه
            </h1>
            <div className="inline-flex items-center rounded-full border border-emerald/20 bg-emerald-soft/60 px-3 py-1 text-xs font-medium text-emerald mb-5">
              {coverage.badge}
            </div>
            <p className="text-base md:text-lg font-medium text-foreground mb-2 max-w-2xl mx-auto text-pretty">
              گزارش‌های تولید و فروش و صورت‌های مالی شرکت‌ها را با زبان طبیعی بررسی کنید.
            </p>
            <p className="text-sm leading-7 text-muted-foreground mb-7 max-w-2xl mx-auto text-pretty">
              {coverage.sentence}
            </p>
            {starterSet.personalized && (
              <p className="mb-3 text-right text-xs text-muted-foreground">
                پیشنهادها بر اساس نمادهای دیده‌بان شما
              </p>
            )}
            <StarterCardGrid
              prompts={starterPrompts}
              disabled={startChat.isPending}
              onSelect={submit}
            />
          </div>
        )}
      </div>
      <ChatErrorNotice failure={startChat.failure} />
      <PromptInput
        key={seed.version}
        initialValue={seed.text}
        onSubmit={submit}
        loading={startChat.isPending}
        showAssistedQuery
        showReleaseVersion
      />
    </>
  );
}
