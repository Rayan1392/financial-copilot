import { describe, expect, it } from "vitest";
import { symbolAnalysisPrompts, symbolQuestionPrefill } from "@/lib/chat/symbol-prompts";

describe("symbol prompts (D-Q1: one shared module)", () => {
  it("builds the four analysis prompts for a symbol", () => {
    expect(symbolAnalysisPrompts(" شپنا ").map((prompt) => prompt.text)).toEqual([
      "روند فروش ماهانه شپنا را نشان بده",
      "گزارش فصلی شپنا",
      "ترازنامه شپنا",
      "شپنا را با صنعت خودش مقایسه کن",
    ]);
  });

  it("adds the valuation-gauge shortcut only when the API makes it available", () => {
    expect(symbolAnalysisPrompts("شاراک").map((prompt) => prompt.id)).not.toContain(
      "valuation-gauges",
    );
    expect(symbolAnalysisPrompts(" شاراک ", { valuationGauges: true }).at(-1)).toEqual({
      id: "valuation-gauges",
      text: "گیج‌های ارزش‌گذاری شاراک",
    });
  });

  it("prefills the composer with the symbol and a trailing space", () => {
    expect(symbolQuestionPrefill("شپنا")).toBe("شپنا ");
  });
});
