import { describe, expect, it } from "vitest";
import { levelDistance } from "@/lib/chat/valuation-gauges";

// Change «equilibrium-distance-from-price»: the direction is a word, never a sign.
describe("levelDistance", () => {
  it("reads a level above today's price as a rise", () => {
    expect(levelDistance("33.2")).toEqual({ text: "۳۳٫۲٪ رشد", direction: "rise" });
    expect(levelDistance("+123.2")).toEqual({ text: "۱۲۳٫۲٪ رشد", direction: "rise" });
  });

  it("reads a level below today's price as a fall, without the minus sign", () => {
    expect(levelDistance("-56.7")).toEqual({ text: "۵۶٫۷٪ افت", direction: "fall" });
  });

  it.each(["0", "0.0", "-0.0", "-0.04", "0.04"])("reads %s as level with the price", (value) => {
    expect(levelDistance(value)).toEqual({ text: "هم‌سطح", direction: "level" });
  });

  it.each(["", "abc", "12.3.4", "NaN"])("ignores the malformed value %j", (value) => {
    expect(levelDistance(value)).toBeNull();
  });
});
