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
import {
  StarterCardDetail,
  StarterExampleMark,
  StarterPromptPhrase,
} from "@/components/app/starter-card";
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
  sales: { tile: "emerald", gradient: "from-emerald/10", topic: "emerald-ink-text" },
  statement: { tile: "gold", gradient: "from-gold/10", topic: "mobile-gold-text" },
  valuation: { tile: "valuation", gradient: "from-valuation/10", topic: "text-valuation" },
};

/**
 * The phone starter cards, in list order. Tapping a direct card sends its question; a symbol card opens the symbol
 * picker, which the page hosts.
 */
export function MobileStarterList({
  prompts,
  disabled,
  onSelect,
}: {
  prompts: readonly StarterPrompt[];
  disabled: boolean;
  onSelect: (prompt: StarterPrompt) => void;
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
            <button
              type="button"
              onClick={() => onSelect(prompt)}
              disabled={disabled}
              aria-haspopup={prompt.kind === "symbol" ? "dialog" : undefined}
              className={cn(
                "flex min-h-[72px] w-full items-center gap-3 rounded-2xl bg-surface bg-linear-to-l to-transparent to-65% px-3.5 py-2.5 text-right ring-1 ring-hairline transition-colors active:bg-surface-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40 disabled:opacity-50",
                tone.gradient,
              )}
            >
              <IconTile icon={STARTER_ICONS[prompt.id]} tone={tone.tile} className="size-11" />
              <span className="min-w-0 flex-1">
                <span className="flex items-center gap-1.5">
                  <span className={cn("text-m-cap font-bold", tone.topic)}>{prompt.topic}</span>
                  {prompt.kind === "direct" && prompt.example && (
                    <StarterExampleMark className="text-m-cap leading-5" />
                  )}
                </span>
                <span className="block text-balance text-m-sub font-medium leading-7 text-foreground">
                  <StarterPromptPhrase prompt={prompt} />
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
