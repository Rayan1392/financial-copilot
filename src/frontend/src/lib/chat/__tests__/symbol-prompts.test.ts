import { describe, expect, it } from "vitest";
import { symbolAnalysisPrompts, symbolQuestionPrefill } from "@/lib/chat/symbol-prompts";

describe("symbol prompts (D-Q1: one shared module)", () => {
  it("builds the three analysis prompts for a symbol", () => {
    expect(symbolAnalysisPrompts(" شپنا ").map((prompt) => prompt.text)).toEqual([
      "روند فروش ماهانه شپنا را نشان بده",
      "گزارش فصلی شپنا",
      "ترازنامه شپنا",
    ]);
  });

  it("prefills the composer with the symbol and a trailing space", () => {
    expect(symbolQuestionPrefill("شپنا")).toBe("شپنا ");
  });
});
