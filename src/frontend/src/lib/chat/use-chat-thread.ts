import { useEffect, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useServerFn } from "@tanstack/react-start";
import { getThreadMessages, sendChatMessage, type ChatMessage } from "@/lib/chat.functions";
import { getFollowedSymbols } from "@/lib/followed-symbols.functions";
import { getAuthenticatedUser } from "@/integrations/financial-copilot/auth";
import {
  adminPermissions,
  hasPermission,
} from "@/integrations/financial-copilot/admin-permissions";
import { ChatSendError, classifyClientError, type ChatFailure } from "@/lib/chat/chat-errors";
import {
  initialRecovery,
  reconcileAfterRefetch,
  type SendAttempt,
  type SendRecovery,
} from "@/lib/chat/send-recovery";

interface SendVariables {
  message: string;
  scannerPage?: number;
  scannerPageSize?: number;
  disclosurePage?: number;
  suggestedActionId?: string;
  idempotencyKey: string;
  knownMessageIds: readonly string[];
}

/** The question being sent, with the message IDs the conversation had when it was sent. */
interface PendingQuestion {
  text: string;
  knownMessageIds: readonly string[];
}

/**
 * The pending question is shown until the loaded conversation holds a user message that was not there when
 * it was sent — its persisted copy. Deriving this from the loaded messages (instead of clearing it when the
 * send succeeds) means the question is never missing or doubled while the answer is loaded (BUG-023).
 */
function pendingQuestionText(
  pending: PendingQuestion | null,
  messages: readonly ChatMessage[],
): string | null {
  if (!pending) return null;
  const persisted = messages.some(
    (message) => message.role === "user" && !pending.knownMessageIds.includes(message.id),
  );
  return persisted ? null : pending.text;
}

export interface ThreadSendFailure extends ChatFailure {
  draft: string;
  recovery: SendRecovery;
  /** Kept so «بررسی دوباره» compares against the same pre-send message IDs. */
  attempt: SendAttempt;
}

/**
 * Loads one conversation and sends follow-ups (shared by desktop `/c/$threadId` and mobile `/m/c/$threadId`).
 *
 * With `reconcileOnFailure`, an indeterminate failure refetches the conversation and reconciles it before any
 * resend is offered (spec AC-06b). Desktop leaves it off and keeps its earlier behavior.
 */
export function useChatThread(threadId: string, { reconcileOnFailure = false } = {}) {
  const qc = useQueryClient();
  const fetchMessages = useServerFn(getThreadMessages);
  const fetchFollowed = useServerFn(getFollowedSymbols);
  const send = useServerFn(sendChatMessage);
  const lastMessageRef = useRef<string>("");
  const [failure, setFailure] = useState<ThreadSendFailure | null>(null);
  const [pending, setPending] = useState<PendingQuestion | null>(null);
  const [showDiagnostics, setShowDiagnostics] = useState(false);

  useEffect(() => {
    let active = true;
    getAuthenticatedUser().then((user) => {
      if (active)
        setShowDiagnostics(hasPermission(user, adminPermissions.orchestrationDiagnostics));
    });
    return () => {
      active = false;
    };
  }, []);

  const messagesQuery = useQuery({
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

  async function reconcile(attempt: SendAttempt) {
    setFailure((current) =>
      current ? { ...current, recovery: { kind: "reconciling" } } : current,
    );
    const refetched = await messagesQuery.refetch();
    const recovery: SendRecovery =
      refetched.isSuccess && refetched.data
        ? reconcileAfterRefetch(attempt, refetched.data)
        : { kind: "reconcile_failed" };
    if (recovery.kind === "answered") {
      setFailure(null);
      void qc.invalidateQueries({ queryKey: ["threads"] });
      return;
    }
    setFailure((current) => (current ? { ...current, recovery } : current));
  }

  const sendMutation = useMutation({
    mutationFn: async ({ knownMessageIds: _known, ...variables }: SendVariables) => {
      const response = await send({
        data: {
          threadId,
          message: variables.message,
          scannerPage: variables.scannerPage ?? 1,
          scannerPageSize: variables.scannerPageSize ?? 20,
          disclosurePage: variables.disclosurePage ?? 1,
          suggestedActionId: variables.suggestedActionId,
          idempotencyKey: variables.idempotencyKey,
        },
      });
      if (!response.ok) throw new ChatSendError(response.failure.code, response.failure.message);
      return response.result;
    },
    onSuccess: async () => {
      setFailure(null);
      qc.invalidateQueries({ queryKey: ["threads"] });
      qc.invalidateQueries({ queryKey: ["subscription"] });
      qc.invalidateQueries({ queryKey: ["usage"] });
      // Stay pending until the answer is loaded, so the progress indicator has no gap before it (BUG-023).
      await qc.invalidateQueries({ queryKey: ["messages", threadId] });
    },
    onError: (error: Error, variables) => {
      const classified = classifyClientError(error);
      const attempt: SendAttempt = {
        draft: variables.message,
        conversationId: threadId,
        knownMessageIds: variables.knownMessageIds,
      };
      const recovery = initialRecovery(attempt, classified.code);
      setPending(null);
      setFailure({ ...classified, draft: variables.message, recovery, attempt });
      void qc.invalidateQueries({ queryKey: ["usage"] });
      if (reconcileOnFailure && recovery.kind === "reconciling") void reconcile(attempt);
    },
  });

  function knownIds(): string[] {
    return (messagesQuery.data ?? []).map((message: ChatMessage) => message.id);
  }

  function mutate(
    variables: Omit<SendVariables, "idempotencyKey" | "knownMessageIds">,
    showAsPending: boolean,
  ) {
    const knownMessageIds = knownIds();
    setFailure(null);
    setPending(showAsPending ? { text: variables.message, knownMessageIds } : null);
    sendMutation.mutate({
      ...variables,
      idempotencyKey: crypto.randomUUID(),
      knownMessageIds,
    });
  }

  function submit(text: string, suggestedActionId?: string) {
    if (sendMutation.isPending) return;
    lastMessageRef.current = text;
    mutate({ message: text, suggestedActionId }, true);
  }

  function changeScannerPage(page: number, originalQuery?: string, pageSize?: number) {
    if (originalQuery) lastMessageRef.current = originalQuery;
    if (!lastMessageRef.current) return;
    mutate(
      { message: lastMessageRef.current, scannerPage: page, scannerPageSize: pageSize },
      false,
    );
  }

  function changeDisclosurePage(page: number, originalQuery: string) {
    lastMessageRef.current = originalQuery;
    mutate({ message: originalQuery, disclosurePage: page }, false);
  }

  /** «بررسی دوباره»: repeats only the refetch, never the send. */
  function recheck() {
    if (!failure || failure.recovery.kind !== "reconcile_failed") return;
    void reconcile(failure.attempt);
  }

  const messages = messagesQuery.data ?? [];

  return {
    messages,
    isLoading: messagesQuery.isLoading,
    loadError: messagesQuery.isError ? messagesQuery.error : null,
    refetchMessages: () => messagesQuery.refetch(),
    followedSymbols: new Set(followed?.symbols.map((item) => item.symbol) ?? []),
    showDiagnostics,
    isPending: sendMutation.isPending,
    pendingMessage: pendingQuestionText(pending, messages),
    failure,
    clearFailure: () => setFailure(null),
    submit,
    changeScannerPage,
    changeDisclosurePage,
    recheck,
  };
}
