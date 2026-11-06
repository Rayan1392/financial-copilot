import { describe, expect, it } from "vitest";
import { formatNumber, formatPercent, toPersianNumerals } from "@/lib/format/persian";

describe("one product-wide number style («٬» thousands, «٫» decimals)", () => {
  it("formats numbers and percentages with Persian separators", () => {
    expect(formatNumber(82_642)).toBe("۸۲٬۶۴۲");
    expect(formatNumber(1_234_567.891, { maximumFractionDigits: 2 })).toBe("۱٬۲۳۴٬۵۶۷٫۸۹");
    expect(formatPercent(89.84)).toBe("+۸۹٫۸۴٪");
    expect(formatPercent(-1.12)).toBe("-۱٫۱۲٪");
  });

  it("converts separators only between digits in free text", () => {
    expect(toPersianNumerals("فروش 9,216.639 میلیارد تومان، رشد +74.1٪")).toBe(
      "فروش ۹٬۲۱۶٫۶۳۹ میلیارد تومان، رشد +۷۴٫۱٪",
    );
    // A list comma, a sentence period and a date are not numbers to regroup.
    expect(toPersianNumerals("گزینه 1,2 و دوره 1404/05.")).toBe("گزینه ۱,۲ و دوره ۱۴۰۴/۰۵.");
  });
});
