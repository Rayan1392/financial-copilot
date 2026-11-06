/**
 * Reads the repository CHANGELOG.md (Keep a Changelog 1.1.0 + SemVer 2.0.0) into release notes.
 * The format contract lives in docs/release/release-process.md; this parser is its one reader in code.
 *
 * Parsing is lenient so a malformed entry never breaks the app at runtime: what cannot be read is reported
 * in `problems` and skipped. lib/release-notes/__tests__/changelog.test.ts fails on any problem in the real
 * file, and ops/release/check-release-version.sh refuses a deploy whose tag does not match the top release.
 */

/** Keep a Changelog section names, in display order. */
export const CHANGE_KINDS = [
  "Added",
  "Changed",
  "Fixed",
  "Security",
  "Deprecated",
  "Removed",
] as const;
export type ChangeKind = (typeof CHANGE_KINDS)[number];

export interface ReleaseSection {
  kind: ChangeKind;
  items: string[];
}

export interface Release {
  /** SemVer without the `v` prefix, e.g. "1.0.0". The git tag is `v${version}`. */
  version: string;
  /** Release day, ISO `YYYY-MM-DD`. */
  date: string;
  /** Optional one-line description written under the version heading. */
  summary?: string;
  /** Non-empty sections in CHANGE_KINDS order. */
  sections: ReleaseSection[];
}

export interface ParsedChangelog {
  /** Published releases, newest first as written in the file. */
  releases: Release[];
  /** Sections under `## [Unreleased]`: merged but not yet tagged. Never shown as a release. */
  unreleased: ReleaseSection[];
  /** Lines the parser could not read, with 1-based line numbers. Empty for a valid file. */
  problems: string[];
}

const SEMVER = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/;
const RELEASE_HEADING = /^## \[([^\]]+)\] - (\d{4}-\d{2}-\d{2})$/;
const UNRELEASED_HEADING = /^## \[Unreleased\]$/;
const SECTION_HEADING = /^### (.+)$/;
const ITEM = /^[-*] (.+)$/;
const CONTINUATION = /^\s{2,}(\S.*)$/;
const LINK_REFERENCE = /^\[[^\]]+\]:\s/;

function isValidDate(iso: string): boolean {
  const date = new Date(`${iso}T00:00:00Z`);
  return !Number.isNaN(date.getTime()) && date.toISOString().startsWith(iso);
}

function isChangeKind(value: string): value is ChangeKind {
  return (CHANGE_KINDS as readonly string[]).includes(value);
}

interface Block {
  release?: { version: string; date: string; summary: string[] };
  sections: Map<ChangeKind, string[]>;
  /** Items go here; `null` under an unknown (already reported) section, whose items are skipped. */
  current?: string[] | null;
}

function orderedSections(sections: Map<ChangeKind, string[]>): ReleaseSection[] {
  return CHANGE_KINDS.flatMap((kind) => {
    const items = sections.get(kind) ?? [];
    return items.length > 0 ? [{ kind, items }] : [];
  });
}

export function parseChangelog(markdown: string): ParsedChangelog {
  const releases: Release[] = [];
  const problems: string[] = [];
  let unreleased: ReleaseSection[] = [];
  let block: Block | undefined;
  let inComment = false;

  const close = () => {
    if (!block) return;
    const sections = orderedSections(block.sections);
    if (block.release) {
      const summary = block.release.summary.join(" ").trim();
      releases.push({
        version: block.release.version,
        date: block.release.date,
        ...(summary ? { summary } : {}),
        sections,
      });
    } else {
      unreleased = sections;
    }
    block = undefined;
  };

  markdown.split(/\r?\n/).forEach((raw, index) => {
    const line = raw.trimEnd();
    const at = `line ${index + 1}`;

    if (inComment) {
      if (line.includes("-->")) inComment = false;
      return;
    }
    if (line.trimStart().startsWith("<!--")) {
      inComment = !line.includes("-->");
      return;
    }
    if (line.startsWith("# ") || LINK_REFERENCE.test(line)) return;

    if (line.startsWith("## ")) {
      close();
      if (UNRELEASED_HEADING.test(line)) {
        block = { sections: new Map() };
        return;
      }
      const heading = RELEASE_HEADING.exec(line);
      if (!heading) {
        problems.push(`${at}: expected "## [MAJOR.MINOR.PATCH] - YYYY-MM-DD", got "${line}"`);
        return;
      }
      const [, version, date] = heading;
      if (!SEMVER.test(version)) problems.push(`${at}: "${version}" is not MAJOR.MINOR.PATCH`);
      else if (!isValidDate(date)) problems.push(`${at}: "${date}" is not a real date`);
      else block = { release: { version, date, summary: [] }, sections: new Map() };
      return;
    }

    // Preamble before the first version heading, or the body of a heading that failed to parse.
    if (!block) return;
    if (line.trim() === "") return;

    const section = SECTION_HEADING.exec(line);
    if (section) {
      const kind = section[1].trim();
      if (!isChangeKind(kind)) {
        problems.push(`${at}: unknown section "${kind}" (use ${CHANGE_KINDS.join(", ")})`);
        block.current = null;
        return;
      }
      if (!block.sections.has(kind)) block.sections.set(kind, []);
      block.current = block.sections.get(kind);
      return;
    }

    if (block.current === null) return;

    const item = ITEM.exec(line);
    if (item) {
      if (!block.current) problems.push(`${at}: list item outside a ### section`);
      else block.current.push(item[1].trim());
      return;
    }

    const continuation = CONTINUATION.exec(raw);
    if (continuation && block.current && block.current.length > 0) {
      const last = block.current.length - 1;
      block.current[last] = `${block.current[last]} ${continuation[1].trim()}`;
      return;
    }

    if (block.release && block.sections.size === 0) {
      block.release.summary.push(line.trim());
      return;
    }
    problems.push(`${at}: text outside a list item: "${line.trim()}"`);
  });
  close();

  return { releases, unreleased, problems };
}

/** Negative when `a` is older than `b`. Both must be valid MAJOR.MINOR.PATCH. */
export function compareVersions(a: string, b: string): number {
  const pa = a.split(".").map(Number);
  const pb = b.split(".").map(Number);
  for (let i = 0; i < 3; i++) {
    if (pa[i] !== pb[i]) return pa[i] - pb[i];
  }
  return 0;
}
