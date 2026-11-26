/**
 * Symbol-analysis prompt templates (Feature 153 decision D-Q1). This module is their only owner; every frontend
 * surface that offers symbol prompts (watchlist, market movers, later surfaces) reuses it. They follow the
 * phrasing of prompts the assistant already answers well.
 */
export interface SymbolPrompt {
  id:
    | "monthly-sales"
    | "quarterly-report"
    | "balance-sheet"
    | "industry-comparison"
    | "valuation-gauges";
  text: string;
}

export interface SymbolPromptOptions {
  /**
   * Feature 159 change «standalone-valuation-gauge-cards» (OQ-132): offered only when the API says the gauge cards
   * are available to this user. The shortcut sends the same phrase a user can type.
   */
  valuationGauges?: boolean;
}

export function symbolAnalysisPrompts(
  symbol: string,
  options: SymbolPromptOptions = {},
): SymbolPrompt[] {
  const ticker = symbol.trim();
  return [
    { id: "monthly-sales", text: `روند فروش ماهانه ${ticker} را نشان بده` },
    { id: "quarterly-report", text: `گزارش فصلی ${ticker}` },
    // Technical analysis is not answered yet, so the menu offers the balance sheet (Feature 154) instead.
    { id: "balance-sheet", text: `ترازنامه ${ticker}` },
    { id: "industry-comparison", text: `${ticker} را با صنعت خودش مقایسه کن` },
    ...(options.valuationGauges
      ? [{ id: "valuation-gauges" as const, text: `گیج‌های ارزش‌گذاری ${ticker}` }]
      : []),
  ];
}

/** Composer prefill for «پرسش دیگری درباره …», ending with a space so the user can type straight on. */
export function symbolQuestionPrefill(symbol: string): string {
  return `${symbol.trim()} `;
}
