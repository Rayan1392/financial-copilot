import { useInfiniteQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { Inbox, Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { FinancialCopilotApiError } from "@/integrations/financial-copilot/api-client";
import {
  messageApi,
  type ConversationSummary,
  type MessageFilter,
} from "@/integrations/financial-copilot/in-app-messages";
import { useMessageScope } from "./in-app-message-queries";

const dateFormat = new Intl.DateTimeFormat("fa-IR", {
  dateStyle: "medium",
  timeStyle: "short",
  timeZone: "Asia/Tehran",
});
const dateText = (date: string) => dateFormat.format(new Date(date));
const supportLabels = {
  waitingForSupport: "در انتظار پاسخ",
  answered: "پاسخ داده شد",
};

function SupportRequestForm({
  scope,
  onOpen,
}: {
  scope: ReturnType<typeof useMessageScope>;
  onOpen: (id: string) => void;
}) {
  const client = useQueryClient();
  const [open, setOpen] = useState(false);
  const [subject, setSubject] = useState("");
  const [body, setBody] = useState("");
  const [submitted, setSubmitted] = useState(false);
  const attempt = useRef<Parameters<typeof messageApi.startSupport>[0]>(null);
  const send = useMutation({
    mutationFn: messageApi.startSupport,
    networkMode: "always",
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: [...scope, "list", false] });
    },
  });
  const normalizedSubject = subject.trim();
  const normalizedBody = body.trim();
  const subjectError = submitted && !normalizedSubject;
  const bodyError = submitted && !normalizedBody;

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!send.isPending) setOpen(next);
      }}
    >
      <DialogTrigger asChild>
        <Button size="sm" className="ms-auto">
          <Plus aria-hidden="true" />
          درخواست جدید
        </Button>
      </DialogTrigger>
      <DialogContent
        dir="rtl"
        className="max-h-[85dvh] overflow-y-auto text-right [&>button:last-child]:right-auto [&>button:last-child]:left-4"
      >
        <DialogTitle>درخواست جدید</DialogTitle>
        <DialogDescription>پیامتان را برای تیم پشتیبانی ساپیو بنویسید.</DialogDescription>
        <form
          className="space-y-4"
          noValidate
          onSubmit={(event) => {
            event.preventDefault();
            if (send.isPending) return;
            setSubmitted(true);
            if (!normalizedSubject || !normalizedBody) return;
            if (
              attempt.current?.subject !== normalizedSubject ||
              attempt.current.body !== normalizedBody
            )
              attempt.current = {
                subject: normalizedSubject,
                body: normalizedBody,
                idempotencyKey: crypto.randomUUID(),
              };
            send.mutate(attempt.current, {
              onSuccess: ({ conversationId }) => {
                attempt.current = null;
                setSubject("");
                setBody("");
                setSubmitted(false);
                setOpen(false);
                onOpen(conversationId);
              },
            });
          }}
        >
          <div className="space-y-2">
            <label htmlFor="support-request-subject" className="block text-sm font-medium">
              موضوع
            </label>
            <Input
              id="support-request-subject"
              dir="auto"
              value={subject}
              maxLength={120}
              required
              disabled={send.isPending}
              aria-invalid={subjectError || undefined}
              aria-describedby={subjectError ? "support-request-subject-error" : undefined}
              onChange={(event) => setSubject(event.target.value)}
            />
            {subjectError && (
              <p
                id="support-request-subject-error"
                role="alert"
                className="text-sm text-destructive"
              >
                موضوع را وارد کنید.
              </p>
            )}
          </div>
          <div className="space-y-2">
            <label htmlFor="support-request-body" className="block text-sm font-medium">
              متن پیام
            </label>
            <Textarea
              id="support-request-body"
              dir="auto"
              value={body}
              maxLength={2000}
              required
              disabled={send.isPending}
              aria-invalid={bodyError || undefined}
              aria-describedby={bodyError ? "support-request-body-error" : undefined}
              onChange={(event) => setBody(event.target.value)}
            />
            {bodyError && (
              <p id="support-request-body-error" role="alert" className="text-sm text-destructive">
                متن پیام را وارد کنید.
              </p>
            )}
          </div>
          {send.isError && (
            <p role="alert" className="text-sm text-destructive">
              ثبت درخواست تأیید نشد. دوباره تلاش کنید.
            </p>
          )}
          <Button type="submit" disabled={send.isPending}>
            {send.isPending ? "در حال ارسال…" : "ارسال به پشتیبانی"}
          </Button>
        </form>
      </DialogContent>
    </Dialog>
  );
}

export function MessageError({
  retry,
  text = "دریافت پیام‌ها ممکن نشد.",
}: {
  retry: () => void;
  text?: string;
}) {
  return (
    <div
      className="flex flex-wrap items-center gap-3 rounded-xl border border-border px-4 py-3"
      role="alert"
    >
      <span>{text}</span>
      <Button size="sm" variant="outline" onClick={retry}>
        تلاش دوباره
      </Button>
    </div>
  );
}

export function MessageInbox({
  staff = false,
  onOpen,
}: {
  staff?: boolean;
  onOpen: (id: string) => void;
}) {
  const scope = useMessageScope();
  const [filter, setFilter] = useState<MessageFilter>(staff ? "pending" : "all");
  const query = useInfiniteQuery({
    queryKey: [...scope, "list", staff, filter],
    queryFn: ({ pageParam, signal }) => messageApi.list(staff, filter, pageParam, signal),
    initialPageParam: null as string | null,
    getNextPageParam: (page) => page.nextCursor ?? undefined,
    enabled: scope[1] !== null,
    refetchInterval: staff ? 60_000 : false,
    refetchOnWindowFocus: true,
    retry: false,
  });
  const rows = new Map<string, ConversationSummary>();
  for (const page of query.data?.pages ?? [])
    for (const row of page.items)
      if (!rows.has(row.conversationId)) rows.set(row.conversationId, row);

  const filters = staff
    ? ([
        { key: "pending", label: "نیازمند رسیدگی" },
        { key: "all", label: "همهٔ گفتگوهای پشتیبانی" },
      ] as const)
    : ([
        { key: "all", label: "همه" },
        { key: "unread", label: "خوانده‌نشده" },
      ] as const);

  return (
    <section className="space-y-4" dir="rtl">
      <div className="flex flex-wrap gap-2">
        {filters.map(({ key, label }) => (
          <Button
            key={key}
            variant={filter === key ? "secondary" : "outline"}
            size="sm"
            aria-pressed={filter === key}
            onClick={() => setFilter(key)}
          >
            {label}
          </Button>
        ))}
        {!staff && scope[1] && <SupportRequestForm key={scope[1]} scope={scope} onOpen={onOpen} />}
      </div>
      {query.isPending && !query.isPaused && (
        <p role="status" className="px-1 text-sm text-muted-foreground">
          در حال دریافت پیام‌ها…
        </p>
      )}
      {query.isPaused && (
        <p role="status" className="text-sm text-muted-foreground">
          برای دریافت پیام‌ها به اینترنت وصل شوید.
        </p>
      )}
      {query.isError && <MessageError retry={() => void query.refetch()} />}
      {query.isSuccess && rows.size === 0 && (
        <div className="flex items-center gap-3 rounded-xl border border-border p-5 text-sm text-muted-foreground">
          <Inbox className="size-5" aria-hidden="true" />
          {filter === "pending"
            ? "گفتگویی نیازمند رسیدگی نیست."
            : filter === "unread"
              ? "پیام خوانده‌نشده‌ای ندارید."
              : staff
                ? "هنوز پیامی وجود ندارد."
                : "برای ارتباط با پشتیبانی ساپیو، یک درخواست جدید ثبت کنید."}
        </div>
      )}
      {query.isSuccess && rows.size > 0 && (
        <ul className="divide-y divide-border overflow-hidden rounded-xl border border-border bg-card">
          {Array.from(rows.values(), (row) => (
            <li key={row.conversationId}>
              <button
                type="button"
                onClick={() => onOpen(row.conversationId)}
                className="w-full px-4 py-3 text-right hover:bg-muted focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring"
              >
                <span className="flex flex-wrap items-center gap-2">
                  <span
                    className={row.unread ? "break-words font-bold" : "break-words font-medium"}
                  >
                    {row.subject}
                  </span>
                  {row.unread && (
                    <span className="rounded-full bg-muted px-2 py-0.5 text-xs">خوانده‌نشده</span>
                  )}
                </span>
                <span className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted-foreground">
                  {staff && (
                    <span className="break-all">{row.recipientLabel || row.recipientUserId}</span>
                  )}
                  <time dateTime={row.latestActivityUtc}>{dateText(row.latestActivityUtc)}</time>
                  {row.supportStatus && <span>{supportLabels[row.supportStatus]}</span>}
                  {staff && row.pendingSinceUtc && (
                    <span>در انتظار از {dateText(row.pendingSinceUtc)}</span>
                  )}
                </span>
              </button>
            </li>
          ))}
        </ul>
      )}
      {query.isSuccess && query.hasNextPage && (
        <Button
          variant="outline"
          disabled={query.isFetchingNextPage}
          onClick={() => void query.fetchNextPage()}
        >
          پیام‌های بیشتر
        </Button>
      )}
    </section>
  );
}

export function MessageConversation({ id, staff = false }: { id: string; staff?: boolean }) {
  const scope = useMessageScope();
  return (
    <ScopedConversation key={`${scope[1]}:${staff}:${id}`} scope={scope} id={id} staff={staff} />
  );
}

function ScopedConversation({
  id,
  staff,
  scope,
}: {
  id: string;
  staff: boolean;
  scope: ReturnType<typeof useMessageScope>;
}) {
  const client = useQueryClient();
  const [draft, setDraft] = useState("");
  const [replyError, setReplyError] = useState("");
  const [needsReview, setNeedsReview] = useState(false);
  const reviewedRevision = useRef<number>(undefined);
  const [ackError, setAckError] = useState(false);
  const acknowledged = useRef(new Set<string>());
  const attempt = useRef<{ body: string; revision?: number; key: string }>(null);
  const detailKey = [...scope, "detail", staff, id];
  const query = useInfiniteQuery({
    queryKey: detailKey,
    queryFn: ({ pageParam, signal }) => messageApi.detail(staff, id, pageParam, signal),
    initialPageParam: null as string | null,
    getNextPageParam: (page) => page.messages.nextCursor ?? undefined,
    enabled: scope[1] !== null,
    retry: false,
  });
  const conversation = query.data?.pages[0];
  const entries = (query.data?.pages ?? [])
    .slice()
    .reverse()
    .flatMap((page) => page.messages.items);
  const unread = entries.filter(
    (entry) => entry.fromStaff && !entry.readAtUtc && !acknowledged.current.has(entry.id),
  );
  const unreadIds = unread.slice(0, 50).map(({ id }) => id);
  const ack = useMutation({
    mutationFn: (ids: string[]) => messageApi.read(id, ids),
    onSuccess: (_, ids) => {
      ids.forEach((messageId) => acknowledged.current.add(messageId));
      void client.invalidateQueries({ queryKey: [...scope, "summary"] });
      void client.invalidateQueries({ queryKey: [...scope, "list", false] });
    },
    onError: () => setAckError(true),
  });
  useEffect(() => {
    if (staff || !query.isSuccess || !unreadIds.length || ack.isPending || ackError) return;
    if (document.visibilityState === "visible") ack.mutate(unreadIds);
    else {
      const onVisible = () => {
        if (document.visibilityState === "visible") ack.mutate(unreadIds);
      };
      document.addEventListener("visibilitychange", onVisible);
      return () => document.removeEventListener("visibilitychange", onVisible);
    }
  }, [ack, ackError, query.isSuccess, staff, unreadIds]);

  const reply = useMutation({
    mutationFn: ({ body, key, revision }: { body: string; key: string; revision?: number }) =>
      messageApi.reply(staff, id, body, key, revision),
    onSuccess: async () => {
      setDraft("");
      attempt.current = null;
      reviewedRevision.current = undefined;
      await client.invalidateQueries({ queryKey: [...scope, "list", staff] });
      await query.refetch();
    },
    onError: (error) => {
      if (
        error instanceof FinancialCopilotApiError &&
        error.type?.endsWith("/message-revision-conflict")
      ) {
        attempt.current = null;
        setNeedsReview(true);
        setReplyError("گفتگو تغییر کرده است. پیام‌های تازه را مرور کنید.");
        void query.refetch();
      } else {
        setReplyError("پاسخ ثبت نشد. دوباره تلاش کنید.");
      }
    },
  });

  if (query.isPending)
    return (
      <p role="status">
        {query.isPaused ? "برای دریافت گفتگو به اینترنت وصل شوید." : "در حال دریافت گفتگو…"}
      </p>
    );
  if (query.isError || !conversation) return <MessageError retry={() => void query.refetch()} />;
  const { subject, recipientLabel, recipientUserId, repliesEnabled, supportStatus } =
    conversation.conversation;

  function submit() {
    const body = draft.trim();
    if (!body || needsReview || reply.isPending) return;
    const revision = staff ? (reviewedRevision.current ?? conversation?.revision) : undefined;
    if (!attempt.current || attempt.current.body !== body)
      attempt.current = { body, revision, key: crypto.randomUUID() };
    setReplyError("");
    reply.mutate(attempt.current);
  }

  return (
    <section dir="rtl" className="space-y-4">
      <header>
        <h2 className="break-words text-lg font-bold">{subject}</h2>
        {supportStatus && (
          <p className="mt-1 text-sm text-muted-foreground">{supportLabels[supportStatus]}</p>
        )}
        {staff && (
          <p className="break-all text-sm text-muted-foreground">
            {recipientLabel || recipientUserId}
          </p>
        )}
      </header>
      {query.hasNextPage && (
        <Button
          variant="outline"
          disabled={query.isFetchingNextPage}
          onClick={() => void query.fetchNextPage()}
        >
          پیام‌های قدیمی‌تر
        </Button>
      )}
      <ol className="space-y-3">
        {entries.map((entry) => (
          <li key={entry.id} className="rounded-xl border border-border bg-card p-4">
            <div className="mb-2 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted-foreground">
              <strong>{entry.fromStaff ? "تیم ساپیو" : staff ? "کاربر" : "شما"}</strong>
              <time dateTime={entry.sentAtUtc}>{dateText(entry.sentAtUtc)}</time>
            </div>
            <p dir="auto" className="whitespace-pre-wrap break-words text-sm leading-relaxed">
              {entry.body}
            </p>
          </li>
        ))}
      </ol>
      {ackError && (
        <MessageError retry={() => setAckError(false)} text="ثبت وضعیت خواندن ممکن نشد." />
      )}
      {repliesEnabled && (
        <form
          className="space-y-3 rounded-xl border border-border p-4"
          onSubmit={(event) => {
            event.preventDefault();
            submit();
          }}
        >
          <label className="block text-sm font-medium" htmlFor="message-reply">
            پاسخ
          </label>
          <Textarea
            id="message-reply"
            value={draft}
            maxLength={2000}
            disabled={reply.isPending}
            onChange={(event) => {
              if (staff && reviewedRevision.current === undefined)
                reviewedRevision.current = conversation.revision;
              setDraft(event.target.value);
            }}
          />
          {replyError && (
            <p role="alert" className="text-sm text-destructive">
              {replyError}
            </p>
          )}
          {needsReview && (
            <Button
              type="button"
              variant="outline"
              disabled={query.isFetching}
              onClick={() => {
                reviewedRevision.current = conversation.revision;
                setNeedsReview(false);
                setReplyError("");
              }}
            >
              گفتگو را مرور کردم
            </Button>
          )}
          <Button type="submit" disabled={!draft.trim() || reply.isPending || needsReview}>
            ارسال پاسخ
          </Button>
        </form>
      )}
    </section>
  );
}
