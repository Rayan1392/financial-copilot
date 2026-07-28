import { createFileRoute, Link } from "@tanstack/react-router";
import { useEffect, useState } from "react";
import { isAuthenticated } from "@/integrations/financial-copilot/auth";
import {
  confirmTelegramLink,
  previewTelegramLink,
  type TelegramLinkPreview,
} from "@/integrations/financial-copilot/telegram-link";

export const Route = createFileRoute("/telegram/link/confirm")({
  validateSearch: (search: Record<string, unknown>) => ({
    token: typeof search.token === "string" ? search.token : "",
  }),
  head: () => ({ meta: [{ title: "تأیید اتصال تلگرام — ساپیو - دستیار هوشمند بازار" }] }),
  component: TelegramLinkConfirmationPage,
});

function TelegramLinkConfirmationPage() {
  const { token } = Route.useSearch();
  const [authenticated, setAuthenticated] = useState<boolean | null>(null);
  const [preview, setPreview] = useState<TelegramLinkPreview | null>(null);
  const [status, setStatus] = useState<"loading" | "ready" | "confirmed" | "error">("loading");
  const [error, setError] = useState("");

  useEffect(() => {
    let active = true;
    void (async () => {
      const signedIn = await isAuthenticated();
      if (!active) return;
      setAuthenticated(signedIn);
      if (!signedIn || !token) {
        setStatus(token ? "ready" : "error");
        if (!token) setError("پیوند تأیید نامعتبر است.");
        return;
      }
      try {
        const value = await previewTelegramLink(token);
        if (active) {
          setPreview(value);
          setStatus("ready");
        }
      } catch {
        if (active) {
          setError("این پیوند نامعتبر، منقضی یا قبلاً استفاده شده است.");
          setStatus("error");
        }
      }
    })();
    return () => {
      active = false;
    };
  }, [token]);

  async function confirm() {
    setStatus("loading");
    try {
      await confirmTelegramLink(token);
      setStatus("confirmed");
    } catch {
      setError("اتصال انجام نشد. لطفاً یک پیوند جدید از ربات دریافت کنید.");
      setStatus("error");
    }
  }

  const authTarget = `/auth?redirect=${encodeURIComponent(`/telegram/link/confirm?token=${token}`)}`;
  return (
    <main dir="rtl" className="min-h-screen flex items-center justify-center bg-background p-6">
      <section className="w-full max-w-md rounded-2xl border border-border bg-surface p-6 text-right">
        <h1 className="text-lg font-bold text-foreground">اتصال حساب تلگرام</h1>
        {authenticated === false ? (
          <>
            <p className="mt-3 text-sm text-muted-foreground">برای تأیید اتصال، ابتدا وارد حساب کاربری خود شوید.</p>
            <a href={authTarget} className="mt-5 block rounded-lg bg-emerald px-4 py-2.5 text-center text-sm font-semibold text-primary-foreground">ورود به حساب</a>
          </>
        ) : status === "confirmed" ? (
          <>
            <p className="mt-3 text-sm text-foreground">حساب تلگرام با موفقیت متصل شد.</p>
            <Link to="/chat" className="mt-5 block text-center text-sm text-emerald">بازگشت به گفتگو</Link>
          </>
        ) : status === "error" ? (
          <p className="mt-3 text-sm text-destructive">{error}</p>
        ) : preview ? (
          <>
            <p className="mt-3 text-sm text-muted-foreground">شناسه تلگرام {preview.maskedTelegramUserId}{preview.username ? ` (@${preview.username})` : ""} به حساب فعلی متصل شود؟</p>
            <button onClick={confirm} disabled={status === "loading"} className="mt-5 w-full rounded-lg bg-emerald px-4 py-2.5 text-sm font-semibold text-primary-foreground disabled:opacity-50">تأیید اتصال</button>
          </>
        ) : (
          <p className="mt-3 text-sm text-muted-foreground">در حال بررسی پیوند…</p>
        )}
      </section>
    </main>
  );
}
