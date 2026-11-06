/**
 * The product's Persian UI glossary: one spelling per concept, so the same idea never reads two ways on two
 * screens. This list is the single source; docs/ui-copy-glossary-fa.md explains it and
 * lib/copy/__tests__/fa-terminology.test.ts fails the build when an avoided form appears in user-facing text.
 *
 * Add a term when the same concept shows up spelled differently. Prefer the Academy of Persian Language and
 * Literature spelling, then the form already used most in the app.
 */
export interface TermRule {
  /** The one form to use in UI text. */
  preferred: string;
  /** Forms that must not appear in UI text. */
  avoid: readonly string[];
  /** Why this form, for reviewers. */
  note: string;
}

export const FA_TERMINOLOGY: readonly TermRule[] = [
  {
    preferred: "گذرواژه",
    avoid: ["رمز عبور"],
    note: "Already the dominant form (profile, reset and enrollment flows).",
  },
  {
    preferred: "تأیید",
    avoid: ["تایید"],
    note: "Standard spelling with hamza: تأیید، تأییدشده، تأیید نشده.",
  },
  {
    preferred: "جست‌وجو",
    avoid: ["جستجو"],
    note: "Standard spelling with a zero-width non-joiner (U+200C): جست‌وجو، جست‌وجوی.",
  },
  {
    preferred: "تحلیل‌اپ",
    avoid: ["TahlilApp"],
    note: "Inside Persian text the partner is named in Persian; Latin stays only in code and API names.",
  },
];
