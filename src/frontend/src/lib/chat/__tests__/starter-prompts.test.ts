import { describe, expect, it } from "vitest";
import {
  availableStarterPrompts,
  homeStarterPrompts,
  personalizedStarterPrompts,
  STARTER_PROMPTS,
  WATCHLIST_STARTER_PROMPTS_ENABLED,
} from "../starter-prompts";

const followed = (symbol: string, followedAtUtc: string) => ({ symbol, followedAtUtc });

describe("homeStarterPrompts", () => {
  it("shows the fixed eight cards whatever the watchlist holds while personalization is off", () => {
    expect(WATCHLIST_STARTER_PROMPTS_ENABLED).toBe(false);

    const set = homeStarterPrompts([
      followed("ثبهساز", "2026-11-06T10:00:00Z"),
      followed("فاذر", "2026-11-05T10:00:00Z"),
    ]);

    expect(set.personalized).toBe(false);
    expect(set.prompts).toEqual(availableStarterPrompts());
    expect(set.prompts).toHaveLength(8);
    expect(set.prompts.every((prompt) => !prompt.text.includes("ثبهساز"))).toBe(true);
  });
});

describe("personalizedStarterPrompts", () => {
  it("falls back to the current static cards when the watchlist is empty", () => {
    const set = personalizedStarterPrompts([]);

    expect(set.personalized).toBe(false);
    expect(set.prompts).toEqual(availableStarterPrompts());
  });

  it("keeps the same capability cards and only swaps in watchlist symbols, newest first", () => {
    const set = personalizedStarterPrompts([
      followed("شگل", "2026-11-01T10:00:00Z"),
      followed("فاذر", "2026-11-05T10:00:00Z"),
      followed("کگهر", "2026-11-03T10:00:00Z"),
      followed("اخابر", "2026-11-04T10:00:00Z"),
    ]);

    expect(set.personalized).toBe(true);
    // Same topics, same order, each once: no capability is dropped or repeated.
    expect(set.prompts.map((prompt) => prompt.topic)).toEqual(
      availableStarterPrompts().map((prompt) => prompt.topic),
    );
    expect(set.prompts.map((prompt) => prompt.text)).toEqual([
      // Sales mix stays static by owner decision (not yet reliable for every company).
      "ترکیب فروش محصولات کگهر را بررسی کن",
      "روند فروش ماهانه فاذر را نشان بده",
      // Product card: the product name is company-specific, so its static example stays.
      "روند فروش گندله کگهر در ۱۲ ماهه اخیر چطور بوده؟",
      "رتبه‌بندی گزارش‌های ماهانه را نشان بده",
      "اخابر را با صنعت خودش مقایسه کن",
      "عملکرد مالی فصلی کگهر را بررسی کن",
      "صورت وضعیت مالی شگل را بررسی کن",
      "جریان وجه نقد فاذر",
    ]);
  });

  it("uses a single followed symbol on every card that takes one", () => {
    const set = personalizedStarterPrompts([followed("فاذر", "2026-11-05T10:00:00Z")]);
    const texts = set.prompts.map((prompt) => prompt.text);

    expect(texts).toHaveLength(availableStarterPrompts().length);
    expect(new Set(texts).size).toBe(texts.length);
    expect(texts).toEqual([
      "ترکیب فروش محصولات کگهر را بررسی کن",
      "روند فروش ماهانه فاذر را نشان بده",
      "روند فروش گندله کگهر در ۱۲ ماهه اخیر چطور بوده؟",
      "رتبه‌بندی گزارش‌های ماهانه را نشان بده",
      "فاذر را با صنعت خودش مقایسه کن",
      "عملکرد مالی فصلی فاذر را بررسی کن",
      "صورت وضعیت مالی فاذر را بررسی کن",
      "جریان وجه نقد فاذر",
    ]);
  });

  it("leaves rights issues (symbol + «ح») out, falling back when only they are followed", () => {
    const mixed = personalizedStarterPrompts([
      followed("شگلح", "2026-11-06T10:00:00Z"),
      followed("شگل", "2026-11-05T10:00:00Z"),
    ]);
    expect(mixed.prompts.every((prompt) => !prompt.text.includes("شگلح"))).toBe(true);
    expect(mixed.prompts[1].text).toBe("روند فروش ماهانه شگل را نشان بده");

    const onlyRights = personalizedStarterPrompts([followed("شگلح", "2026-11-06T10:00:00Z")]);
    expect(onlyRights.personalized).toBe(false);
    expect(onlyRights.prompts).toEqual(availableStarterPrompts());
  });

  it("ignores blank and repeated symbols", () => {
    const set = personalizedStarterPrompts([
      followed(" ", "2026-11-05T10:00:00Z"),
      followed("کگهر", "2026-11-04T10:00:00Z"),
      followed("کگهر", "2026-11-03T10:00:00Z"),
    ]);

    expect(set.prompts[1].text).toBe("روند فروش ماهانه کگهر را نشان بده");
    expect(set.prompts.every((prompt) => !prompt.text.includes("  "))).toBe(true);
  });
});

// Change «valuation-gauge-starter-card»: the gauge card is shown first, and only while the API reports it available.
describe("the valuation gauge starter card", () => {
  const eight = [
    "ترکیب فروش محصولات کگهر را بررسی کن",
    "روند فروش ماهانه کگهر را نشان بده",
    "روند فروش گندله کگهر در ۱۲ ماهه اخیر چطور بوده؟",
    "رتبه‌بندی گزارش‌های ماهانه را نشان بده",
    "شاراک را با صنعت خودش مقایسه کن",
    "عملکرد مالی فصلی فولاد را بررسی کن",
    "صورت وضعیت مالی فاذر را بررسی کن",
    "جریان وجه نقد اخابر",
  ];

  it("comes first with nine cards while gauge cards are available", () => {
    const set = homeStarterPrompts([], { valuationGaugeCards: true });

    expect(set.prompts.map((prompt) => prompt.text)).toEqual(["گیج‌های ارزش‌گذاری گدنا", ...eight]);
    expect(set.prompts[0]).toMatchObject({
      id: "valuation-gauges",
      topic: "ارزش‌گذاری",
      detail: "P/E · P/S · قیمت تعادلی",
      tone: "valuation",
      featured: true,
    });
    expect(new Set(set.prompts.map((prompt) => prompt.id)).size).toBe(9);
  });

  it.each([undefined, {}, { valuationGaugeCards: false }])(
    "leaves today's eight cards in today's order otherwise (%o)",
    (availability) => {
      expect(homeStarterPrompts([], availability).prompts.map((prompt) => prompt.text)).toEqual(
        eight,
      );
    },
  );

  it("sends the routed phrase with zero-width non-joiners", () => {
    const gauge = STARTER_PROMPTS.find((prompt) => prompt.id === "valuation-gauges")!;

    expect(gauge.text).toBe("گیج‌های ارزش‌گذاری گدنا");
    expect(gauge.forSymbol?.("شاراک")).toBe("گیج‌های ارزش‌گذاری شاراک");
  });
});
