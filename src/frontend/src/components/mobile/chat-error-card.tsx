import { Link } from "@tanstack/react-router";
import { Loader2 } from "lucide-react";
import {
  GENERIC_CHAT_ERROR_TEXT,
  INSUFFICIENT_CREDIT_TEXT,
  type ChatFailure,
} from "@/lib/chat/chat-errors";
import { recoveryActions, type SendRecovery } from "@/lib/chat/send-recovery";

export const INDETERMINATE_TEXT =
  "وضعیت این درخواست مشخص نیست و ممکن است پردازش شده باشد. پیش از ارسال دوباره، تاریخچه را بررسی کنید.";
export const NOT_FOUND_TEXT =
  "پاسخی برای این پرسش ثبت نشد. ممکن است درخواست قبلی هنوز در حال پردازش باشد.";
export const RECONCILE_FAILED_TEXT = "وضعیت این درخواست مشخص نیست و ممکن است پردازش شده باشد.";

function messageFor(failure: ChatFailure, recovery: SendRecovery): string {
  switch (recovery.kind) {
    case "indeterminate":
      return INDETERMINATE_TEXT;
    case "not_found":
      return NOT_FOUND_TEXT;
    case "reconcile_failed":
      return RECONCILE_FAILED_TEXT;
    default:
      if (failure.code === "cooldown") return failure.message;
      if (failure.code === "insufficient_credit") return INSUFFICIENT_CREDIT_TEXT;
      return GENERIC_CHAT_ERROR_TEXT;
  }
}

const ACTION =
  "inline-flex min-h-11 items-center rounded-xl px-4 text-sm font-medium focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40";

/**
 * Failed-send card (spec AC-06a/b, AC-07). It never offers a retry of the original request: a same-key resend
 * currently re-runs the AI (decision D-A1). Purchase guidance stays text-only (D-Q6).
 */
export function ChatErrorCard({
  failure,
  recovery,
  onEditAndResend,
  onRecheck,
}: {
  failure: ChatFailure;
  recovery: SendRecovery;
  onEditAndResend?: () => void;
  onRecheck?: () => void;
}) {
  if (recovery.kind === "answered") return null;
  if (recovery.kind === "reconciling") {
    return (
      <div
        role="status"
        className="mx-4 flex items-center gap-2 rounded-2xl bg-surface px-4 py-3 text-sm text-muted-foreground"
      >
        <Loader2 className="size-4 animate-spin" aria-hidden="true" />
        در حال بررسی وضعیت...
      </div>
    );
  }
  const actions = recoveryActions(recovery);
  const hasActions =
    actions.history || actions.viewCredit || actions.editAndResend || actions.recheck;
  return (
    <div
      role="alert"
      className="mx-4 rounded-2xl border border-rose/30 bg-rose/10 px-4 py-3 text-right"
    >
      <p className="text-sm leading-7 text-foreground">{messageFor(failure, recovery)}</p>
      {hasActions && (
        <div className="mt-2 flex flex-wrap gap-2">
          {actions.history && (
            <Link
              to="/m/history"
              className={`${ACTION} bg-surface text-foreground ring-1 ring-hairline`}
            >
              تاریخچه
            </Link>
          )}
          {actions.viewCredit && (
            <Link
              to="/m/account"
              className={`${ACTION} bg-surface text-foreground ring-1 ring-hairline`}
            >
              مشاهده اعتبار
            </Link>
          )}
          {actions.editAndResend && onEditAndResend && (
            <button
              type="button"
              onClick={onEditAndResend}
              className={`${ACTION} bg-emerald text-primary-foreground`}
            >
              ویرایش و ارسال دوباره
            </button>
          )}
          {actions.recheck && onRecheck && (
            <button
              type="button"
              onClick={onRecheck}
              className={`${ACTION} bg-surface text-foreground ring-1 ring-hairline`}
            >
              بررسی دوباره
            </button>
          )}
        </div>
      )}
    </div>
  );
}
