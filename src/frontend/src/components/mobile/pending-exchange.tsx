import { useEffect, useState } from "react";
import { Loader2 } from "lucide-react";

const LONG_REQUEST_MS = 20_000;

/** The user's question as a right-aligned bubble, optionally marked when its outcome is unknown. */
export function MobileUserBubble({ text, uncertain }: { text: string; uncertain?: boolean }) {
  return (
    <div className="flex flex-col items-start px-4">
      <div
        dir="auto"
        className="max-w-[85%] whitespace-pre-wrap rounded-2xl bg-surface px-4 py-2.5 text-[15px] leading-7 text-foreground ring-1 ring-hairline"
      >
        {text}
      </div>
      {uncertain && <span className="mt-1 text-xs text-gold">وضعیت نامشخص</span>}
    </div>
  );
}

/**
 * «در حال تحلیل...» in a polite live region. After 20 s it adds a reassurance; there is no client timeout
 * (decision D-Q4), so the request runs until the server answers or the connection fails.
 */
export function PendingAnswer() {
  const [long, setLong] = useState(false);
  useEffect(() => {
    const timer = window.setTimeout(() => setLong(true), LONG_REQUEST_MS);
    return () => window.clearTimeout(timer);
  }, []);
  return (
    <div aria-live="polite" className="flex items-start gap-3 px-4">
      <span className="mt-0.5 flex size-7 shrink-0 items-center justify-center rounded-lg bg-emerald-soft ring-1 ring-emerald/30">
        <Loader2 className="size-3.5 animate-spin text-emerald" aria-hidden="true" />
      </span>
      <div className="text-sm leading-7 text-muted-foreground">
        <p>در حال تحلیل...</p>
        {long && <p>تحلیل‌های مفصل ممکن است کمی بیشتر طول بکشد</p>}
      </div>
    </div>
  );
}
