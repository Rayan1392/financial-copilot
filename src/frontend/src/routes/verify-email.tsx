import { createFileRoute, Link } from "@tanstack/react-router";
import { useEffect, useState } from "react";
import { verifyEmail } from "@/integrations/financial-copilot/auth";

export const Route = createFileRoute("/verify-email")({ component: VerifyEmailPage });

function VerifyEmailPage() {
  const [status, setStatus] = useState<"loading" | "success" | "error">("loading");
  useEffect(() => {
    const fragment = new URLSearchParams(window.location.hash.slice(1));
    const token = fragment.get("token") ?? "";
    window.history.replaceState(null, "", window.location.pathname + window.location.search);
    if (!token) {
      setStatus("error");
      return;
    }
    void verifyEmail(token).then(() => setStatus("success")).catch(() => setStatus("error"));
  }, []);

  return (
    <main dir="rtl" className="min-h-screen flex items-center justify-center bg-background p-6">
      <section className="w-full max-w-md rounded-2xl border border-border bg-surface p-6 text-right">
        <h1 className="text-lg font-bold">تایید ایمیل</h1>
        {status === "loading" ? <p className="mt-3 text-sm text-muted-foreground">در حال تایید پیوند…</p> : null}
        {status === "success" ? <><p className="mt-3 text-sm">ایمیل شما تایید شد. اکنون می‌توانید وارد شوید.</p><Link to="/auth" className="mt-5 block text-sm text-emerald">ورود به حساب</Link></> : null}
        {status === "error" ? <><p className="mt-3 text-sm text-destructive">این پیوند نامعتبر یا منقضی شده است.</p><Link to="/auth" className="mt-5 block text-sm text-emerald">بازگشت به ورود</Link></> : null}
      </section>
    </main>
  );
}
