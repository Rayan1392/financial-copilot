import type { ChatMessage } from "@/lib/chat.functions";
import { isIndeterminateFailure, type ChatErrorCode } from "@/lib/chat/chat-errors";

/**
 * Recovery after a failed chat send (Feature 153 T02, spec AC-06a/AC-06b, decision D-A1).
 *
 * A same-key resend currently re-runs the AI and can create a duplicate conversation, so recovery never resends
 * on its own and never reuses the original idempotency key. These functions are pure; the screens own the I/O.
 */

/** What the client knew when it sent the question. */
export interface SendAttempt {
  draft: string;
  /** Null for the first message of a new chat: the server creates the ID and returns it only on success. */
  conversationId: string | null;
  /** Message IDs of the conversation before sending; empty for a new chat. */
  knownMessageIds: readonly string[];
}

export type SendRecovery =
  /** First message, outcome unknown: the client cannot look up a conversation it was never told about. */
  | { kind: "indeterminate" }
  /** Follow-up, outcome unknown: refetching the conversation before offering anything. */
  | { kind: "reconciling" }
  /** The refetch found the answer the failed request produced. */
  | { kind: "answered" }
  /** The refetch succeeded and holds no answer for the draft; a deliberate new request may be sent. */
  | { kind: "not_found" }
  /** The refetch failed too, so the outcome is still unknown; only another check is offered. */
  | { kind: "reconcile_failed" }
  /** The server answered with a definite refusal; the question was not processed. */
  | { kind: "definite"; code: ChatErrorCode };

export function initialRecovery(attempt: SendAttempt, code: ChatErrorCode): SendRecovery {
  if (!isIndeterminateFailure(code)) return { kind: "definite", code };
  return attempt.conversationId === null ? { kind: "indeterminate" } : { kind: "reconciling" };
}

function normalizeQuestion(text: string): string {
  return text.replace(/\s+/g, " ").trim();
}

function userText(message: ChatMessage): string | null {
  return message.role === "user" && "text" in message.content ? message.content.text : null;
}

/**
 * Compares the refetched conversation with the IDs known before sending. The server persists a question and its
 * answer together, so a new user message that matches the draft and is followed by an assistant message means the
 * failed request completed.
 */
export function reconcileAfterRefetch(
  attempt: SendAttempt,
  messages: readonly ChatMessage[],
): SendRecovery {
  const known = new Set(attempt.knownMessageIds);
  const draft = normalizeQuestion(attempt.draft);
  const answered = messages.some((message, index) => {
    if (known.has(message.id)) return false;
    const text = userText(message);
    if (text === null || normalizeQuestion(text) !== draft) return false;
    return messages.slice(index + 1).some((next) => next.role === "assistant");
  });
  return answered ? { kind: "answered" } : { kind: "not_found" };
}

export interface RecoveryActions {
  /** «ویرایش و ارسال دوباره»: moves the draft into the composer; sending it is a new request with a new key. */
  editAndResend: boolean;
  /** «بررسی دوباره»: repeats only the conversation refetch. */
  recheck: boolean;
  /** Link to تاریخچه, where a conversation created by the failed request would appear. */
  history: boolean;
  /** «مشاهده اعتبار». */
  viewCredit: boolean;
}

const NO_ACTIONS: RecoveryActions = {
  editAndResend: false,
  recheck: false,
  history: false,
  viewCredit: false,
};

/** No state ever offers a retry of the original request. */
export function recoveryActions(state: SendRecovery): RecoveryActions {
  switch (state.kind) {
    case "indeterminate":
      return { ...NO_ACTIONS, history: true };
    case "not_found":
      return { ...NO_ACTIONS, editAndResend: true };
    case "reconcile_failed":
      return { ...NO_ACTIONS, recheck: true };
    case "definite":
      return { ...NO_ACTIONS, viewCredit: state.code === "insufficient_credit" };
    default:
      return NO_ACTIONS;
  }
}

/** True when the draft should go straight back into the composer. Follow-ups wait for reconciliation. */
export function restoresDraftImmediately(state: SendRecovery): boolean {
  return state.kind === "indeterminate" || state.kind === "definite";
}
