import { useEffect, useState } from "react";
import { AnalysisProgress } from "@/components/app/analysis-progress";

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
 * The analysis stages in a polite live region. After 20 s it adds a reassurance; there is no client timeout
 * (decision D-Q4), so the request runs until the server answers or the connection fails.
 */
export function PendingAnswer() {
  const [long, setLong] = useState(false);
  useEffect(() => {
    const timer = window.setTimeout(() => setLong(true), LONG_REQUEST_MS);
    return () => window.clearTimeout(timer);
  }, []);
  return (
    <AnalysisProgress className="px-4">
      {long && <p aria-live="polite">تحلیل‌های مفصل ممکن است کمی بیشتر طول بکشد</p>}
    </AnalysisProgress>
  );
}
