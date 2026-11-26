import { Fragment } from "react";
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
  // The `--valuation` token is set per theme for >= 4.5:1 small text, so it needs no light-theme override.
  valuation: {
    accent: "from-valuation to-valuation/20",
    tile: "bg-valuation/12 text-valuation ring-valuation/25",
    topic: "text-valuation",
    card: "hover:border-valuation/45 focus-visible:ring-valuation/45",
    arrow: "group-hover:text-valuation",
  },
};

const CARD_BASE =
  "group relative flex items-center overflow-hidden rounded-2xl border border-border py-3.5 pe-4 ps-5 text-right shadow-sm transition duration-200 hover:-translate-y-0.5 hover:shadow-md focus-visible:outline-none focus-visible:ring-2 disabled:pointer-events-none disabled:opacity-50 motion-reduce:transition-none motion-reduce:hover:translate-y-0";

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
  if (prompt.featured)
    return <FeaturedStarterCard prompt={prompt} disabled={disabled} onSelect={onSelect} />;
  return (
    <button
      type="button"
      onClick={() => onSelect(prompt.text)}
      disabled={disabled}
      className={`${CARD_BASE} min-h-[5.25rem] gap-3.5 bg-gradient-to-l from-surface to-surface-2/60 ${tone.card}`}
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
      <StarterCardText prompt={prompt} topicClass={tone.topic} />
      <ArrowLeft
        className={`size-4 shrink-0 text-muted-foreground/50 transition duration-200 group-hover:-translate-x-1 motion-reduce:group-hover:translate-x-0 ${tone.arrow}`}
        aria-hidden="true"
      />
    </button>
  );
}

function StarterCardText({ prompt, topicClass }: { prompt: StarterPrompt; topicClass: string }) {
  return (
    <span className="flex min-w-0 flex-1 flex-col gap-0.5">
      <span className={`text-[11px] font-semibold tracking-wide ${topicClass}`}>
        {prompt.topic}
      </span>
      <span className="text-sm font-medium leading-6 text-foreground text-pretty">
        {prompt.text}
      </span>
      {prompt.detail && <StarterCardDetail detail={prompt.detail} />}
    </span>
  );
}

/**
 * «P/E · P/S · قیمت تعادلی» with each item isolated, so the Latin ratio names keep the right-to-left list order
 * instead of joining into one left-to-right run.
 */
export function StarterCardDetail({
  detail,
  className = "",
}: {
  detail: string;
  className?: string;
}) {
  return (
    <span
      className={`text-xs leading-5 text-muted-foreground ${className}`}
      data-testid="starter-card-detail"
    >
      {detail.split(" · ").map((part, index) => (
        <Fragment key={part}>
          {index > 0 && " · "}
          <bdi>{part}</bdi>
        </Fragment>
      ))}
    </span>
  );
}

/**
 * The wide card that opens the grid (change «valuation-gauge-starter-card»): it spans both desktop columns, so an odd
 * card count still leaves the other cards in full rows. The half-dial is decoration in the gauge cards' colours.
 */
function FeaturedStarterCard({
  prompt,
  disabled,
  onSelect,
}: {
  prompt: StarterPrompt;
  disabled: boolean;
  onSelect: (text: string) => void;
}) {
  const tone = STARTER_TONE_CLASSES[prompt.tone];
  return (
    <button
      type="button"
      onClick={() => onSelect(prompt.text)}
      disabled={disabled}
      data-featured=""
      className={`${CARD_BASE} min-h-[6.5rem] gap-4 bg-gradient-to-l from-surface via-surface to-valuation/8 md:col-span-2 ${tone.card}`}
    >
      <span
        aria-hidden="true"
        className={`pointer-events-none absolute inset-y-3 start-0 w-1 rounded-e-full bg-gradient-to-b opacity-70 transition-opacity group-hover:opacity-100 ${tone.accent}`}
      />
      <StarterCardText prompt={prompt} topicClass={tone.topic} />
      <HalfDial />
      <ArrowLeft
        className={`size-4 shrink-0 self-end text-muted-foreground/50 transition duration-200 group-hover:-translate-x-1 motion-reduce:group-hover:translate-x-0 ${tone.arrow}`}
        aria-hidden="true"
      />
    </button>
  );
}

const DIAL = { cx: 60, cy: 62, r: 46, width: 11 };

/** Arc from `from` to `to` degrees (180 = left, 0 = right) on the dial circle. */
function arc(from: number, to: number) {
  const point = (deg: number) => {
    const rad = (deg * Math.PI) / 180;
    return `${(DIAL.cx + DIAL.r * Math.cos(rad)).toFixed(2)} ${(DIAL.cy - DIAL.r * Math.sin(rad)).toFixed(2)}`;
  };
  return `M ${point(from)} A ${DIAL.r} ${DIAL.r} 0 0 1 ${point(to)}`;
}

/** Cheap (green), fair (neutral) and expensive (red) bands, drawn left to right like the real gauges. */
function HalfDial() {
  return (
    <svg
      aria-hidden="true"
      viewBox="0 0 120 70"
      className="h-14 w-24 shrink-0 text-foreground"
      data-testid="starter-card-dial"
    >
      <path
        d={arc(178, 122)}
        fill="none"
        stroke="var(--gauge-band-1)"
        strokeWidth={DIAL.width}
        strokeLinecap="round"
      />
      <path
        d={arc(116, 64)}
        fill="none"
        stroke="var(--gauge-band-3)"
        strokeWidth={DIAL.width}
        strokeLinecap="round"
      />
      <path
        d={arc(58, 2)}
        fill="none"
        stroke="var(--gauge-band-6)"
        strokeWidth={DIAL.width}
        strokeLinecap="round"
      />
      {/* The pointer leans into the cheap side and eases toward the middle on hover or focus. */}
      <g
        className="transition-transform duration-500 ease-out group-hover:rotate-[16deg] group-focus-visible:rotate-[16deg] motion-reduce:transition-none motion-reduce:group-hover:rotate-0 motion-reduce:group-focus-visible:rotate-0"
        style={{ transformOrigin: `${DIAL.cx}px ${DIAL.cy}px` }}
        data-testid="starter-card-dial-pointer"
      >
        <line
          x1={DIAL.cx}
          y1={DIAL.cy}
          x2={DIAL.cx + 34 * Math.cos((132 * Math.PI) / 180)}
          y2={DIAL.cy - 34 * Math.sin((132 * Math.PI) / 180)}
          stroke="currentColor"
          strokeWidth={3}
          strokeLinecap="round"
        />
        <circle cx={DIAL.cx} cy={DIAL.cy} r={5} fill="currentColor" />
      </g>
    </svg>
  );
}
