import { describe, expect, it } from "vitest";
import { lowerBoundComparison } from "@/lib/chat/valuation-gauges";

// OQ-137 (owner, 2026-11-26): the equilibrium card's price-vs-lower-bound figure.
describe("lowerBoundComparison", () => {
  it("says «زیر حد پایین» with the absolute value below the lower bound", () => {
    expect(lowerBoundComparison("-5.0")).toEqual({ label: "زیر حد پایین", percent: "۵٫۰٪" });
  });

  it("keeps «بالای حد پایین» with a sign above the lower bound, as released", () => {
    expect(lowerBoundComparison("8.4")).toEqual({ label: "بالای حد پایین", percent: "‎+۸٫۴٪" });
  });

  it.each(["0", "0.0", "-0.04"])("keeps the released label when %s rounds to zero", (value) => {
    expect(lowerBoundComparison(value).label).toBe("بالای حد پایین");
  });
});
