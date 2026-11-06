import { RefreshButton } from "@/components/mobile/refresh-button";
import { Skeleton } from "@/components/ui/skeleton";
import { computeCreditProgress, creditQuotaLabel, type CreditTone } from "@/lib/credit-progress";
import { formatNumber } from "@/lib/format/persian";
import { useUsage } from "@/lib/workspace/queries";
import { cn } from "@/lib/utils";

/**
 * Text-only purchase guidance (decision D-Q6). No external purchase link is rendered until the official
 * TahlilApp destination is confirmed.
 */
export const PURCHASE_GUIDANCE_TEXT =
  "برای خرید یا تمدید اعتبار، از طریق حساب کاربری خود در تحلیل‌اپ اقدام کنید.";

const TONE_BAR: Record<CreditTone, string> = {
  healthy: "bg-emerald",
  warning: "bg-gold",
  danger: "bg-rose",
};
const TONE_LABEL: Record<CreditTone, string> = {
  healthy: "کافی",
  warning: "رو به اتمام",
  danger: "بسیار کم",
};

/** Plan and AI credit (design §7), reusing the desktop finite/unlimited logic from `credit-progress.ts`. */
export function CreditSummary() {
  const usage = useUsage({ freshness: true });
  const data = usage.data;
  const credit =
    data && data.creditQuotaMode !== "Unlimited"
      ? computeCreditProgress(data.availableSpendingCapacity, data.planIncludedCredits)
      : null;

  return (
    <section
      aria-labelledby="credit-title"
      className="rounded-2xl bg-surface p-4 ring-1 ring-hairline"
    >
      <div className="flex items-center gap-2">
        <h2 id="credit-title" className="min-w-0 flex-1 text-[15px] font-semibold">
          اشتراک: {data?.planName || data?.planCode || "–"}
        </h2>
        <RefreshButton
          label="به‌روزرسانی اعتبار"
          onRefresh={() => usage.refetch()}
          busy={usage.isFetching}
        />
      </div>
      {usage.isLoading ? (
        <Skeleton className="mt-2 h-10 w-full" />
      ) : !data ? (
        <div role="alert" className="mt-2 text-sm">
          <p className="mobile-danger-text">اعتبار در دسترس نیست</p>
          <button
            type="button"
            onClick={() => void usage.refetch()}
            className="mt-2 min-h-11 rounded-xl bg-surface-2 px-4 font-medium focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40"
          >
            تلاش مجدد
          </button>
        </div>
      ) : (
        <>
          <div className="mt-2 flex items-center justify-between gap-2 text-sm">
            <span className="text-muted-foreground">اعتبار هوش مصنوعی</span>
            <span className="tabular-nums text-foreground">
              {creditQuotaLabel(
                data.creditQuotaMode,
                credit?.limit != null
                  ? `${formatNumber(credit.remaining)} از ${formatNumber(credit.limit)}`
                  : formatNumber(credit?.remaining ?? 0),
              )}
            </span>
          </div>
          {data.creditQuotaMode === "Unlimited" ? (
            data.subscriptionExpiresAtUtc && (
              <p className="mt-1 text-xs text-muted-foreground">
                تا {new Date(data.subscriptionExpiresAtUtc).toLocaleDateString("fa-IR")}
              </p>
            )
          ) : (
            <>
              <div
                className="mt-2 h-2 w-full overflow-hidden rounded-full bg-muted"
                role="progressbar"
                aria-label="اعتبار باقی‌مانده هوش مصنوعی"
                aria-valuemin={0}
                aria-valuemax={100}
                aria-valuenow={
                  credit?.percentage != null ? Math.round(credit.percentage) : undefined
                }
              >
                <div
                  className={cn(
                    "h-full transition-all",
                    credit?.tone ? TONE_BAR[credit.tone] : "bg-muted",
                  )}
                  style={{ width: `${credit?.percentage ?? 0}%` }}
                />
              </div>
              {credit?.tone && (
                <p className="mt-1 text-xs text-muted-foreground">
                  وضعیت اعتبار: {TONE_LABEL[credit.tone]}
                </p>
              )}
              {data.subscriptionExpiresAtUtc && (
                <p className="mt-1 text-xs text-muted-foreground">
                  اشتراک تا {new Date(data.subscriptionExpiresAtUtc).toLocaleDateString("fa-IR")}
                </p>
              )}
            </>
          )}
        </>
      )}
      <p className="mt-3 text-sm leading-7 text-muted-foreground">{PURCHASE_GUIDANCE_TEXT}</p>
    </section>
  );
}
