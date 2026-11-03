import { createFileRoute } from "@tanstack/react-router";
import { MessageList } from "@/components/app/message-list";
import { PromptInput } from "@/components/app/prompt-input";
import { useEffect, useRef } from "react";
import { chatErrorMessage } from "@/lib/chat/chat-errors";
import { useChatThread } from "@/lib/chat/use-chat-thread";

export const Route = createFileRoute("/_app/c/$threadId")({
  component: ChatThreadPage,
});

function ChatThreadPage() {
  const { threadId } = Route.useParams();
  const scrollRef = useRef<HTMLDivElement>(null);
  const thread = useChatThread(threadId);
  const { messages, isLoading, submit, showDiagnostics } = thread;
  const queryError = thread.failure ? chatErrorMessage(thread.failure) : null;

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight, behavior: "smooth" });
  }, [messages.length, thread.isPending]);

  return (
    <>
      <div ref={scrollRef} className="flex-1 overflow-y-auto scrollbar-thin">
        <MessageList
          messages={messages}
          loading={isLoading}
          streaming={thread.isPending}
          onSuggested={submit}
          onPageChange={thread.changeScannerPage}
          onDisclosurePageChange={thread.changeDisclosurePage}
          showDiagnostics={showDiagnostics}
          followedSymbols={thread.followedSymbols}
        />
      </div>
      {queryError && (
        <div className="mx-4 mb-2 rounded-lg border border-destructive/30 bg-destructive/10 px-4 py-2.5 text-sm text-destructive text-right">
          {queryError}
        </div>
      )}
      <PromptInput
        onSubmit={submit}
        loading={thread.isPending}
        showAssistedQuery
      />
    </>
  );
}
