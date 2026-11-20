import { afterEach, describe, expect, it, vi } from "vitest";
import type { GroundedAnalysisHint } from "@/lib/chat.functions";
import {
  analysisOptionsFor,
  canConfirm,
  costSentence,
  creditsFor,
  defaultChoice,
  readStoredChoice,
  storeChoice,
  UNLIMITED_COST_TEXT,
} from "@/lib/chat/analysis-choice";

const hint = (overrides: Partial<GroundedAnalysisHint> = {}): GroundedAnalysisHint => ({
  available: true,
  pricingVersion: "v1",
  prices: [
    { depth: "simple", webSearch: false, credits: 8 },
    { depth: "simple", webSearch: true, credits: 12 },
    { depth: "deep", webSearch: false, credits: 16 },
    { depth: "deep", webSearch: true, credits: 25 },
  ],
  defaultDepth: "deep",
  defaultWebSearch: true,
  unlimited: false,
  offer: null,
  ...overrides,
});
const offer = (sufficientBalance = true) => ({
  offerId: "o1",
  symbol: "فولاد",
  expiresAt: "2026-11-20T10:00:00Z",
  sufficientBalance,
  confirmActionId: "ga1:o1:confirm",
  cancelActionId: "ga1:o1:cancel",
});

afterEach(() => {
  vi.restoreAllMocks();
  localStorage.clear();
});

describe("analysis choice (change «sol-deep-analysis-web-search», T12)", () => {
  it("prices come only from the backend hint", () => {
    expect(creditsFor(hint(), { depth: "simple", webSearch: false })).toBe(8);
    expect(creditsFor(hint(), { depth: "deep", webSearch: true })).toBe(25);
    expect(creditsFor(hint({ prices: [] }), { depth: "deep", webSearch: true })).toBeUndefined();
    expect(defaultChoice(hint({ defaultDepth: "simple", defaultWebSearch: false }))).toEqual({
      depth: "simple",
      webSearch: false,
    });
    expect(defaultChoice(null)).toEqual({ depth: "deep", webSearch: true });
  });

  it("the cost sentence follows the choice and the price list", () => {
    expect(costSentence(hint(), { depth: "deep", webSearch: true }, "discussion")).toBe(
      "اگر پاسخ از روی همین گزارش باشد، با تنظیمات انتخاب‌شده ۲۵ اعتبار مصرف می‌شود.",
    );
    expect(costSentence(hint(), { depth: "simple", webSearch: true }, "offer")).toBe(
      "این تحلیل با تنظیمات انتخاب‌شده ۱۲ اعتبار مصرف می‌کند.",
    );
    expect(
      costSentence(hint({ unlimited: true }), { depth: "deep", webSearch: true }, "offer"),
    ).toBe(UNLIMITED_COST_TEXT);
    expect(
      costSentence(hint({ prices: [] }), { depth: "deep", webSearch: true }, "offer"),
    ).toBeNull();
  });

  it("options never carry an amount; offer actions carry the choice only on confirm", () => {
    const choice = { depth: "simple" as const, webSearch: true };
    expect(analysisOptionsFor(hint(), choice)).toEqual({
      depth: "simple",
      webSearch: true,
      pricingVersion: "v1",
    });
    expect(analysisOptionsFor(hint({ available: false }), choice)).toBeUndefined();
    expect(analysisOptionsFor(null, choice)).toBeUndefined();
    const withOffer = hint({ available: false, offer: offer() });
    expect(analysisOptionsFor(withOffer, choice, "ga1:o1:confirm")).toEqual({
      depth: "simple",
      webSearch: true,
    });
    expect(analysisOptionsFor(withOffer, choice, "ga1:o1:cancel")).toBeUndefined();
    expect(analysisOptionsFor(withOffer, choice, "ga1:other:confirm")).toBeUndefined();
  });

  it("a short balance blocks the default tier and dearer ones, not a cheaper one", () => {
    const short = hint({ offer: offer(false) });
    expect(canConfirm(short, { depth: "deep", webSearch: true })).toBe(false);
    expect(canConfirm(short, { depth: "simple", webSearch: false })).toBe(true);
    expect(canConfirm(hint({ offer: offer(true) }), { depth: "deep", webSearch: true })).toBe(true);
    expect(
      canConfirm(hint({ offer: offer(false), unlimited: true }), {
        depth: "deep",
        webSearch: true,
      }),
    ).toBe(true);
    expect(canConfirm(hint(), { depth: "deep", webSearch: true })).toBe(false);
  });

  it("the remembered choice survives a reload and a blocked storage falls back quietly", () => {
    storeChoice({ depth: "simple", webSearch: false });
    expect(readStoredChoice()).toEqual({ depth: "simple", webSearch: false });

    localStorage.setItem("sapio.analysis-choice.v1", '{"depth":"huge","webSearch":1}');
    expect(readStoredChoice()).toBeNull();

    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
      throw new Error("blocked");
    });
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new Error("blocked");
    });
    expect(readStoredChoice()).toBeNull();
    expect(() => storeChoice({ depth: "deep", webSearch: true })).not.toThrow();
  });
});
