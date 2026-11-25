import { useEffect, useState } from "react";
import { useQuery, type QueryClient } from "@tanstack/react-query";
import type { ChatMessage, GroundedAnalysisHint } from "@/lib/chat.functions";
import {
  defaultChoice,
  readStoredChoice,
  storeChoice,
  type AnalysisChoice,
} from "@/lib/chat/analysis-choice";

/**
 * Change «sol-deep-analysis-web-search»: the analysis hint (prices, defaults, 163 offer) of the conversation's latest
 * answer. The conversation's messages carry it on that answer (OQ-113 a), so a reload or a conversation reopened
 * from history shows it too. The live response's hint is also kept in the query cache per conversation, with the id
 * of the answer it belongs to, so the controls never flicker away while the messages are reloaded; the new-chat page
 * stores it the same way before it opens the conversation.
 */
interface LiveAnalysisHint {
  messageId: string;
  hint: GroundedAnalysisHint;
}

const liveHintKey = (threadId: string) => ["grounded-analysis-hint", threadId] as const;

function rememberLiveHint(
  queryClient: QueryClient,
  threadId: string,
  assistantMessageId: string,
  hint: GroundedAnalysisHint | undefined,
) {
  const value: LiveAnalysisHint | null = hint ? { messageId: assistantMessageId, hint } : null;
  queryClient.setQueryData(liveHintKey(threadId), () => value);
}

/** Keeps the hint of a send result's answer (or clears it when the answer has none). */
export function rememberAnswerHint(
  queryClient: QueryClient,
  result: { threadId: string; aiMsg?: ChatMessage },
) {
  const answer = result.aiMsg;
  if (!answer) return;
  rememberLiveHint(
    queryClient,
    result.threadId,
    answer.id,
    "groundedAnalysis" in answer.content ? answer.content.groundedAnalysis : undefined,
  );
}

/** The hint of the conversation's latest answer (saved first, else live), or null when that answer has none. */
export function useLiveAnalysisHint(
  threadId: string,
  messages: readonly ChatMessage[],
): { messageId: string; hint: GroundedAnalysisHint } | null {
  const { data } = useQuery<LiveAnalysisHint | null>({
    queryKey: liveHintKey(threadId),
    queryFn: () => null,
    enabled: false,
    staleTime: Infinity,
  });
  const latestAnswer = [...messages].reverse().find((message) => message.role === "assistant");
  if (!latestAnswer) return null;
  const saved =
    "groundedAnalysis" in latestAnswer.content ? latestAnswer.content.groundedAnalysis : undefined;
  if (saved) return { messageId: latestAnswer.id, hint: saved };
  return data && latestAnswer.id === data.messageId ? data : null;
}

/**
 * The viewer's depth and web choice: the remembered one, else the backend defaults. Storage is read after mount, so
 * the server render and the first client render agree.
 */
export function useAnalysisChoice(
  hint: GroundedAnalysisHint | null | undefined,
  reportKey?: string,
): [AnalysisChoice, (choice: AnalysisChoice) => void] {
  const [stored, setStored] = useState<AnalysisChoice | null>(null);
  const [reportChoice, setReportChoice] = useState<{ key: string; choice: AnalysisChoice }>();
  useEffect(() => {
    setStored(readStoredChoice());
  }, []);
  useEffect(() => {
    setReportChoice((previous) => (previous?.key === reportKey ? previous : undefined));
  }, [reportKey]);

  if (reportKey) {
    return [
      reportChoice?.key === reportKey ? reportChoice.choice : defaultChoice(hint),
      (choice) => setReportChoice({ key: reportKey, choice }),
    ];
  }
  return [
    stored ?? defaultChoice(hint),
    (choice) => {
      setStored(choice);
      storeChoice(choice);
    },
  ];
}
