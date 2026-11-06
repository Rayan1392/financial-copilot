import { describe, expect, it } from "vitest";
import { personalizedStarterPrompts, STARTER_PROMPTS } from "../starter-prompts";

const followed = (symbol: string, followedAtUtc: string) => ({ symbol, followedAtUtc });

describe("personalizedStarterPrompts", () => {
  it("falls back to the current static cards when the watchlist is empty", () => {
    const set = personalizedStarterPrompts([]);

    expect(set.personalized).toBe(false);
    expect(set.prompts).toBe(STARTER_PROMPTS);
  });

  it("deals card types across the watchlist, newest followed first, and keeps the ranking card", () => {
    const set = personalizedStarterPrompts([
      followed("شگل", "2026-11-01T10:00:00Z"),
      followed("فاذر", "2026-11-05T10:00:00Z"),
      followed("کگهر", "2026-11-03T10:00:00Z"),
      followed("اخابر", "2026-11-04T10:00:00Z"),
    ]);

    expect(set.personalized).toBe(true);
    expect(set.prompts.map((prompt) => prompt.text)).toEqual([
      "ترکیب فروش محصولات فاذر را بررسی کن",
      "روند فروش ماهانه اخابر را نشان بده",
      "عملکرد مالی فصلی کگهر را بررسی کن",
      "صورت وضعیت مالی شگل را بررسی کن",
      "روند فروش ماهانه فاذر را نشان بده",
      "رتبه‌بندی گزارش‌های ماهانه را نشان بده",
    ]);
  });

  it("fills a one-symbol watchlist to the full set without duplicate cards", () => {
    const set = personalizedStarterPrompts([followed("فاذر", "2026-11-05T10:00:00Z")]);
    const texts = set.prompts.map((prompt) => prompt.text);

    expect(texts).toHaveLength(STARTER_PROMPTS.length);
    expect(new Set(texts).size).toBe(texts.length);
    expect(texts.slice(0, 4)).toEqual([
      "ترکیب فروش محصولات فاذر را بررسی کن",
      "روند فروش ماهانه فاذر را نشان بده",
      "عملکرد مالی فصلی فاذر را بررسی کن",
      "صورت وضعیت مالی فاذر را بررسی کن",
    ]);
    expect(texts).toContain("رتبه‌بندی گزارش‌های ماهانه را نشان بده");
  });

  it("ignores blank and repeated symbols", () => {
    const set = personalizedStarterPrompts([
      followed(" ", "2026-11-05T10:00:00Z"),
      followed("کگهر", "2026-11-04T10:00:00Z"),
      followed("کگهر", "2026-11-03T10:00:00Z"),
    ]);

    expect(set.prompts[0].text).toBe("ترکیب فروش محصولات کگهر را بررسی کن");
    expect(set.prompts.every((prompt) => !prompt.text.includes("  "))).toBe(true);
  });
});
