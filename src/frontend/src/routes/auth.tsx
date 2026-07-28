import { createFileRoute } from "@tanstack/react-router";
import { useState } from "react";
import { login, register } from "@/integrations/financial-copilot/auth";

export const Route = createFileRoute("/auth")({
  validateSearch: (search: Record<string, unknown>) => ({
    redirect:
      typeof search.redirect === "string" && search.redirect.startsWith("/") && !search.redirect.startsWith("//")
        ? search.redirect
        : "/chat",
  }),
  head: () => ({ meta: [{ title: "ورود — ساپیو - دستیار هوشمند بازار" }] }),
  component: AuthPage,
});

export function AuthPage() {
  const { redirect } = Route.useSearch();
  return <AuthScreen redirect={redirect} />;
}

export function AuthScreen({ redirect }: { redirect: string }) {
  const [mode, setMode] = useState<"signin" | "signup">("signin");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setLoading(true);
    setError(null);
    try {
      await (mode === "signin" ? login(email, password) : register(email, password));
      window.location.assign(redirect);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "خطا");
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-background p-6">
      <div className="w-full max-w-sm">
        <div className="flex items-center gap-3 mb-10 justify-center">
          <div className="size-10 rounded-xl bg-emerald-soft ring-1 ring-emerald/30 flex items-center justify-center">
            <div className="size-3.5 bg-emerald rounded-full" />
          </div>
          <h1 className="text-lg font-bold text-foreground">ساپیو - دستیار هوشمند بازار</h1>
        </div>

        <div className="rounded-2xl border border-border bg-surface p-6">
          <h2 className="text-base font-semibold mb-1">
            {mode === "signin" ? "ورود به حساب" : "ساخت حساب جدید"}
          </h2>
          <p className="text-xs text-muted-foreground mb-5">
            برای دسترسی به تحلیل‌های هوشمند بازار
          </p>

          <form onSubmit={submit} className="space-y-3">
            <div>
              <label className="block text-xs text-muted-foreground mb-1.5">ایمیل</label>
              <input
                type="email"
                required
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                className="w-full bg-background border border-input rounded-lg px-3 py-2.5 text-sm focus:outline-none focus:ring-2 focus:ring-ring"
              />
            </div>
            <div>
              <label className="block text-xs text-muted-foreground mb-1.5">رمز عبور</label>
              <input
                type="password"
                required
                minLength={6}
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                className="w-full bg-background border border-input rounded-lg px-3 py-2.5 text-sm focus:outline-none focus:ring-2 focus:ring-ring"
              />
            </div>
            {error && <div className="text-xs text-destructive">{error}</div>}
            <button
              disabled={loading}
              className="w-full bg-emerald text-primary-foreground rounded-lg py-2.5 text-sm font-semibold disabled:opacity-50 hover:brightness-110 transition"
            >
              {loading ? "..." : mode === "signin" ? "ورود" : "ساخت حساب"}
            </button>
          </form>

          <button
            onClick={() => setMode(mode === "signin" ? "signup" : "signin")}
            className="w-full text-xs text-muted-foreground hover:text-foreground mt-4"
          >
            {mode === "signin" ? "حساب ندارم — ساخت حساب جدید" : "حساب دارم — ورود"}
          </button>
        </div>
      </div>
    </div>
  );
}
