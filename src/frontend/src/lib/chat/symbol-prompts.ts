/**
 * Symbol-analysis prompt templates (Feature 153 decision D-Q1). This module is their only owner; every frontend
 * surface that offers symbol prompts (watchlist, market movers, later surfaces) reuses it. They follow the
 * phrasing of prompts the assistant already answers well.
 */
export interface SymbolPrompt {
  id: "monthly-sales" | "quarterly-report" | "balance-sheet";
  text: string;
}

export function symbolAnalysisPrompts(symbol: string): SymbolPrompt[] {
  const ticker = symbol.trim();
  return [
    { id: "monthly-sales", text: `روند فروش ماهانه ${ticker} را نشان بده` },
    { id: "quarterly-report", text: `گزارش فصلی ${ticker}` },
    // Technical analysis is not answered yet, so the menu offers the balance sheet (Feature 154) instead.
    { id: "balance-sheet", text: `ترازنامه ${ticker}` },
  ];
}

/** Composer prefill for «پرسش دیگری درباره …», ending with a space so the user can type straight on. */
export function symbolQuestionPrefill(symbol: string): string {
  return `${symbol.trim()} `;
}
