import {
  BarChart3,
  FileText,
  ListOrdered,
  PieChart,
  Scale,
  TrendingUp,
  type LucideIcon,
} from "lucide-react";

/** Visual family of a starter card; each tone maps to theme-aware design tokens. */
export type StarterPromptTone = "sales" | "statement";

export interface StarterPrompt {
  /** The exact question sent to the assistant. */
  text: string;
  icon: LucideIcon;
  /** Short topic label shown above the question on the desktop cards. */
  topic: string;
  tone: StarterPromptTone;
}

/** The one list of new-chat starter prompts, shared by desktop `/chat` and mobile `/m`. */
export const STARTER_PROMPTS: readonly StarterPrompt[] = [
  {
    text: "ترکیب فروش محصولات کگهر را بررسی کن",
    icon: PieChart,
    topic: "ترکیب فروش",
    tone: "sales",
  },
  {
    text: "روند فروش ماهانه کگهر را نشان بده",
    icon: TrendingUp,
    topic: "فروش ماهانه",
    tone: "sales",
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
  },
  { text: "صورت وضعیت مالی فاذر را بررسی کن", icon: Scale, topic: "ترازنامه", tone: "statement" },
];

/** Card templates for a followed symbol, in the order they are dealt out across the watchlist. */
const SYMBOL_TEMPLATES: readonly ((symbol: string) => StarterPrompt)[] = [
  (s) => ({
    text: `ترکیب فروش محصولات ${s} را بررسی کن`,
    icon: PieChart,
    topic: "ترکیب فروش",
    tone: "sales",
  }),
  (s) => ({
    text: `روند فروش ماهانه ${s} را نشان بده`,
    icon: TrendingUp,
    topic: "فروش ماهانه",
    tone: "sales",
  }),
  (s) => ({
    text: `عملکرد مالی فصلی ${s} را بررسی کن`,
    icon: FileText,
    topic: "سود و زیان فصلی",
    tone: "statement",
  }),
  (s) => ({
    text: `صورت وضعیت مالی ${s} را بررسی کن`,
    icon: Scale,
    topic: "ترازنامه",
    tone: "statement",
  }),
  (s) => ({
    text: `روند فروش ماهانه ${s} را نشان بده`,
    icon: BarChart3,
    topic: "فروش ماهانه",
    tone: "sales",
  }),
];

/** Market-wide card kept in every personalized set: it does not depend on a symbol. */
const RANKING_PROMPT = STARTER_PROMPTS.find((prompt) => prompt.topic === "رتبه‌بندی")!;

export interface StarterPromptSet {
  prompts: readonly StarterPrompt[];
  /** True when at least one card comes from the user's watchlist. */
  personalized: boolean;
}

/**
 * Starter cards built from the user's watchlist, newest followed first: each card type goes to a different
 * symbol so the set covers the watchlist, plus the market-wide ranking card. With no followed symbols the
 * static STARTER_PROMPTS are used as they are; when the watchlist yields fewer distinct cards than the static
 * set has, the remaining slots are filled from it.
 */
export function personalizedStarterPrompts(
  followed: readonly { symbol: string; followedAtUtc?: string }[],
): StarterPromptSet {
  const symbols = [...followed]
    .filter((item) => item.symbol.trim().length > 0)
    .sort((a, b) => (b.followedAtUtc ?? "").localeCompare(a.followedAtUtc ?? ""))
    .map((item) => item.symbol.trim())
    .filter((symbol, index, all) => all.indexOf(symbol) === index);
  if (symbols.length === 0) return { prompts: STARTER_PROMPTS, personalized: false };

  const size = STARTER_PROMPTS.length;
  const cards: StarterPrompt[] = [];
  const add = (prompt: StarterPrompt) => {
    if (cards.length < size && !cards.some((card) => card.text === prompt.text)) cards.push(prompt);
  };
  SYMBOL_TEMPLATES.forEach((template, index) => add(template(symbols[index % symbols.length])));
  // Symbol cards first, the market-wide ranking card closes the set, then static fill-ins if still short.
  cards.splice(Math.min(cards.length, size - 1));
  add(RANKING_PROMPT);
  STARTER_PROMPTS.forEach(add);
  return { prompts: cards, personalized: true };
}
