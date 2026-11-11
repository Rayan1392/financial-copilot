import { createFileRoute } from "@tanstack/react-router";
import { MessageList } from "@/components/app/message-list";
import { PromptInput } from "@/components/app/prompt-input";
import { useEffect, useRef } from "react";
import { ChatErrorNotice } from "@/components/app/chat-error-notice";
import { useChatThread } from "@/lib/chat/use-chat-thread";
import { withPendingQuestion } from "@/lib/chat/pending-question";

export const Route = createFileRoute("/_app/c/$threadId")({
  component: ChatThreadPage,
});

function ChatThreadPage() {
  const { threadId } = Route.useParams();
  const scrollRef = useRef<HTMLDivElement>(null);
  const thread = useChatThread(threadId);
  const { isLoading, submit, showDiagnostics, pendingMessage } = thread;
  // The question being sent shows at once, not only after its answer is persisted (BUG-023).
  const messages = withPendingQuestion(thread.messages, pendingMessage);

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
      <ChatErrorNotice failure={thread.failure} />
      <PromptInput onSubmit={submit} loading={thread.isPending} showAssistedQuery />
    </>
  );
}
