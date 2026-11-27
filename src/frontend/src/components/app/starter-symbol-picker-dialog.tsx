import { StarterSymbolPicker } from "@/components/app/starter-symbol-picker";
import { Dialog, DialogContent, DialogDescription, DialogTitle } from "@/components/ui/dialog";
import type { SymbolStarterPrompt } from "@/lib/chat/starter-prompts";

/** Desktop host of the starter cards' symbol picker; the phone hosts the same picker in a bottom sheet. */
export function StarterSymbolPickerDialog({
  prompt,
  open,
  watchlistSymbols,
  onPick,
  onClose,
  onCloseAutoFocus,
}: {
  /** The card being asked about; it stays set while the dialog animates out. */
  prompt: SymbolStarterPrompt | null;
  open: boolean;
  watchlistSymbols: readonly string[];
  onPick: (symbol: string) => void;
  onClose: () => void;
  /** Returns focus to the card that opened the dialog; the dialog has no trigger of its own. */
  onCloseAutoFocus: (event: Event) => void;
}) {
  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent
        dir="rtl"
        className="max-w-md gap-3 px-2 pb-2 pt-5"
        onCloseAutoFocus={onCloseAutoFocus}
      >
        {/* The dialog's close button sits at the physical right, where a right-to-left title starts. */}
        <DialogTitle className="pl-4 pr-12 text-right text-base">{prompt?.topic}</DialogTitle>
        <DialogDescription className="sr-only">
          نماد را جست‌وجو و انتخاب کنید تا پرسش ارسال شود.
        </DialogDescription>
        {prompt && (
          <StarterSymbolPicker
            key={prompt.id}
            prompt={prompt}
            watchlistSymbols={watchlistSymbols}
            onPick={onPick}
          />
        )}
      </DialogContent>
    </Dialog>
  );
}
