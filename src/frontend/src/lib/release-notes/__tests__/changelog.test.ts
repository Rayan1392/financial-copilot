import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { FA_TERMINOLOGY } from "@/lib/copy/fa-terminology";
import { compareVersions, parseChangelog } from "../changelog";
import { CURRENT_RELEASE, formatReleaseDate, RELEASES } from "../release-history";

describe("parseChangelog", () => {
  const sample = [
    "# Changelog",
    "",
    "Preamble text is ignored.",
    "",
    "## [Unreleased]",
    "",
    "### Added",
    "",
    "- merged, not tagged",
    "",
    "## [1.1.0] - 2026-11-10",
    "",
    "A short summary.",
    "",
    "### Fixed",
    "",
    "- a fix that wraps",
    "  onto a second line",
    "",
    "### Added",
    "",
    "- a feature",
    "",
    "<!-- internal note",
    "spanning lines -->",
    "## [1.0.0] - 2026-11-06",
    "",
    "### Changed",
    "",
    "- a change",
    "",
    "[1.1.0]: https://example.test/compare/v1.0.0...v1.1.0",
  ].join("\n");

  it("reads releases newest first with summary and sections in display order", () => {
    const parsed = parseChangelog(sample);
    expect(parsed.problems).toEqual([]);
    expect(parsed.releases).toEqual([
      {
        version: "1.1.0",
        date: "2026-11-10",
        summary: "A short summary.",
        sections: [
          { kind: "Added", items: ["a feature"] },
          { kind: "Fixed", items: ["a fix that wraps onto a second line"] },
        ],
      },
      {
        version: "1.0.0",
        date: "2026-11-06",
        sections: [{ kind: "Changed", items: ["a change"] }],
      },
    ]);
  });

  it("keeps Unreleased entries out of the releases", () => {
    const parsed = parseChangelog(sample);
    expect(parsed.unreleased).toEqual([{ kind: "Added", items: ["merged, not tagged"] }]);
    expect(parsed.releases.map((r) => r.version)).not.toContain("Unreleased");
  });

  it("reports malformed headings, versions, dates and sections without throwing", () => {
    const parsed = parseChangelog(
      [
        "## 1.2.0",
        "- skipped with its heading",
        "## [v1.2.0] - 2026-11-06",
        "## [1.2.0] - 2026-02-30",
        "## [1.1.0] - 2026-11-06",
        "### Bugfixes",
        "- under an unknown section",
        "## [1.0.0] - 2026-11-05",
        "- outside a section",
      ].join("\n"),
    );
    expect(parsed.problems).toHaveLength(5);
    expect(parsed.problems[0]).toMatch(/^line 1: expected/);
    expect(parsed.problems[1]).toMatch(/"v1.2.0" is not MAJOR.MINOR.PATCH/);
    expect(parsed.problems[2]).toMatch(/"2026-02-30" is not a real date/);
    expect(parsed.problems[3]).toMatch(/unknown section "Bugfixes"/);
    expect(parsed.problems[4]).toMatch(/line 9: list item outside a ### section/);
    expect(parsed.releases.map((r) => r.version)).toEqual(["1.1.0", "1.0.0"]);
  });
});

describe("compareVersions", () => {
  it("orders by major, minor, then patch numerically", () => {
    expect(compareVersions("1.10.0", "1.9.9")).toBeGreaterThan(0);
    expect(compareVersions("0.3.5", "1.0.0")).toBeLessThan(0);
    expect(compareVersions("2.0.1", "2.0.1")).toBe(0);
  });
});

// The repository CHANGELOG.md is the release contract (docs/release/release-process.md); it must stay valid.
describe("CHANGELOG.md", () => {
  const markdown = readFileSync(join(process.cwd(), "..", "..", "CHANGELOG.md"), "utf8");
  const parsed = parseChangelog(markdown);

  it("parses without problems", () => {
    expect(parsed.problems).toEqual([]);
  });

  it("has an Unreleased section before the first release", () => {
    expect(markdown.indexOf("## [Unreleased]")).toBeGreaterThan(-1);
    expect(markdown.indexOf("## [Unreleased]")).toBeLessThan(markdown.search(/^## \[\d/m));
  });

  it("lists strictly descending versions with non-increasing dates", () => {
    for (let i = 1; i < parsed.releases.length; i++) {
      const newer = parsed.releases[i - 1];
      const older = parsed.releases[i];
      expect(
        compareVersions(newer.version, older.version),
        `${newer.version} > ${older.version}`,
      ).toBeGreaterThan(0);
      expect(newer.date >= older.date, `${newer.version} dated on/after ${older.version}`).toBe(
        true,
      );
    }
  });

  it("gives every release at least one entry", () => {
    for (const release of parsed.releases) {
      expect(release.sections.length, release.version).toBeGreaterThan(0);
    }
  });

  it("has a compare link for every release", () => {
    for (const release of parsed.releases) {
      expect(markdown, release.version).toMatch(
        new RegExp(`^\\[${release.version.replaceAll(".", "\\.")}\\]: https://`, "m"),
      );
    }
  });

  it("follows the Persian UI glossary (entries are shown in the app)", () => {
    const shown = [
      ...parsed.releases.flatMap((r) => [r.summary ?? "", ...r.sections.flatMap((s) => s.items)]),
      ...parsed.unreleased.flatMap((s) => s.items),
    ];
    const violations = FA_TERMINOLOGY.flatMap((rule) =>
      rule.avoid.flatMap((avoided) =>
        shown
          .filter((text) => text.includes(avoided))
          .map((text) => `«${avoided}» → «${rule.preferred}»: ${text}`),
      ),
    );
    expect(violations).toEqual([]);
  });

  it("is what the app bundles", () => {
    expect(RELEASES).toEqual(parsed.releases);
    expect(CURRENT_RELEASE?.version).toBe(parsed.releases[0]?.version);
  });
});

describe("formatReleaseDate", () => {
  it("shows the release day in the Jalali calendar", () => {
    expect(formatReleaseDate("2026-11-06")).toBe("۱۵ آبان ۱۴۰۵");
  });
});
