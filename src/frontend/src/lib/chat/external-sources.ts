import type { ExternalSource } from "@/lib/chat.functions";

/**
 * Change «sol-deep-analysis-web-search», amendment A3 (owner, 2026-11-20): the web sources of an analysis are shown at
 * the end of the paragraph or list item that used them, as short links. The server keeps writing the `[Wn]` markers
 * and a plain «منابع» block (Telegram and copies use them); on screen the markers become links and the block goes.
 * Only verified sources the backend returned, with http(s) URLs, become links; nothing else is ever a link.
 */
const SOURCES_TEXT_BLOCK = /\n*\*\*منابع\*\*\n[\s\S]*$/u;
const MARKER = /\s*\[W([0-9۰-۹٠-٩]{1,2})\]/gu;
const LIST_ITEM = /^\s*(?:[-*+]\s|\d+[.)]\s)/u;
const HEADING = /^\s*#{1,6}\s/u;
const TABLE_ROW = /^\s*\|/u;
const MAXIMUM_TITLE_LENGTH = 40;

/** Only absolute http(s) links are rendered; anything else from the model is not a link. */
export function safeSourceUrl(url: string): string | null {
  try {
    const parsed = new URL(url);
    return parsed.protocol === "http:" || parsed.protocol === "https:" ? parsed.href : null;
  } catch {
    return null;
  }
}

/** A short, readable link text: the title cut at a word near 40 characters, or the site name. */
export function shortSourceTitle(source: ExternalSource, href: string): string {
  const title = source.title.replace(/\s+/gu, " ").trim();
  if (title.length === 0) return new URL(href).hostname.replace(/^www\./u, "");
  if (title.length <= MAXIMUM_TITLE_LENGTH) return title;
  const cut = title.slice(0, MAXIMUM_TITLE_LENGTH);
  const lastSpace = cut.lastIndexOf(" ");
  return `${(lastSpace > MAXIMUM_TITLE_LENGTH / 2 ? cut.slice(0, lastSpace) : cut).trimEnd()}…`;
}

/**
 * The answer text for the screen: each paragraph or list item that carries `[Wn]` markers ends with
 * «— منبع: [title](url)» (or «منابع» for several), the markers are removed and the trailing plain block is dropped.
 * Without sources the text is returned unchanged.
 */
export function withInlineSources(
  text: string,
  sources: readonly ExternalSource[] | undefined,
): string {
  if (!sources || sources.length === 0) return text;
  const links = new Map<string, { href: string; label: string }>();
  for (const source of sources) {
    const href = safeSourceUrl(source.url);
    if (href && !links.has(source.id))
      links.set(source.id, { href, label: shortSourceTitle(source, href) });
  }

  const lines = text.replace(SOURCES_TEXT_BLOCK, "").trimEnd().split("\n");
  const output: string[] = [];
  let pending: string[] = [];
  lines.forEach((line, index) => {
    const ids = [...line.matchAll(MARKER)].map((match) => `W${asciiDigits(match[1])}`);
    pending.push(...ids);
    const shown = ids.length > 0 ? line.replace(MARKER, "") : line;
    const next = lines[index + 1];
    if (TABLE_ROW.test(line)) {
      output.push(shown);
      if (next === undefined || !TABLE_ROW.test(next)) {
        const suffix = sourceSuffix(pending, links);
        if (suffix) output.push("", suffix.replace(/^ — /u, ""));
        pending = [];
      }
      return;
    }
    if (endsBlock(line, next)) {
      output.push(shown + sourceSuffix(pending, links));
      pending = [];
      return;
    }
    output.push(shown);
  });
  return output.join("\n");
}

function endsBlock(line: string, next: string | undefined) {
  return (
    next === undefined ||
    next.trim() === "" ||
    HEADING.test(line) ||
    LIST_ITEM.test(next) ||
    HEADING.test(next) ||
    TABLE_ROW.test(next)
  );
}

function sourceSuffix(
  ids: readonly string[],
  links: ReadonlyMap<string, { href: string; label: string }>,
) {
  const seen = new Set<string>();
  const shown = ids.flatMap((id) => {
    const link = links.get(id);
    if (!link || seen.has(link.href)) return [];
    seen.add(link.href);
    return [`[${escapeLinkText(link.label)}](${escapeLinkUrl(link.href)})`];
  });
  if (shown.length === 0) return "";
  return ` — ${shown.length === 1 ? "منبع" : "منابع"}: ${shown.join("، ")}`;
}

function asciiDigits(value: string) {
  return value
    .replace(/[۰-۹]/gu, (digit) => String(digit.charCodeAt(0) - 0x06f0))
    .replace(/[٠-٩]/gu, (digit) => String(digit.charCodeAt(0) - 0x0660));
}

function escapeLinkText(label: string) {
  return label.replace(/[\\[\]*_`<>]/gu, (character) => `\\${character}`);
}

function escapeLinkUrl(href: string) {
  return href.replace(/\(/gu, "%28").replace(/\)/gu, "%29").replace(/ /gu, "%20");
}
