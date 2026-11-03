import { useEffect, useState } from "react";
import { Monitor, X } from "lucide-react";
import { useMediaQuery } from "@/hooks/use-media-query";
import { dismiss, isDismissed } from "@/lib/dismissal";
import { useExperienceSwitch } from "@/lib/experience/use-experience-switch";

const DISMISS_KEY = "sapio-desktop-suggestion-dismissed";

/** `/m` on a wide screen with a fine pointer suggests desktop. It never redirects (spec §7.6). */
export function DesktopSuggestion() {
  const wide = useMediaQuery("(min-width: 1024px) and (pointer: fine)");
  const [dismissed, setDismissed] = useState(true);
  const { switchToDesktop } = useExperienceSwitch();
  useEffect(() => setDismissed(isDismissed(DISMISS_KEY)), []);

  if (!wide || dismissed) return null;
  return (
    <div className="flex shrink-0 items-center gap-3 border-b border-hairline bg-surface px-4 py-2 text-sm">
      <Monitor className="size-4 shrink-0 text-emerald" aria-hidden="true" />
      <p className="min-w-0 flex-1">برای صفحه‌های بزرگ، نسخه دسکتاپ مناسب‌تر است.</p>
      <button
        type="button"
        onClick={switchToDesktop}
        className="min-h-11 shrink-0 rounded-lg px-3 font-medium text-emerald hover:bg-emerald-soft/60 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40"
      >
        نسخه دسکتاپ
      </button>
      <button
        type="button"
        onClick={() => {
          dismiss(DISMISS_KEY);
          setDismissed(true);
        }}
        aria-label="بستن پیشنهاد نسخه دسکتاپ"
        className="flex size-11 shrink-0 items-center justify-center rounded-lg text-muted-foreground hover:bg-surface-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
      >
        <X className="size-4" aria-hidden="true" />
      </button>
    </div>
  );
}
