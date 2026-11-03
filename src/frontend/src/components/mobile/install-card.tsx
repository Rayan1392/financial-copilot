import { useState } from "react";
import { Download } from "lucide-react";
import { useInstallState } from "@/lib/pwa/install";
import { IosInstallSheet } from "@/components/mobile/ios-install-sheet";

/** Post-answer install card on `/m` (design §9). «بعداً» hides it for 30 days. */
export function InstallCard() {
  const install = useInstallState();
  const [iosOpen, setIosOpen] = useState(false);
  if (!install.showCard) return null;
  return (
    <section
      aria-labelledby="install-card-title"
      className="mt-6 rounded-2xl bg-surface p-4 ring-1 ring-hairline"
    >
      <div className="flex items-start gap-3">
        <Download className="mt-1 size-5 shrink-0 text-emerald" aria-hidden="true" />
        <p id="install-card-title" className="text-sm leading-7 text-foreground">
          ساپیو را روی صفحه اصلی نصب کنید تا سریع‌تر به آن دسترسی داشته باشید.
        </p>
      </div>
      <div className="mt-3 flex gap-2">
        <button
          type="button"
          onClick={() => (install.mode === "ios" ? setIosOpen(true) : void install.promptInstall())}
          className="min-h-11 rounded-xl bg-emerald px-4 text-sm font-medium text-primary-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40"
        >
          نصب
        </button>
        <button
          type="button"
          onClick={install.snooze}
          className="min-h-11 rounded-xl px-4 text-sm font-medium text-muted-foreground ring-1 ring-hairline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
        >
          بعداً
        </button>
      </div>
      <IosInstallSheet open={iosOpen} onOpenChange={setIosOpen} />
    </section>
  );
}
