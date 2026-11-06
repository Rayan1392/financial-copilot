import { Link } from "@tanstack/react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { BellRing, Trash2 } from "lucide-react";
import { Switch } from "@/components/ui/switch";
import {
  CODAL_ANNOUNCEMENT_TYPES,
  createCodalAlert,
  deleteCodalAlert,
  getMyCodalAlerts,
  updateCodalAlert,
  type CodalAlertSettings as Settings,
  type CodalAlertSubscription,
  type CodalAnnouncementType,
} from "@/integrations/financial-copilot/codal-alerts";
import { useFollowedSymbols } from "@/lib/workspace/queries";

const TYPE_LABEL: Record<CodalAnnouncementType, string> = {
  FinancialStatement: "صورت‌های مالی",
  MonthlyActivity: "گزارش ماهانه تولید و فروش",
};

/** New alerts watch both announcement kinds and send the raw message; the smart summary is opt-in. */
const DEFAULT_SETTINGS: Settings = {
  announcementTypes: [...CODAL_ANNOUNCEMENT_TYPES],
  minimumImportance: "Notice",
  rawAlertEnabled: true,
  aiSummaryEnabled: false,
};

const ALERTS_KEY = ["codal-alerts"] as const;

function settingsOf(subscription: CodalAlertSubscription): Settings {
  return {
    announcementTypes: subscription.announcementTypes,
    minimumImportance: subscription.minimumImportance,
    rawAlertEnabled: subscription.rawAlertEnabled,
    aiSummaryEnabled: subscription.aiSummaryEnabled,
  };
}

/**
 * Codal alert subscriptions delivered to the linked Telegram account: which companies, which announcement
 * kinds, whether a smart summary is added, and pause/resume. Companies are picked from the watchlist.
 */
export function CodalAlertSettings() {
  const qc = useQueryClient();
  const alerts = useQuery({ queryKey: ALERTS_KEY, queryFn: getMyCodalAlerts, retry: false });
  const followed = useFollowedSymbols();
  const [pick, setPick] = useState("");
  const [failure, setFailure] = useState<string | null>(null);
  const refresh = () => qc.invalidateQueries({ queryKey: ALERTS_KEY });
  const onError = (text: string) => () => setFailure(text);

  const create = useMutation({
    mutationFn: (externalCompanyId: string) =>
      createCodalAlert(externalCompanyId, DEFAULT_SETTINGS),
    onSuccess: () => {
      setPick("");
      setFailure(null);
      void refresh();
    },
    onError: onError("افزودن هشدار انجام نشد. دوباره تلاش کنید."),
  });
  const update = useMutation({
    mutationFn: ({
      subscription,
      patch,
    }: {
      subscription: CodalAlertSubscription;
      patch: Partial<Settings & { state: CodalAlertSubscription["state"] }>;
    }) =>
      updateCodalAlert(subscription.id, {
        ...settingsOf(subscription),
        state: subscription.state,
        ...patch,
      }),
    onSuccess: () => {
      setFailure(null);
      void refresh();
    },
    onError: onError("ذخیرهٔ تغییر انجام نشد. دوباره تلاش کنید."),
  });
  const remove = useMutation({
    mutationFn: (id: string) => deleteCodalAlert(id),
    onSuccess: () => {
      setFailure(null);
      void refresh();
    },
    onError: onError("حذف هشدار انجام نشد. دوباره تلاش کنید."),
  });

  const items = alerts.data?.items ?? [];
  const subscribed = new Set(items.map((item) => item.externalCompanyId));
  const followedSymbols = followed.data?.symbols ?? [];
  const candidates = followedSymbols.filter((item) => !subscribed.has(item.externalCompanyId));
  const busy = create.isPending || update.isPending || remove.isPending;

  return (
    <section aria-labelledby="codal-alerts-heading" className="mt-6 border-t border-hairline pt-5">
      <h2 id="codal-alerts-heading" className="flex items-center gap-2 text-base font-semibold">
        <BellRing className="size-4 text-emerald" aria-hidden="true" />
        هشدار اطلاعیه‌های کدال
      </h2>
      <p className="mt-1 text-sm leading-7 text-muted-foreground">
        وقتی شرکت‌های انتخابی شما صورت مالی یا گزارش ماهانه منتشر کنند، پیام آن در تلگرام برایتان
        فرستاده می‌شود.
      </p>

      {failure && (
        <p
          role="alert"
          className="mt-3 rounded-lg bg-rose/10 px-3 py-2 text-xs leading-6 text-rose"
        >
          {failure}
        </p>
      )}

      {followedSymbols.length === 0 && !followed.isLoading ? (
        <p className="mt-4 rounded-xl border border-dashed border-hairline px-4 py-3 text-sm leading-7 text-muted-foreground">
          هشدارها برای نمادهای دیده‌بان تنظیم می‌شوند.{" "}
          <Link to="/followed-symbols" className="font-medium text-emerald hover:underline">
            افزودن نماد به دیده‌بان
          </Link>
        </p>
      ) : candidates.length > 0 ? (
        <div className="mt-4 flex flex-wrap items-center gap-2">
          <label htmlFor="codal-alert-pick" className="sr-only">
            انتخاب نماد برای هشدار
          </label>
          <select
            id="codal-alert-pick"
            value={pick}
            onChange={(event) => setPick(event.target.value)}
            className="min-h-10 min-w-0 flex-1 rounded-lg border border-input bg-background px-3 text-sm focus:outline-none focus:ring-2 focus:ring-ring"
          >
            <option value="">انتخاب نماد از دیده‌بان…</option>
            {candidates.map((item) => (
              <option key={item.externalCompanyId} value={item.externalCompanyId}>
                {item.symbol} — {item.companyName}
              </option>
            ))}
          </select>
          <button
            type="button"
            disabled={!pick || busy}
            onClick={() => create.mutate(pick)}
            className="min-h-10 rounded-lg bg-emerald px-4 text-sm font-semibold text-primary-foreground transition hover:brightness-110 disabled:opacity-50"
          >
            افزودن هشدار
          </button>
        </div>
      ) : items.length > 0 ? (
        <p className="mt-4 text-xs text-muted-foreground">
          برای همهٔ نمادهای دیده‌بان هشدار تنظیم شده است.
        </p>
      ) : null}

      {alerts.isLoading ? (
        <p className="mt-4 text-sm text-muted-foreground">در حال بارگذاری هشدارها…</p>
      ) : alerts.isError ? (
        <p role="alert" className="mt-4 text-sm text-muted-foreground">
          فهرست هشدارها در دسترس نیست.{" "}
          <button
            type="button"
            onClick={() => void alerts.refetch()}
            className="text-emerald underline"
          >
            تلاش مجدد
          </button>
        </p>
      ) : items.length === 0 ? (
        <p className="mt-4 text-sm text-muted-foreground">هنوز هشداری تنظیم نکرده‌اید.</p>
      ) : (
        <ul className="mt-4 space-y-2">
          {items.map((subscription) => {
            const paused = subscription.state === "Paused";
            return (
              <li
                key={subscription.id}
                className={`rounded-xl border border-hairline bg-background/50 p-4 ${paused ? "opacity-70" : ""}`}
              >
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <p className="font-semibold text-foreground">
                      <bdi>{subscription.symbol}</bdi>
                      {paused && (
                        <span className="mr-2 rounded-full bg-muted px-2 py-0.5 text-[10px] font-medium text-muted-foreground">
                          متوقف
                        </span>
                      )}
                    </p>
                    <p className="truncate text-xs text-muted-foreground">
                      {subscription.companyName}
                    </p>
                  </div>
                  <div className="flex shrink-0 items-center gap-1">
                    <button
                      type="button"
                      disabled={busy}
                      onClick={() =>
                        update.mutate({
                          subscription,
                          patch: { state: paused ? "Active" : "Paused" },
                        })
                      }
                      className="min-h-9 rounded-lg px-3 text-xs font-medium text-muted-foreground ring-1 ring-hairline transition hover:text-foreground disabled:opacity-50"
                    >
                      {paused ? "ادامه" : "توقف"}
                    </button>
                    <button
                      type="button"
                      disabled={busy}
                      onClick={() => remove.mutate(subscription.id)}
                      aria-label={`حذف هشدار ${subscription.symbol}`}
                      title="حذف هشدار"
                      className="flex size-9 items-center justify-center rounded-lg text-muted-foreground transition hover:bg-rose/10 hover:text-rose disabled:opacity-50"
                    >
                      <Trash2 className="size-4" aria-hidden="true" />
                    </button>
                  </div>
                </div>
                <div className="mt-3 flex flex-wrap items-center gap-2">
                  {CODAL_ANNOUNCEMENT_TYPES.map((type) => {
                    const on = subscription.announcementTypes.includes(type);
                    const last = on && subscription.announcementTypes.length === 1;
                    return (
                      <button
                        key={type}
                        type="button"
                        aria-pressed={on}
                        disabled={busy || last}
                        title={last ? "دست‌کم یک نوع اطلاعیه لازم است" : undefined}
                        onClick={() =>
                          update.mutate({
                            subscription,
                            patch: {
                              announcementTypes: on
                                ? subscription.announcementTypes.filter((item) => item !== type)
                                : [...subscription.announcementTypes, type],
                            },
                          })
                        }
                        className={`rounded-full px-3 py-1 text-xs font-medium ring-1 transition disabled:cursor-not-allowed ${on ? "bg-emerald-soft/70 text-emerald ring-emerald/30" : "text-muted-foreground ring-hairline hover:text-foreground"}`}
                      >
                        {TYPE_LABEL[type]}
                      </button>
                    );
                  })}
                  <label className="mr-auto flex items-center gap-2 text-xs text-muted-foreground">
                    خلاصهٔ هوشمند
                    <Switch
                      checked={subscription.aiSummaryEnabled}
                      disabled={busy}
                      onCheckedChange={(checked) =>
                        update.mutate({ subscription, patch: { aiSummaryEnabled: checked } })
                      }
                      aria-label={`خلاصهٔ هوشمند برای ${subscription.symbol}`}
                    />
                  </label>
                </div>
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}
