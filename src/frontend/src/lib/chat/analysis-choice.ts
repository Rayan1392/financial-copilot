import type { AnalysisDepth, AnalysisOptions, GroundedAnalysisHint } from "@/lib/chat.functions";
import { toPersianDigits } from "@/lib/format/persian";

/**
 * Change «sol-deep-analysis-web-search»: the viewer's analysis choice (depth and web search) and what it costs.
 * Prices always come from the backend hint; this module only looks them up and never sends an amount.
 */
export interface AnalysisChoice {
  depth: AnalysisDepth;
  webSearch: boolean;
}

/** Per-viewer convenience only (the backend never reads it); a missing or blocked storage falls back to defaults. */
export const ANALYSIS_CHOICE_STORAGE_KEY = "sapio.analysis-choice.v1";

const OFFER_ACTION_PREFIX = "ga1:";

export function defaultChoice(hint: GroundedAnalysisHint | null | undefined): AnalysisChoice {
  return {
    depth: hint?.defaultDepth === "simple" ? "simple" : "deep",
    webSearch: hint?.defaultWebSearch ?? true,
  };
}

export function readStoredChoice(): AnalysisChoice | null {
  try {
    const raw = globalThis.localStorage?.getItem(ANALYSIS_CHOICE_STORAGE_KEY);
    if (!raw) return null;
    const parsed: unknown = JSON.parse(raw);
    if (
      typeof parsed === "object" &&
      parsed !== null &&
      ((parsed as AnalysisChoice).depth === "simple" ||
        (parsed as AnalysisChoice).depth === "deep") &&
      typeof (parsed as AnalysisChoice).webSearch === "boolean"
    ) {
      return {
        depth: (parsed as AnalysisChoice).depth,
        webSearch: (parsed as AnalysisChoice).webSearch,
      };
    }
    return null;
  } catch {
    return null;
  }
}

export function storeChoice(choice: AnalysisChoice) {
  try {
    globalThis.localStorage?.setItem(ANALYSIS_CHOICE_STORAGE_KEY, JSON.stringify(choice));
  } catch {
    // Private windows and blocked storage: the choice simply is not remembered.
  }
}

/** The backend price of a choice; undefined when the hint has no price for it (then no amount is shown). */
export function creditsFor(
  hint: GroundedAnalysisHint | null | undefined,
  choice: AnalysisChoice,
): number | undefined {
  return hint?.prices.find(
    (price) => price.depth === choice.depth && price.webSearch === choice.webSearch,
  )?.credits;
}

/**
 * The options a send carries. A 163 confirmation carries the choice (the offer holds its own price version); a
 * cancel or any other offer action carries none; a question after a discussable answer carries the choice and the
 * price version shown, so the backend refuses a stale price list without charging.
 */
export function analysisOptionsFor(
  hint: GroundedAnalysisHint | null | undefined,
  choice: AnalysisChoice,
  suggestedActionId?: string,
): AnalysisOptions | undefined {
  if (!hint) return undefined;
  if (suggestedActionId?.startsWith(OFFER_ACTION_PREFIX)) {
    return hint.offer && suggestedActionId === hint.offer.confirmActionId
      ? { depth: choice.depth, webSearch: choice.webSearch }
      : undefined;
  }
  return hint.available
    ? { depth: choice.depth, webSearch: choice.webSearch, pricingVersion: hint.pricingVersion }
    : undefined;
}

export const UNLIMITED_COST_TEXT = "با اشتراک Max اعتباری کسر نمی‌شود.";

/** The live cost sentence of the composer bar (162) or of the offer card (163). */
export function costSentence(
  hint: GroundedAnalysisHint,
  choice: AnalysisChoice,
  kind: "discussion" | "offer",
): string | null {
  if (hint.unlimited) return UNLIMITED_COST_TEXT;
  const credits = creditsFor(hint, choice);
  if (credits === undefined) return null;
  const amount = toPersianDigits(credits);
  return kind === "offer"
    ? `این تحلیل با تنظیمات انتخاب‌شده ${amount} اعتبار مصرف می‌کند.`
    : `اگر پاسخ از روی همین گزارش باشد، با تنظیمات انتخاب‌شده ${amount} اعتبار مصرف می‌شود.`;
}

/**
 * Whether «تحلیل کن» is enabled. The hint only says whether the balance covers the default tier, so a short balance
 * blocks that tier and anything dearer; a cheaper tier stays possible and the backend still enforces the balance.
 */
export function canConfirm(hint: GroundedAnalysisHint, choice: AnalysisChoice): boolean {
  const offer = hint.offer;
  if (!offer) return false;
  if (hint.unlimited || offer.sufficientBalance) return true;
  const chosen = creditsFor(hint, choice);
  const atDefault = creditsFor(hint, defaultChoice(hint));
  return chosen !== undefined && atDefault !== undefined && chosen < atDefault;
}
