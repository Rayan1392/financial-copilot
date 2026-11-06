import { describe, expect, it } from "vitest";
import { answerPlainText } from "@/lib/chat/answer-text";

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
