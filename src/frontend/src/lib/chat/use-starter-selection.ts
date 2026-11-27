import { useCallback, useRef, useState } from "react";
import type { StarterPrompt, SymbolStarterPrompt } from "@/lib/chat/starter-prompts";

/**
 * What selecting a starter card does, the same on desktop and phone: a direct card sends its question; a symbol
 * card opens the symbol picker, and the picked symbol sends the card's question for it. Closing the picker sends
 * nothing. `prompt` stays set after closing so the picker keeps its content while it animates out.
 */
export function useStarterSelection(submit: (text: string) => void) {
  const [picker, setPicker] = useState<{ prompt: SymbolStarterPrompt | null; open: boolean }>({
    prompt: null,
    open: false,
  });
  // The card that opened the picker. The dialog and the sheet have no trigger element of their own, so they would
  // drop focus on the page body when they close.
  const opener = useRef<HTMLElement | null>(null);
  const select = useCallback(
    (prompt: StarterPrompt) => {
      if (prompt.kind === "symbol") {
        opener.current =
          document.activeElement instanceof HTMLElement ? document.activeElement : null;
        setPicker({ prompt, open: true });
      } else submit(prompt.text);
    },
    [submit],
  );
  const close = useCallback(() => setPicker((current) => ({ ...current, open: false })), []);
  const pick = useCallback(
    (symbol: string) => {
      if (!picker.open || !picker.prompt) return;
      close();
      submit(picker.prompt.forSymbol(symbol));
    },
    [picker, close, submit],
  );
  /** Give to the host's `onCloseAutoFocus`: returns focus to the card that opened the picker. */
  const restoreFocus = useCallback((event: Event) => {
    if (!opener.current?.isConnected) return;
    event.preventDefault();
    opener.current.focus();
  }, []);
  return { prompt: picker.prompt, open: picker.open, select, pick, close, restoreFocus };
}
