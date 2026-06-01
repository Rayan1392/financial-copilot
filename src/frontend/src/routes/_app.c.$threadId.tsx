import { createFileRoute } from "@tanstack/react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useServerFn } from "@tanstack/react-start";
import { getThreadMessages, sendChatMessage } from "@/lib/chat.functions";
import { MessageList } from "@/components/app/message-list";
import { PromptInput } from "@/components/app/prompt-input";
import { useEffect, useRef } from "react";

export const Route = createFileRoute("/_app/c/$threadId")({
  component: ChatThreadPage,
});

function ChatThreadPage() {
  const { threadId } = Route.useParams();
  const qc = useQueryClient();
  const fetchMessages = useServerFn(getThreadMessages);
  const send = useServerFn(sendChatMessage);
  const scrollRef = useRef<HTMLDivElement>(null);

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
      qc.invalidateQueries({ queryKey: ["messages", threadId] });
      qc.invalidateQueries({ queryKey: ["threads"] });
      qc.invalidateQueries({ queryKey: ["subscription"] });
    },
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
          onSuggested={(q) => sendMutation.mutate(q)}
        />
      </div>
      <PromptInput
        onSubmit={(text) => sendMutation.mutate(text)}
        loading={sendMutation.isPending}
      />
    </>
  );
}
