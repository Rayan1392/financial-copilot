import { createFileRoute, Link, useRouter } from "@tanstack/react-router";
import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { ArrowDown, MoreHorizontal } from "lucide-react";
import { MessageList } from "@/components/app/message-list";
import { MobileScreen } from "@/components/mobile/mobile-screen";
import { MobileComposer } from "@/components/mobile/mobile-composer";
import { ChatErrorCard } from "@/components/mobile/chat-error-card";
import { MobileUserBubble, PendingAnswer } from "@/components/mobile/pending-exchange";
import { BottomSheet, SheetAction } from "@/components/mobile/bottom-sheet";
import { ConfirmSheet } from "@/components/mobile/confirm-sheet";
import { Skeleton } from "@/components/ui/skeleton";
import { useChatThread } from "@/lib/chat/use-chat-thread";
import { restoresDraftImmediately } from "@/lib/chat/send-recovery";
import { useConversations, useDeleteConversation } from "@/lib/workspace/queries";
import { useOnlineStatus } from "@/hooks/use-online-status";

export const Route = createFileRoute("/m/c/$threadId")({
  head: () => ({ meta: [{ title: "گفتگو — ساپیو" }] }),
  component: MobileConversation,
});

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function isNotFound(error: unknown): boolean {
  const message = error instanceof Error ? error.message : String(error ?? "");
  return /not found|\(404\)/i.test(message);
}

function MobileConversation() {
  const { threadId } = Route.useParams();
  if (!UUID.test(threadId)) return <ConversationNotFound />;
  return <ConversationScreen key={threadId} threadId={threadId} />;
}

function ConversationScreen({ threadId }: { threadId: string }) {
  const router = useRouter();
  const online = useOnlineStatus();
  const thread = useChatThread(threadId, { reconcileOnFailure: true });
  const conversations = useConversations({ limit: 100, freshness: true });
  const title = conversations.data?.find((item) => item.id === threadId)?.title || "گفتگو";
  const mainRef = useRef<HTMLElement>(null);
  const [draft, setDraft] = useState("");
  const [menuOpen, setMenuOpen] = useState(false);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [showNewPill, setShowNewPill] = useState(false);
  const remove = useDeleteConversation({
    onDeleted: () => void router.navigate({ to: "/m/history", replace: true }),
  });
  const failure = thread.failure;

  // Definite failures put the text straight back; indeterminate follow-ups wait for reconciliation (AC-06b).
  useEffect(() => {
    if (failure && restoresDraftImmediately(failure.recovery)) {
      setDraft((current) => (current.trim() ? current : failure.draft));
    }
  }, [failure]);

  // Scroll: jump to the bottom on open; when an answer arrives, show its start (long answers read top-down),
  // unless the user has scrolled up to read, in which case a «پیام‌های جدید» pill appears instead.
  const seenCount = useRef<number | null>(null);
  const scrolledAway = useRef(false);
  useEffect(() => {
    const main = mainRef.current;
    if (!main) return;
    const onScroll = () => {
      scrolledAway.current = main.scrollHeight - main.scrollTop - main.clientHeight > 240;
      if (!scrolledAway.current) setShowNewPill(false);
    };
    main.addEventListener("scroll", onScroll, { passive: true });
    return () => main.removeEventListener("scroll", onScroll);
  }, []);
  useLayoutEffect(() => {
    const main = mainRef.current;
    if (!main || thread.isLoading) return;
    const count = thread.messages.length;
    if (seenCount.current === null) {
      main.scrollTop = main.scrollHeight;
    } else if (count > seenCount.current) {
      const answers = main.querySelectorAll<HTMLElement>('[data-role="assistant"]');
      if (scrolledAway.current) setShowNewPill(true);
      else answers[answers.length - 1]?.scrollIntoView({ block: "start" });
    }
    seenCount.current = count;
  }, [thread.messages.length, thread.isLoading]);

  // Keep the pending question and status in view.
  useEffect(() => {
    if (thread.pendingMessage || failure) {
      const main = mainRef.current;
      main?.scrollTo({ top: main.scrollHeight });
    }
  }, [thread.pendingMessage, failure]);

  function submit(text: string) {
    setDraft("");
    thread.submit(text);
  }

  if (thread.loadError && isNotFound(thread.loadError)) return <ConversationNotFound />;

  const exchangeText =
    thread.pendingMessage ??
    (failure && failure.recovery.kind !== "answered" ? failure.draft : null);

  return (
    <MobileScreen
      title={title}
      backFallback="/m/history"
      showTabBar={false}
      mainRef={mainRef}
      actions={
        <button
          type="button"
          aria-label="گزینه‌های گفتگو"
          onClick={() => setMenuOpen(true)}
          className="flex size-11 shrink-0 items-center justify-center rounded-xl text-muted-foreground active:bg-surface-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40"
        >
          <MoreHorizontal className="size-5" aria-hidden="true" />
        </button>
      }
      footer={
        <MobileComposer
          value={draft}
          onChange={setDraft}
          onSubmit={submit}
          pending={thread.isPending}
          online={online}
        />
      }
    >
      {thread.isLoading ? (
        <div className="space-y-4 p-4" aria-busy="true" aria-label="در حال بارگذاری گفتگو">
          <Skeleton className="ms-auto h-12 w-3/5 rounded-2xl" />
          <Skeleton className="h-24 w-full rounded-2xl" />
          <Skeleton className="ms-auto h-12 w-2/5 rounded-2xl" />
        </div>
      ) : thread.loadError ? (
        <div role="alert" className="m-4 rounded-2xl bg-surface p-4 text-sm ring-1 ring-hairline">
          <p>بارگذاری گفتگو ناموفق بود</p>
          <button
            type="button"
            onClick={() => void thread.refetchMessages()}
            className="mt-2 min-h-11 rounded-xl bg-surface-2 px-4 font-medium focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40"
          >
            تلاش مجدد
          </button>
        </div>
      ) : (
        <MessageList
          messages={thread.messages}
          loading={false}
          streaming={false}
          density="compact"
          onSuggested={thread.submit}
          onPageChange={thread.changeScannerPage}
          onDisclosurePageChange={thread.changeDisclosurePage}
          showDiagnostics={false}
          followedSymbols={thread.followedSymbols}
        />
      )}
      {(exchangeText !== null || thread.isPending) && (
        <div className="space-y-5 pb-5">
          {exchangeText !== null && (
            <MobileUserBubble
              text={exchangeText}
              uncertain={failure?.recovery.kind === "reconcile_failed"}
            />
          )}
          {thread.isPending && <PendingAnswer />}
        </div>
      )}
      {failure && (
        <div className="pb-5">
          <ChatErrorCard
            failure={failure}
            recovery={failure.recovery}
            onEditAndResend={() => {
              setDraft(failure.draft);
              thread.clearFailure();
            }}
            onRecheck={thread.recheck}
          />
        </div>
      )}
      {showNewPill && (
        <button
          type="button"
          onClick={() => {
            setShowNewPill(false);
            const answers =
              mainRef.current?.querySelectorAll<HTMLElement>('[data-role="assistant"]');
            answers?.[answers.length - 1]?.scrollIntoView({ block: "start", behavior: "smooth" });
          }}
          className="sticky bottom-3 mx-auto flex min-h-11 items-center gap-2 rounded-full bg-emerald px-4 text-sm font-medium text-primary-foreground shadow-lg"
        >
          <ArrowDown className="size-4" aria-hidden="true" />
          پیام‌های جدید
        </button>
      )}
      <BottomSheet open={menuOpen} onOpenChange={setMenuOpen} title={title}>
        <SheetAction
          tone="danger"
          onClick={() => {
            setMenuOpen(false);
            setConfirmOpen(true);
          }}
        >
          حذف گفتگو
        </SheetAction>
      </BottomSheet>
      <ConfirmSheet
        open={confirmOpen}
        onOpenChange={setConfirmOpen}
        title="حذف گفتگو"
        description="این گفتگو برای همیشه حذف می‌شود."
        confirmLabel="حذف"
        pending={remove.isPending}
        error={remove.isError ? "حذف گفتگو انجام نشد. دوباره تلاش کنید." : null}
        onConfirm={() => remove.mutate(threadId)}
      />
    </MobileScreen>
  );
}

function ConversationNotFound() {
  return (
    <MobileScreen title="گفتگو" backFallback="/m/history" showTabBar={false}>
      <div className="flex flex-col items-center gap-4 px-6 py-16 text-center">
        <p className="text-base font-semibold">این گفتگو پیدا نشد</p>
        <div className="flex gap-2">
          <Link
            to="/m/history"
            className="flex min-h-11 items-center rounded-xl bg-surface px-4 text-sm font-medium ring-1 ring-hairline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40"
          >
            تاریخچه
          </Link>
          <Link
            to="/m"
            className="flex min-h-11 items-center rounded-xl bg-emerald px-4 text-sm font-medium text-primary-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40"
          >
            گفتگوی جدید
          </Link>
        </div>
      </div>
    </MobileScreen>
  );
}
