import { readdirSync, readFileSync, statSync } from "node:fs";
import { join, relative } from "node:path";
import { describe, expect, it } from "vitest";
import { FA_TERMINOLOGY } from "../fa-terminology";

// Vitest runs from the frontend root (jsdom gives import.meta.url a non-file scheme).
const SRC_ROOT = join(process.cwd(), "src");
const SKIP = new Set(["lib/copy/fa-terminology.ts", "lib/copy/__tests__/fa-terminology.test.ts"]);
const PERSIAN = /[؀-ۿ]/;
// String literals and JSX text on one line: "…", '…', `…` and >…<.
const TEXT_SEGMENTS = /"(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*'|`(?:[^`\\]|\\.)*`|>[^<>{}]+</g;

function sourceFiles(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name);
    if (statSync(path).isDirectory()) return name === "node_modules" ? [] : sourceFiles(path);
    return /\.(ts|tsx)$/.test(name) ? [path] : [];
  });
}

function isComment(line: string) {
  const trimmed = line.trim();
  return trimmed.startsWith("//") || trimmed.startsWith("*") || trimmed.startsWith("/*");
}

describe("Persian UI terminology", () => {
  it("uses one spelling per concept in user-facing text (see docs/ui-copy-glossary-fa.md)", () => {
    const violations: string[] = [];
    for (const file of sourceFiles(SRC_ROOT)) {
      const rel = relative(SRC_ROOT, file).replaceAll("\\", "/");
      if (SKIP.has(rel)) continue;
      readFileSync(file, "utf8")
        .split(/\r?\n/)
        .forEach((line, index) => {
          if (isComment(line)) return;
          for (const rule of FA_TERMINOLOGY) {
            for (const avoided of rule.avoid) {
              // A Latin name is only wrong inside Persian text; code identifiers and API names keep it.
              const hit = PERSIAN.test(avoided)
                ? line.includes(avoided)
                : (line.match(TEXT_SEGMENTS) ?? []).some(
                    (segment) => segment.includes(avoided) && PERSIAN.test(segment),
                  );
              if (hit) violations.push(`${rel}:${index + 1} «${avoided}» → «${rule.preferred}»`);
            }
          }
        });
    }
    expect(violations).toEqual([]);
  });
});
