import { describe, expect, it } from "vitest";
import { personalizedStarterPrompts, STARTER_PROMPTS } from "../starter-prompts";

const followed = (symbol: string, followedAtUtc: string) => ({ symbol, followedAtUtc });

describe("personalizedStarterPrompts", () => {
  it("falls back to the current static cards when the watchlist is empty", () => {
    const set = personalizedStarterPrompts([]);

    expect(set.personalized).toBe(false);
    expect(set.prompts).toBe(STARTER_PROMPTS);
  });

  it("keeps the same six capability cards and only swaps in watchlist symbols, newest first", () => {
    const set = personalizedStarterPrompts([
      followed("شگل", "2026-11-01T10:00:00Z"),
      followed("فاذر", "2026-11-05T10:00:00Z"),
      followed("کگهر", "2026-11-03T10:00:00Z"),
      followed("اخابر", "2026-11-04T10:00:00Z"),
    ]);

    expect(set.personalized).toBe(true);
    // Same topics, same order, each once: no capability is dropped or repeated.
    expect(set.prompts.map((prompt) => prompt.topic)).toEqual(
      STARTER_PROMPTS.map((prompt) => prompt.topic),
    );
    expect(set.prompts.map((prompt) => prompt.text)).toEqual([
      // Sales mix stays static by owner decision (not yet reliable for every company).
      "ترکیب فروش محصولات کگهر را بررسی کن",
      "روند فروش ماهانه فاذر را نشان بده",
      // Product card: the product name is company-specific, so its static example stays.
      "روند فروش گندله کگهر در ۱۲ ماهه اخیر چطور بوده؟",
      "رتبه‌بندی گزارش‌های ماهانه را نشان بده",
      "عملکرد مالی فصلی اخابر را بررسی کن",
      "صورت وضعیت مالی کگهر را بررسی کن",
    ]);
  });

  it("uses a single followed symbol on every card that takes one", () => {
    const set = personalizedStarterPrompts([followed("فاذر", "2026-11-05T10:00:00Z")]);
    const texts = set.prompts.map((prompt) => prompt.text);

    expect(texts).toHaveLength(STARTER_PROMPTS.length);
    expect(new Set(texts).size).toBe(texts.length);
    expect(texts).toEqual([
      "ترکیب فروش محصولات کگهر را بررسی کن",
      "روند فروش ماهانه فاذر را نشان بده",
      "روند فروش گندله کگهر در ۱۲ ماهه اخیر چطور بوده؟",
      "رتبه‌بندی گزارش‌های ماهانه را نشان بده",
      "عملکرد مالی فصلی فاذر را بررسی کن",
      "صورت وضعیت مالی فاذر را بررسی کن",
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
    expect(onlyRights.prompts).toBe(STARTER_PROMPTS);
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
