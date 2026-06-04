import { createFileRoute } from "@tanstack/react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useServerFn } from "@tanstack/react-start";
import { getThreadMessages, sendChatMessage } from "@/lib/chat.functions";
import { MessageList } from "@/components/app/message-list";
import { PromptInput } from "@/components/app/prompt-input";
import { useEffect, useRef, useState } from "react";

export const Route = createFileRoute("/_app/c/$threadId")({
  component: ChatThreadPage,
});

function chatErrorMessage(error: Error): string {
  if (error.message.toLowerCase().includes("insufficient"))
    return "اعتبار کافی برای پردازش درخواست وجود ندارد. لطفاً حساب خود را شارژ کنید.";
  return "متأسفیم، خطایی در پردازش درخواست رخ داد. لطفاً دوباره امتحان کنید.";
}

function ChatThreadPage() {
  const { threadId } = Route.useParams();
  const qc = useQueryClient();
  const fetchMessages = useServerFn(getThreadMessages);
  const send = useServerFn(sendChatMessage);
  const scrollRef = useRef<HTMLDivElement>(null);
  const [queryError, setQueryError] = useState<string | null>(null);

  const { data: messages = [], isLoading } = useQuery({
    queryKey: ["messages", threadId],
    queryFn: () => fetchMessages({ data: { threadId } }),
    retry: false,
    throwOnError: false,
    refetchOnWindowFocus: false,
  });

  const sendMutation = useMutation({
    mutationFn: (message: string) => send({ data: { threadId, message } }),
    onSuccess: () => {
      setQueryError(null);
      qc.invalidateQueries({ queryKey: ["messages", threadId] });
      qc.invalidateQueries({ queryKey: ["threads"] });
      qc.invalidateQueries({ queryKey: ["subscription"] });
    },
    onError: (error: Error) => setQueryError(chatErrorMessage(error)),
  });

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight, behavior: "smooth" });
  }, [messages.length, sendMutation.isPending]);

  return (
    <>
      <div ref={scrollRef} className="flex-1 overflow-y-auto scrollbar-thin">
        <MessageList
          messages={messages}
          loading={isLoading}
          streaming={sendMutation.isPending}
          onSuggested={(q) => { setQueryError(null); sendMutation.mutate(q); }}
        />
      </div>
      {queryError && (
        <div className="mx-4 mb-2 rounded-lg border border-destructive/30 bg-destructive/10 px-4 py-2.5 text-sm text-destructive text-right">
          {queryError}
        </div>
      )}
      <PromptInput
        onSubmit={(text) => { setQueryError(null); sendMutation.mutate(text); }}
        loading={sendMutation.isPending}
      />
    </>
  );
}
