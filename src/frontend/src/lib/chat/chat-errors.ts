import { isSubscriptionCooldownMessage } from "@/lib/subscription-cooldown";

/**
 * Stable classification of a failed chat send (Feature 153 T02). There is deliberately no `timeout` code:
 * the client adds no timeout of its own (decision D-Q4).
 */
export type ChatErrorCode =
  | "insufficient_credit"
  | "cooldown"
  | "idempotency_conflict"
  | "not_found"
  | "validation"
  | "network"
  | "server";

export interface ChatFailure {
  code: ChatErrorCode;
  message: string;
}

/** The thrown form of a classified failure, so React Query's `onError` receives the code. */
export class ChatSendError extends Error {
  constructor(
    public readonly code: ChatErrorCode,
    message: string,
  ) {
    super(message);
    this.name = "ChatSendError";
  }
}

const COOLDOWN_PROBLEM_CODE = "SUBSCRIPTION_COOLDOWN_ACTIVE";
const IDEMPOTENCY_CONFLICT_TITLE = "Idempotency key conflict";

/** Maps an API problem response to a code. Runs on the server, where the HTTP status is still known. */
export function classifyApiFailure(
  status: number,
  problemCode: string | null,
  message: string,
): ChatErrorCode {
  if (problemCode === COOLDOWN_PROBLEM_CODE || isSubscriptionCooldownMessage(message))
    return "cooldown";
  if (status === 402) return "insufficient_credit";
  if (
    status === 409 &&
    (problemCode === IDEMPOTENCY_CONFLICT_TITLE ||
      message === IDEMPOTENCY_CONFLICT_TITLE ||
      message.includes("idempotency key"))
  ) {
    return "idempotency_conflict";
  }
  if (status === 404) return "not_found";
  if (status === 400 || status === 422) return "validation";
  return "server";
}

const NETWORK_MESSAGE =
  /failed to fetch|networkerror|network error|load failed|network request failed/i;

/**
 * Classifies an error that reached the browser without a structured result: a failed `/_serverFn` request
 * (network), or a message-only error from older paths.
 */
export function classifyClientError(error: unknown): ChatFailure {
  if (error instanceof ChatSendError) return { code: error.code, message: error.message };
  const message = error instanceof Error ? error.message : String(error ?? "");
  if (error instanceof TypeError || NETWORK_MESSAGE.test(message))
    return { code: "network", message };
  if (isSubscriptionCooldownMessage(message)) return { code: "cooldown", message };
  if (message.toLowerCase().includes("insufficient"))
    return { code: "insufficient_credit", message };
  return { code: "server", message };
}

export const INSUFFICIENT_CREDIT_TEXT =
  "اعتبار کافی برای پردازش درخواست وجود ندارد. لطفاً حساب خود را شارژ کنید.";
export const GENERIC_CHAT_ERROR_TEXT =
  "متأسفیم، خطایی در پردازش درخواست رخ داد. لطفاً دوباره امتحان کنید.";

function isChatFailure(value: unknown): value is ChatFailure {
  return typeof value === "object" && value !== null && "code" in value && "message" in value;
}

/** The Persian text desktop shows for a failed send. Unchanged from before the structured codes. */
export function chatErrorMessage(error: ChatFailure | unknown): string {
  const failure = isChatFailure(error) ? error : classifyClientError(error);
  if (failure.code === "cooldown") return failure.message;
  if (failure.code === "insufficient_credit") return INSUFFICIENT_CREDIT_TEXT;
  return GENERIC_CHAT_ERROR_TEXT;
}

/** Network and server failures leave the request's outcome unknown: the server may still have processed it. */
export function isIndeterminateFailure(code: ChatErrorCode): boolean {
  return code === "network" || code === "server";
}
