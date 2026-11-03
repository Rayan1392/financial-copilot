import { useEffect, useState } from "react";
import { Smartphone, X } from "lucide-react";
import { useMediaQuery } from "@/hooks/use-media-query";
import { dismiss, isDismissed } from "@/lib/dismissal";
import { useExperienceSwitch } from "@/lib/experience/use-experience-switch";

const DISMISS_KEY = "sapio-mobile-suggestion-dismissed";

/**
 * Desktop shell, viewport < 1024 px: the context panel is hidden there, so suggest the mobile workspace.
 * Resizing only shows or hides this banner; it never redirects (spec §7.6).
 */
export function ExperienceBanner() {
  const narrow = useMediaQuery("(max-width: 1023.98px)");
  const [dismissed, setDismissed] = useState(true);
  const { switchToMobile } = useExperienceSwitch();

  useEffect(() => setDismissed(isDismissed(DISMISS_KEY)), []);

  if (!narrow || dismissed) return null;
  return (
    <div
      role="region"
      aria-label="پیشنهاد نسخه موبایل"
      className="flex shrink-0 items-center gap-3 border-b border-hairline bg-emerald-soft/40 px-4 py-2 text-sm text-foreground"
    >
      <Smartphone className="size-4 shrink-0 text-emerald" aria-hidden="true" />
      <p className="min-w-0 flex-1 leading-6">
        برای دسترسی کامل به تاریخچه، بازار و دیده‌بان، نسخه موبایل را باز کنید.
      </p>
      <button
        type="button"
        onClick={switchToMobile}
        className="shrink-0 rounded-lg bg-emerald px-3 py-1.5 text-sm font-medium text-primary-foreground transition hover:brightness-110 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40"
      >
        باز کردن
      </button>
      <button
        type="button"
        onClick={() => {
          dismiss(DISMISS_KEY);
          setDismissed(true);
        }}
        aria-label="بستن پیشنهاد نسخه موبایل"
        className="flex size-9 shrink-0 items-center justify-center rounded-lg text-muted-foreground transition hover:bg-surface hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
      >
        <X className="size-4" aria-hidden="true" />
      </button>
    </div>
  );
}
