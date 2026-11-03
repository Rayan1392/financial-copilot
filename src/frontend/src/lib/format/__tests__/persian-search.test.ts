import { describe, expect, it } from "vitest";
import { matchesPersianSearch, normalizePersianSearch } from "@/lib/format/persian-search";

describe("normalizePersianSearch (AC-11)", () => {
  it("unifies Arabic and Persian yeh and kaf", () => {
    expect(normalizePersianSearch("كگهر")).toBe(normalizePersianSearch("کگهر"));
    expect(normalizePersianSearch("فولادي")).toBe(normalizePersianSearch("فولادی"));
  });

  it("ignores ZWNJ and spaces", () => {
    expect(normalizePersianSearch("می‌شود")).toBe(normalizePersianSearch("می شود"));
    expect(normalizePersianSearch("می‌شود")).toBe(normalizePersianSearch("میشود"));
  });

  it("maps Persian, Arabic and Latin digits to the same value", () => {
    expect(normalizePersianSearch("۱۴۰۵")).toBe("1405");
    expect(normalizePersianSearch("١٤٠٥")).toBe("1405");
  });

  it("lower-cases Latin text", () => {
    expect(normalizePersianSearch("P/E")).toBe("p/e");
  });
});

describe("matchesPersianSearch", () => {
  it("finds titles regardless of spelling variants", () => {
    expect(matchesPersianSearch("روند فروش ماهانه کگهر در ۱۴۰۵", "كگهر 1405")).toBe(true);
    expect(matchesPersianSearch("گزارش فصلی فملی", "فولاد")).toBe(false);
  });

  it("matches everything for an empty query", () => {
    expect(matchesPersianSearch("هر عنوانی", "  ")).toBe(true);
  });
});
