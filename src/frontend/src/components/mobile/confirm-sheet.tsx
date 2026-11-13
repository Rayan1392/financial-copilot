import { BottomSheet } from "@/components/mobile/bottom-sheet";

/** Destructive confirmation as a bottom sheet (design §5): rose confirm, neutral cancel. */
export function ConfirmSheet({
  open,
  onOpenChange,
  title,
  description,
  confirmLabel,
  onConfirm,
  pending,
  error,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description: string;
  confirmLabel: string;
  onConfirm: () => void;
  pending?: boolean;
  error?: string | null;
}) {
  return (
    <BottomSheet open={open} onOpenChange={onOpenChange} title={title} description={description}>
      {error && (
        <p
          role="alert"
          className="mx-2 mb-2 rounded-xl bg-rose/10 px-3 py-2 text-m-title mobile-danger-text"
        >
          {error}
        </p>
      )}
      <div className="flex gap-2 px-2 pt-2">
        <button
          type="button"
          onClick={onConfirm}
          disabled={pending}
          className="min-h-12 flex-1 rounded-xl bg-rose text-m-body font-semibold text-black focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-rose/40 disabled:opacity-60"
        >
          {confirmLabel}
        </button>
        <button
          type="button"
          onClick={() => onOpenChange(false)}
          className="min-h-12 flex-1 rounded-xl bg-surface text-m-body font-medium text-foreground ring-1 ring-hairline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
        >
          انصراف
        </button>
      </div>
    </BottomSheet>
  );
}
