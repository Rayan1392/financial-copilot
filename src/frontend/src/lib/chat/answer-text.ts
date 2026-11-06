/**
 * Plain text of an assistant answer for copying and sharing: markdown emphasis and heading markers are removed,
 * table rows and line breaks are kept, and bidi isolate controls added for display are stripped so pasted text
 * carries no invisible characters.
 */
export function answerPlainText(markdown: string): string {
  return markdown
    .replace(/[⁦-⁩‎‏]/gu, "")
    .split("\n")
    .map((line) =>
      line
        .replace(/^\s{0,3}#{1,6}\s+/u, "")
        .replace(/\*\*(.+?)\*\*/gu, "$1")
        .replace(/__(.+?)__/gu, "$1")
        .replace(/`([^`]+)`/gu, "$1")
        .trimEnd(),
    )
    .join("\n")
    .replace(/\n{3,}/gu, "\n\n")
    .trim();
}
