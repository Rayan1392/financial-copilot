import { IconTile, type IconTileTone } from "@/components/mobile/icon-tile";
import {
  BalanceSheetIcon,
  CashFlowIcon,
  IndustryComparisonIcon,
  MonthlySalesIcon,
  ProductTrendIcon,
  QuarterlyReportIcon,
  RankingIcon,
  SalesMixIcon,
  ValuationGaugesIcon,
  type MobileIcon,
} from "@/components/mobile/icons";
import { StarterCardDetail } from "@/components/app/starter-card";
import type { StarterPrompt, StarterPromptId, StarterPromptTone } from "@/lib/chat/starter-prompts";
import { cn } from "@/lib/utils";

/** Phone icon of each starter capability; the shared prompt list carries the desktop icons. */
const STARTER_ICONS: Record<StarterPromptId, MobileIcon> = {
  "valuation-gauges": ValuationGaugesIcon,
  "sales-mix": SalesMixIcon,
  "monthly-sales": MonthlySalesIcon,
  "product-trend": ProductTrendIcon,
  ranking: RankingIcon,
  "industry-comparison": IndustryComparisonIcon,
  "quarterly-report": QuarterlyReportIcon,
  "balance-sheet": BalanceSheetIcon,
  "cash-flow": CashFlowIcon,
};

/** Literal class strings per tone so Tailwind keeps them. */
const ROW_TONES: Record<
  StarterPromptTone,
  { tile: IconTileTone; gradient: string; topic: string }
> = {
  sales: { tile: "emerald", gradient: "from-emerald/10", topic: "text-emerald" },
  statement: { tile: "gold", gradient: "from-gold/10", topic: "mobile-gold-text" },
  valuation: { tile: "valuation", gradient: "from-valuation/10", topic: "text-valuation" },
};

/** The phone starter cards, in list order; tapping one sends its question. */
export function MobileStarterList({
  prompts,
  disabled,
  onSelect,
}: {
  prompts: readonly StarterPrompt[];
  disabled: boolean;
  onSelect: (text: string) => void;
}) {
  const ordered = [
    ...prompts.filter((prompt) => prompt.featured),
    ...prompts.filter((prompt) => !prompt.featured),
  ];
  return (
    <ul data-starters className="flex flex-col gap-2.5">
      {ordered.map((prompt) => {
        const tone = ROW_TONES[prompt.tone];
        return (
          <li key={prompt.id}>
            {/* The question is shown in full: the cards teach how to phrase a question. */}
            <button
              type="button"
              onClick={() => onSelect(prompt.text)}
              disabled={disabled}
              className={cn(
                "flex min-h-[72px] w-full items-center gap-3 rounded-2xl bg-surface bg-linear-to-l to-transparent to-65% px-3.5 py-2.5 text-right ring-1 ring-hairline transition-colors active:bg-surface-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40 disabled:opacity-50",
                tone.gradient,
              )}
            >
              <IconTile icon={STARTER_ICONS[prompt.id]} tone={tone.tile} className="size-11" />
              <span className="min-w-0 flex-1">
                <span className={cn("block text-m-cap font-bold", tone.topic)}>{prompt.topic}</span>
                <span className="block text-balance text-m-sub font-medium leading-7 text-foreground">
                  {prompt.text}
                </span>
                {prompt.detail && <StarterCardDetail detail={prompt.detail} className="block" />}
              </span>
            </button>
          </li>
        );
      })}
    </ul>
  );
}
