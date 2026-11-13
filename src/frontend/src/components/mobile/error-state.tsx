import { IconTile } from "@/components/mobile/icon-tile";
import { ErrorIcon, RefreshIcon } from "@/components/mobile/icons";

/**
 * A screen whose data could not be loaded: what failed, and «تلاش مجدد» when trying again can help. Announced
 * to assistive technology as an alert.
 */
export function ErrorState({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <div
      role="alert"
      className="m-4 flex flex-col items-center gap-4 rounded-2xl bg-surface px-6 py-8 text-center ring-1 ring-hairline"
    >
      <IconTile icon={ErrorIcon} tone="danger" size="lg" />
      <p className="text-m-body font-semibold text-foreground">{message}</p>
      {onRetry && (
        <button
          type="button"
          onClick={onRetry}
          className="flex min-h-12 items-center gap-2 rounded-xl bg-surface-2 px-5 text-m-title font-semibold text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40"
        >
          <RefreshIcon className="size-5" aria-hidden="true" />
          تلاش مجدد
        </button>
      )}
    </div>
  );
}
