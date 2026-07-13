import { createFileRoute } from "@tanstack/react-router";
import { useEffect, useState } from "react";
import {
  createTelegramLinkChallenge,
  getTelegramLink,
  unlinkTelegram,
  type TelegramLinkChallenge,
  type TelegramLinkView,
} from "@/integrations/financial-copilot/telegram-link";

export const Route = createFileRoute("/_app/telegram-link")({
  head: () => ({ meta: [{ title: "اتصال تلگرام — دستیار هوشمند تحلیل بازار" }] }),
  component: TelegramLinkPage,
});

function TelegramLinkPage() {
  const [link, setLink] = useState<TelegramLinkView | null>(null);
  const [challenge, setChallenge] = useState<TelegramLinkChallenge | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    void getTelegramLink()
      .then(setLink)
      .catch((reason: unknown) => {
        if (!(reason instanceof Error) || !("status" in reason) || reason.status !== 404) setError("دریافت وضعیت اتصال ناموفق بود.");
      })
      .finally(() => setLoading(false));
  }, []);

  async function createChallenge() {
    setLoading(true);
    setError("");
    try {
      setChallenge(await createTelegramLinkChallenge());
    } catch {
      setError("ساخت پیوند اتصال ناموفق بود.");
    } finally {
      setLoading(false);
    }
  }

  async function unlink() {
    setLoading(true);
    setError("");
    try {
      await unlinkTelegram();
      setLink(null);
      setChallenge(null);
    } catch {
      setError("قطع اتصال ناموفق بود.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <div dir="rtl" className="mx-auto w-full max-w-2xl p-6 text-right">
      <div className="rounded-2xl border border-border bg-surface p-6">
        <h1 className="text-lg font-bold">اتصال حساب تلگرام</h1>
        <p className="mt-2 text-sm text-muted-foreground">تلگرام فقط یک مسیر دسترسی است و حساب، اعتبار و سابقه شما همان حساب فعلی باقی می‌ماند.</p>
        {error && <p className="mt-4 text-sm text-destructive">{error}</p>}
        {link ? (
          <div className="mt-5 space-y-3">
            <p className="text-sm">متصل به شناسه ***{String(link.telegramUserId).slice(-4)}{link.username ? ` (@${link.username})` : ""}</p>
            <button onClick={unlink} disabled={loading} className="rounded-lg border border-destructive px-4 py-2 text-sm text-destructive disabled:opacity-50">قطع اتصال</button>
          </div>
        ) : challenge ? (
          <div className="mt-5 space-y-3">
            <p className="text-sm text-muted-foreground">این پیوند یک‌بارمصرف است و در {new Date(challenge.expiresAtUtc).toLocaleTimeString("fa-IR")} منقضی می‌شود.</p>
            <a href={challenge.deepLink} target="_blank" rel="noreferrer" className="block rounded-lg bg-emerald px-4 py-2.5 text-center text-sm font-semibold text-primary-foreground">باز کردن ربات تلگرام</a>
            <button onClick={createChallenge} disabled={loading} className="text-sm text-muted-foreground">ساخت پیوند جدید</button>
          </div>
        ) : (
          <button onClick={createChallenge} disabled={loading} className="mt-5 rounded-lg bg-emerald px-4 py-2.5 text-sm font-semibold text-primary-foreground disabled:opacity-50">{loading ? "در حال بررسی…" : "اتصال به تلگرام"}</button>
        )}
      </div>
    </div>
  );
}
