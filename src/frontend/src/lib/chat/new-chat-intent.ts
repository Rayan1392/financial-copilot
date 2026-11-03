import { useRouter } from "@tanstack/react-router";
import { symbolQuestionPrefill } from "@/lib/chat/symbol-prompts";

/**
 * Intents handed to the mobile new-chat screen through router state (never the URL, so questions do not leak
 * into history entries, logs, or referrers). `/m` consumes an intent once and then clears it from its history
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

export function useOpenNewChat() {
  const router = useRouter();
  return {
    ask(question: string) {
      void router.navigate({ to: "/m", state: { submitQuestion: question } });
    },
    askAbout(symbol: string) {
      void router.navigate({ to: "/m", state: { prefillQuestion: symbolQuestionPrefill(symbol) } });
    },
  };
}
