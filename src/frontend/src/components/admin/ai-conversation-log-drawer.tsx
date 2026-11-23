import { useEffect, useRef, useState, type ReactNode } from "react";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@/components/ui/sheet";
import { MarkdownMessage } from "@/components/app/markdown-message";
import { ReadOnlyAssistantAnswer } from "@/components/app/message-list";
import { Loading, Message } from "@/components/admin/admin-ui";
import { showError } from "@/components/admin/admin-errors";
import { AdminConversationStatusBadge } from "@/components/admin/ai-conversation-log-labels";
import {
  channelLabels,
  formatTehranDateTime,
  unresolvedActorLabel,
} from "@/components/admin/ai-conversation-log-format";
import {
  adminApi,
  type AdminConversationLogAnswer,
  type AdminConversationLogTurn,
} from "@/integrations/financial-copilot/admin-client";
import { mapAssistantBlock } from "@/lib/chat/assistant-block-mapper";
import { toPersianDigits } from "@/lib/format/persian";

const notStored = "ذخیره نشده";

/**
 * Feature 164: one turn as the user saw it. Opening it reads the full content (audited by the API); nothing here
 * changes stored messages or calls the model.
 */
export function AiConversationLogDrawer({
  turnId,
  onClose,
}: {
  turnId: string | null;
  onClose: () => void;
}) {
  const [turn, setTurn] = useState<AdminConversationLogTurn | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const requestId = useRef(0);

  useEffect(() => {
    const id = ++requestId.current;
    setTurn(null);
    setError(null);
    if (!turnId) {
      setLoading(false);
      return;
    }
    setLoading(true);
    adminApi
      .aiConversationLogTurn(turnId)
      .then((result) => {
        if (id === requestId.current) setTurn(result);
      })
      .catch((reason: unknown) => {
        if (id === requestId.current) setError(showError(reason));
      })
      .finally(() => {
        if (id === requestId.current) setLoading(false);
      });
  }, [turnId]);

  return (
    <Sheet open={turnId !== null} onOpenChange={(open) => !open && onClose()}>
      <SheetContent
        side="left"
        dir="rtl"
        className="light scheme-light w-full overflow-y-auto bg-white text-right text-slate-900 sm:max-w-3xl"
      >
        <SheetHeader className="text-right sm:text-right">
          <SheetTitle>جزئیات پرسش و پاسخ</SheetTitle>
          <SheetDescription>
            پاسخ همان‌طور نمایش داده می‌شود که کاربر دیده است؛ چیزی تغییر نمی‌کند.
          </SheetDescription>
        </SheetHeader>
        <div className="mt-4 space-y-5">
          <Loading active={loading} />
          <Message error={error} />
          {turn && <TurnDetail turn={turn} />}
        </div>
      </SheetContent>
    </Sheet>
  );
}

function sameInstant(left: string, right: string) {
  return new Date(left).getTime() === new Date(right).getTime();
}

function TurnDetail({ turn }: { turn: AdminConversationLogTurn }) {
  const first = turn.answers[0];
  // One exchange is stored with one time; then it is the time the answer was completed.
  const singleTime =
    first !== undefined && sameInstant(first.createdAtUtc, turn.questionCreatedAtUtc);
  const user = turn.user;

  return (
    <>
      <dl className="grid gap-x-4 gap-y-3 rounded-xl border border-slate-200 bg-slate-50/70 p-4 text-sm sm:grid-cols-2">
        <Field label="کاربر">
          <span className="block font-medium">
            {user.hasUserAccount === true
              ? (user.displayName ?? "بدون نام")
              : unresolvedActorLabel(user)}
          </span>
          {user.email && (
            <span className="block text-xs text-slate-500" dir="ltr">
              {user.email}
            </span>
          )}
          {user.maskedMobile && (
            <span className="block text-xs text-slate-500" dir="ltr">
              {user.maskedMobile}
            </span>
          )}
          <span className="block break-all text-[11px] text-slate-400" dir="ltr">
            {user.userId}
          </span>
        </Field>
        <Field label="گفتگو">
          <span className="block">{turn.conversationTitle ?? "بدون عنوان"}</span>
          <span className="block break-all text-[11px] text-slate-400" dir="ltr">
            {turn.conversationId}
          </span>
        </Field>
        <Field label="کانال">{channelLabels[turn.channel]}</Field>
        <Field label="وضعیت">
          <AdminConversationStatusBadge status={turn.status} />
        </Field>
        <Field label="شناسه نشست">{notStored}</Field>
        <Field label="زمان دریافت پرسش">{notStored}</Field>
        {singleTime ? (
          <Field label="زمان تکمیل پاسخ">{formatTehranDateTime(first.createdAtUtc)}</Field>
        ) : (
          <Field label="زمان ثبت پرسش">{formatTehranDateTime(turn.questionCreatedAtUtc)}</Field>
        )}
      </dl>

      <section aria-label="پرسش" className="space-y-2">
        <h3 className="text-xs font-semibold text-slate-500">پرسش</h3>
        <div className="max-w-full overflow-x-auto break-words rounded-xl border border-slate-200 p-3 text-sm">
          <MarkdownMessage content={turn.question} direction="rtl" />
        </div>
      </section>

      {turn.answers.length === 0 ? (
        <p className="rounded-xl border border-dashed border-slate-200 p-4 text-sm text-slate-500">
          پاسخی برای این پرسش ذخیره نشده است.
        </p>
      ) : (
        turn.answers.map((answer, index) => (
          <AnswerSection
            key={answer.messageId}
            answer={answer}
            index={index}
            count={turn.answers.length}
            showTime={!singleTime || index > 0}
          />
        ))
      )}
    </>
  );
}

function AnswerSection({
  answer,
  index,
  count,
  showTime,
}: {
  answer: AdminConversationLogAnswer;
  index: number;
  count: number;
  showTime: boolean;
}) {
  const usage = answer.usage;
  const title = count > 1 ? `پاسخ ${toPersianDigits(index + 1)}` : "پاسخ";
  return (
    <section aria-label={title} className="space-y-3">
      <div className="flex flex-wrap items-center gap-2 text-xs text-slate-500">
        <h3 className="font-semibold">{title}</h3>
        <AdminConversationStatusBadge status={answer.status} />
        {answer.outcomeReasonCode && (
          <code dir="ltr" className="rounded bg-slate-100 px-1.5 py-0.5 text-[11px]">
            {answer.outcomeReasonCode}
          </code>
        )}
        {showTime && <span>{formatTehranDateTime(answer.createdAtUtc)}</span>}
      </div>
      {usage && (
        <dl className="flex flex-wrap gap-x-4 gap-y-1 text-[11px] text-slate-500">
          <UsageItem label="مدل" value={usage.modelName ?? usage.providerName} ltr />
          <UsageItem
            label="توکن‌ها"
            value={
              usage.totalTokens === null
                ? null
                : `${toPersianDigits(usage.totalTokens)} (${toPersianDigits(usage.promptTokens ?? 0)} + ${toPersianDigits(usage.completionTokens ?? 0)})`
            }
          />
          <UsageItem label="اعتبار" value={toPersianDigits(usage.creditsCharged)} />
          <UsageItem label="وضعیت اجرا" value={usage.completionStatus} ltr />
          <UsageItem label="مسیر" value={usage.executionRoute} ltr />
        </dl>
      )}
      <div className="max-w-full overflow-x-auto rounded-xl border border-slate-200 p-3">
        <ReadOnlyAssistantAnswer
          block={mapAssistantBlock(answer.assistantContent ?? undefined, answer.content)}
        />
      </div>
    </section>
  );
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="min-w-0">
      <dt className="text-xs text-slate-500">{label}</dt>
      <dd className="mt-1 min-w-0 break-words">{children}</dd>
    </div>
  );
}

function UsageItem({
  label,
  value,
  ltr = false,
}: {
  label: string;
  value: string | null;
  ltr?: boolean;
}) {
  if (value === null) return null;
  return (
    <div className="flex gap-1">
      <dt>{label}:</dt>
      <dd dir={ltr ? "ltr" : undefined}>{value}</dd>
    </div>
  );
}
