import { createFileRoute } from "@tanstack/react-router";
import { useState } from "react";
import { Eye, EyeOff, FileText, Megaphone, Radar, TrendingUp } from "lucide-react";
import { login } from "@/integrations/financial-copilot/auth";
import { safeInternalPath } from "@/lib/safe-redirect";
import { SapioMark } from "@/components/app/sapio-mark";
import { TAHLILAPP_SAPIO_URL } from "@/lib/external-links";

export const Route = createFileRoute("/auth")({
  validateSearch: (search: Record<string, unknown>): { redirect: string; reason?: "expired" } => ({
    redirect: safeInternalPath(search.redirect),
    // Closed value: only "expired" is recognized, so no arbitrary text reaches the page.
    reason: search.reason === "expired" ? "expired" : undefined,
  }),
  head: () => ({ meta: [{ title: "ورود — ساپیو - دستیار هوشمند بازار" }] }),
  component: AuthPage,
});

export function AuthPage() {
  const { redirect, reason } = Route.useSearch();
  return <AuthScreen redirect={redirect} sessionExpired={reason === "expired"} />;
}

const INPUT_CLASS =
  "w-full bg-background border border-input rounded-lg px-3 py-2.5 text-base sm:text-sm focus:outline-none focus:ring-2 focus:ring-ring aria-[invalid=true]:border-destructive/60";

/**
 * Sign-in form. `withIntro` (the site root, where first-time visitors land) places a short product
 * introduction beside the form; `/auth` (redirects, expired sessions) keeps the compact form only.
 */
export function AuthScreen({
  redirect,
  sessionExpired = false,
  withIntro = false,
}: {
  redirect: string;
  sessionExpired?: boolean;
  withIntro?: boolean;
}) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setLoading(true);
    setError(null);
    try {
      await login(email, password);
      window.location.assign(redirect);
    } catch (err: unknown) {
      setError(
        err instanceof Error && err.message
          ? err.message
          : "ورود انجام نشد. کمی بعد دوباره تلاش کنید.",
      );
    } finally {
      setLoading(false);
    }
  }

  const describedBy = error ? "auth-error" : undefined;
  const card = (
    <div className="w-full max-w-sm">
      {!withIntro && (
        <div className="mb-10 flex items-center justify-center gap-3">
          <SapioMark className="size-11 text-emerald shadow-sm" />
          <h1 className="text-lg font-bold text-foreground">ساپیو - دستیار هوشمند بازار</h1>
        </div>
      )}

      <div className="rounded-2xl border border-border bg-surface p-6">
        <h2 className="text-base font-semibold mb-1">ورود به حساب</h2>
        <p className="text-xs text-muted-foreground mb-5">برای دسترسی به تحلیل‌های هوشمند بازار</p>
        {sessionExpired && (
          <p role="status" className="mb-4 rounded-lg bg-gold/10 px-3 py-2 text-xs leading-6 text-gold">
            نشست شما به پایان رسید؛ دوباره وارد شوید.
          </p>
        )}

        <form onSubmit={submit} className="space-y-3">
          <div>
            <label htmlFor="auth-email" className="block text-xs text-muted-foreground mb-1.5">
              ایمیل
            </label>
            <input
              id="auth-email"
              type="email"
              required
              autoComplete="email"
              inputMode="email"
              dir="ltr"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              aria-invalid={error ? true : undefined}
              aria-describedby={describedBy}
              className={INPUT_CLASS}
            />
          </div>
          <div>
            <label htmlFor="auth-password" className="block text-xs text-muted-foreground mb-1.5">
              گذرواژه
            </label>
            <div className="relative" dir="ltr">
              <input
                id="auth-password"
                type={showPassword ? "text" : "password"}
                required
                autoComplete="current-password"
                dir="ltr"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                aria-invalid={error ? true : undefined}
                aria-describedby={describedBy}
                className={`${INPUT_CLASS} pr-11`}
              />
              <button
                type="button"
                onClick={() => setShowPassword((shown) => !shown)}
                aria-pressed={showPassword}
                aria-controls="auth-password"
                aria-label={showPassword ? "پنهان کردن گذرواژه" : "نمایش گذرواژه"}
                title={showPassword ? "پنهان کردن گذرواژه" : "نمایش گذرواژه"}
                className="absolute inset-y-0 right-1 my-auto flex size-9 items-center justify-center rounded-md text-muted-foreground transition hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
              >
                {showPassword ? (
                  <EyeOff className="size-4" aria-hidden="true" />
                ) : (
                  <Eye className="size-4" aria-hidden="true" />
                )}
              </button>
            </div>
          </div>
          {error && (
            <p id="auth-error" role="alert" className="text-xs leading-6 text-destructive">
              {error}
            </p>
          )}
          <button
            type="submit"
            disabled={loading}
            aria-busy={loading}
            className="w-full bg-emerald text-primary-foreground rounded-lg py-2.5 text-sm font-semibold disabled:opacity-60 hover:brightness-110 transition"
          >
            {loading ? "در حال ورود…" : "ورود"}
          </button>
        </form>

        <a href="/forgot-password" className="mt-4 block text-center text-xs text-emerald">
          گذرواژه را فراموش کرده‌اید؟
        </a>
        <p dir="rtl" className="mt-4 text-right text-xs leading-6 text-muted-foreground sm:mt-5">
          هنوز حساب کاربری ندارید؟
          <br />
          برای ثبت‌نام و ورود به ساپیو، از طریق{" "}
          <a
            href={TAHLILAPP_SAPIO_URL}
            target="_blank"
            rel="noopener noreferrer"
            className="text-emerald hover:underline focus-visible:underline"
          >
            تحلیل‌اپ
          </a>{" "}
          اقدام کنید.
        </p>
      </div>
    </div>
  );

  if (!withIntro) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-background p-6 pt-[max(1.5rem,env(safe-area-inset-top))] pb-[max(1.5rem,env(safe-area-inset-bottom))]">
        {card}
      </div>
    );
  }
  return (
    <div className="min-h-screen bg-background px-6 pt-[max(2rem,env(safe-area-inset-top))] pb-[max(2rem,env(safe-area-inset-bottom))]">
      {/* Phone order: short intro → form → features, so returning users reach the form without scrolling
          past the feature list. Desktop: intro and features in one column, the form beside them. */}
      <div className="mx-auto grid min-h-[calc(100vh-4rem)] max-w-6xl content-center gap-8 lg:grid-cols-[minmax(0,1fr)_24rem] lg:gap-x-16 lg:gap-y-8">
        <ProductIntroHeader />
        <div className="flex justify-center lg:col-start-2 lg:row-span-2 lg:row-start-1 lg:items-center lg:justify-end">
          {card}
        </div>
        <ProductFeatures />
      </div>
    </div>
  );
}

const FEATURES = [
  {
    icon: TrendingUp,
    title: "روند فروش ماهانه و ترکیب محصولات",
    text: "مقایسه با ماه قبل، ماه مشابه سال قبل و میانگین ۱۲ ماهه، از گزارش‌های تولید و فروش.",
  },
  {
    icon: FileText,
    title: "سود و زیان فصلی و ترازنامه",
    text: "اقلام اصلی صورت‌های مالی شرکت، دوره‌به‌دوره و با جدول و نمودار.",
  },
  {
    icon: Radar,
    title: "دیده‌بان نمادها",
    text: "قیمت نمادهای منتخب شما و رویدادهای مهم آن‌ها، با امکان هشدار در تلگرام.",
  },
  {
    icon: Megaphone,
    title: "اطلاعیه‌های کدال",
    text: "فهرست گزارش‌های منتشرشده با فیلتر نماد، نوع اطلاعیه و تاریخ انتشار.",
  },
] as const;

/** First-visit introduction: what Sapio answers, in the product's real capabilities. */
function ProductIntroHeader() {
  return (
    <section aria-labelledby="intro-title" className="text-right lg:col-start-1 lg:row-start-1 lg:self-end">
      <div className="mb-6 flex items-center gap-3">
        <SapioMark className="size-11 text-emerald shadow-sm" />
        <span className="text-lg font-bold text-foreground">ساپیو - دستیار هوشمند بازار</span>
      </div>
      <h1
        id="intro-title"
        className="text-2xl font-bold leading-[1.6] text-foreground text-balance md:text-3xl md:leading-[1.6]"
      >
        گزارش‌های مالی شرکت‌های بورسی را به زبان فارسی بپرسید
      </h1>
      <p className="mt-4 max-w-xl text-sm leading-8 text-muted-foreground md:text-base md:leading-8">
        ساپیو گزارش‌های ماهانهٔ تولید و فروش، صورت‌های مالی و اطلاعیه‌های کدال را می‌خواند و به پرسش شما با
        جدول، نمودار و منبع داده پاسخ می‌دهد.
      </p>
    </section>
  );
}

function ProductFeatures() {
  return (
    <section aria-label="امکانات ساپیو" className="text-right lg:col-start-1 lg:row-start-2 lg:self-start">
      <ul className="grid gap-3 sm:grid-cols-2">
        {FEATURES.map(({ icon: Icon, title, text }) => (
          <li key={title} className="flex gap-3 rounded-2xl border border-hairline bg-surface/60 p-4">
            <span className="flex size-10 shrink-0 items-center justify-center rounded-xl bg-emerald-soft/70 text-emerald">
              <Icon className="size-5" aria-hidden="true" />
            </span>
            <span className="min-w-0">
              <span className="block text-sm font-semibold text-foreground">{title}</span>
              <span className="mt-1 block text-xs leading-6 text-muted-foreground">{text}</span>
            </span>
          </li>
        ))}
      </ul>
      <p className="mt-6 text-xs text-muted-foreground">
        داده‌ها و تحلیل‌ها صرفاً جنبهٔ آموزشی دارند و توصیهٔ سرمایه‌گذاری نیستند.
      </p>
    </section>
  );
}
