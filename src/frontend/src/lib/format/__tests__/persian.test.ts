import { describe, expect, it } from "vitest";
import { isolateSignedNumbers } from "@/lib/format/persian";

const LRI = "⁦";
const PDI = "⁩";

describe("isolateSignedNumbers", () => {
  it("keeps a negative percent left-to-right so -40.2% is not read as -2.40%", () => {
    expect(isolateSignedNumbers("**تغییر نسبت به ماه قبل:** -۴۰.۲٪")).toBe(
      `**تغییر نسبت به ماه قبل:** ${LRI}-۴۰.۲٪${PDI}`,
    );
  });

  it("isolates positive signs inside parentheses and grouped amounts", () => {
    expect(isolateSignedNumbers("۸,۹۸۰.۰۸۲ میلیارد تومان (+۲.۶٪)")).toBe(
      `۸,۹۸۰.۰۸۲ میلیارد تومان (${LRI}+۲.۶٪${PDI})`,
    );
    expect(isolateSignedNumbers("تغییر: -۱,۲۳۴.۵ میلیارد")).toBe(
      `تغییر: ${LRI}-۱,۲۳۴.۵${PDI} میلیارد`,
    );
  });

  it("leaves list markers, ranges, dates and markdown table rows untouched", () => {
    const text = [
      "- نکته ۱",
      "دوره ۱۴۰۴-۱۴۰۵ و تاریخ ۱۴۰۵/۰۵",
      "| ردیف | تغییر |",
      "| ۱ | -۳.۵٪ |",
    ].join("\n");
    expect(isolateSignedNumbers(text)).toBe(text);
  });

  it("is idempotent", () => {
    const once = isolateSignedNumbers("فروش -۴۰.۲٪");
    expect(isolateSignedNumbers(once)).toBe(once);
  });
});
