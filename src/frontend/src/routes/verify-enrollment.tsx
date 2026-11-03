import { createFileRoute, Link } from "@tanstack/react-router";
import { useEffect, useRef, useState } from "react";
import {
  confirmEmailEnrollment,
  FinancialCopilotAuthError,
  isAuthenticated,
} from "@/integrations/financial-copilot/auth";
import { safeInternalPath } from "@/lib/safe-redirect";

export const Route = createFileRoute("/verify-enrollment")({
  head: () => ({
    meta: [
      { name: "referrer", content: "no-referrer" },
      { name: "robots", content: "noindex" },
    ],
  }),
  component: VerifyEnrollmentPage,
});

type Outcome =
  | "checking"
  | "signed-out"
  | "success"
  | "invalid"
  | "conflict"
  | "session"
  | "unavailable";

// Target of the sign-in link. Passed through the safe-redirect helper like every other redirect value.
const PROFILE_TARGET = safeInternalPath("/profile");

function VerifyEnrollmentPage() {
  const [outcome, setOutcome] = useState<Outcome>("checking");
  // React StrictMode runs effects twice in development; the one-time token must be submitted only once.
  const attempted = useRef(false);

  useEffect(() => {
    if (attempted.current) return;
    attempted.current = true;

    const fragment = new URLSearchParams(window.location.hash.slice(1));
    const token = fragment.get("token") ?? "";
    // Remove the token from the address bar and history before any request is made.
    window.history.replaceState(null, "", window.location.pathname + window.location.search);

    if (!token) {
      setOutcome("invalid");
      return;
    }

    void (async () => {
      // Confirmation is authorized by the signed-in session. Without one the token is not spent, and the
      // user is asked to sign in and reopen the email link.
      if (!(await isAuthenticated())) {
        setOutcome("signed-out");
        return;
      }
      try {
        await confirmEmailEnrollment(token);
        setOutcome("success");
      } catch (error) {
        setOutcome(outcomeFor(error));
      }
    })();
  }, []);

  return (
    <main dir="rtl" className="min-h-screen flex items-center justify-center bg-background p-6">
      <section className="w-full max-w-md rounded-2xl border border-border bg-surface p-6 text-right">
        <h1 className="text-lg font-bold">تایید نشانی ایمیل</h1>
        <div aria-live="polite">
          {outcome === "checking" ? (
            <p className="mt-3 text-sm text-muted-foreground">در حال تایید پیوند…</p>
          ) : null}
          {outcome === "success" ? (
            <>
              <p className="mt-3 text-sm">نشانی ایمیل شما تایید شد. اکنون می‌توانید برای حساب خود گذرواژه تعیین کنید.</p>
              <Link to="/profile" className="mt-5 block text-sm text-emerald">
                رفتن به پروفایل حساب
              </Link>
            </>
          ) : null}
          {outcome === "invalid" ? (
            <>
              <p className="mt-3 text-sm text-destructive">این پیوند نامعتبر یا منقضی شده است.</p>
              <Link to="/profile" className="mt-5 block text-sm text-emerald">
                رفتن به پروفایل حساب
              </Link>
            </>
          ) : null}
          {outcome === "conflict" ? (
            <>
              <p className="mt-3 text-sm text-destructive">
                این نشانی ایمیل قبلاً برای حساب دیگری استفاده شده است.
              </p>
              <Link to="/profile" className="mt-5 block text-sm text-emerald">
                رفتن به پروفایل حساب
              </Link>
            </>
          ) : null}
          {outcome === "unavailable" ? (
            <>
              <p className="mt-3 text-sm text-destructive">تایید انجام نشد. کمی بعد دوباره تلاش کنید.</p>
              <Link to="/profile" className="mt-5 block text-sm text-emerald">
                رفتن به پروفایل حساب
              </Link>
            </>
          ) : null}
          {outcome === "signed-out" || outcome === "session" ? (
            <>
              <p className="mt-3 text-sm text-muted-foreground">
                {outcome === "session"
                  ? "نشست شما معتبر نیست."
                  : "برای تایید نشانی ایمیل ابتدا وارد حساب شوید."}{" "}
                پس از ورود، پیوند ایمیل را دوباره باز کنید.
              </p>
              <Link to="/auth" search={{ redirect: PROFILE_TARGET }} className="mt-5 block text-sm text-emerald">
                ورود به حساب
              </Link>
            </>
          ) : null}
        </div>
      </section>
    </main>
  );
}

function outcomeFor(error: unknown): Outcome {
  if (!(error instanceof FinancialCopilotAuthError)) return "unavailable";
  switch (error.status) {
    case 400:
      return "invalid";
    case 409:
      return "conflict";
    case 401:
      return "session";
    default:
      return "unavailable";
  }
}
