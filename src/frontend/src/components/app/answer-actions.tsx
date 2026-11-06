import { useEffect, useRef, useState } from "react";
import { Check, Copy, RefreshCw, Share2 } from "lucide-react";
import { answerPlainText } from "@/lib/chat/answer-text";

const CONFIRMATION_MS = 2000;

type CopyState = "idle" | "copied" | "failed";

/**
 * Actions under an assistant answer: copy its text, share it through the device share sheet (only where the browser
 * supports one), and ask the same question again. Asking again is a new query and is charged like any other.
 */
export function AnswerActions({
  text,
  onRegenerate,
  disabled = false,
}: {
  /** The answer's markdown; copied and shared as plain text. */
  text: string;
  /** Present only for the latest answer that has a question to repeat. */
  onRegenerate?: () => void;
  disabled?: boolean;
}) {
  const [copyState, setCopyState] = useState<CopyState>("idle");
  const resetTimer = useRef<number | undefined>(undefined);
  const canShare = typeof navigator !== "undefined" && typeof navigator.share === "function";
  const plainText = answerPlainText(text);

  useEffect(() => () => window.clearTimeout(resetTimer.current), []);

  if (!plainText && !onRegenerate) return null;

  function showCopyState(state: CopyState) {
    setCopyState(state);
    window.clearTimeout(resetTimer.current);
    resetTimer.current = window.setTimeout(() => setCopyState("idle"), CONFIRMATION_MS);
  }

  async function copy() {
    try {
      await navigator.clipboard.writeText(plainText);
      showCopyState("copied");
    } catch {
      showCopyState("failed");
    }
  }

  async function share() {
    try {
      await navigator.share({ title: "پاسخ ساپیو", text: plainText });
    } catch {
      // The person closed the share sheet, or the browser refused; nothing to report.
    }
  }

  const buttonClass =
    "inline-flex min-h-8 items-center gap-1 rounded-md px-2 text-[11px] text-muted-foreground transition-colors hover:bg-white/5 hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40 disabled:cursor-not-allowed disabled:opacity-50";

  return (
    <div role="group" aria-label="کارهای پاسخ" className="-ms-2 flex flex-wrap items-center gap-1">
      {plainText && (
        <button type="button" onClick={copy} className={buttonClass}>
          {copyState === "copied" ? (
            <Check className="size-3.5 text-emerald" aria-hidden="true" />
          ) : (
            <Copy className="size-3.5" aria-hidden="true" />
          )}
          <span aria-live="polite">
            {copyState === "copied" ? "کپی شد" : copyState === "failed" ? "کپی نشد" : "کپی"}
          </span>
        </button>
      )}
      {plainText && canShare && (
        <button type="button" onClick={share} className={buttonClass}>
          <Share2 className="size-3.5" aria-hidden="true" />
          اشتراک‌گذاری
        </button>
      )}
      {onRegenerate && (
        <button
          type="button"
          onClick={() => !disabled && onRegenerate()}
          disabled={disabled}
          className={buttonClass}
          title="همین پرسش دوباره پرسیده می‌شود و اعتبار مصرف می‌کند"
        >
          <RefreshCw className="size-3.5" aria-hidden="true" />
          پاسخ دوباره
        </button>
      )}
    </div>
  );
}
