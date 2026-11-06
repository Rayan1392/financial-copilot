import {
  BarChart3,
  FileText,
  ListOrdered,
  PieChart,
  Scale,
  TrendingUp,
  type LucideIcon,
} from "lucide-react";
import { isRightsIssueSymbol } from "@/lib/market/instrument-kind";

/** Visual family of a starter card; each tone maps to theme-aware design tokens. */
export type StarterPromptTone = "sales" | "statement";

export interface StarterPrompt {
  /** The exact question sent to the assistant. */
  text: string;
  icon: LucideIcon;
  /** Short topic label shown above the question on the desktop cards. */
  topic: string;
  tone: StarterPromptTone;
  /**
   * The same question for another symbol, so the card can use one from the user's watchlist. Absent when the
   * example cannot be generalized: the product card names a company-specific product, the ranking card is
   * market-wide.
   */
  forSymbol?: (symbol: string) => string;
}

/**
 * The one list of new-chat starter prompts, shared by desktop `/chat` and mobile `/m`: one card per product
 * capability. Personalization only swaps the example symbol; it never adds, drops or repeats a card type.
 */
export const STARTER_PROMPTS: readonly StarterPrompt[] = [
  {
    text: "ترکیب فروش محصولات کگهر را بررسی کن",
    icon: PieChart,
    topic: "ترکیب فروش",
    tone: "sales",
    forSymbol: (s) => `ترکیب فروش محصولات ${s} را بررسی کن`,
  },
  {
    text: "روند فروش ماهانه کگهر را نشان بده",
    icon: TrendingUp,
    topic: "فروش ماهانه",
    tone: "sales",
    forSymbol: (s) => `روند فروش ماهانه ${s} را نشان بده`,
  },
  {
    text: "روند فروش گندله کگهر در ۱۲ ماهه اخیر چطور بوده؟",
    icon: BarChart3,
    topic: "عملکرد محصول",
    tone: "sales",
  },
  {
    text: "رتبه‌بندی گزارش‌های ماهانه را نشان بده",
    icon: ListOrdered,
    topic: "رتبه‌بندی",
    tone: "sales",
  },
  {
    text: "عملکرد مالی فصلی فولاد را بررسی کن",
    icon: FileText,
    topic: "سود و زیان فصلی",
    tone: "statement",
    forSymbol: (s) => `عملکرد مالی فصلی ${s} را بررسی کن`,
  },
  {
    text: "صورت وضعیت مالی فاذر را بررسی کن",
    icon: Scale,
    topic: "ترازنامه",
    tone: "statement",
    forSymbol: (s) => `صورت وضعیت مالی ${s} را بررسی کن`,
  },
];

export interface StarterPromptSet {
  prompts: readonly StarterPrompt[];
  /** True when at least one card uses a symbol from the user's watchlist. */
  personalized: boolean;
}

/**
 * The same six capability cards, with examples taken from the user's watchlist: symbols, newest followed
 * first, are dealt across the cards that can take one, so different cards show different symbols. Rights
 * issues (شگلح) are skipped because they have no statements or monthly sales of their own. With no usable
 * symbol the static cards are returned as they are.
 */
export function personalizedStarterPrompts(
  followed: readonly { symbol: string; followedAtUtc?: string }[],
): StarterPromptSet {
  const symbols = [...followed]
    .filter((item) => item.symbol.trim().length > 0 && !isRightsIssueSymbol(item.symbol))
    .sort((a, b) => (b.followedAtUtc ?? "").localeCompare(a.followedAtUtc ?? ""))
    .map((item) => item.symbol.trim())
    .filter((symbol, index, all) => all.indexOf(symbol) === index);
  if (symbols.length === 0) return { prompts: STARTER_PROMPTS, personalized: false };

  let next = 0;
  const prompts = STARTER_PROMPTS.map((prompt) =>
    prompt.forSymbol ? { ...prompt, text: prompt.forSymbol(symbols[next++ % symbols.length]) } : prompt,
  );
  return { prompts, personalized: true };
}
