import { describe, expect, it } from "vitest";
import { parseProductRevenueMix } from "@/lib/chat/product-revenue-mix";

// Feature 075 change «product-mix-answer-image-share»: the text the server writes in `BuildProductRevenueMixContent`.
const answer = (label: string) =>
  [
    `### ترکیب درآمد محصولات — ${label}`,
    "دوره: 1405/07",
    "",
    "| ردیف | محصول | فروش (تومان) | سهم (٪) | غالب |",
    "|------|-------|------------|---------|------|",
    "| 1 | گندله | 5,467,502,800,000 | 42.3٪ | ✓ |",
  ].join("\n");

describe("parseProductRevenueMix", () => {
  it("recognises the answer and reads the symbol from the company label", () => {
    expect(parseProductRevenueMix(answer("معدنی و صنعتی گهر زمین (کگهر)"))).toEqual({
      symbol: "کگهر",
    });
    expect(parseProductRevenueMix(answer("سرمایه گذاری (سهامی عام) (وغدیر)"))).toEqual({
      symbol: "وغدیر",
    });
  });

  it("uses the label when the answer names the company by symbol only", () => {
    expect(parseProductRevenueMix(answer("کگهر"))).toEqual({ symbol: "کگهر" });
  });

  it("ignores other answers", () => {
    expect(parseProductRevenueMix("### ترکیب درآمد محصولات — کگهر\nداده‌ای یافت نشد")).toBeNull();
    expect(
      parseProductRevenueMix(
        "| ردیف | محصول | فروش (تومان) | سهم (٪) | غالب |\n|---|---|---|---|---|",
      ),
    ).toBeNull();
    expect(parseProductRevenueMix("قیمت فولاد برابر است با 5000 ریال")).toBeNull();
  });
});
