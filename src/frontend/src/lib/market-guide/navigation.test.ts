import { describe, expect, it } from "vitest";
import { marketGuideGroups } from "./catalog";
import { guideDestination, guideReturnPath, parseGuideSearch } from "./navigation";
import { safeInternalPath } from "@/lib/safe-redirect";

describe("market guide navigation", () => {
  it("contains five groups, fifteen unique examples and fourteen captures", () => {
    const examples = marketGuideGroups.flatMap((group) => group.examples);
    expect(marketGuideGroups).toHaveLength(5);
    expect(examples).toHaveLength(15);
    expect(new Set(examples.map((example) => example.id)).size).toBe(15);
    expect(examples.filter((example) => example.image)).toHaveLength(14);
    expect(examples.find((example) => !example.image)?.id).toBe("telegram");
  });

  it("only preserves known example and workspace IDs across sign-in", () => {
    const path = guideReturnPath("quarter", "mobile");
    expect(safeInternalPath(path)).toBe(path);
    expect(
      parseGuideSearch(Object.fromEntries(new URL(path, "https://internal.invalid").searchParams)),
    ).toEqual({ example: "quarter", source: "mobile" });
    expect(parseGuideSearch({ example: "https://other.invalid/", source: "unknown" })).toEqual({
      example: undefined,
      source: undefined,
    });
    expect(parseGuideSearch({ example: ["quarter", "cash"] }).example).toBeUndefined();
  });

  it.each([
    ["chat", "/chat", "/m"],
    ["watchlist", "/followed-symbols", "/m/watchlist"],
    ["events", "/followed-symbols", "/followed-symbols"],
    ["telegram", "/telegram-link", "/telegram-link"],
    ["support", "/messages", "/m/account/messages"],
  ] as const)("routes %s to the existing workspace screens", (action, desktop, mobile) => {
    expect(guideDestination(action, "desktop").to).toBe(desktop);
    expect(guideDestination(action, "mobile").to).toBe(mobile);
  });

  it("selects the existing watchlist events tab from either experience", () => {
    expect(guideDestination("events", "desktop")).toEqual({
      to: "/followed-symbols",
      search: { tab: "events" },
    });
    expect(guideDestination("events", "mobile")).toEqual(guideDestination("events", "desktop"));
  });
});
