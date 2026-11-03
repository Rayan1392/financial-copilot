import { useEffect, useId, useRef } from "react";
import { ArrowUp } from "lucide-react";
import { useMediaQuery } from "@/hooks/use-media-query";
import { cn } from "@/lib/utils";

interface MobileComposerProps {
  value: string;
  onChange: (value: string) => void;
  /** Called with the trimmed text. The parent clears or keeps the value; a failed send restores it. */
  onSubmit: (text: string) => void;
  pending: boolean;
  online: boolean;
}

const MAX_ROWS = 6;
const LINE_HEIGHT_PX = 26;

/**
 * Docked mobile composer (Feature 153 T08). Controlled, so a failed send can put the text back.
 * On touch screens Enter inserts a newline and only the button sends; hardware keyboards send on Enter.
 */
export function MobileComposer({
  value,
  onChange,
  onSubmit,
  pending,
  online,
}: MobileComposerProps) {
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const hintId = useId();
  const coarsePointer = useMediaQuery("(pointer: coarse)");
  const text = value.trim();
  const disabled = !text || pending || !online;

  useEffect(() => {
    const textarea = textareaRef.current;
    if (!textarea) return;
    textarea.style.height = "auto";
    textarea.style.height = `${Math.min(textarea.scrollHeight, LINE_HEIGHT_PX * MAX_ROWS + 20)}px`;
  }, [value]);

  function submit() {
    if (disabled) return;
    onSubmit(text);
  }

  return (
    <div className="shrink-0 border-t border-hairline bg-background px-3 pt-2 pb-[max(8px,env(safe-area-inset-bottom))]">
      <form
        className="mx-auto flex max-w-2xl items-end gap-2"
        onSubmit={(event) => {
          event.preventDefault();
          submit();
        }}
      >
        <label htmlFor={`${hintId}-input`} className="sr-only">
          پرسش خود را بنویسید
        </label>
        <textarea
          id={`${hintId}-input`}
          ref={textareaRef}
          rows={1}
          value={value}
          dir="auto"
          onChange={(event) => onChange(event.target.value)}
          onKeyDown={(event) => {
            if (event.key !== "Enter" || event.shiftKey || coarsePointer) return;
            event.preventDefault();
            submit();
          }}
          enterKeyHint={coarsePointer ? "enter" : "send"}
          placeholder="پرسش خود را بنویسید…"
          className="min-h-11 flex-1 resize-none rounded-2xl bg-surface px-4 py-2.5 text-base leading-[26px] text-foreground ring-1 ring-hairline placeholder:text-muted-foreground/70 focus:outline-none focus:ring-2 focus:ring-emerald/40"
        />
        <button
          type="submit"
          disabled={disabled}
          aria-label="ارسال"
          aria-describedby={!online ? `${hintId}-offline` : undefined}
          className={cn(
            "flex size-11 shrink-0 items-center justify-center rounded-full bg-emerald text-primary-foreground transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40 focus-visible:ring-offset-2 focus-visible:ring-offset-background",
            "disabled:cursor-not-allowed disabled:opacity-40",
          )}
        >
          <ArrowUp className="size-5" aria-hidden="true" />
        </button>
      </form>
      {!online ? (
        <p id={`${hintId}-offline`} className="mt-1.5 text-center text-xs text-gold">
          برای ارسال پرسش به اینترنت نیاز است
        </p>
      ) : (
        <p className="mt-1.5 text-center text-xs text-muted-foreground">
          صرفاً جنبه آموزشی؛ توصیه سرمایه‌گذاری نیست
        </p>
      )}
    </div>
  );
}
