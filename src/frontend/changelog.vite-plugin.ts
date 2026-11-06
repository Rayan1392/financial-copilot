import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import type { Plugin } from "vite";

/**
 * Exposes the repository CHANGELOG.md (the single source of version numbers and release notes, see
 * docs/release/release-process.md) as `virtual:release-changelog`, a module whose default export is the
 * Markdown text. A virtual module keeps the dev server's file allow-list at the frontend root, and a
 * missing CHANGELOG.md fails the build instead of shipping an app without its version.
 *
 * The frontend Docker build copies CHANGELOG.md to the same relative path (docker/frontend.Dockerfile).
 */
const MODULE_ID = "virtual:release-changelog";
const RESOLVED_ID = `\0${MODULE_ID}`;
const CHANGELOG_PATH = fileURLToPath(new URL("../../CHANGELOG.md", import.meta.url));

export function releaseChangelog(): Plugin {
  return {
    name: "sapio-release-changelog",
    resolveId(id) {
      return id === MODULE_ID ? RESOLVED_ID : undefined;
    },
    load(id) {
      if (id !== RESOLVED_ID) return undefined;
      this.addWatchFile(CHANGELOG_PATH);
      return `export default ${JSON.stringify(readFileSync(CHANGELOG_PATH, "utf8"))};`;
    },
  };
}
