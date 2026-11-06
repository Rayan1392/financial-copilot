import { ArrowDown, ArrowUp, Minus } from "lucide-react";
import type { MonthlyActivityTrendResult } from "@/lib/chat.functions";
import {
  createMonthlyTrendSummaryCards,
  hasMonthlyTrendSummary,
  type MonthlyTrendSummaryCard,
} from "@/components/app/monthly-activity-trend-chart-view-model";
import { cn } from "@/lib/utils";

const toneClass: Record<MonthlyTrendSummaryCard["tone"], string> = {
  positive: "text-emerald-600 dark:text-emerald-400",
  negative: "text-rose-600 dark:text-rose-400",
  neutral: "text-foreground",
};

/** The headline figures of a monthly sales answer as a row of small cards above the answer text. */
export function MonthlyTrendSummaryCards({ data }: { data: MonthlyActivityTrendResult }) {
  if (!hasMonthlyTrendSummary(data)) return null;
  const cards = createMonthlyTrendSummaryCards(data);

  return (
    <ul
      aria-label="خلاصه فروش ماهانه"
      data-testid="monthly-trend-summary-cards"
      className="grid grid-cols-2 gap-2 sm:grid-cols-4"
      dir="rtl"
    >
      {cards.map((card) => (
        <li
          key={card.id}
          data-card={card.id}
          className="flex min-w-0 flex-col gap-1 rounded-xl bg-surface/60 p-3 ring-1 ring-hairline"
        >
          <span className="text-[11px] leading-5 text-muted-foreground">{card.label}</span>
          <span
            className={cn("flex items-center gap-1 text-lg font-semibold leading-7 tabular-nums", toneClass[card.tone])}
            aria-label={card.accessibleValue}
          >
            <DirectionIcon direction={card.direction} />
            <bdi>{card.value}</bdi>
            {card.unit && <span className="text-[11px] font-normal text-muted-foreground">{card.unit}</span>}
          </span>
          <span className="truncate text-[11px] leading-5 text-muted-foreground" title={card.caption}>
            {card.caption}
          </span>
        </li>
      ))}
    </ul>
  );
}

function DirectionIcon({ direction }: { direction: MonthlyTrendSummaryCard["direction"] }) {
  if (direction === "up") return <ArrowUp className="size-4 shrink-0" aria-hidden="true" />;
  if (direction === "down") return <ArrowDown className="size-4 shrink-0" aria-hidden="true" />;
  if (direction === "flat") return <Minus className="size-4 shrink-0" aria-hidden="true" />;
  return null;
}
