import { createFileRoute, Link } from "@tanstack/react-router";
import { useState } from "react";
import { requestPasswordReset } from "@/integrations/financial-copilot/auth";

export const Route = createFileRoute("/forgot-password")({ component: ForgotPasswordPage });

function ForgotPasswordPage() {
  const [email, setEmail] = useState("");
  const [sent, setSent] = useState(false);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setLoading(true);
    setError("");
    try {
      await requestPasswordReset(email);
      setSent(true);
    } catch {
      setError("درخواست انجام نشد. کمی بعد دوباره تلاش کنید.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <main dir="rtl" className="min-h-screen flex items-center justify-center bg-background p-6">
      <section className="w-full max-w-md rounded-2xl border border-border bg-surface p-6 text-right">
        <h1 className="text-lg font-bold">بازیابی گذرواژه</h1>
        {sent ? <p className="mt-3 text-sm text-muted-foreground">اگر این ایمیل برای حسابی قابل استفاده باشد، دستورهای بازیابی ارسال می‌شود.</p> : <form onSubmit={submit} className="mt-5 space-y-3">
          <label className="block text-xs text-muted-foreground">ایمیل</label>
          <input type="email" required value={email} onChange={(event) => setEmail(event.target.value)} className="w-full rounded-lg border border-input bg-background px-3 py-2.5 text-sm" />
          {error && <p className="text-xs text-destructive">{error}</p>}
          <button disabled={loading} className="w-full rounded-lg bg-emerald py-2.5 text-sm font-semibold text-primary-foreground disabled:opacity-50">{loading ? "..." : "ارسال پیوند بازیابی"}</button>
        </form>}
        <Link to="/auth" className="mt-5 block text-center text-xs text-emerald">بازگشت به ورود</Link>
      </section>
    </main>
  );
}
