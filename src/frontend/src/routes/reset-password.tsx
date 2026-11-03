import { createFileRoute, Link } from "@tanstack/react-router";
import { useEffect, useState } from "react";
import { resetPassword } from "@/integrations/financial-copilot/auth";

export const Route = createFileRoute("/reset-password")({ component: ResetPasswordPage });

function ResetPasswordPage() {
  const [token, setToken] = useState("");
  const [password, setPassword] = useState("");
  const [confirmation, setConfirmation] = useState("");
  const [complete, setComplete] = useState(false);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    const fragment = new URLSearchParams(window.location.hash.slice(1));
    setToken(fragment.get("token") ?? "");
    window.history.replaceState(null, "", window.location.pathname + window.location.search);
  }, []);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    if (!token) { setError("پیوند بازیابی نامعتبر است."); return; }
    if (password !== confirmation) { setError("گذرواژه‌ها یکسان نیستند."); return; }
    setLoading(true);
    setError("");
    try {
      await resetPassword(token, password);
      setComplete(true);
    } catch {
      setError("پیوند نامعتبر یا منقضی است، یا گذرواژه شرایط لازم را ندارد.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <main dir="rtl" className="min-h-screen flex items-center justify-center bg-background p-6">
      <section className="w-full max-w-md rounded-2xl border border-border bg-surface p-6 text-right">
        <h1 className="text-lg font-bold">تنظیم گذرواژه جدید</h1>
        {complete ? <><p className="mt-3 text-sm">گذرواژه تغییر کرد. برای ادامه وارد حساب شوید.</p><Link to="/auth" className="mt-5 block text-sm text-emerald">ورود به حساب</Link></> : <form onSubmit={submit} className="mt-5 space-y-3">
          <label className="block text-xs text-muted-foreground">گذرواژه جدید</label>
          <input type="password" minLength={6} required value={password} onChange={(event) => setPassword(event.target.value)} className="w-full rounded-lg border border-input bg-background px-3 py-2.5 text-sm" />
          <label className="block text-xs text-muted-foreground">تکرار گذرواژه</label>
          <input type="password" minLength={6} required value={confirmation} onChange={(event) => setConfirmation(event.target.value)} className="w-full rounded-lg border border-input bg-background px-3 py-2.5 text-sm" />
          {error && <p className="text-xs text-destructive">{error}</p>}
          <button disabled={loading} className="w-full rounded-lg bg-emerald py-2.5 text-sm font-semibold text-primary-foreground disabled:opacity-50">{loading ? "..." : "ثبت گذرواژه جدید"}</button>
        </form>}
      </section>
    </main>
  );
}
