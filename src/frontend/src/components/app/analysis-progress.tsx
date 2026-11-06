import { useEffect, useState, type ReactNode } from "react";
import { Check, Loader2 } from "lucide-react";
import { SapioMark } from "@/components/app/sapio-mark";
import { ANALYSIS_STAGES } from "@/components/app/analysis-stages";
import { cn } from "@/lib/utils";

/** «در حال تحلیل» as visible stages, with the Sapio mark as the speaker. */
export function AnalysisProgress({ className, children }: { className?: string; children?: ReactNode }) {
  const [stage, setStage] = useState(0);

  useEffect(() => {
    const timers = ANALYSIS_STAGES.slice(1).map((item, index) =>
      window.setTimeout(() => setStage(index + 1), item.startsAtMs),
    );
    return () => timers.forEach((timer) => window.clearTimeout(timer));
  }, []);

  return (
    <div className={cn("flex items-start gap-3", className)} data-testid="analysis-progress">
      <AssistantAvatar busy />
      <div className="min-w-0 space-y-1 text-sm leading-7 text-muted-foreground">
        <p className="sr-only" aria-live="polite">
          در حال تحلیل: {ANALYSIS_STAGES[stage].label}
        </p>
        <ol className="flex flex-wrap items-center gap-x-2 gap-y-1" aria-hidden="true">
          {ANALYSIS_STAGES.map((item, index) => (
            <li
              key={item.label}
              data-state={index < stage ? "done" : index === stage ? "current" : "pending"}
              className={cn(
                "inline-flex items-center gap-1.5",
                index < stage && "text-emerald",
                index === stage && "font-medium text-foreground",
                index > stage && "opacity-50",
              )}
            >
              {index < stage ? (
                <Check className="size-3.5" />
              ) : index === stage ? (
                <Loader2 className="size-3.5 animate-spin text-emerald" />
              ) : null}
              {item.label}
              {index < ANALYSIS_STAGES.length - 1 && <span className="ms-1 text-muted-foreground">←</span>}
            </li>
          ))}
        </ol>
        {children}
      </div>
    </div>
  );
}

/** The assistant's speaker mark: Sapio's logo instead of a generic «AI» badge. */
export function AssistantAvatar({ busy = false }: { busy?: boolean }) {
  return (
    <span
      className={cn(
        "flex size-7 shrink-0 items-center justify-center overflow-hidden rounded-lg bg-emerald-soft ring-1 ring-emerald/30",
        busy && "animate-pulse",
      )}
      aria-hidden="true"
    >
      <SapioMark className="size-5" />
    </span>
  );
}
