import type { WatchlistQuote } from "@/lib/market-view.functions";
import type { FollowedSymbol } from "@/lib/followed-symbols.functions";

export interface WatchlistRow extends WatchlistQuote {
  /** Null when no followed-symbol row matches the quote (for example a legacy watchlist entry). */
  externalCompanyId: string | null;
  companyName: string | null;
}

export interface WatchlistJoin {
  rows: WatchlistRow[];
  /** Quotes with no followed-symbol match, or with more than one; recorded so B-2 can be revisited (D-Q2). */
  unmatchedSymbols: string[];
  ambiguousSymbols: string[];
}

function symbolKey(symbol: string): string {
  return symbol.trim().toLocaleLowerCase("fa-IR");
}

/**
 * `GET /watchlists/me` returns quotes without company identity, so the client joins them with
 * `GET /followed-symbols/me` by symbol (Feature 153 T03). Quote order is kept. A missing or ambiguous match
 * yields a row without identity rather than a guess.
 */
export function joinWatchlistWithFollowed(
  quotes: readonly WatchlistQuote[],
  followed: readonly FollowedSymbol[],
): WatchlistJoin {
  const bySymbol = new Map<string, FollowedSymbol[]>();
  for (const item of followed) {
    const key = symbolKey(item.symbol);
    bySymbol.set(key, [...(bySymbol.get(key) ?? []), item]);
  }
  const unmatchedSymbols: string[] = [];
  const ambiguousSymbols: string[] = [];
  const rows = quotes.map((quote) => {
    const matches = bySymbol.get(symbolKey(quote.symbol)) ?? [];
    if (matches.length === 0) unmatchedSymbols.push(quote.symbol);
    if (matches.length > 1) ambiguousSymbols.push(quote.symbol);
    const match = matches.length === 1 ? matches[0] : null;
    return {
      ...quote,
      externalCompanyId: match?.externalCompanyId ?? null,
      companyName: match?.companyName?.trim() ? match.companyName : null,
    };
  });
  return { rows, unmatchedSymbols, ambiguousSymbols };
}
