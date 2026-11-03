import { Share, SquarePlus } from "lucide-react";
import { BottomSheet } from "@/components/mobile/bottom-sheet";

/** iOS Safari has no install prompt; this sheet explains Share → Add to Home Screen (design §9). */
export function IosInstallSheet({
  open,
  onOpenChange,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  return (
    <BottomSheet open={open} onOpenChange={onOpenChange} title="افزودن ساپیو به صفحه اصلی">
      <ol className="space-y-4 px-3 py-3 text-[15px] leading-7">
        <li className="flex items-center gap-3">
          <span className="flex size-9 shrink-0 items-center justify-center rounded-xl bg-surface ring-1 ring-hairline">
            <Share className="size-5 text-emerald" aria-hidden="true" />
          </span>
          <span>۱. دکمه اشتراک‌گذاری (Share) را در نوار مرورگر بزنید.</span>
        </li>
        <li className="flex items-center gap-3">
          <span className="flex size-9 shrink-0 items-center justify-center rounded-xl bg-surface ring-1 ring-hairline">
            <SquarePlus className="size-5 text-emerald" aria-hidden="true" />
          </span>
          <span>۲. گزینه «افزودن به صفحه اصلی» (Add to Home Screen) را انتخاب کنید.</span>
        </li>
        <li className="flex items-center gap-3">
          <span className="flex size-9 shrink-0 items-center justify-center rounded-xl bg-surface ring-1 ring-hairline font-semibold text-emerald">
            ✓
          </span>
          <span>۳. در پایان «افزودن» (Add) را بزنید.</span>
        </li>
      </ol>
      <p className="px-3 pb-2 text-sm leading-7 text-muted-foreground">
        پس از نصب، یک بار داخل برنامه وارد حساب خود شوید.
      </p>
    </BottomSheet>
  );
}
