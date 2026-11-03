import { describe, expect, it } from "vitest";
import { formatDisclosureDateOnly, formatDisclosurePagination, formatDisclosureReceiptDate, parseDisclosureDateOnly, serializeDisclosureDateOnly } from "../format/disclosure";

describe("published disclosure receipt formatting", () => {
  it("shows the Persian date and local time without a redundant Tehran suffix", () => {
    const formatted = formatDisclosureReceiptDate("2026-08-01T20:30:00Z");

    expect(formatted).not.toContain("تهران");
    expect(formatted).not.toBe("");
  });
});

describe("published disclosure pagination formatting", () => {
  it("shows the current and total page numbers using Persian digits", () => {
    expect(formatDisclosurePagination(1, 1796)).toBe("صفحه ۱ از ۱۷۹۶");
  });
});

describe("published disclosure Jalali date selection", () => {
  it("round trips Gregorian API dates through the Jalali calendar display", () => {
    const date = parseDisclosureDateOnly("2026-03-21");

    expect(date).toBeInstanceOf(Date);
    expect(serializeDisclosureDateOnly(date!)).toBe("2026-03-21");
    expect(formatDisclosureDateOnly("2026-03-21")).toContain("۱۴۰۵");
  });

  it("rejects malformed or impossible Gregorian API date values", () => {
    expect(parseDisclosureDateOnly("1405/01/01")).toBeUndefined();
    expect(parseDisclosureDateOnly("2026-02-31")).toBeUndefined();
    expect(formatDisclosureDateOnly("2026-02-31")).toBe("");
  });
});
