import { describe, expect, it } from "vitest";
import { answerPlainText, answerTextWithSource } from "@/lib/chat/answer-text";

describe("answerPlainText", () => {
  it("drops markdown markers and display-only bidi controls but keeps lines and tables", () => {
    const markdown = [
      "### روند فروش ماهانه - کگهر",
      "**تغییر نسبت به ماه قبل:** ⁦-۴۰٫۲٪⁩",
      "",
      "",
      "",
      "| ماه | فروش |",
      "| --- | --- |",
    ].join("\n");

    expect(answerPlainText(markdown)).toBe(
      "روند فروش ماهانه - کگهر\nتغییر نسبت به ماه قبل: -۴۰٫۲٪\n\n| ماه | فروش |\n| --- | --- |",
    );
  });

  it("returns an empty string for an empty answer", () => {
    expect(answerPlainText("  \n ")).toBe("");
  });
});

describe("answerTextWithSource", () => {
  it("ends the answer with Sapio's name and site link", () => {
    expect(answerTextWithSource("**جمع کل دارایی‌ها:** ۳۸٬۳۴۲٬۶۵۵")).toBe(
      "جمع کل دارایی‌ها: ۳۸٬۳۴۲٬۶۵۵\n\n—\nساپیو | دستیار هوشمند بازار سرمایه\nhttps://sapioai.ir",
    );
  });

  it("returns nothing for an empty answer", () => {
    expect(answerTextWithSource("  \n ")).toBe("");
  });
});
