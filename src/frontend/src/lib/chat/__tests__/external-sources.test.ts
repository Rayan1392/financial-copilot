import { describe, expect, it } from "vitest";
import type { ExternalSource } from "@/lib/chat.functions";
import { safeSourceUrl, shortSourceTitle, withInlineSources } from "@/lib/chat/external-sources";

/** Change «sol-deep-analysis-web-search», amendment A3: web sources at the end of the section that used them. */
const source = (id: string, title: string, url: string): ExternalSource => ({
  id,
  title,
  url,
  publishedAt: "1405/08/01",
});
const W1 = source("W1", "خبر فولاد", "https://example.com/a");
const W2 = source("W2", "Reuters", "https://www.reuters.com/b");
const block = "\n\n**منابع**\n- [W1] خبر فولاد (1405/08/01): https://example.com/a";

describe("withInlineSources", () => {
  it("links each list item to its own sources and drops the plain block", () => {
    const text = `- فروش افزایش یافته است [W1].\n- قیمت جهانی کاهش یافته است [W2][W1].\n- بند بدون منبع.${block}`;
    expect(withInlineSources(text, [W1, W2])).toBe(
      "- فروش افزایش یافته است. — منبع: [خبر فولاد](https://example.com/a)\n" +
        "- قیمت جهانی کاهش یافته است. — منابع: [Reuters](https://www.reuters.com/b)، [خبر فولاد](https://example.com/a)\n" +
        "- بند بدون منبع.",
    );
  });

  it("puts the links once at the end of a multi-line paragraph", () => {
    const text = "جملهٔ اول [W1].\nجملهٔ دوم [W1].\n\nبند بعدی.";
    expect(withInlineSources(text, [W1])).toBe(
      "جملهٔ اول.\nجملهٔ دوم. — منبع: [خبر فولاد](https://example.com/a)\n\nبند بعدی.",
    );
  });

  it("reads Persian digits in markers", () => {
    expect(withInlineSources("قیمت کاهش یافته است [W۱].", [W1])).toBe(
      "قیمت کاهش یافته است. — منبع: [خبر فولاد](https://example.com/a)",
    );
  });

  it("never links an unknown marker or an unsafe URL, and removes the marker", () => {
    const text = "بند اول [W3].\n\nبند دوم [W2].";
    expect(withInlineSources(text, [W1, source("W2", "بد", "javascript:alert(1)")])).toBe(
      "بند اول.\n\nبند دوم.",
    );
  });

  it("puts the sources of a table under the table", () => {
    const text = "| قلم | مقدار |\n|---|---|\n| قیمت جهانی | کاهش [W1] |\n\nادامه.";
    expect(withInlineSources(text, [W1])).toBe(
      "| قلم | مقدار |\n|---|---|\n| قیمت جهانی | کاهش |\n\nمنبع: [خبر فولاد](https://example.com/a)\n\nادامه.",
    );
  });

  it("escapes the link text and the URL", () => {
    const text = "بند [W1].";
    expect(withInlineSources(text, [source("W1", "a [b] *c*", "https://example.com/x_(y)")])).toBe(
      "بند. — منبع: [a \\[b\\] \\*c\\*](https://example.com/x_%28y%29)",
    );
  });

  it("returns the text unchanged without sources", () => {
    const text = `بند [W1].${block}`;
    expect(withInlineSources(text, undefined)).toBe(text);
    expect(withInlineSources(text, [])).toBe(text);
  });
});

describe("shortSourceTitle", () => {
  it("keeps a short title", () => {
    expect(shortSourceTitle(W1, "https://example.com/a")).toBe("خبر فولاد");
  });

  it("cuts a long title at a word and adds an ellipsis", () => {
    const long = source(
      "W1",
      "گزارش فعالیت ماهانه شرکت فولاد مبارکه اصفهان برای دوره یک ماهه منتهی به آبان",
      "https://codal.ir/x",
    );
    const shown = shortSourceTitle(long, "https://codal.ir/x");
    expect(shown.endsWith("…")).toBe(true);
    expect(shown.length).toBeLessThanOrEqual(41);
    expect(long.title.startsWith(shown.slice(0, -1))).toBe(true);
  });

  it("uses the site name when there is no title", () => {
    expect(
      shortSourceTitle(source("W1", " ", "https://www.reuters.com/b"), "https://www.reuters.com/b"),
    ).toBe("reuters.com");
  });
});

describe("safeSourceUrl", () => {
  it("accepts http(s) only", () => {
    expect(safeSourceUrl("https://example.com/a")).toBe("https://example.com/a");
    expect(safeSourceUrl("javascript:alert(1)")).toBeNull();
    expect(safeSourceUrl("not a url")).toBeNull();
  });
});
