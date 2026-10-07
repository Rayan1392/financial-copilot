import { describe, expect, it } from "vitest";
import { computeCreditProgress, toneForPercentage } from "../credit-progress";

describe("computeCreditProgress", () => {
  it.each([
    [18, 25, 72],
    [18, 300, 6],
    [18, 1000, 1.8],
    [25, 25, 100],
    [10, 25, 40],
    [2, 25, 8],
    [0, 25, 0],
  ])("%d of %d => %d%%", (remaining, limit, expected) => {
    expect(computeCreditProgress(remaining, limit).percentage).toBeCloseTo(expected, 6);
  });

  it("clamps negative remaining credit to zero", () => {
    const result = computeCreditProgress(-5, 25);
    expect(result.remaining).toBe(0);
    expect(result.percentage).toBe(0);
    expect(result.tone).toBe("danger");
  });

  it("clamps remaining credit above the plan limit to 100%", () => {
    expect(computeCreditProgress(40, 25).percentage).toBe(100);
  });

  it.each([null, undefined, 0, -10, Number.NaN])(
    "reports no percentage for unknown plan limit %s",
    (limit) => {
      const result = computeCreditProgress(18, limit);
      expect(result.percentage).toBeNull();
      expect(result.tone).toBeNull();
      expect(result.remaining).toBe(18);
    },
  );

  it("treats non-finite remaining credit as zero", () => {
    expect(computeCreditProgress(Number.NaN, 25).percentage).toBe(0);
  });
});

describe("toneForPercentage", () => {
  it.each([
    [100, "healthy"],
    [50.1, "healthy"],
    [50, "warning"],
    [20, "warning"],
    [19.9, "danger"],
    [0, "danger"],
  ])("%d%% => %s", (percentage, tone) => {
    expect(toneForPercentage(percentage)).toBe(tone);
  });
});
