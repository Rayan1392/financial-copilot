import { Link } from "@tanstack/react-router";
import { MoreHorizontal } from "lucide-react";
import type { ChatThread } from "@/lib/chat.functions";

/** History row (design §5): title clamped to two lines, time or date, and a 44 px ⋯ for row actions. */
export function ConversationRow({
  thread,
  timestamp,
  onActions,
}: {
  thread: ChatThread;
  timestamp: string;
  onActions: (thread: ChatThread) => void;
}) {
  return (
    <li className="flex items-center border-b border-hairline last:border-b-0">
      <Link
        to="/m/c/$threadId"
        params={{ threadId: thread.id }}
        className="flex min-h-14 min-w-0 flex-1 items-center gap-3 px-4 py-2 active:bg-surface-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-emerald/40"
      >
        <span
          dir="auto"
          className="line-clamp-2 min-w-0 flex-1 text-[15px] leading-7 text-foreground"
        >
          {thread.title || "گفتگوی بدون عنوان"}
        </span>
        <span className="shrink-0 text-xs tabular-nums text-muted-foreground">{timestamp}</span>
      </Link>
      <button
        type="button"
        onClick={() => onActions(thread)}
        aria-label={`گزینه‌های گفتگوی ${thread.title || "بدون عنوان"}`}
        className="flex size-11 shrink-0 items-center justify-center rounded-xl text-muted-foreground active:bg-surface-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40"
      >
        <MoreHorizontal className="size-5" aria-hidden="true" />
      </button>
    </li>
  );
}
