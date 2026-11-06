import { Link } from "@tanstack/react-router";
import { chatErrorMessage, INSUFFICIENT_CREDIT_TEXT } from "@/lib/chat/chat-errors";

/**
 * Desktop notice for a failed send, shared by the new-chat and thread pages. When credit ran out it points to
 * the account page, which shows the plan, remaining credit and how to top up (mobile does the same with
 * «مشاهده اعتبار» → /m/account).
 */
export function ChatErrorNotice({ failure }: { failure: unknown }) {
  if (!failure) return null;
  const text = chatErrorMessage(failure);
  return (
    <div
      role="alert"
      className="mx-4 mb-2 flex flex-wrap items-center justify-between gap-2 rounded-lg border border-destructive/30 bg-destructive/10 px-4 py-2.5 text-right text-sm text-destructive"
    >
      <span>{text}</span>
      {text === INSUFFICIENT_CREDIT_TEXT && (
        <Link
          to="/profile"
          className="shrink-0 rounded-md px-2 py-1 font-medium text-foreground underline-offset-4 hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
        >
          مشاهده اعتبار
        </Link>
      )}
    </div>
  );
}
