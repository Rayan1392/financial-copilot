import {
  ArrowLeftRight,
  Banknote,
  BarChart3,
  FileText,
  Gauge,
  ListOrdered,
  PieChart,
  Scale,
  TrendingUp,
  type LucideIcon,
} from "lucide-react";
import { isRightsIssueSymbol } from "@/lib/market/instrument-kind";

/** Visual family of a starter card; each tone maps to theme-aware design tokens. */
export type StarterPromptTone = "sales" | "statement" | "valuation";

export type StarterPromptId =
  | "valuation-gauges"
  | "sales-mix"
  | "monthly-sales"
  | "product-trend"
  | "ranking"
  | "industry-comparison"
  | "quarterly-report"
  | "balance-sheet"
  | "cash-flow";

/** An optional capability the API can switch off; a card that needs one is shown only while it is available. */
export type StarterPromptCapability = "valuationGaugeCards";

/** What the API reports as available (`useChatCapabilityAvailability`); a missing entry means unavailable. */
export type StarterPromptAvailability = Partial<Record<StarterPromptCapability, boolean>>;

export interface StarterPrompt {
  /** Stable key of the capability; the phone workspace picks its own icon by it. */
  id: StarterPromptId;
  /** The exact question sent to the assistant. */
  text: string;
  icon: LucideIcon;
  /** Short topic label shown above the question on the desktop cards. */
  topic: string;
  tone: StarterPromptTone;
  /** A short line under the question naming what the answer contains. */
  detail?: string;
  /** Shown as the wide featured card on desktop, first in the list. */
  featured?: boolean;
  /** The optional capability the question needs; the card is left out while it is unavailable. */
  requires?: StarterPromptCapability;
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
    // Change «valuation-gauge-starter-card» (owner, 2026-11-26): the example symbol is the owner's choice. The text is
    // the routed phrase «گیج‌های ارزش‌گذاری ‹نماد›», with the zero-width non-joiners.
    id: "valuation-gauges",
    text: "گیج‌های ارزش‌گذاری گدنا",
    icon: Gauge,
    topic: "ارزش‌گذاری",
    detail: "P/E · P/S · قیمت تعادلی",
    tone: "valuation",
    featured: true,
    requires: "valuationGaugeCards",
    forSymbol: (s) => `گیج‌های ارزش‌گذاری ${s}`,
  },
  {
    id: "sales-mix",
    text: "ترکیب فروش محصولات کگهر را بررسی کن",
    icon: PieChart,
    topic: "ترکیب فروش",
    tone: "sales",
    // Kept static by owner decision: the sales-mix answer is not yet reliable for every company
    // (e.g. گدنا), so the example stays on a symbol known to work.
  },
  {
    id: "monthly-sales",
    text: "روند فروش ماهانه کگهر را نشان بده",
    icon: TrendingUp,
    topic: "فروش ماهانه",
    tone: "sales",
    forSymbol: (s) => `روند فروش ماهانه ${s} را نشان بده`,
  },
  {
    id: "product-trend",
    text: "روند فروش گندله کگهر در ۱۲ ماهه اخیر چطور بوده؟",
    icon: BarChart3,
    topic: "عملکرد محصول",
    tone: "sales",
  },
  {
    id: "ranking",
    text: "رتبه‌بندی گزارش‌های ماهانه را نشان بده",
    icon: ListOrdered,
    topic: "رتبه‌بندی",
    tone: "sales",
  },
  {
    id: "industry-comparison",
    text: "شاراک را با صنعت خودش مقایسه کن",
    icon: ArrowLeftRight,
    topic: "مقایسه با صنعت",
    tone: "sales",
    forSymbol: (s) => `${s} را با صنعت خودش مقایسه کن`,
  },
  {
    id: "quarterly-report",
    text: "عملکرد مالی فصلی فولاد را بررسی کن",
    icon: FileText,
    topic: "سود و زیان فصلی",
    tone: "statement",
    forSymbol: (s) => `عملکرد مالی فصلی ${s} را بررسی کن`,
  },
  {
    id: "balance-sheet",
    text: "صورت وضعیت مالی فاذر را بررسی کن",
    icon: Scale,
    topic: "ترازنامه",
    tone: "statement",
    forSymbol: (s) => `صورت وضعیت مالی ${s} را بررسی کن`,
  },
  {
    id: "cash-flow",
    text: "جریان وجه نقد اخابر",
    icon: Banknote,
    topic: "جریان وجوه نقد",
    tone: "statement",
    forSymbol: (s) => `جریان وجه نقد ${s}`,
  },
];

export interface StarterPromptSet {
  prompts: readonly StarterPrompt[];
  /** True when at least one card uses a symbol from the user's watchlist. */
  personalized: boolean;
}

/**
 * Off by owner decision (2026-11-06): a watchlist symbol is not known to have the data a card asks about —
 * e.g. a non-producing company such as ثبهساز has no monthly sales trend — so personalized cards could
 * promise answers the assistant cannot give. Turn back on only once a card is offered just for symbols
 * that support its capability.
 */
export const WATCHLIST_STARTER_PROMPTS_ENABLED = false;

/** The starter cards whose capability is available; by default, only the cards that need no optional capability. */
export function availableStarterPrompts(
  availability: StarterPromptAvailability = {},
): readonly StarterPrompt[] {
  return STARTER_PROMPTS.filter(
    (prompt) => prompt.requires === undefined || availability[prompt.requires] === true,
  );
}

/**
 * The cards the new-chat pages show: the available cards, fixed while watchlist personalization is off. While the
 * availability answer is loading or has failed, the caller passes nothing, so no optional card is shown.
 */
export function homeStarterPrompts(
  followed: readonly { symbol: string; followedAtUtc?: string }[],
  availability: StarterPromptAvailability = {},
): StarterPromptSet {
  const prompts = availableStarterPrompts(availability);
  return WATCHLIST_STARTER_PROMPTS_ENABLED
    ? personalizedStarterPrompts(followed, prompts)
    : { prompts, personalized: false };
}

/**
 * The same capability cards, with examples taken from the user's watchlist: symbols, newest followed
 * first, are dealt across the cards that can take one, so different cards show different symbols. Rights
 * issues (شگلح) are skipped because they have no statements or monthly sales of their own. With no usable
 * symbol the static cards are returned as they are.
 */
export function personalizedStarterPrompts(
  followed: readonly { symbol: string; followedAtUtc?: string }[],
  available: readonly StarterPrompt[] = availableStarterPrompts(),
): StarterPromptSet {
  const symbols = [...followed]
    .filter((item) => item.symbol.trim().length > 0 && !isRightsIssueSymbol(item.symbol))
    .sort((a, b) => (b.followedAtUtc ?? "").localeCompare(a.followedAtUtc ?? ""))
    .map((item) => item.symbol.trim())
    .filter((symbol, index, all) => all.indexOf(symbol) === index);
  if (symbols.length === 0) return { prompts: available, personalized: false };

  let next = 0;
  const prompts = available.map((prompt) =>
    prompt.forSymbol
      ? { ...prompt, text: prompt.forSymbol(symbols[next++ % symbols.length]) }
      : prompt,
  );
  return { prompts, personalized: true };
}

/** The coverage badge and capability sentence above the cards; valuation is named only while its card is shown (OQ-142). */
export function starterCoverage(prompts: readonly StarterPrompt[]): {
  badge: string;
  sentence: string;
} {
  const valuation = prompts.some((prompt) => prompt.requires === "valuationGaugeCards");
  return valuation
    ? {
        badge: "پوشش فعلی: تولید، فروش، گزارش‌های مالی و ارزش‌گذاری",
        sentence:
          "روند فروش ماهانه، ترکیب فروش محصولات، عملکرد یک محصول، رتبه‌بندی گزارش‌های ماهانه، مقایسهٔ نماد با صنعت و سود و زیان فصلی، ترازنامه (صورت وضعیت مالی)، جریان وجوه نقد و گیج‌های ارزش‌گذاری نمادها را بررسی کنید.",
      }
    : {
        badge: "پوشش فعلی: تولید، فروش و گزارش‌های مالی",
        sentence:
          "روند فروش ماهانه، ترکیب فروش محصولات، عملکرد یک محصول، رتبه‌بندی گزارش‌های ماهانه، مقایسهٔ نماد با صنعت و سود و زیان فصلی، ترازنامه (صورت وضعیت مالی) و جریان وجوه نقد نمادها را بررسی کنید.",
      };
}
