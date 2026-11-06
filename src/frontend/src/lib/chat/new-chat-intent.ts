import { useLocation, useRouter } from "@tanstack/react-router";
import { useEffect, useRef } from "react";
import { symbolQuestionPrefill } from "@/lib/chat/symbol-prompts";

/**
 * Intents handed to a new-chat screen through router state (never the URL, so questions do not leak into
 * history entries, logs, or referrers). The screen consumes an intent once and then clears it from its history
 * entry, so going back never re-submits a question.
 */
declare module "@tanstack/react-router" {
  interface HistoryState {
    /** Submit this question as a new conversation on arrival. */
    submitQuestion?: string;
    /** Prefill the composer with this text. */
    prefillQuestion?: string;
    /** «گفتگوی جدید»: start from an empty composer. */
    newChat?: boolean;
  }
}

/** The new-chat screens that accept an intent: the mobile shell and the desktop workspace. */
export type NewChatScreen = "/m" | "/chat";

export interface NewChatIntent {
  submitQuestion?: string;
  prefillQuestion?: string;
  newChat?: boolean;
}

export function useOpenNewChat(screen: NewChatScreen = "/m") {
  const router = useRouter();
  return {
    ask(question: string) {
      void router.navigate({ to: screen, state: { submitQuestion: question } });
    },
    askAbout(symbol: string) {
      void router.navigate({
        to: screen,
        state: { prefillQuestion: symbolQuestionPrefill(symbol) },
      });
    },
  };
}

/**
 * Hands the intent on the current history entry to `handle` once, after clearing it from that entry. Runs per
 * history entry, so a second intent sent while the screen is open (a new entry) is handled too.
 */
export function useNewChatIntent(screen: NewChatScreen, handle: (intent: NewChatIntent) => void) {
  const router = useRouter();
  const location = useLocation();
  const consumedEntry = useRef<string | null>(null);
  const entryKey = location.state.__TSR_key ?? location.state.key ?? "";

  useEffect(() => {
    if (consumedEntry.current === entryKey) return;
    consumedEntry.current = entryKey;
    const { submitQuestion, prefillQuestion, newChat } = location.state;
    if (!submitQuestion && !prefillQuestion && !newChat) return;
    void router.navigate({ to: screen, replace: true, state: {} });
    handle({ submitQuestion, prefillQuestion, newChat });
    // Runs per history entry by design; `handle` is the one from the render that saw the new entry.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [entryKey]);
}
