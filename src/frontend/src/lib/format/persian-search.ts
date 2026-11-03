/**
 * Normalization for client-side Persian title search (Feature 153 T11, AC-11). Both the query and the titles go
 * through it, so these match each other:
 *   - Arabic ي ى / ك → Persian ی / ک;
 *   - Persian ۰–۹ and Arabic ٠–٩ digits → Latin digits;
 *   - ZWNJ/ZWJ, tatweel, diacritics, and all whitespace removed («می‌شود» = «می شود» = «میشود»);
 *   - Latin letters lower-cased.
 */
const CHARACTER_MAP: Record<string, string> = { ي: "ی", ى: "ی", ك: "ک", ة: "ه", ۀ: "ه" };
// ZWNJ, ZWJ, tatweel, Arabic diacritics, superscript alef, and whitespace (an alternation, not one character
// class, because joiners inside a class are easy to misread). Escaped strings keep the invisible characters
// readable; formatters rewrite escapes inside regex literals.
const IGNORED = new RegExp("\u200c|\u200d|\u0640|[\u064b-\u065f]|\u0670|\\s", "g");
const PERSIAN_DIGITS = new RegExp("[\u06f0-\u06f9]", "g");
const ARABIC_DIGITS = new RegExp("[\u0660-\u0669]", "g");

export function normalizePersianSearch(text: string): string {
  return text
    .replace(/[يىكةۀ]/g, (character) => CHARACTER_MAP[character] ?? character)
    .replace(PERSIAN_DIGITS, (digit) => String(digit.charCodeAt(0) - 0x06f0))
    .replace(ARABIC_DIGITS, (digit) => String(digit.charCodeAt(0) - 0x0660))
    .replace(IGNORED, "")
    .toLowerCase();
}

/** Every word of the query must appear in the title, in any order. An empty query matches everything. */
export function matchesPersianSearch(title: string, query: string): boolean {
  const haystack = normalizePersianSearch(title);
  return query
    .split(/\s+/)
    .map(normalizePersianSearch)
    .filter((word) => word.length > 0)
    .every((word) => haystack.includes(word));
}
