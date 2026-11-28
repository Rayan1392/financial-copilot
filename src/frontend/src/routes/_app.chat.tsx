import { createFileRoute, useNavigate } from "@tanstack/react-router";
import { PromptInput } from "@/components/app/prompt-input";
import { MessageList } from "@/components/app/message-list";
import { NewChatIntroduction } from "@/components/app/new-chat-introduction";
import { StarterCardGrid } from "@/components/app/starter-card-grid";
import { StarterSymbolPickerDialog } from "@/components/app/starter-symbol-picker-dialog";
import { useMemo, useState } from "react";
import { useNewChatIntent } from "@/lib/chat/new-chat-intent";
import { homeStarterPrompts, starterPickerSymbols } from "@/lib/chat/starter-prompts";
import { useStarterSelection } from "@/lib/chat/use-starter-selection";
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
  // The starter cards; an optional card (the valuation gauges) is shown only while the API reports its capability
  // available. A symbol card opens the symbol picker, which offers the watchlist symbols first.
  const followed = useFollowedSymbols();
  const { valuationGaugeCards, optionComparisonStarterCard } = useChatCapabilityAvailability();
  const starterPrompts = useMemo(
    () => homeStarterPrompts({ valuationGaugeCards, optionComparisonStarterCard }),
    [valuationGaugeCards, optionComparisonStarterCard],
  );
  const watchlistSymbols = useMemo(
    () => starterPickerSymbols(followed.data?.symbols ?? []),
    [followed.data],
  );
  const starter = useStarterSelection(submit);

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
            <NewChatIntroduction />
            <StarterCardGrid
              prompts={starterPrompts}
              disabled={startChat.isPending}
              onSelect={starter.select}
            />
          </div>
        )}
      </div>
      <StarterSymbolPickerDialog
        prompt={starter.prompt}
        open={starter.open}
        watchlistSymbols={watchlistSymbols}
        onPick={starter.pick}
        onClose={starter.close}
        onCloseAutoFocus={starter.restoreFocus}
      />
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
