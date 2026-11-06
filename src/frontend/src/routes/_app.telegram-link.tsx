import { createFileRoute } from "@tanstack/react-router";
import { useQuery } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { BellRing, Bot, CheckCircle2, ExternalLink, Sparkles } from "lucide-react";
import {
  createTelegramLinkChallenge,
  getTelegramEntitlement,
  getTelegramLink,
  unlinkTelegram,
  type TelegramLinkChallenge,
  type TelegramLinkView,
} from "@/integrations/financial-copilot/telegram-link";
import { CodalAlertSettings } from "@/components/app/codal-alert-settings";
import {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import { formatNumber } from "@/lib/format/persian";

export const Route = createFileRoute("/_app/telegram-link")({
  head: () => ({ meta: [{ title: "اتصال تلگرام — ساپیو - دستیار هوشمند بازار" }] }),
  component: TelegramLinkPage,
});

function TelegramLinkPage() {
  const [link, setLink] = useState<TelegramLinkView | null>(null);
  const [challenge, setChallenge] = useState<TelegramLinkChallenge | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [confirmUnlink, setConfirmUnlink] = useState(false);
  // Today's free Telegram allowance, shown only for a linked account; a failure just hides the line.
  const entitlement = useQuery({
    queryKey: ["telegram-entitlement"],
    queryFn: getTelegramEntitlement,
    enabled: link !== null,
    retry: false,
  });

  useEffect(() => {
    void getTelegramLink()
      .then(setLink)
      .catch((reason: unknown) => {
        if (!(reason instanceof Error) || !("status" in reason) || reason.status !== 404)
          setError("دریافت وضعیت اتصال ناموفق بود.");
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
      setConfirmUnlink(false);
    } catch {
      setError("قطع اتصال ناموفق بود.");
    } finally {
      setLoading(false);
    }
  }

  const allowance = entitlement.data?.freeDailyAllowance;

  return (
    <div dir="rtl" className="mx-auto w-full max-w-2xl p-6 text-right">
      <div className="rounded-2xl border border-border bg-surface p-6">
        <h1 className="text-lg font-bold">اتصال حساب تلگرام</h1>
        <p className="mt-2 text-sm leading-7 text-muted-foreground">
          تلگرام فقط یک مسیر دسترسی است و حساب، اعتبار و سابقه شما همان حساب فعلی باقی می‌ماند.
        </p>
        {error && (
          <p role="alert" className="mt-4 text-sm text-destructive">
            {error}
          </p>
        )}

        {link ? (
          <div className="mt-5 rounded-xl bg-emerald-soft/30 p-4 ring-1 ring-emerald/20">
            <p className="flex items-center gap-2 text-sm font-semibold text-emerald">
              <CheckCircle2 className="size-4" aria-hidden="true" />
              اتصال برقرار است
            </p>
            {/* Handle and masked id are LTR runs isolated with <bdi>, so «@» and «***» stay in place in RTL text. */}
            <p className="mt-2 text-sm">
              متصل به{" "}
              {link.username ? (
                <>
                  حساب{" "}
                  <bdi dir="ltr" className="font-medium">
                    @{link.username}
                  </bdi>{" "}
                  ·{" "}
                </>
              ) : null}
              شناسه <bdi dir="ltr">***{String(link.telegramUserId).slice(-4)}</bdi>
            </p>
            <p className="mt-1 text-xs text-muted-foreground">
              از {new Date(link.linkedAtUtc).toLocaleDateString("fa-IR")}
            </p>
            <div className="mt-4 flex flex-wrap items-center gap-2">
              {link.botUrl && (
                <a
                  href={link.botUrl}
                  target="_blank"
                  rel="noreferrer"
                  className="inline-flex min-h-10 items-center gap-2 rounded-lg bg-emerald px-4 text-sm font-semibold text-primary-foreground transition hover:brightness-110"
                >
                  باز کردن ربات ساپیو
                  <ExternalLink className="size-4" aria-hidden="true" />
                </a>
              )}
              <button
                type="button"
                onClick={() => setConfirmUnlink(true)}
                disabled={loading}
                className="min-h-10 rounded-lg px-3 text-sm text-muted-foreground transition hover:bg-rose/10 hover:text-rose disabled:opacity-50"
              >
                قطع اتصال
              </button>
            </div>
          </div>
        ) : challenge ? (
          <div className="mt-5 space-y-3">
            <p className="text-sm text-muted-foreground">
              این پیوند یک‌بارمصرف است و در{" "}
              {new Date(challenge.expiresAtUtc).toLocaleTimeString("fa-IR")} منقضی می‌شود.
            </p>
            <a
              href={challenge.deepLink}
              target="_blank"
              rel="noreferrer"
              className="block rounded-lg bg-emerald px-4 py-2.5 text-center text-sm font-semibold text-primary-foreground"
            >
              باز کردن ربات تلگرام
            </a>
            <button
              type="button"
              onClick={createChallenge}
              disabled={loading}
              className="text-sm text-muted-foreground"
            >
              ساخت پیوند جدید
            </button>
          </div>
        ) : (
          <button
            type="button"
            onClick={createChallenge}
            disabled={loading}
            className="mt-5 rounded-lg bg-emerald px-4 py-2.5 text-sm font-semibold text-primary-foreground disabled:opacity-50"
          >
            {loading ? "در حال بررسی…" : "اتصال به تلگرام"}
          </button>
        )}

        <section
          aria-labelledby="telegram-features-heading"
          className="mt-6 border-t border-hairline pt-5"
        >
          <h2 id="telegram-features-heading" className="text-base font-semibold">
            در تلگرام چه دریافت می‌کنید
          </h2>
          <ul className="mt-3 space-y-3">
            <Feature icon={Bot} title="پرسش از ساپیو در ربات">
              همان پرسش‌های سایت را در ربات بفرستید؛ پاسخ با همان داده‌ها و در صورت نیاز همراه با
              نمودار برمی‌گردد.
              {allowance && (
                <span className="mt-1 block text-foreground">
                  سهمیهٔ رایگان امروز: {formatNumber(allowance.remainingCredits)} از{" "}
                  {formatNumber(allowance.totalCredits)} اعتبار
                </span>
              )}
            </Feature>
            <Feature icon={BellRing} title="هشدار اطلاعیه‌های کدال">
              انتشار صورت مالی یا گزارش ماهانهٔ نمادهای انتخابی، با پیام تلگرام به شما اطلاع داده
              می‌شود.
            </Feature>
            <Feature icon={Sparkles} title="خلاصهٔ هوشمند اطلاعیه">
              اگر برای یک نماد فعالش کنید، خلاصهٔ کوتاهی از اطلاعیه هم همراه پیام می‌آید.
            </Feature>
          </ul>
        </section>

        {link ? (
          <CodalAlertSettings />
        ) : (
          <p className="mt-6 rounded-xl border border-dashed border-hairline px-4 py-3 text-sm leading-7 text-muted-foreground">
            پس از اتصال تلگرام، تنظیم هشدار اطلاعیه‌های کدال در همین صفحه فعال می‌شود.
          </p>
        )}
      </div>

      <AlertDialog open={confirmUnlink} onOpenChange={(open) => !loading && setConfirmUnlink(open)}>
        <AlertDialogContent dir="rtl" className="max-w-sm rounded-2xl border-hairline bg-surface">
          <AlertDialogHeader className="text-right sm:text-right">
            <AlertDialogTitle className="text-base">قطع اتصال تلگرام</AlertDialogTitle>
            <AlertDialogDescription className="leading-7">
              پس از قطع اتصال، پرسش در ربات و هشدارهای کدال در تلگرام متوقف می‌شود. حساب و اعتبار
              شما تغییری نمی‌کند.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter className="gap-2 sm:justify-start sm:space-x-0">
            <button
              type="button"
              onClick={() => void unlink()}
              disabled={loading}
              className="inline-flex min-h-10 items-center justify-center rounded-lg bg-destructive px-4 text-sm font-semibold text-destructive-foreground transition hover:brightness-110 disabled:opacity-60"
            >
              {loading ? "در حال قطع…" : "قطع اتصال"}
            </button>
            <AlertDialogCancel disabled={loading} className="mt-0 min-h-10">
              انصراف
            </AlertDialogCancel>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}

function Feature({
  icon: Icon,
  title,
  children,
}: {
  icon: typeof Bot;
  title: string;
  children: React.ReactNode;
}) {
  return (
    <li className="flex gap-3">
      <span className="flex size-9 shrink-0 items-center justify-center rounded-xl bg-emerald-soft/70 text-emerald">
        <Icon className="size-4" aria-hidden="true" />
      </span>
      <span className="min-w-0">
        <span className="block text-sm font-semibold text-foreground">{title}</span>
        <span className="mt-0.5 block text-xs leading-6 text-muted-foreground">{children}</span>
      </span>
    </li>
  );
}
