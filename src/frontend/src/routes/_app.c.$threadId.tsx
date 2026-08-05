import { createFileRoute } from "@tanstack/react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useServerFn } from "@tanstack/react-start";
import { getThreadMessages, sendChatMessage } from "@/lib/chat.functions";
import { getFollowedSymbols } from "@/lib/followed-symbols.functions";
import { MessageList } from "@/components/app/message-list";
import { PromptInput } from "@/components/app/prompt-input";
import { useEffect, useRef, useState } from "react";
import { getAuthenticatedUser } from "@/integrations/financial-copilot/auth";
import { adminPermissions, hasPermission } from "@/integrations/financial-copilot/admin-permissions";

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
  const fetchFollowed = useServerFn(getFollowedSymbols);
  const send = useServerFn(sendChatMessage);
  const scrollRef = useRef<HTMLDivElement>(null);
  const [queryError, setQueryError] = useState<string | null>(null);
  const lastMessageRef = useRef<string>("");
  const [showDiagnostics, setShowDiagnostics] = useState(false);

  useEffect(() => {
    getAuthenticatedUser().then((user) => {
      setShowDiagnostics(hasPermission(user, adminPermissions.orchestrationDiagnostics));
    });
  }, []);

  const { data: messages = [], isLoading } = useQuery({
    queryKey: ["messages", threadId],
    queryFn: () => fetchMessages({ data: { threadId } }),
    retry: false,
    throwOnError: false,
    refetchOnWindowFocus: false,
  });
  const { data: followed } = useQuery({
    queryKey: ["followed-symbols"],
    queryFn: () => fetchFollowed(),
    retry: false,
    throwOnError: false,
    refetchOnWindowFocus: false,
  });

  const sendMutation = useMutation({
    mutationFn: ({ message, scannerPage = 1, scannerPageSize = 20, disclosurePage = 1 }: { message: string; scannerPage?: number; scannerPageSize?: number; disclosurePage?: number }) =>
      send({ data: { threadId, message, scannerPage, scannerPageSize, disclosurePage } }),
    onSuccess: () => {
      setQueryError(null);
      qc.invalidateQueries({ queryKey: ["messages", threadId] });
      qc.invalidateQueries({ queryKey: ["threads"] });
      qc.invalidateQueries({ queryKey: ["subscription"] });
    },
    onError: (error: Error) => setQueryError(chatErrorMessage(error)),
  });

  const submit = (text: string) => {
    lastMessageRef.current = text;
    setQueryError(null);
    sendMutation.mutate({ message: text });
  };

  const handlePageChange = (page: number, originalQuery?: string, pageSize?: number) => {
    if (originalQuery) lastMessageRef.current = originalQuery;
    if (!lastMessageRef.current) return;
    setQueryError(null);
    sendMutation.mutate({ message: lastMessageRef.current, scannerPage: page, scannerPageSize: pageSize });
  };

  const handleDisclosurePageChange = (page: number, originalQuery: string) => {
    lastMessageRef.current = originalQuery;
    setQueryError(null);
    sendMutation.mutate({ message: originalQuery, disclosurePage: page });
  };

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
          onSuggested={submit}
          onPageChange={handlePageChange}
          onDisclosurePageChange={handleDisclosurePageChange}
          showDiagnostics={showDiagnostics}
          followedSymbols={new Set(followed?.symbols.map((item) => item.symbol) ?? [])}
        />
      </div>
      {queryError && (
        <div className="mx-4 mb-2 rounded-lg border border-destructive/30 bg-destructive/10 px-4 py-2.5 text-sm text-destructive text-right">
          {queryError}
        </div>
      )}
      <PromptInput
        onSubmit={submit}
        loading={sendMutation.isPending}
      />
    </>
  );
}
