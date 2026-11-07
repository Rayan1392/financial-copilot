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

/** Public address shown in copied and shared answers, so someone who receives one can find Sapio. */
export const SAPIO_SITE_URL = "https://sapioai.ir";

const SOURCE_SIGNATURE = `ساپیو | دستیار هوشمند بازار سرمایه\n${SAPIO_SITE_URL}`;

/**
 * Text an answer leaves the app with, by copy or share: the plain answer followed by Sapio's name and link as its
 * source. The link sits in the text itself because many share targets (Telegram, WhatsApp, notes) ignore the share
 * title. Empty answers stay empty.
 */
export function answerTextWithSource(markdown: string): string {
  const plain = answerPlainText(markdown);
  return plain ? `${plain}\n\n—\n${SOURCE_SIGNATURE}` : "";
}
