import { useCallback, useEffect, useState } from "react";
import { LoaderCircle, LockKeyhole, RefreshCw, UserCheck, Users } from "lucide-react";
import { Button } from "@/components/ui/button";
import { adminApi, type AdminUserAnalyticsSnapshot } from "@/integrations/financial-copilot/admin-client";

const number = (value: number) => value.toLocaleString("fa-IR");
const sourceLabels: Record<string, string> = {
  native: "ثبت‌نام مستقیم",
  tahlilapp: "تحلیل‌اپ",
  unknown: "نامشخص",
};
const planLabels: Record<string, string> = {
  Free: "رایگان",
  Plus: "پلاس",
  Pro: "حرفه‌ای",
  Max: "مکس",
  none: "بدون اشتراک فعال",
};
const colors: Record<string, string> = {
  native: "bg-sky-500",
  tahlilapp: "bg-emerald-500",
  unknown: "bg-slate-400",
};

function percent(count: number, total: number) {
  return total > 0 ? Math.round((count / total) * 100) : 0;
}

function monthLabel(period: string) {
  const [year, month] = period.split("-").map(Number);
  const date = new Date(Date.UTC(year, month - 1, 15));
  return new Intl.DateTimeFormat("fa-IR-u-ca-persian", { month: "short", year: "2-digit" }).format(date);
}

export function UserAnalyticsPanel() {
  const [snapshot, setSnapshot] = useState<AdminUserAnalyticsSnapshot | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setSnapshot(await adminApi.userAnalytics());
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "دریافت آمار کاربران ناموفق بود.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const maxMonthly = Math.max(
    1,
    ...(snapshot?.registrationsByMonth.map(
      (row) => row.nativeUsers + row.tahlilAppUsers + row.unknownSourceUsers,
    ) ?? []),
  );

  return (
    <div dir="rtl" className="space-y-5">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h2 className="text-lg font-bold text-slate-900">نمای کلی کاربران</h2>
          <p className="mt-1 text-sm text-slate-500">
            آمار حساب‌های این سازمان بر اساس منبع جذب و اشتراک فعال
          </p>
        </div>
        <Button variant="outline" className="rounded-xl" onClick={() => void refresh()} disabled={loading}>
          {loading ? <LoaderCircle className="ml-2 size-4 animate-spin" /> : <RefreshCw className="ml-2 size-4" />}
          به‌روزرسانی
        </Button>
      </div>

      {error && (
        <div role="alert" className="rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-800">
          {error}
        </div>
      )}
      {loading && !snapshot && (
        <div role="status" className="flex items-center gap-2 rounded-xl border border-slate-200 bg-white p-5 text-sm text-slate-500">
          <LoaderCircle className="size-4 animate-spin" /> در حال بارگذاری آمار...
        </div>
      )}
      {snapshot && (
        <>
          <div className="grid gap-3 sm:grid-cols-3">
            <MetricCard icon={Users} label="کل کاربران" value={snapshot.totalUsers} tone="slate" />
            <MetricCard icon={UserCheck} label="حساب‌های فعال" value={snapshot.enabledUsers} tone="emerald" />
            <MetricCard icon={LockKeyhole} label="حساب‌های قفل‌شده" value={snapshot.lockedUsers} tone="amber" />
          </div>

          <section className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm sm:p-6">
            <div className="mb-5 flex flex-wrap items-start justify-between gap-2">
              <div>
                <h3 className="font-bold text-slate-900">روند ثبت‌نام در ۱۲ ماه اخیر</h3>
                <p className="mt-1 text-xs text-slate-500">تفکیک بر اساس منبع جذب ثبت‌شده هنگام ساخت حساب</p>
              </div>
              <div className="flex flex-wrap gap-x-4 gap-y-2 text-xs text-slate-600">
                <Legend color="bg-sky-500" label="ثبت‌نام مستقیم" />
                <Legend color="bg-emerald-500" label="تحلیل‌اپ" />
                <Legend color="bg-slate-400" label="نامشخص" />
              </div>
            </div>
            <div className="grid h-52 grid-cols-12 items-end gap-2 border-b border-slate-100 pb-2 sm:gap-3">
              {snapshot.registrationsByMonth.map((row) => {
                const total = row.nativeUsers + row.tahlilAppUsers + row.unknownSourceUsers;
                const height = total === 0 ? 2 : Math.max(8, (total / maxMonthly) * 100);
                const segmentHeight = (value: number) => total > 0 ? `${(value / total) * height}%` : "0%";
                return (
                  <div key={row.period} className="flex h-full min-w-0 flex-col items-center justify-end gap-1">
                    <span className="text-[10px] tabular-nums text-slate-500">{total ? number(total) : ""}</span>
                    <div
                      className="flex w-full max-w-9 flex-col-reverse overflow-hidden rounded-t-md bg-slate-50"
                      style={{ height: `${height}%`, minHeight: 2 }}
                      title={`${monthLabel(row.period)}: ${number(total)}`}
                    >
                      <span className="bg-sky-500" style={{ height: segmentHeight(row.nativeUsers) }} />
                      <span className="bg-emerald-500" style={{ height: segmentHeight(row.tahlilAppUsers) }} />
                      <span className="bg-slate-400" style={{ height: segmentHeight(row.unknownSourceUsers) }} />
                    </div>
                    <span className="text-[10px] text-slate-500">{monthLabel(row.period)}</span>
                  </div>
                );
              })}
            </div>
            <p className="mt-3 text-[11px] text-slate-400">
              آخرین به‌روزرسانی: {new Intl.DateTimeFormat("fa-IR", { dateStyle: "medium", timeStyle: "short" }).format(new Date(snapshot.asOfUtc))}
            </p>
          </section>

          <div className="grid gap-5 xl:grid-cols-2">
            <BreakdownCard title="کاربران به تفکیک منبع جذب" subtitle="سهم هر منبع از کل کاربران">
              {snapshot.acquisitionSources.map((row) => (
                <BreakdownRow
                  key={row.code}
                  label={sourceLabels[row.code] ?? row.code}
                  count={row.userCount}
                  total={snapshot.totalUsers}
                  color={colors[row.code] ?? "bg-slate-400"}
                />
              ))}
            </BreakdownCard>

            <BreakdownCard title="کاربران به تفکیک اشتراک" subtitle="اشتراک‌های فعال در زمان گزارش">
              {snapshot.subscriptionBreakdownAvailable ? (
                snapshot.subscriptionPlans.map((row) => (
                  <BreakdownRow
                    key={row.code}
                    label={planLabels[row.code] ?? row.code}
                    count={row.userCount}
                    total={snapshot.totalUsers}
                    color={row.code === "none" ? "bg-slate-300" : "bg-indigo-500"}
                  />
                ))
              ) : (
                <p className="rounded-xl bg-amber-50 px-3 py-3 text-sm text-amber-800">
                  برای مشاهدهٔ تفکیک اشتراک، مجوز «خواندن اشتراک‌ها» لازم است.
                </p>
              )}
            </BreakdownCard>
          </div>
        </>
      )}
    </div>
  );
}

function MetricCard({
  icon: Icon,
  label,
  value,
  tone,
}: {
  icon: typeof Users;
  label: string;
  value: number;
  tone: "slate" | "emerald" | "amber";
}) {
  const tones = {
    slate: "bg-slate-100 text-slate-700",
    emerald: "bg-emerald-100 text-emerald-700",
    amber: "bg-amber-100 text-amber-700",
  };
  return (
    <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm sm:p-5">
      <div className="flex items-center justify-between gap-3">
        <span className="text-sm text-slate-500">{label}</span>
        <span className={`flex size-9 items-center justify-center rounded-xl ${tones[tone]}`}>
          <Icon className="size-4" />
        </span>
      </div>
      <p className="mt-3 text-2xl font-bold tabular-nums text-slate-900">{number(value)}</p>
    </div>
  );
}

function Legend({ color, label }: { color: string; label: string }) {
  return <span className="inline-flex items-center gap-1.5"><span className={`size-2 rounded-full ${color}`} />{label}</span>;
}

function BreakdownCard({
  title,
  subtitle,
  children,
}: {
  title: string;
  subtitle: string;
  children: React.ReactNode;
}) {
  return (
    <section className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm sm:p-6">
      <h3 className="font-bold text-slate-900">{title}</h3>
      <p className="mt-1 text-xs text-slate-500">{subtitle}</p>
      <div className="mt-5 space-y-4">{children}</div>
    </section>
  );
}

function BreakdownRow({
  label,
  count,
  total,
  color,
}: {
  label: string;
  count: number;
  total: number;
  color: string;
}) {
  const share = percent(count, total);
  return (
    <div>
      <div className="mb-1.5 flex items-center justify-between gap-3 text-sm">
        <span className="truncate text-slate-700">{label}</span>
        <span className="shrink-0 tabular-nums text-slate-600">
          {number(count)} <span className="text-xs text-slate-400">({number(share)}٪)</span>
        </span>
      </div>
      <div className="h-2 overflow-hidden rounded-full bg-slate-100" role="progressbar" aria-valuenow={share} aria-valuemin={0} aria-valuemax={100} aria-label={label}>
        <div className={`h-full rounded-full ${color}`} style={{ width: `${share}%` }} />
      </div>
    </div>
  );
}
