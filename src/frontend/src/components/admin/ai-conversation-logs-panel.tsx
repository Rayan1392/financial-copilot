import { useEffect, useRef, useState, type FormEvent } from "react";
import { ChevronLeft, ChevronRight, RefreshCw, Search } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { JalaliDateFilter } from "@/components/app/jalali-date-filter";
import { Loading, Message, Panel } from "@/components/admin/admin-ui";
import { showError } from "@/components/admin/admin-errors";
import {
  ConversationLogActorDialog,
  type ActorSelection,
} from "@/components/admin/ai-conversation-log-actor-dialog";
import { AiConversationLogDrawer } from "@/components/admin/ai-conversation-log-drawer";
import { AdminConversationStatusBadge } from "@/components/admin/ai-conversation-log-labels";
import {
  channelLabels,
  conversationLogStatusLabels,
  formatTehranDateTime,
  shortActorId,
  unresolvedActorLabel,
} from "@/components/admin/ai-conversation-log-format";
import {
  adminApi,
  adminConversationLogStatuses,
  type AdminConversationLogPage,
  type AdminConversationLogPageSize,
  type AdminConversationLogQuery,
  type AdminConversationLogStatus,
  type AdminConversationLogUser,
} from "@/integrations/financial-copilot/admin-client";
import { lastTehranDays, tehranDayRangeToUtc } from "@/lib/format/tehran-day-range";
import { toPersianDigits } from "@/lib/format/persian";

const pageSizes: AdminConversationLogPageSize[] = [20, 50, 100];
const defaultRangeDays = 30;

type Filters = {
  text: string;
  user: string;
  fromDay: string;
  toDay: string;
  status: AdminConversationLogStatus | "";
};

function defaultFilters(): Filters {
  const { from, to } = lastTehranDays(defaultRangeDays);
  return { text: "", user: "", fromDay: from, toDay: to, status: "" };
}

function toQuery(
  filters: Filters,
  page: number,
  pageSize: AdminConversationLogPageSize,
): AdminConversationLogQuery {
  const { fromUtc, toUtc } = tehranDayRangeToUtc(filters.fromDay, filters.toDay);
  return {
    page,
    pageSize,
    q: filters.text.trim() || undefined,
    user: filters.user.trim() || undefined,
    fromUtc,
    toUtc,
    status: filters.status || undefined,
  };
}

type OpenLogUser = (user: AdminConversationLogUser, trigger: HTMLButtonElement) => void;

function ConversationLogAccount({
  user,
  onOpenUser,
  onOpenActor,
}: {
  user: AdminConversationLogUser;
  onOpenUser?: OpenLogUser;
  onOpenActor: OpenLogUser;
}) {
  // No user account in the tenant (BUG-045): show the actor ID; its dialog makes no Customer 360 request.
  if (user.hasUserAccount !== true) {
    return (
      <button
        type="button"
        title={user.userId}
        className="block max-w-full rounded-sm text-start underline-offset-4 hover:underline focus-visible:outline-2 focus-visible:outline-ring"
        onClick={(event) => onOpenActor(user, event.currentTarget)}
      >
        <span className="block truncate font-medium text-slate-700">
          <bdi>{unresolvedActorLabel(user)}</bdi>
        </span>
        <bdi dir="ltr" className="block font-mono text-[11px] text-slate-500">
          {shortActorId(user.userId)}
        </bdi>
      </button>
    );
  }

  const label = user.displayName || user.email || user.maskedMobile || "بدون نام";
  return (
    <>
      {onOpenUser ? (
        <button
          type="button"
          className="block max-w-full truncate rounded-sm text-start font-semibold text-primary underline-offset-4 hover:underline focus-visible:outline-2 focus-visible:outline-ring"
          onClick={(event) => onOpenUser(user, event.currentTarget)}
        >
          <bdi>{label}</bdi>
        </button>
      ) : (
        <span className="block truncate font-medium text-slate-800">
          <bdi>{label}</bdi>
        </span>
      )}
      {user.displayName && user.email && (
        <span className="block truncate text-slate-500" dir="ltr">
          {user.email}
        </span>
      )}
      {user.maskedMobile && (user.displayName || user.email) && (
        <span className="block text-slate-500" dir="ltr">
          {user.maskedMobile}
        </span>
      )}
    </>
  );
}

/** Feature 164: «تاریخچه پرسش و پاسخ کاربران» — every user's questions and the AI answers, read-only. */
export function AiConversationLogsPanel({
  onOpenUser,
}: {
  onOpenUser?: OpenLogUser;
} = {}) {
  const [draft, setDraft] = useState<Filters>(defaultFilters);
  const [applied, setApplied] = useState<Filters>(() => draft);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState<AdminConversationLogPageSize>(20);
  const [refresh, setRefresh] = useState(0);
  const [result, setResult] = useState<AdminConversationLogPage | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selectedTurnId, setSelectedTurnId] = useState<string | null>(null);
  const [selectedActor, setSelectedActor] = useState<ActorSelection | null>(null);
  const requestId = useRef(0);

  useEffect(() => {
    const id = ++requestId.current;
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    adminApi
      .aiConversationLogs(toQuery(applied, page, pageSize), controller.signal)
      .then((next) => {
        if (id === requestId.current) setResult(next);
      })
      .catch((reason: unknown) => {
        if (id === requestId.current && !controller.signal.aborted) setError(showError(reason));
      })
      .finally(() => {
        if (id === requestId.current) setLoading(false);
      });
    return () => controller.abort();
  }, [applied, page, pageSize, refresh]);

  function apply(event: FormEvent) {
    event.preventDefault();
    setApplied(draft);
    setPage(1);
  }

  function showActorQuestions(actorId: string) {
    const next = { ...draft, user: actorId };
    setDraft(next);
    setApplied(next);
    setPage(1);
    setSelectedActor(null);
  }

  function reset() {
    const initial = defaultFilters();
    setDraft(initial);
    setApplied(initial);
    setPage(1);
  }

  const items = result?.items ?? [];
  const invalidRange = Boolean(draft.fromDay && draft.toDay && draft.fromDay > draft.toDay);

  return (
    <Panel title="تاریخچه پرسش و پاسخ کاربران">
      <form
        onSubmit={apply}
        aria-label="فیلتر پرسش‌ها"
        className="grid gap-3 sm:grid-cols-2 xl:grid-cols-[minmax(0,2fr)_minmax(0,2fr)_repeat(4,minmax(0,1fr))]"
      >
        <label className="space-y-1 text-xs text-muted-foreground">
          <span className="block">متن پرسش</span>
          <Input
            value={draft.text}
            maxLength={200}
            placeholder="جست‌وجو در متن پرسش"
            onChange={(event) => setDraft({ ...draft, text: event.target.value })}
          />
        </label>
        <label className="space-y-1 text-xs text-muted-foreground">
          <span className="block">کاربر</span>
          <Input
            value={draft.user}
            maxLength={200}
            placeholder="شناسه، ایمیل، نام یا شماره همراه"
            onChange={(event) => setDraft({ ...draft, user: event.target.value })}
          />
        </label>
        <JalaliDateFilter
          label="از"
          ariaLabel="از تاریخ"
          value={draft.fromDay}
          onChange={(fromDay) => setDraft({ ...draft, fromDay })}
        />
        <JalaliDateFilter
          label="تا"
          ariaLabel="تا تاریخ"
          value={draft.toDay}
          onChange={(toDay) => setDraft({ ...draft, toDay })}
        />
        <label className="space-y-1 text-xs text-muted-foreground">
          <span className="block">وضعیت</span>
          <select
            aria-label="وضعیت"
            className="h-10 w-full rounded-xl border border-slate-200 bg-white px-3 text-sm text-slate-900"
            value={draft.status}
            onChange={(event) =>
              setDraft({ ...draft, status: event.target.value as Filters["status"] })
            }
          >
            <option value="">همه</option>
            {adminConversationLogStatuses.map((status) => (
              <option key={status} value={status}>
                {conversationLogStatusLabels[status]}
              </option>
            ))}
          </select>
        </label>
        <label className="space-y-1 text-xs text-muted-foreground">
          <span className="block">تعداد در هر صفحه</span>
          <select
            aria-label="تعداد در هر صفحه"
            className="h-10 w-full rounded-xl border border-slate-200 bg-white px-3 text-sm text-slate-900"
            value={pageSize}
            onChange={(event) => {
              setPageSize(Number(event.target.value) as AdminConversationLogPageSize);
              setPage(1);
            }}
          >
            {pageSizes.map((size) => (
              <option key={size} value={size}>
                {toPersianDigits(size)}
              </option>
            ))}
          </select>
        </label>
        <div className="flex flex-wrap items-center gap-2 sm:col-span-2 xl:col-span-6">
          <Button type="submit" disabled={invalidRange}>
            <Search className="ml-2 size-4" />
            اعمال فیلتر
          </Button>
          <Button type="button" variant="outline" onClick={reset}>
            پاک کردن فیلترها
          </Button>
          <Button
            type="button"
            variant="ghost"
            disabled={loading}
            onClick={() => setRefresh((value) => value + 1)}
          >
            <RefreshCw className="ml-2 size-4" />
            تازه‌سازی
          </Button>
          {invalidRange && (
            <span role="alert" className="text-xs text-rose-700">
              تاریخ شروع باید پیش از تاریخ پایان باشد.
            </span>
          )}
        </div>
      </form>

      <Message error={error} />
      <Loading active={loading} />

      {!loading && !error && result && items.length === 0 && (
        <p className="my-4 rounded-xl border border-dashed border-slate-200 p-6 text-center text-sm text-slate-500">
          پرسشی با این فیلترها پیدا نشد.
        </p>
      )}

      {!error && items.length > 0 && (
        <div className={`mt-4 overflow-x-auto ${loading ? "opacity-60" : ""}`}>
          <Table className="min-w-[960px] text-right">
            <TableHeader>
              <TableRow>
                <TableHead className="text-right">زمان پرسش</TableHead>
                <TableHead className="text-right">کاربر</TableHead>
                <TableHead className="text-right">پرسش</TableHead>
                <TableHead className="text-right">پاسخ</TableHead>
                <TableHead className="text-right">وضعیت</TableHead>
                <TableHead className="text-right">کانال</TableHead>
                <TableHead className="text-right">
                  <span className="sr-only">جزئیات</span>
                </TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {items.map((item) => (
                <TableRow key={item.turnId} className="align-top">
                  <TableCell className="whitespace-nowrap text-xs text-slate-600">
                    {formatTehranDateTime(item.questionCreatedAtUtc)}
                  </TableCell>
                  <TableCell className="max-w-[200px] text-xs">
                    <ConversationLogAccount
                      user={item.user}
                      onOpenUser={onOpenUser}
                      onOpenActor={(user, trigger) => setSelectedActor({ user, trigger })}
                    />
                  </TableCell>
                  <TableCell className="max-w-[320px] text-sm">
                    <span className="line-clamp-3 break-words text-slate-900" dir="auto">
                      {item.questionPreview}
                    </span>
                  </TableCell>
                  <TableCell className="max-w-[360px] text-sm">
                    <span className="line-clamp-3 break-words text-slate-600" dir="auto">
                      {item.answerPreview ?? "—"}
                    </span>
                  </TableCell>
                  <TableCell>
                    <AdminConversationStatusBadge status={item.status} />
                  </TableCell>
                  <TableCell className="text-xs text-slate-600">
                    {channelLabels[item.channel]}
                  </TableCell>
                  <TableCell>
                    <Button
                      type="button"
                      size="sm"
                      variant="outline"
                      onClick={() => setSelectedTurnId(item.turnId)}
                    >
                      مشاهده
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}

      {result && result.totalCount > 0 && (
        <div className="mt-4 flex flex-wrap items-center gap-3 border-t border-slate-100 pt-3">
          <div className="flex items-center gap-2">
            <Button
              type="button"
              size="sm"
              variant="outline"
              aria-label="صفحه قبل"
              disabled={loading || page <= 1}
              onClick={() => setPage((value) => Math.max(1, value - 1))}
            >
              <ChevronRight className="size-4" />
            </Button>
            <span aria-live="polite" className="text-xs text-slate-600">
              صفحه {toPersianDigits(result.page)} از {toPersianDigits(result.totalPages)} ·{" "}
              {toPersianDigits(result.totalCount)} پرسش
            </span>
            <Button
              type="button"
              size="sm"
              variant="outline"
              aria-label="صفحه بعد"
              disabled={loading || page >= result.totalPages}
              onClick={() => setPage((value) => value + 1)}
            >
              <ChevronLeft className="size-4" />
            </Button>
          </div>
        </div>
      )}

      <AiConversationLogDrawer turnId={selectedTurnId} onClose={() => setSelectedTurnId(null)} />
      {selectedActor && (
        <ConversationLogActorDialog
          key={selectedActor.user.userId}
          selection={selectedActor}
          onShowQuestions={showActorQuestions}
          onClose={() => setSelectedActor(null)}
        />
      )}
    </Panel>
  );
}
