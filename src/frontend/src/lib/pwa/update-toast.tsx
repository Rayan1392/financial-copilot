import { useEffect, useState } from "react";
import { useIsMutating } from "@tanstack/react-query";
import { RefreshCw } from "lucide-react";
import { registerServiceWorker } from "@/lib/pwa/register";
import { captureInstallPrompt } from "@/lib/pwa/install";

/**
 * PWA runtime for both experiences (Feature 153 T18): registers the worker, captures the install prompt, and shows
 * «نسخه جدید آماده است» for a waiting worker or a chunk that failed to load after a deploy (`vite:preloadError`).
 * It never reloads on its own, and not while a question is being answered.
 */
export function PwaRuntime() {
  const [activate, setActivate] = useState<(() => void) | null>(null);
  const pendingMutations = useIsMutating();

  useEffect(() => {
    captureInstallPrompt();
    void registerServiceWorker((apply) => setActivate(() => apply));
    const onPreloadError = (event: Event) => {
      event.preventDefault();
      setActivate(() => () => window.location.reload());
    };
    window.addEventListener("vite:preloadError", onPreloadError);
    return () => window.removeEventListener("vite:preloadError", onPreloadError);
  }, []);

  if (!activate) return null;
  const busy = pendingMutations > 0;
  return (
    <div
      role="status"
      className="fixed inset-x-3 bottom-[calc(env(safe-area-inset-bottom)+80px)] z-50 mx-auto flex max-w-md items-center gap-3 rounded-2xl bg-surface px-4 py-3 text-sm text-foreground shadow-lg ring-1 ring-hairline"
      dir="rtl"
    >
      <RefreshCw className="size-4 shrink-0 text-emerald" aria-hidden="true" />
      <span className="min-w-0 flex-1">
        {busy ? "نسخه جدید پس از پایان پاسخ آماده نصب است" : "نسخه جدید آماده است"}
      </span>
      <button
        type="button"
        disabled={busy}
        onClick={() => activate()}
        className="min-h-11 shrink-0 rounded-xl bg-emerald px-4 font-medium text-primary-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40 disabled:opacity-50"
      >
        به‌روزرسانی
      </button>
    </div>
  );
}
