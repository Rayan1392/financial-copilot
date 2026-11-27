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

interface StarterPromptBase {
  /** Stable key of the capability; the phone workspace picks its own icon by it. */
  id: StarterPromptId;
  icon: LucideIcon;
  /** Short topic label shown above the question; also the title of the symbol picker. */
  topic: string;
  tone: StarterPromptTone;
  /** A short line under the question naming what the answer contains. */
  detail?: string;
  /** Shown as the wide featured card on desktop, first in the list. */
  featured?: boolean;
  /** The optional capability the question needs; the card is left out while it is unavailable. */
  requires?: StarterPromptCapability;
}

/** A card that sends its question in one tap. */
export interface DirectStarterPrompt extends StarterPromptBase {
  kind: "direct";
  /** The exact question shown on the card and sent to the assistant. */
  text: string;
  /** The question names one fixed symbol that cannot be swapped; the card is marked «نمونه» (OQ-151). */
  example?: boolean;
}

/** A card that first asks for the symbol (change «starter-cards-symbol-picker»). */
export interface SymbolStarterPrompt extends StarterPromptBase {
  kind: "symbol";
  /** The short phrase on the card, around the «نماد» slot. */
  label: { before: string; after: string };
  /** The symbol the picker offers as «نمونه». */
  exampleSymbol: string;
  /** The exact question sent for a symbol; the only owner of the question wording. */
  forSymbol: (symbol: string) => string;
}

export type StarterPrompt = DirectStarterPrompt | SymbolStarterPrompt;

/** The word shown in a symbol card's slot. */
export const STARTER_SYMBOL_SLOT = "نماد";

/** The card's phrase as plain text: the question of a direct card, the label with the slot word of a symbol card. */
export function starterPromptTitle(prompt: StarterPrompt): string {
  return prompt.kind === "direct"
    ? prompt.text
    : `${prompt.label.before}${STARTER_SYMBOL_SLOT}${prompt.label.after}`;
}

/**
 * The one list of new-chat starter prompts, shared by desktop `/chat` and mobile `/m`: one card per product
 * capability. A symbol card never names a symbol; the user picks one and `forSymbol` builds the question.
 */
export const STARTER_PROMPTS: readonly StarterPrompt[] = [
  {
    // The question is the routed phrase «گیج‌های ارزش‌گذاری ‹نماد›», with the zero-width non-joiners.
    id: "valuation-gauges",
    kind: "symbol",
    label: { before: "گیج‌های ارزش‌گذاری ", after: "" },
    exampleSymbol: "گدنا",
    forSymbol: (s) => `گیج‌های ارزش‌گذاری ${s}`,
    icon: Gauge,
    topic: "ارزش‌گذاری",
    detail: "P/E · P/S · قیمت تعادلی",
    tone: "valuation",
    featured: true,
    requires: "valuationGaugeCards",
  },
  {
    // Kept on one symbol by owner decision: the sales-mix answer is not yet reliable for every company
    // (e.g. گدنا), so the example stays on a symbol known to work.
    id: "sales-mix",
    kind: "direct",
    text: "ترکیب فروش محصولات کگهر را بررسی کن",
    example: true,
    icon: PieChart,
    topic: "ترکیب فروش",
    tone: "sales",
  },
  {
    id: "monthly-sales",
    kind: "symbol",
    label: { before: "روند فروش ماهانه ", after: "" },
    exampleSymbol: "کگهر",
    forSymbol: (s) => `روند فروش ماهانه ${s} را نشان بده`,
    icon: TrendingUp,
    topic: "فروش ماهانه",
    tone: "sales",
  },
  {
    // The product name is company-specific, so the question cannot take another symbol.
    id: "product-trend",
    kind: "direct",
    text: "روند فروش گندله کگهر در ۱۲ ماهه اخیر چطور بوده؟",
    example: true,
    icon: BarChart3,
    topic: "عملکرد محصول",
    tone: "sales",
  },
  {
    id: "ranking",
    kind: "direct",
    text: "رتبه‌بندی گزارش‌های ماهانه را نشان بده",
    icon: ListOrdered,
    topic: "رتبه‌بندی",
    tone: "sales",
  },
  {
    id: "industry-comparison",
    kind: "symbol",
    label: { before: "مقایسه ", after: " با صنعت" },
    exampleSymbol: "شاراک",
    forSymbol: (s) => `${s} را با صنعت خودش مقایسه کن`,
    icon: ArrowLeftRight,
    topic: "مقایسه با صنعت",
    tone: "sales",
  },
  {
    id: "quarterly-report",
    kind: "symbol",
    label: { before: "عملکرد مالی فصلی ", after: "" },
    exampleSymbol: "فولاد",
    forSymbol: (s) => `عملکرد مالی فصلی ${s} را بررسی کن`,
    icon: FileText,
    topic: "سود و زیان فصلی",
    tone: "statement",
  },
  {
    id: "balance-sheet",
    kind: "symbol",
    label: { before: "صورت وضعیت مالی ", after: "" },
    exampleSymbol: "فاذر",
    forSymbol: (s) => `صورت وضعیت مالی ${s} را بررسی کن`,
    icon: Scale,
    topic: "ترازنامه",
    tone: "statement",
  },
  {
    id: "cash-flow",
    kind: "symbol",
    label: { before: "جریان وجه نقد ", after: "" },
    exampleSymbol: "اخابر",
    forSymbol: (s) => `جریان وجه نقد ${s}`,
    icon: Banknote,
    topic: "جریان وجوه نقد",
    tone: "statement",
  },
];

/**
 * The cards the new-chat pages show: every card whose capability is available. While the availability answer is
 * loading or has failed, the caller passes nothing, so no optional card is shown.
 */
export function homeStarterPrompts(
  availability: StarterPromptAvailability = {},
): readonly StarterPrompt[] {
  return STARTER_PROMPTS.filter(
    (prompt) => prompt.requires === undefined || availability[prompt.requires] === true,
  );
}

/** How many watchlist symbols the symbol picker offers before the user types. */
export const STARTER_PICKER_WATCHLIST_LIMIT = 8;

/**
 * The watchlist symbols the symbol picker offers: newest followed first, each once. Rights issues (شگلح) are
 * left out because they have no statements or monthly sales of their own.
 */
export function starterPickerSymbols(
  followed: readonly { symbol: string; followedAtUtc?: string }[],
): string[] {
  return [...followed]
    .filter((item) => item.symbol.trim().length > 0 && !isRightsIssueSymbol(item.symbol))
    .sort((a, b) => (b.followedAtUtc ?? "").localeCompare(a.followedAtUtc ?? ""))
    .map((item) => item.symbol.trim())
    .filter((symbol, index, all) => all.indexOf(symbol) === index)
    .slice(0, STARTER_PICKER_WATCHLIST_LIMIT);
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
