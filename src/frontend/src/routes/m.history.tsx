import { createFileRoute, Link } from "@tanstack/react-router";
import { useMemo, useState } from "react";
import { MobileScreen } from "@/components/mobile/mobile-screen";
import { ConversationRow } from "@/components/mobile/conversation-row";
import { BottomSheet, SheetAction } from "@/components/mobile/bottom-sheet";
import { ConfirmSheet } from "@/components/mobile/confirm-sheet";
import { EmptyState, stateActionClass } from "@/components/mobile/empty-state";
import { ErrorState } from "@/components/mobile/error-state";
import { CloseIcon, DeleteIcon, NavChatIcon, SearchIcon } from "@/components/mobile/icons";
import { Skeleton } from "@/components/ui/skeleton";
import type { ChatThread } from "@/lib/chat.functions";
import { matchesPersianSearch } from "@/lib/format/persian-search";
import {
  conversationTimestamp,
  groupConversationsByRecency,
} from "@/lib/workspace/conversation-groups";
import { useConversations, useDeleteConversation } from "@/lib/workspace/queries";

export const Route = createFileRoute("/m/history")({
  head: () => ({ meta: [{ title: "تاریخچه — ساپیو" }] }),
  component: MobileHistory,
});

/** The API caps the list at 100 and has no cursor or search (B-1 deferred), so search is client-side. */
const HISTORY_LIMIT = 100;

function MobileHistory() {
  const conversations = useConversations({ limit: HISTORY_LIMIT, freshness: true });
  const [query, setQuery] = useState("");
  const [actionsFor, setActionsFor] = useState<ChatThread | null>(null);
  const [confirmFor, setConfirmFor] = useState<ChatThread | null>(null);
  const remove = useDeleteConversation({ onDeleted: () => setConfirmFor(null) });

  const threads = useMemo(() => conversations.data ?? [], [conversations.data]);
  const groups = useMemo(
    () =>
      groupConversationsByRecency(
        threads.filter((thread) => matchesPersianSearch(thread.title, query)),
        new Date(),
      ),
    [threads, query],
  );

  return (
    <MobileScreen title="تاریخچه">
      <div className="sticky top-0 z-10 bg-background px-4 pb-2 pt-3">
        <div className="flex items-center gap-2 rounded-2xl bg-surface px-3.5 ring-1 ring-hairline focus-within:ring-2 focus-within:ring-emerald/40">
          <SearchIcon className="size-5 shrink-0 text-muted-foreground" aria-hidden="true" />
          <label htmlFor="history-search" className="sr-only">
            جست‌وجو در عنوان گفتگوها
          </label>
          <input
            id="history-search"
            type="search"
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            placeholder="جست‌وجو…"
            className="min-h-12 min-w-0 flex-1 bg-transparent text-m-body focus:outline-none"
          />
          {query && (
            <button
              type="button"
              onClick={() => setQuery("")}
              aria-label="پاک کردن جست‌وجو"
              className="flex size-11 shrink-0 items-center justify-center text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40"
            >
              <CloseIcon className="size-5" aria-hidden="true" />
            </button>
          )}
        </div>
      </div>

      {conversations.isLoading ? (
        <div className="space-y-2 px-4 pt-2" aria-busy="true" aria-label="در حال بارگذاری تاریخچه">
          {Array.from({ length: 6 }, (_, index) => (
            <Skeleton key={index} className="h-14 w-full rounded-xl" />
          ))}
        </div>
      ) : conversations.isError && threads.length === 0 ? (
        <ErrorState message="تاریخچه در دسترس نیست" onRetry={() => void conversations.refetch()} />
      ) : threads.length === 0 ? (
        <EmptyState
          icon={NavChatIcon}
          title="هنوز گفتگویی ندارید"
          action={
            <Link to="/m" className={stateActionClass}>
              شروع گفتگو
            </Link>
          }
        />
      ) : groups.length === 0 ? (
        <p role="status" className="px-6 py-12 text-center text-m-title text-muted-foreground">
          نتیجه‌ای یافت نشد
        </p>
      ) : (
        <div className="space-y-4 px-4 pb-6 pt-1">
          {groups.map(({ group, label, threads: items }) => (
            <section key={group} aria-labelledby={`history-${group}`}>
              <h2
                id={`history-${group}`}
                className="mb-2 px-1 text-m-title font-bold text-muted-foreground"
              >
                {label}
              </h2>
              <ul className="overflow-hidden rounded-2xl bg-surface ring-1 ring-hairline">
                {items.map((thread) => (
                  <ConversationRow
                    key={thread.id}
                    thread={thread}
                    timestamp={conversationTimestamp(thread.updated_at, group)}
                    onActions={setActionsFor}
                  />
                ))}
              </ul>
            </section>
          ))}
          {threads.length === HISTORY_LIMIT && (
            <p className="py-2 text-center text-m-cap text-muted-foreground">۱۰۰ گفتگوی اخیر</p>
          )}
        </div>
      )}

      <BottomSheet
        open={actionsFor !== null}
        onOpenChange={(open) => !open && setActionsFor(null)}
        title={actionsFor?.title || "گفتگو"}
      >
        <SheetAction
          tone="danger"
          icon={DeleteIcon}
          onClick={() => {
            setConfirmFor(actionsFor);
            setActionsFor(null);
          }}
        >
          حذف گفتگو
        </SheetAction>
      </BottomSheet>
      <ConfirmSheet
        open={confirmFor !== null}
        onOpenChange={(open) => {
          if (!open) {
            setConfirmFor(null);
            remove.reset();
          }
        }}
        title="حذف گفتگو"
        description="این گفتگو برای همیشه حذف می‌شود."
        confirmLabel="حذف"
        pending={remove.isPending}
        error={remove.isError ? "حذف گفتگو انجام نشد. دوباره تلاش کنید." : null}
        onConfirm={() => confirmFor && remove.mutate(confirmFor.id)}
      />
    </MobileScreen>
  );
}
