import { describe, expect, it } from "vitest";
import { joinWatchlistWithFollowed } from "@/lib/workspace/watchlist-join";
import type { FollowedSymbol } from "@/lib/followed-symbols.functions";
import type { WatchlistQuote } from "@/lib/market-view.functions";

const quote = (symbol: string): WatchlistQuote => ({
  symbol,
  latestPrice: 1000,
  changePercent: 1.5,
  isStale: false,
});
const followed = (
  symbol: string,
  externalCompanyId: string,
  companyName = `${symbol} co`,
): FollowedSymbol => ({
  symbol,
  externalCompanyId,
  companyName,
  followedAtUtc: "2026-11-01T00:00:00Z",
});

describe("joinWatchlistWithFollowed", () => {
  it("adds company identity by symbol and keeps the quote order", () => {
    const join = joinWatchlistWithFollowed(
      [quote("فولاد"), quote("شپنا")],
      [followed("شپنا", "E2", "پالایش نفت اصفهان"), followed("فولاد", "E1", "فولاد مبارکه")],
    );
    expect(join.rows.map((row) => [row.symbol, row.externalCompanyId, row.companyName])).toEqual([
      ["فولاد", "E1", "فولاد مبارکه"],
      ["شپنا", "E2", "پالایش نفت اصفهان"],
    ]);
    expect(join.unmatchedSymbols).toEqual([]);
  });

  it("matches Latin tickers case-insensitively and trims whitespace", () => {
    const join = joinWatchlistWithFollowed([quote("abc ")], [followed("ABC", "E9")]);
    expect(join.rows[0].externalCompanyId).toBe("E9");
  });

  it("keeps an unmatched quote without a name and records it", () => {
    const join = joinWatchlistWithFollowed([quote("LEGACY")], []);
    expect(join.rows[0]).toMatchObject({
      symbol: "LEGACY",
      externalCompanyId: null,
      companyName: null,
    });
    expect(join.unmatchedSymbols).toEqual(["LEGACY"]);
  });

  it("refuses to guess when a symbol matches two followed rows", () => {
    const join = joinWatchlistWithFollowed(
      [quote("X")],
      [followed("X", "E1"), followed("x", "E2")],
    );
    expect(join.rows[0].externalCompanyId).toBeNull();
    expect(join.ambiguousSymbols).toEqual(["X"]);
  });

  it("treats a blank company name as missing", () => {
    const join = joinWatchlistWithFollowed([quote("X")], [followed("X", "E1", "  ")]);
    expect(join.rows[0].companyName).toBeNull();
  });
});
