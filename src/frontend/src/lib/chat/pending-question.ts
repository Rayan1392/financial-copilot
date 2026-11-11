import type { ChatMessage } from "@/lib/chat.functions";

/** ID of the not-yet-persisted question; the persisted copy replaces it once the conversation is refetched. */
export const PENDING_QUESTION_ID = "pending-user-message";

/**
 * Appends the question being sent to the conversation, so the user sees it while the answer is prepared
 * (BUG-023). Returns `messages` unchanged when nothing is pending.
 */
export function withPendingQuestion(
  messages: readonly ChatMessage[],
  pendingMessage: string | null,
): ChatMessage[] {
  if (!pendingMessage) return [...messages];
  return [
    ...messages,
    { id: PENDING_QUESTION_ID, role: "user", created_at: "", content: { text: pendingMessage } },
  ];
}
