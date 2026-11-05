import { ArrowLeft } from "lucide-react";
import type { StarterPrompt, StarterPromptTone } from "@/lib/chat/starter-prompts";

/** Theme-aware classes per card tone; literal strings so Tailwind keeps them. */
const STARTER_TONE_CLASSES: Record<
  StarterPromptTone,
  { accent: string; tile: string; topic: string; card: string; arrow: string }
> = {
  sales: {
    accent: "from-emerald to-emerald/20",
    tile: "bg-emerald-soft text-emerald ring-emerald/25",
    // Darker ink in the light theme keeps the small label at >= 4.5:1 contrast.
    topic: "text-emerald [.light_&]:text-[oklch(0.45_0.12_160)]",
    card: "hover:border-emerald/45 focus-visible:ring-emerald/45",
    arrow: "group-hover:text-emerald",
  },
  statement: {
    accent: "from-gold to-gold/20",
    tile: "bg-gold/15 text-gold ring-gold/30",
    topic: "text-gold [.light_&]:text-[oklch(0.47_0.11_70)]",
    card: "hover:border-gold/50 focus-visible:ring-gold/45",
    arrow: "group-hover:text-gold",
  },
};

/** One new-chat suggestion card on the desktop landing page; clicking sends `prompt.text`. */
export function StarterCard({
  prompt,
  disabled,
  onSelect,
}: {
  prompt: StarterPrompt;
  disabled: boolean;
  onSelect: (text: string) => void;
}) {
  const tone = STARTER_TONE_CLASSES[prompt.tone];
  const Icon = prompt.icon;
  return (
    <button
      type="button"
      onClick={() => onSelect(prompt.text)}
      disabled={disabled}
      className={`group relative flex min-h-[5.25rem] items-center gap-3.5 overflow-hidden rounded-2xl border border-border bg-gradient-to-l from-surface to-surface-2/60 py-3.5 pe-4 ps-5 text-right shadow-sm transition duration-200 hover:-translate-y-0.5 hover:shadow-md focus-visible:outline-none focus-visible:ring-2 disabled:pointer-events-none disabled:opacity-50 motion-reduce:transition-none motion-reduce:hover:translate-y-0 ${tone.card}`}
    >
      <span
        aria-hidden="true"
        className={`pointer-events-none absolute inset-y-3 start-0 w-1 rounded-e-full bg-gradient-to-b opacity-70 transition-opacity group-hover:opacity-100 ${tone.accent}`}
      />
      <span
        className={`flex size-11 shrink-0 items-center justify-center rounded-xl ring-1 transition-transform duration-200 group-hover:scale-105 motion-reduce:group-hover:scale-100 ${tone.tile}`}
      >
        <Icon className="size-5" aria-hidden="true" />
      </span>
      <span className="flex min-w-0 flex-1 flex-col gap-0.5">
        <span className={`text-[11px] font-semibold tracking-wide ${tone.topic}`}>
          {prompt.topic}
        </span>
        <span className="text-sm font-medium leading-6 text-foreground text-pretty">
          {prompt.text}
        </span>
      </span>
      <ArrowLeft
        className={`size-4 shrink-0 text-muted-foreground/50 transition duration-200 group-hover:-translate-x-1 motion-reduce:group-hover:translate-x-0 ${tone.arrow}`}
        aria-hidden="true"
      />
    </button>
  );
}
