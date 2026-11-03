import { useEffect, useRef, useState } from "react";
import { ChevronDown, ChevronUp, LoaderCircle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { MarkdownMessage } from "@/components/app/markdown-message";
import {
  adminApi,
  type AdminChatHistoryPage,
  type AdminChatHistoryTurn,
} from "@/integrations/financial-copilot/admin-client";

function dateLabel(value: string | null) {
  if (!value) return "نامشخص";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "نامشخص" : date.toLocaleString("fa-IR");
}

export function AdminCustomerChatHistory({ userId }: { userId: string }) {
  const [pageNumber, setPageNumber] = useState(1);
  const [page, setPage] = useState<AdminChatHistoryPage | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [expandedTurnId, setExpandedTurnId] = useState<string | null>(null);
  const [detail, setDetail] = useState<AdminChatHistoryTurn | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [detailError, setDetailError] = useState<string | null>(null);
  const [requestVersion, setRequestVersion] = useState(0);
  const detailRequestId = useRef(0);

  useEffect(() => {
    let active = true;
    setLoading(true);
    setError(null);
    setExpandedTurnId(null);
    setDetail(null);
    setDetailError(null);
    detailRequestId.current += 1;
    adminApi
      .customerChatHistory(userId, pageNumber)
      .then((result) => {
        if (active) setPage(result);
      })
      .catch((reason: unknown) => {
        if (active)
          setError(reason instanceof Error ? reason.message : "گرفتن تاریخچه گفتگو ناموفق بود.");
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [userId, pageNumber, requestVersion]);

  async function toggleTurn(turnId: string) {
    if (expandedTurnId === turnId) {
      detailRequestId.current += 1;
      setExpandedTurnId(null);
      setDetail(null);
      setDetailError(null);
      return;
    }
    setExpandedTurnId(turnId);
    setDetail(null);
    setDetailError(null);
    setDetailLoading(true);
    const requestId = ++detailRequestId.current;
    try {
      const result = await adminApi.customerChatHistoryTurn(userId, turnId);
      if (requestId === detailRequestId.current) setDetail(result);
    } catch (reason) {
      if (requestId === detailRequestId.current) {
        setDetailError(reason instanceof Error ? reason.message : "گرفتن جزئیات گفتگو ناموفق بود.");
      }
    } finally {
      if (requestId === detailRequestId.current) setDetailLoading(false);
    }
  }

  return (
    <section dir="rtl" aria-label="تاریخچه گفتگوها" className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="font-semibold text-slate-900">تاریخچه گفتگوها</h3>
        <Button
          type="button"
          size="sm"
          variant="outline"
          disabled={loading}
          onClick={() => setRequestVersion((version) => version + 1)}
        >
          تازه‌سازی
        </Button>
      </div>
      {loading && (
        <p role="status" className="flex items-center gap-2 py-6 text-sm text-muted-foreground">
          <LoaderCircle className="size-4 animate-spin" />
          در حال بارگذاری گفتگوها…
        </p>
      )}
      {!loading && error && (
        <div
          role="alert"
          className="rounded-lg border border-rose-200 bg-rose-50 p-3 text-sm text-rose-800"
        >
          {error}
        </div>
      )}
      {!loading && !error && page?.items.length === 0 && (
        <p className="rounded-lg border border-dashed p-6 text-center text-sm text-muted-foreground">
          گفتگویی برای این کاربر ثبت نشده است.
        </p>
      )}
      {!loading && !error && page && page.items.length > 0 && (
        <>
          <div className="space-y-2">
            {page.items.map((item) => {
              const isExpanded = expandedTurnId === item.turnId;
              return (
                <article
                  key={item.turnId}
                  className="overflow-hidden rounded-xl border border-slate-200 bg-white"
                >
                  <button
                    type="button"
                    className="flex w-full items-start gap-3 p-3 text-right hover:bg-slate-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary sm:p-4"
                    aria-expanded={isExpanded}
                    aria-controls={`chat-turn-${item.turnId}`}
                    onClick={() => void toggleTurn(item.turnId)}
                  >
                    <span className="mt-1 shrink-0 text-muted-foreground">
                      {isExpanded ? (
                        <ChevronUp className="size-4" />
                      ) : (
                        <ChevronDown className="size-4" />
                      )}
                    </span>
                    <span className="min-w-0 flex-1 space-y-2">
                      <span className="block text-xs text-muted-foreground">
                        پرسش · {dateLabel(item.questionCreatedAtUtc)}
                        {item.conversationTitle ? ` · ${item.conversationTitle}` : ""}
                      </span>
                      <span className="block break-words text-sm font-medium text-slate-900">
                        {item.questionSummary}
                      </span>
                      <span className="block break-words text-sm text-slate-600">
                        {item.answerSummary ?? "پاسخی برای این پرسش ذخیره نشده است."}
                      </span>
                    </span>
                  </button>
                  {isExpanded && (
                    <div
                      id={`chat-turn-${item.turnId}`}
                      className="space-y-4 border-t bg-slate-50/70 p-3 sm:p-4"
                    >
                      {detailLoading && (
                        <p role="status" className="text-sm text-muted-foreground">
                          در حال دریافت جزئیات…
                        </p>
                      )}
                      {detailError && (
                        <p role="alert" className="text-sm text-rose-700">
                          {detailError}
                        </p>
                      )}
                      {detail && (
                        <>
                          <div className="space-y-2">
                            <p className="text-xs font-semibold text-slate-500">
                              پرسش کامل · {dateLabel(detail.questionCreatedAtUtc)}
                            </p>
                            <div className="max-w-full overflow-x-auto rounded-lg bg-white p-3 text-sm">
                              <MarkdownMessage content={detail.question} direction="rtl" />
                            </div>
                          </div>
                          {detail.answers.length === 0 ? (
                            <p className="rounded-lg border border-dashed bg-white p-3 text-sm text-muted-foreground">
                              پاسخی برای این پرسش ذخیره نشده است.
                            </p>
                          ) : (
                            detail.answers.map((answer) => (
                              <div key={answer.messageId} className="space-y-2">
                                <p className="text-xs font-semibold text-slate-500">
                                  پاسخ ثبت‌شده · {dateLabel(answer.createdAtUtc)}
                                </p>
                                <div className="max-w-full overflow-x-auto rounded-lg bg-white p-3">
                                  <MarkdownMessage content={answer.content} direction="rtl" />
                                </div>
                                {answer.structuredPayload != null && (
                                  <details className="rounded-lg border bg-white p-3 text-xs">
                                    <summary className="cursor-pointer font-medium">
                                      داده ساختاریافته پاسخ
                                    </summary>
                                    <pre
                                      dir="ltr"
                                      className="mt-2 max-h-80 overflow-auto whitespace-pre-wrap break-all text-left"
                                    >
                                      {JSON.stringify(answer.structuredPayload, null, 2)}
                                    </pre>
                                  </details>
                                )}
                              </div>
                            ))
                          )}
                        </>
                      )}
                    </div>
                  )}
                </article>
              );
            })}
          </div>
          <div className="flex items-center justify-between gap-2 border-t pt-3">
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={loading || page.page <= 1}
              onClick={() => setPageNumber((value) => Math.max(1, value - 1))}
            >
              قبلی
            </Button>
            <span aria-live="polite" className="text-xs text-muted-foreground">
              صفحه {page.page.toLocaleString("fa-IR")} از {page.totalPages.toLocaleString("fa-IR")}{" "}
              · {page.totalCount.toLocaleString("fa-IR")} گفتگو
            </span>
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={loading || page.page >= page.totalPages}
              onClick={() => setPageNumber((value) => value + 1)}
            >
              بعدی
            </Button>
          </div>
        </>
      )}
    </section>
  );
}
