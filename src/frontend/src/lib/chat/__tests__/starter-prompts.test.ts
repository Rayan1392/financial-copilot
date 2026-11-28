import { describe, expect, it } from "vitest";
import {
  homeStarterPrompts,
  STARTER_PICKER_WATCHLIST_LIMIT,
  STARTER_PROMPTS,
  starterPickerSymbols,
  starterPromptTitle,
  type SymbolStarterPrompt,
} from "../starter-prompts";

const followed = (symbol: string, followedAtUtc: string) => ({ symbol, followedAtUtc });
const symbolCards = STARTER_PROMPTS.filter(
  (prompt): prompt is SymbolStarterPrompt => prompt.kind === "symbol",
);

// Change «starter-cards-symbol-picker»: the symbol cards ask for the symbol, three send in one tap. Feature 168
// added the option card, so there are seven symbol cards.
describe("the starter card kinds", () => {
  it("shows a symbol-free label with the «نماد» slot on the seven symbol cards", () => {
    expect(symbolCards.map((prompt) => [prompt.id, starterPromptTitle(prompt)])).toEqual([
      ["valuation-gauges", "گیج‌های ارزش‌گذاری نماد"],
      ["option-calls", "اختیار خرید نماد"],
      ["monthly-sales", "روند فروش ماهانه نماد"],
      ["industry-comparison", "مقایسه نماد با صنعت"],
      ["quarterly-report", "عملکرد مالی فصلی نماد"],
      ["balance-sheet", "صورت وضعیت مالی نماد"],
      ["cash-flow", "جریان وجه نقد نماد"],
    ]);
    for (const prompt of symbolCards)
      expect(starterPromptTitle(prompt)).not.toContain(prompt.exampleSymbol);
  });

  it("sends, for the example symbol, exactly the question the card sent before the change", () => {
    expect(symbolCards.map((prompt) => prompt.forSymbol(prompt.exampleSymbol))).toEqual([
      "گیج‌های ارزش‌گذاری گدنا",
      "اختیار خرید اخابر",
      "روند فروش ماهانه کگهر را نشان بده",
      "شاراک را با صنعت خودش مقایسه کن",
      "عملکرد مالی فصلی فولاد را بررسی کن",
      "صورت وضعیت مالی فاذر را بررسی کن",
      "جریان وجه نقد اخابر",
    ]);
  });

  it("sends the gauge question for the picked symbol, with zero-width non-joiners", () => {
    const gauge = symbolCards.find((prompt) => prompt.id === "valuation-gauges")!;

    expect(gauge.forSymbol("فولاد")).toBe("گیج‌های ارزش‌گذاری فولاد");
  });

  it("keeps the ranking card and the two fixed examples as one-tap cards (OQ-151)", () => {
    const direct = STARTER_PROMPTS.flatMap((prompt) =>
      prompt.kind === "direct" ? [[prompt.id, prompt.text, prompt.example ?? false]] : [],
    );

    expect(direct).toEqual([
      ["sales-mix", "ترکیب فروش محصولات کگهر را بررسی کن", true],
      ["product-trend", "روند فروش گندله کگهر در ۱۲ ماهه اخیر چطور بوده؟", true],
      ["ranking", "رتبه‌بندی گزارش‌های ماهانه را نشان بده", false],
    ]);
  });
});

// Change «valuation-gauge-starter-card»: the gauge card is shown first, and only while the API reports it available.
describe("homeStarterPrompts", () => {
  const eight = [
    "sales-mix",
    "monthly-sales",
    "product-trend",
    "ranking",
    "industry-comparison",
    "quarterly-report",
    "balance-sheet",
    "cash-flow",
  ];

  it("puts the gauge card first, with nine cards, while gauge cards are available", () => {
    const prompts = homeStarterPrompts({ valuationGaugeCards: true });

    expect(prompts.map((prompt) => prompt.id)).toEqual(["valuation-gauges", ...eight]);
    expect(prompts[0]).toMatchObject({
      topic: "ارزش‌گذاری",
      detail: "P/E · P/S · قیمت تعادلی",
      tone: "valuation",
      featured: true,
    });
  });

  it.each([undefined, {}, { valuationGaugeCards: false }])(
    "leaves the other eight cards in their order otherwise (%o)",
    (availability) => {
      expect(homeStarterPrompts(availability).map((prompt) => prompt.id)).toEqual(eight);
    },
  );
});

describe("starterPickerSymbols", () => {
  it("lists the watchlist symbols newest first", () => {
    expect(
      starterPickerSymbols([
        followed("شگل", "2026-11-01T10:00:00Z"),
        followed("فاذر", "2026-11-05T10:00:00Z"),
        followed("کگهر", "2026-11-03T10:00:00Z"),
      ]),
    ).toEqual(["فاذر", "کگهر", "شگل"]);
  });

  it("leaves out rights issues (symbol + «ح»), blank and repeated symbols", () => {
    expect(
      starterPickerSymbols([
        followed("شگلح", "2026-11-06T10:00:00Z"),
        followed(" ", "2026-11-05T10:00:00Z"),
        followed(" کگهر ", "2026-11-04T10:00:00Z"),
        followed("کگهر", "2026-11-03T10:00:00Z"),
      ]),
    ).toEqual(["کگهر"]);
  });

  it("offers at most eight symbols", () => {
    const many = Array.from({ length: 12 }, (_, index) =>
      followed(`نماد${index}`, `2026-11-${String(index + 1).padStart(2, "0")}T10:00:00Z`),
    );

    expect(starterPickerSymbols(many)).toHaveLength(STARTER_PICKER_WATCHLIST_LIMIT);
    expect(starterPickerSymbols(many)[0]).toBe("نماد11");
  });

  it("is empty for an empty watchlist", () => {
    expect(starterPickerSymbols([])).toEqual([]);
  });
});
