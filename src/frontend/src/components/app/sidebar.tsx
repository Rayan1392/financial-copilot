import { Link, useNavigate, useParams } from "@tanstack/react-router";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useServerFn } from "@tanstack/react-start";
import { useEffect, useState } from "react";
import {
  Plus,
  Trash2,
  LogOut,
  Settings,
  Send,
  RefreshCw,
  MessageSquare,
  Wallet,
  ChevronLeft,
  ChevronRight,
} from "lucide-react";
import { SapioMark } from "@/components/app/sapio-mark";
import { RailHoverCard } from "@/components/app/rail-hover-card";
import { createThread } from "@/lib/chat.functions";
import { useConversations, useDeleteConversation, useUsage } from "@/lib/workspace/queries";
import {
  getStoredAuthenticatedUser,
  logout,
  subscribeToAuthChanges,
} from "@/integrations/financial-copilot/auth";
import { canAccessAdmin } from "@/integrations/financial-copilot/admin-permissions";
import { formatNumber } from "@/lib/format/persian";
import { computeCreditProgress, creditQuotaLabel, type CreditTone } from "@/lib/credit-progress";

const CREDIT_TONE_CLASS: Record<CreditTone, string> = {
  healthy: "bg-emerald",
  warning: "bg-gold",
  danger: "bg-rose",
};

export function ConversationSidebar({
  collapsed,
  onToggle,
}: {
  collapsed: boolean;
  onToggle: () => void;
}) {
  const navigate = useNavigate();
  const qc = useQueryClient();
  const params = useParams({ strict: false });
  const activeId = (params as { threadId?: string }).threadId;
  const create = useServerFn(createThread);
  const { data: threads = [] } = useConversations();
  const {
    data: usage,
    isLoading: usageLoading,
    isError: usageError,
    isFetching: usageFetching,
    refetch: refetchUsage,
  } = useUsage();
  const [usageRefreshing, setUsageRefreshing] = useState(false);
  async function refreshUsage() {
    if (usageRefreshing) return;
    setUsageRefreshing(true);
    // Keep the spin visible for a minimum time so fast responses still feel like a click.
    await Promise.allSettled([refetchUsage(), new Promise((resolve) => setTimeout(resolve, 700))]);
    setUsageRefreshing(false);
  }
  const newChat = useMutation({
    mutationFn: () => create(),
    onSuccess: (thread) => {
      qc.invalidateQueries({ queryKey: ["threads"] });
      navigate({ to: "/c/$threadId", params: { threadId: thread.id } });
    },
  });
  const removeChat = useDeleteConversation({
    onDeleted: (id) => {
      if (activeId === id) navigate({ to: "/chat" });
    },
  });
  const credit =
    usage && usage.creditQuotaMode !== "Unlimited"
      ? computeCreditProgress(usage.availableSpendingCapacity, usage.planIncludedCredits)
      : null;
  const [showAdmin, setShowAdmin] = useState(false);
  useEffect(() => {
    const updateAccess = () => {
      const user = getStoredAuthenticatedUser();
      setShowAdmin(canAccessAdmin(user));
    };
    updateAccess();
    return subscribeToAuthChanges(updateAccess);
  }, []);

  async function signOut() {
    await logout();
    navigate({ to: "/auth" });
  }

  if (collapsed) {
    return (
      <aside
        className="hidden h-screen w-[4.5rem] shrink-0 flex-col items-center border-l border-hairline bg-surface/70 py-4 md:flex"
        aria-label="نوار گفتگو"
      >
        <Link
          to="/chat"
          className="mb-3 rounded-xl p-2 transition hover:bg-white/5"
          aria-label="صفحه گفتگو"
        >
          <SapioMark className="size-8" />
        </Link>
        <button
          type="button"
          onClick={onToggle}
          className="mb-5 flex size-10 items-center justify-center rounded-xl text-muted-foreground transition hover:bg-white/5 hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/50"
          aria-label="باز کردن پنل گفتگوها"
          title="باز کردن پنل گفتگوها"
        >
          <ChevronLeft className="size-5" />
        </button>
        <button
          type="button"
          onClick={() => newChat.mutate()}
          disabled={newChat.isPending}
          className="mb-2 flex size-11 items-center justify-center rounded-xl bg-emerald text-primary-foreground shadow-sm transition hover:brightness-110 disabled:opacity-50"
          aria-label="گفتگوی جدید"
          title="گفتگوی جدید"
        >
          <Plus className="size-5" />
        </button>
        <button
          type="button"
          onClick={onToggle}
          className={`mb-2 flex size-11 items-center justify-center rounded-xl transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/50 ${activeId ? "bg-emerald-soft/60 text-emerald" : "text-muted-foreground hover:bg-white/5 hover:text-foreground"}`}
          aria-label={`گفتگوهای اخیر، ${threads.length} گفتگو. باز کردن پنل`}
          title={`گفتگوهای اخیر (${threads.length})`}
        >
          <MessageSquare className="size-5" />
        </button>
        <div className="my-3 h-px w-8 bg-hairline" />
        <RailHoverCard
          id="compact-credit-tooltip"
          opensTo="left"
          icon={<Wallet className="size-4" />}
          title="اعتبار هوش مصنوعی"
          subtitle="مانده سهمیه حساب شما"
          trigger={
            <button
              type="button"
              onClick={() => void refreshUsage()}
              disabled={usageRefreshing || usageFetching}
              className="flex size-11 flex-col items-center justify-center gap-1 rounded-xl text-muted-foreground transition hover:bg-white/5 hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/50 disabled:opacity-60"
              aria-label="اعتبار هوش مصنوعی؛ برای به‌روزرسانی کلیک کنید"
              aria-describedby="compact-credit-tooltip"
            >
              <Wallet
                className={`size-5 ${usageRefreshing || usageFetching ? "animate-pulse" : ""}`}
              />
              {credit?.percentage != null && (
                <span className="h-1 w-6 overflow-hidden rounded-full bg-muted">
                  <span
                    className={`block h-full ${credit.tone ? CREDIT_TONE_CLASS[credit.tone] : "bg-muted"}`}
                    style={{ width: `${credit.percentage}%` }}
                  />
                </span>
              )}
            </button>
          }
        >
          {usageLoading && (
            <p className="rounded-lg bg-background/70 px-3 py-2.5 text-xs leading-6 text-muted-foreground">
              در حال دریافت اطلاعات اعتبار…
            </p>
          )}
          {usageError && (
            <p className="rounded-lg bg-rose/10 px-3 py-2.5 text-xs leading-6 text-rose">
              اطلاعات اعتبار در دسترس نیست. برای تلاش دوباره روی آیکون کلیک کنید.
            </p>
          )}
          {usage && usage.creditQuotaMode === "Unlimited" && (
            <div className="rounded-xl bg-emerald-soft/50 px-3 py-3">
              <p className="text-base font-bold text-emerald">اعتبار نامحدود</p>
              {usage.subscriptionExpiresAtUtc && (
                <p className="mt-1 text-xs text-muted-foreground">
                  اعتبار تا {new Date(usage.subscriptionExpiresAtUtc).toLocaleDateString("fa-IR")}{" "}
                  فعال است
                </p>
              )}
            </div>
          )}
          {usage && usage.creditQuotaMode !== "Unlimited" && credit && (
            <div className="rounded-xl bg-background/70 px-3 py-3">
              <div className="flex items-end justify-between gap-2">
                <div>
                  <p className="text-[11px] text-muted-foreground">اعتبار باقی‌مانده</p>
                  <p className="mt-1 text-2xl font-bold leading-none tracking-tight tabular-nums">
                    {formatNumber(credit.remaining)}
                  </p>
                </div>
                {credit.percentage != null && (
                  <span className="rounded-full bg-surface px-2 py-1 text-[11px] font-semibold text-muted-foreground ring-1 ring-hairline">
                    {formatNumber(credit.percentage, { maximumFractionDigits: 0 })}٪
                  </span>
                )}
              </div>
              {credit.limit != null ? (
                <>
                  <p className="mt-2 text-xs text-muted-foreground">
                    از {formatNumber(credit.limit)} اعتبار دوره
                  </p>
                  <div
                    className="mt-3 h-1.5 w-full overflow-hidden rounded-full bg-muted"
                    role="progressbar"
                    aria-label="درصد اعتبار باقی‌مانده"
                    aria-valuemin={0}
                    aria-valuemax={100}
                    aria-valuenow={
                      credit.percentage != null ? Math.round(credit.percentage) : undefined
                    }
                  >
                    <div
                      className={`h-full rounded-full transition-all ${credit.tone ? CREDIT_TONE_CLASS[credit.tone] : "bg-muted"}`}
                      style={{ width: `${credit.percentage ?? 0}%` }}
                    />
                  </div>
                </>
              ) : (
                <p className="mt-2 text-xs text-muted-foreground">
                  سقف دوره برای این حساب مشخص نشده است.
                </p>
              )}
            </div>
          )}
        </RailHoverCard>
        <div className="mt-auto flex flex-col items-center gap-2">
          <Link
            to="/telegram-link"
            className="flex size-10 items-center justify-center rounded-xl text-muted-foreground transition hover:bg-white/5 hover:text-foreground"
            aria-label="اتصال تلگرام"
            title="اتصال تلگرام"
          >
            <Send className="size-5" />
          </Link>
          {showAdmin && (
            <Link
              to="/admin"
              className="flex size-10 items-center justify-center rounded-xl text-muted-foreground transition hover:bg-white/5 hover:text-foreground"
              aria-label="پنل مدیریت"
              title="پنل مدیریت"
            >
              <Settings className="size-5" />
            </Link>
          )}
          <button
            onClick={signOut}
            className="flex size-10 items-center justify-center rounded-xl text-muted-foreground transition hover:bg-rose/10 hover:text-rose"
            aria-label="خروج"
            title="خروج"
          >
            <LogOut className="size-5" />
          </button>
        </div>
      </aside>
    );
  }

  return (
    <aside className="hidden h-screen w-72 flex-shrink-0 flex-col overflow-hidden border-l border-hairline bg-surface/50 transition-[width] duration-200 md:flex">
      <div className="flex min-h-0 flex-1 flex-col p-5">
        <div className="mb-7 flex items-center gap-2">
          <Link to="/chat" className="flex min-w-0 flex-1 items-center gap-3">
            <SapioMark className="size-9 shrink-0 object-contain" />
            <h1 className="truncate text-[15px] font-bold tracking-tight text-foreground leading-tight">
              ساپیو - دستیار هوشمند بازار
            </h1>
          </Link>
          <button
            type="button"
            onClick={onToggle}
            className="flex size-9 shrink-0 items-center justify-center rounded-lg text-muted-foreground transition hover:bg-white/5 hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/50"
            aria-label="جمع کردن پنل گفتگوها"
            title="جمع کردن پنل گفتگوها"
          >
            <ChevronRight className="size-5" />
          </button>
        </div>
        <button
          onClick={() => newChat.mutate()}
          disabled={newChat.isPending}
          className="w-full flex items-center justify-between py-2.5 px-3 rounded-lg bg-emerald text-primary-foreground text-sm font-medium hover:brightness-110 transition disabled:opacity-50 mb-6"
        >
          <span>گفتگوی جدید</span>
          <Plus className="size-4" />
        </button>
        <div className="flex min-h-0 flex-1 flex-col">
          <p className="text-[10px] font-semibold text-muted-foreground uppercase tracking-widest px-2 mb-3">
            گفتگوهای اخیر
          </p>
          <nav className="min-h-0 flex-1 space-y-1 overflow-y-auto pr-1 scrollbar-thin">
            {threads.length === 0 && (
              <p className="text-xs text-muted-foreground px-3 py-2">گفتگویی نیست.</p>
            )}
            {threads.map((thread) => (
              <div key={thread.id} className="group relative">
                <Link
                  to="/c/$threadId"
                  params={{ threadId: thread.id }}
                  className={`flex items-center gap-3 px-3 py-2 rounded-md transition ${
                    activeId === thread.id
                      ? "bg-white/5 text-foreground"
                      : "hover:bg-white/5 text-muted-foreground hover:text-foreground"
                  }`}
                >
                  <div
                    className={`size-1.5 rounded-full ${activeId === thread.id ? "bg-emerald" : "bg-muted-foreground/40"}`}
                  />
                  <span className="text-sm truncate flex-1">{thread.title}</span>
                </Link>
                <button
                  onClick={(event) => {
                    event.preventDefault();
                    removeChat.mutate(thread.id);
                  }}
                  className="absolute left-1.5 top-1/2 -translate-y-1/2 p-1 text-muted-foreground opacity-0 transition group-hover:opacity-100 hover:text-destructive"
                  aria-label="حذف"
                >
                  <Trash2 className="size-3.5" />
                </button>
              </div>
            ))}
          </nav>
        </div>
      </div>
      <div className="mt-auto shrink-0 border-t border-hairline p-5 space-y-4">
        {usageLoading && <p className="text-xs text-muted-foreground">در حال بارگذاری اعتبار...</p>}
        {usageError && <p className="text-xs text-rose">اعتبار در دسترس نیست.</p>}
        {usage && (
          <div className="p-3 rounded-xl bg-background/50 ring-1 ring-hairline">
            <div className="flex items-center justify-between gap-2 mb-2">
              <span className="flex items-center gap-1 text-[11px] text-muted-foreground">
                اعتبار هوش مصنوعی
                <button
                  type="button"
                  onClick={() => void refreshUsage()}
                  disabled={usageRefreshing || usageFetching}
                  className="p-0.5 text-muted-foreground hover:text-foreground transition disabled:opacity-60"
                  aria-label="به‌روزرسانی اعتبار"
                  title="به‌روزرسانی اعتبار"
                >
                  <RefreshCw
                    className={`size-3 ${usageRefreshing || usageFetching ? "animate-spin" : ""}`}
                  />
                </button>
              </span>
              <span className="shrink-0 whitespace-nowrap text-[11px] text-foreground">
                {creditQuotaLabel(
                  usage.creditQuotaMode,
                  credit?.limit != null
                    ? `${formatNumber(credit.remaining)} از ${formatNumber(credit.limit)}`
                    : formatNumber(credit?.remaining ?? 0),
                )}
              </span>
            </div>
            {usage.creditQuotaMode === "Unlimited" ? (
              usage.subscriptionExpiresAtUtc && (
                <div className="text-[11px] text-muted-foreground">
                  تا {new Date(usage.subscriptionExpiresAtUtc).toLocaleDateString("fa-IR")}
                </div>
              )
            ) : (
              <div
                className="h-1 w-full bg-muted rounded-full overflow-hidden"
                role="progressbar"
                aria-label="اعتبار باقی‌مانده هوش مصنوعی"
                aria-valuemin={0}
                aria-valuemax={100}
                aria-valuenow={
                  credit?.percentage != null ? Math.round(credit.percentage) : undefined
                }
              >
                <div
                  className={`h-full transition-all ${credit?.tone ? CREDIT_TONE_CLASS[credit.tone] : "bg-muted"}`}
                  style={{ width: `${credit?.percentage ?? 0}%` }}
                />
              </div>
            )}
          </div>
        )}
        <div className="flex items-center justify-between px-1">
          <span className="text-xs font-medium text-foreground">حساب کاربری</span>
          <Link
            to="/telegram-link"
            className="p-1.5 text-muted-foreground hover:text-foreground transition"
            aria-label="اتصال تلگرام"
          >
            <Send className="size-4" />
          </Link>
          {showAdmin && (
            <Link
              to="/admin"
              className="p-1.5 text-muted-foreground hover:text-foreground transition"
              aria-label="پنل مدیریت"
            >
              <Settings className="size-4" />
            </Link>
          )}
          <button
            onClick={signOut}
            className="p-1.5 text-muted-foreground hover:text-foreground transition"
            aria-label="خروج"
          >
            <LogOut className="size-4" />
          </button>
        </div>
      </div>
    </aside>
  );
}
