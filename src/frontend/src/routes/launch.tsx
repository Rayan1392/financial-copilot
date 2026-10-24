import { createFileRoute, useNavigate } from "@tanstack/react-router";
import { useCallback, useEffect, useRef, useState } from "react";
import { completeTahlilAppLaunch, FinancialCopilotAuthError, getAuthenticatedUser } from "@/integrations/financial-copilot/auth";
import { extractLaunchCode, launchFailureReasonFor } from "@/integrations/financial-copilot/launch";

export const Route = createFileRoute("/launch")({
  head: () => ({
    meta: [
      { name: "referrer", content: "no-referrer" },
      { name: "robots", content: "noindex" },
    ],
  }),
  component: LaunchPage,
});

function LaunchPage() {
  const navigate = useNavigate();
  const attempted = useRef(false);
  const exchangeStarted = useRef(false);
  const [pendingCode, setPendingCode] = useState<string | null>(null);

  const exchange = useCallback(async (code: string) => {
    if (exchangeStarted.current) return;
    exchangeStarted.current = true;
    try {
      await completeTahlilAppLaunch(code);
      // The target is a constant, never a caller-supplied parameter (AC-41).
      window.location.replace("/chat");
    } catch (error: unknown) {
      const type = error instanceof FinancialCopilotAuthError ? error.type : undefined;
      const correlationId = error instanceof FinancialCopilotAuthError ? error.correlationId : undefined;
      void navigate({
        to: "/launch-failed",
        search: { reason: launchFailureReasonFor(type), correlationId },
      });
    }
  }, [navigate]);

  useEffect(() => {
    // React StrictMode runs effects twice in development; the one-time code must be exchanged only once.
    if (attempted.current) return;
    attempted.current = true;

    const code = extractLaunchCode(window.location.search);
    // Remove the code from the address bar and history before any request is made (Design §7, AC-15).
    window.history.replaceState(null, "", window.location.pathname);

    if (code === null) {
      void navigate({ to: "/launch-failed", search: { reason: "invalid" } });
      return;
    }

    void getAuthenticatedUser().then((existingUser) => {
      if (existingUser) {
        setPendingCode(code);
      } else {
        void exchange(code);
      }
    }).catch(() => void exchange(code));
  }, [exchange, navigate]);

  if (pendingCode) {
    return (
      <main dir="rtl" className="min-h-screen flex items-center justify-center bg-background p-6">
        <section className="w-full max-w-md rounded-2xl border border-border bg-surface p-6 text-right">
          <h1 className="text-lg font-bold">حساب دیگری در دسترس است</h1>
          <p className="mt-3 text-sm text-muted-foreground">
            ادامه ورود از تحلیل‌اپ نشست فعلی SapioAI را جایگزین می‌کند.
          </p>
          <button type="button" onClick={() => void exchange(pendingCode)} className="mt-5 w-full rounded-lg bg-emerald px-4 py-2.5 text-sm font-semibold text-primary-foreground">
            ادامه و جایگزینی نشست
          </button>
          <button type="button" onClick={() => void navigate({ to: "/chat" })} className="mt-3 w-full rounded-lg border border-border px-4 py-2.5 text-sm">
            ماندن در SapioAI
          </button>
        </section>
      </main>
    );
  }

  return (
    <main dir="rtl" className="min-h-screen flex items-center justify-center bg-background p-6">
      <section className="w-full max-w-md rounded-2xl border border-border bg-surface p-6 text-right">
        <p className="text-sm text-muted-foreground">در حال ورود به دستیار هوشمند بازار سرمایه…</p>
      </section>
    </main>
  );
}
