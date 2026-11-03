import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useServerFn } from "@tanstack/react-start";
import { sendChatMessage } from "@/lib/chat.functions";
import { ChatSendError, classifyClientError, type ChatFailure } from "@/lib/chat/chat-errors";
import { initialRecovery, type SendRecovery } from "@/lib/chat/send-recovery";

interface StartChatVariables {
  message: string;
  suggestedActionId?: string;
  idempotencyKey: string;
}

export interface StartChatFailure extends ChatFailure {
  /** The question that failed, kept so it is never lost (spec AC-06). */
  draft: string;
  recovery: SendRecovery;
}

/**
 * Starts a new conversation with its first question (shared by desktop `/chat` and mobile `/m`).
 * Each submit uses a fresh idempotency key; nothing here ever resends a failed request (decision D-A1).
 */
export function useStartChat({
  navigateToThread,
}: {
  navigateToThread: (threadId: string) => void;
}) {
  const qc = useQueryClient();
  const send = useServerFn(sendChatMessage);
  const [pendingMessage, setPendingMessage] = useState<string | null>(null);
  const [failure, setFailure] = useState<StartChatFailure | null>(null);

  const mutation = useMutation({
    mutationFn: async ({ message, suggestedActionId, idempotencyKey }: StartChatVariables) => {
      const response = await send({
        data: { message, scannerPage: 1, suggestedActionId, idempotencyKey },
      });
      if (!response.ok) throw new ChatSendError(response.failure.code, response.failure.message);
      return response.result.threadId;
    },
    onSuccess: (threadId) => {
      void qc.invalidateQueries({ queryKey: ["usage"] });
      void qc.invalidateQueries({ queryKey: ["threads"] });
      navigateToThread(threadId);
    },
    onError: (error: Error, variables) => {
      const classified = classifyClientError(error);
      const recovery = initialRecovery(
        { draft: variables.message, conversationId: null, knownMessageIds: [] },
        classified.code,
      );
      setPendingMessage(null);
      setFailure({ ...classified, draft: variables.message, recovery });
      void qc.invalidateQueries({ queryKey: ["usage"] });
      // The failed request may still have created a conversation; refresh history so it can be found there.
      if (recovery.kind === "indeterminate") void qc.invalidateQueries({ queryKey: ["threads"] });
    },
  });

  function submit(text: string, suggestedActionId?: string) {
    if (mutation.isPending) return;
    setFailure(null);
    setPendingMessage(text);
    mutation.mutate({ message: text, suggestedActionId, idempotencyKey: crypto.randomUUID() });
  }

  return {
    submit,
    isPending: mutation.isPending,
    pendingMessage,
    failure,
    clearFailure: () => setFailure(null),
  };
}
