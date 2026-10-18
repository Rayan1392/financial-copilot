import { createFileRoute, Link } from "@tanstack/react-router";
import {
  parseLaunchFailureReason,
  validateTahlilAppReturnUrl,
  type LaunchFailureReason,
} from "@/integrations/financial-copilot/launch";

type LaunchFailedSearch = {
  reason: LaunchFailureReason;
  correlationId?: string;
};

const FAILURE_COPY: Record<LaunchFailureReason, string> = {
  invalid: "ورود به دستیار هوشمند بازار سرمایه انجام نشد. لطفاً از تحلیل‌اپ دوباره وارد شوید.",
  unavailable: "سرویس تحلیل‌اپ در دسترس نیست؛ چند دقیقه دیگر دوباره تلاش کنید.",
  disabled: "حساب شما غیرفعال است.",
  unknown: "ورود به دستیار هوشمند بازار سرمایه انجام نشد. لطفاً از تحلیل‌اپ دوباره وارد شوید.",
};

export const Route = createFileRoute("/launch-failed")({
  // Closed enum only: the reason is never reflected as free text (Design §14).
  validateSearch: (search: Record<string, unknown>): LaunchFailedSearch => ({
    reason: parseLaunchFailureReason(search.reason),
    correlationId:
      typeof search.correlationId === "string" ? search.correlationId.slice(0, 64) : undefined,
  }),
  head: () => ({
    meta: [
      { name: "referrer", content: "no-referrer" },
      { name: "robots", content: "noindex" },
    ],
  }),
  component: LaunchFailedPage,
});

function LaunchFailedPage() {
  const { reason, correlationId } = Route.useSearch();
  // Only a configured, validated URL is rendered; the search input never reaches this value.
  const returnUrl = validateTahlilAppReturnUrl(import.meta.env.VITE_TAHLILAPP_RETURN_URL, {
    isDev: import.meta.env.DEV,
  });

  // This page never redirects on its own, so a failing launch cannot loop (AC-40).
  return (
    <main dir="rtl" className="min-h-screen flex items-center justify-center bg-background p-6">
      <section className="w-full max-w-md rounded-2xl border border-border bg-surface p-6 text-right">
        <h1 className="text-lg font-bold">ورود انجام نشد</h1>
        <p className="mt-3 text-sm text-destructive">{FAILURE_COPY[reason]}</p>
        {correlationId ? (
          <p className="mt-4 text-xs text-muted-foreground">
            کد پیگیری: <span className="select-all font-mono">{correlationId}</span>
          </p>
        ) : null}
        {returnUrl ? (
          <a
            href={returnUrl}
            rel="noopener noreferrer"
            className="mt-5 block rounded-lg bg-emerald px-4 py-2.5 text-center text-sm font-semibold text-primary-foreground"
          >
            بازگشت به تحلیل‌اپ
          </a>
        ) : null}
        <Link to="/auth" search={{ redirect: "/chat" }} className="mt-5 block text-sm text-emerald">
          ورود با ایمیل و رمز عبور
        </Link>
      </section>
    </main>
  );
}
