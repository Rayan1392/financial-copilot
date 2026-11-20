import { useEffect, useId, useRef, useState } from "react";
import { Globe } from "lucide-react";
import type { GroundedAnalysisHint } from "@/lib/chat.functions";
import { canConfirm, costSentence, type AnalysisChoice } from "@/lib/chat/analysis-choice";
import { cn } from "@/lib/utils";

/**
 * Change «sol-deep-analysis-web-search»: the depth and web-search controls, the live cost sentence and the 163 offer
 * card. Every amount comes from the backend hint; nothing here computes or sends a price.
 */
interface ControlsProps {
  choice: AnalysisChoice;
  onChange: (choice: AnalysisChoice) => void;
  disabled?: boolean;
  /** Phone layout: 44 px touch targets. */
  compact?: boolean;
}

const DEPTHS = [
  { value: "simple", label: "ساده" },
  { value: "deep", label: "عمیق" },
] as const;

export function AnalysisControls({ choice, onChange, disabled, compact }: ControlsProps) {
  const target = compact ? "min-h-11 px-4 text-m-cap" : "h-8 px-3 text-xs";
  return (
    <div className="flex flex-wrap items-center gap-2">
      <div
        role="radiogroup"
        aria-label="عمق تحلیل"
        className="inline-flex rounded-full bg-surface-2 p-0.5 ring-1 ring-hairline"
      >
        {DEPTHS.map((depth) => {
          const selected = choice.depth === depth.value;
          return (
            <button
              key={depth.value}
              type="button"
              role="radio"
              aria-checked={selected}
              disabled={disabled}
              onClick={() => onChange({ ...choice, depth: depth.value })}
              className={cn(
                "rounded-full font-medium transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40 disabled:cursor-not-allowed disabled:opacity-50",
                target,
                selected
                  ? "bg-emerald text-primary-foreground"
                  : "text-muted-foreground hover:text-foreground",
              )}
            >
              {depth.label}
            </button>
          );
        })}
      </div>
      <button
        type="button"
        aria-pressed={choice.webSearch}
        disabled={disabled}
        onClick={() => onChange({ ...choice, webSearch: !choice.webSearch })}
        className={cn(
          "inline-flex items-center gap-1.5 rounded-full ring-1 transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40 disabled:cursor-not-allowed disabled:opacity-50",
          target,
          choice.webSearch
            ? "bg-emerald-soft text-emerald ring-emerald/30"
            : "text-muted-foreground ring-hairline hover:text-foreground",
        )}
      >
        <Globe className="size-3.5" aria-hidden="true" />
        جست‌وجوی وب
      </button>
    </div>
  );
}

/** Feature 162: shown over the composer while the latest answer is a discussable report or a 162 answer. */
export function AnalysisSettingsBar({
  hint,
  choice,
  onChange,
  disabled,
  compact,
}: ControlsProps & { hint: GroundedAnalysisHint }) {
  const sentence = costSentence(hint, choice, "discussion");
  return (
    <section
      aria-label="تنظیمات پرسش تحلیلی"
      dir="rtl"
      className={cn(
        "flex flex-wrap items-center gap-x-3 gap-y-1.5",
        compact ? "pb-2" : "px-3 pt-3",
      )}
    >
      <AnalysisControls choice={choice} onChange={onChange} disabled={disabled} compact={compact} />
      {sentence && (
        <p
          aria-live="polite"
          className={cn("min-w-0 text-muted-foreground", compact ? "text-m-cap" : "text-xs")}
        >
          {sentence}
        </p>
      )}
    </section>
  );
}

/**
 * Feature 163: the analysis runs only after «تحلیل کن». The card locks after a click until that request ends; a
 * failed confirmation (for example a short balance) leaves the offer usable, as the backend keeps it.
 */
export function AnalysisOfferCard({
  hint,
  choice,
  onChange,
  disabled = false,
  compact,
  onConfirm,
  onCancel,
  creditHref,
}: ControlsProps & {
  hint: GroundedAnalysisHint;
  onConfirm: () => void;
  onCancel: () => void;
  /** Where the viewer can see and buy credit. */
  creditHref: string;
}) {
  const titleId = useId();
  const [locked, setLocked] = useState(false);
  const wasDisabled = useRef(disabled);
  useEffect(() => {
    if (wasDisabled.current && !disabled) setLocked(false);
    wasDisabled.current = disabled;
  }, [disabled]);

  const offer = hint.offer;
  if (!offer) return null;
  const blocked = disabled || locked;
  const affordable = canConfirm(hint, choice);
  const sentence = costSentence(hint, choice, "offer");
  const button = compact ? "min-h-11 px-5 text-m-body" : "h-9 px-4 text-sm";

  function act(action: () => void) {
    if (blocked) return;
    setLocked(true);
    action();
  }

  return (
    <section
      aria-labelledby={titleId}
      dir="rtl"
      className="space-y-3 rounded-2xl bg-surface p-4 ring-1 ring-emerald/20"
    >
      <h3 id={titleId} className={cn("font-semibold", compact ? "text-m-body" : "text-sm")}>
        تحلیل {offer.symbol}
      </h3>
      <AnalysisControls choice={choice} onChange={onChange} disabled={blocked} compact={compact} />
      {sentence && (
        <p
          aria-live="polite"
          className={cn("text-muted-foreground", compact ? "text-m-cap" : "text-xs")}
        >
          {sentence}
        </p>
      )}
      {!affordable && (
        <p className={cn("mobile-gold-text", compact ? "text-m-cap" : "text-xs")}>
          موجودی برای این تحلیل کافی نیست.{" "}
          <a
            href={creditHref}
            className="font-medium text-foreground underline underline-offset-4 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40"
          >
            خرید اعتبار
          </a>
        </p>
      )}
      <div className="flex flex-wrap gap-2">
        <button
          type="button"
          disabled={blocked || !affordable}
          onClick={() => act(onConfirm)}
          className={cn(
            "rounded-xl bg-emerald font-semibold text-primary-foreground transition hover:brightness-110 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40 disabled:cursor-not-allowed disabled:opacity-40",
            button,
          )}
        >
          تحلیل کن
        </button>
        <button
          type="button"
          disabled={blocked}
          onClick={() => act(onCancel)}
          className={cn(
            "rounded-xl text-muted-foreground ring-1 ring-hairline transition hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40 disabled:cursor-not-allowed disabled:opacity-40",
            button,
          )}
        >
          انصراف
        </button>
      </div>
    </section>
  );
}
