import { createFileRoute, useNavigate } from "@tanstack/react-router";
import { useEffect, useRef } from "react";
import { completeTahlilAppLaunch, FinancialCopilotAuthError } from "@/integrations/financial-copilot/auth";
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

    completeTahlilAppLaunch(code)
      .then(() => {
        // The target is a constant, never a caller-supplied parameter (AC-41).
        window.location.replace("/chat");
      })
      .catch((error: unknown) => {
        const type = error instanceof FinancialCopilotAuthError ? error.type : undefined;
        const correlationId = error instanceof FinancialCopilotAuthError ? error.correlationId : undefined;
        void navigate({
          to: "/launch-failed",
          search: { reason: launchFailureReasonFor(type), correlationId },
        });
      });
  }, [navigate]);

  return (
    <main dir="rtl" className="min-h-screen flex items-center justify-center bg-background p-6">
      <section className="w-full max-w-md rounded-2xl border border-border bg-surface p-6 text-right">
        <p className="text-sm text-muted-foreground">در حال ورود به دستیار هوشمند بازار سرمایه…</p>
      </section>
    </main>
  );
}
