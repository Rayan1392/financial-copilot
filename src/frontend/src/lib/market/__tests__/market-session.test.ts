import { describe, expect, it } from "vitest";
import { isLaggingQuote, marketSessionStatus } from "../market-session";

// Thursday 2026-11-05, 12:30 Tehran; "now" is Friday 2026-11-06, 13:30 Tehran.
const THURSDAY = "2026-11-05T09:00:00Z";
const FRIDAY_NOW = new Date("2026-11-06T10:00:00Z");

describe("marketSessionStatus", () => {
  it("names the last trading session from the trading date, not the later sync time", () => {
    // Production pattern: Wednesday's prices (TradingDate 2026-11-04), synced on Thursday.
    const quotes = [
      { asOf: THURSDAY, tradingDate: "2026-11-04", isStale: true },
      { asOf: "2026-11-04T16:30:00Z", tradingDate: "2026-11-04", isStale: true },
    ];
    const status = marketSessionStatus(quotes, FRIDAY_NOW);

    expect(status.closed).toBe(true);
    expect(status.lastSessionDate).toBe("2026-11-04");
    expect(status.label).toBe("بازار تعطیل است · آخرین معامله چهارشنبه");
    expect(quotes.some((quote) => isLaggingQuote(quote, status))).toBe(false);
  });

  it("reports a closed market once, instead of flagging every quote, on a day without trading", () => {
    const quotes = [
      { asOf: THURSDAY, isStale: true },
      { asOf: "2026-11-05T08:45:00Z", isStale: true },
    ];
    const status = marketSessionStatus(quotes, FRIDAY_NOW);

    expect(status.closed).toBe(true);
    expect(quotes.some((quote) => isLaggingQuote(quote, status))).toBe(false);
  });

  it("compares sessions by trading date when deciding which quote is behind", () => {
    const current = { asOf: THURSDAY, tradingDate: "2026-11-04", isStale: true };
    // Synced on the same Thursday, but its last trade was Saturday: it is really behind.
    const halted = { asOf: THURSDAY, tradingDate: "2026-10-31", isStale: true };
    const status = marketSessionStatus([current, halted], FRIDAY_NOW);

    expect(isLaggingQuote(current, status)).toBe(false);
    expect(isLaggingQuote(halted, status)).toBe(true);
  });

  it("flags only the quote whose last trade is from an earlier session", () => {
    const current = { asOf: THURSDAY, isStale: true };
    const behind = { asOf: "2026-11-02T09:00:00Z", isStale: true };
    const status = marketSessionStatus([current, behind], FRIDAY_NOW);

    expect(isLaggingQuote(current, status)).toBe(false);
    expect(isLaggingQuote(behind, status)).toBe(true);
  });

  it("says nothing while the market trades and quotes are fresh", () => {
    const status = marketSessionStatus(
      [
        { asOf: "2026-11-06T09:30:00Z", isStale: false },
        { asOf: "2026-11-06T09:31:00Z", isStale: true },
      ],
      FRIDAY_NOW,
    );

    expect(status.closed).toBe(false);
    expect(status.delayed).toBe(false);
    expect(status.label).toBeNull();
  });

  it("calls out a delayed feed when today's quotes are all old", () => {
    const status = marketSessionStatus(
      [{ asOf: "2026-11-06T05:00:00Z", isStale: true }],
      FRIDAY_NOW,
    );

    expect(status.delayed).toBe(true);
    expect(status.label).toBe("داده‌های امروز با تأخیر به‌روز می‌شوند");
  });

  it("names an older last session by date instead of an ambiguous weekday", () => {
    const status = marketSessionStatus(
      [{ asOf: "2026-10-20T09:00:00Z", isStale: true }],
      FRIDAY_NOW,
    );

    expect(status.label).toMatch(/^بازار تعطیل است · آخرین معامله .*مهر$/);
  });

  it("treats a stale quote without a timestamp as behind", () => {
    const status = marketSessionStatus([{ asOf: THURSDAY, isStale: true }], FRIDAY_NOW);
    expect(isLaggingQuote({ isStale: true }, status)).toBe(true);
    expect(isLaggingQuote({ isStale: false }, status)).toBe(false);
  });
});
