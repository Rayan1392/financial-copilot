/**
 * The app's release history, read from the repository CHANGELOG.md at build time; changelog.vite-plugin.ts
 * inlines the file as `virtual:release-changelog`.
 */
import changelogMarkdown from "virtual:release-changelog";
import { parseChangelog, type ChangeKind, type Release } from "./changelog";

export const RELEASES: readonly Release[] = parseChangelog(changelogMarkdown).releases;

/** The newest published release, or undefined if the changelog has none yet. */
export const CURRENT_RELEASE: Release | undefined = RELEASES[0];

export const CHANGE_KIND_LABELS: Record<ChangeKind, string> = {
  Added: "امکانات تازه",
  Changed: "بهبودها",
  Fixed: "رفع اشکال",
  Security: "امنیت",
  Deprecated: "منسوخ‌شده",
  Removed: "حذف‌شده",
};

const RELEASE_DATE = new Intl.DateTimeFormat("fa-IR", {
  year: "numeric",
  month: "long",
  day: "numeric",
  timeZone: "UTC",
});

/** Jalali date for an ISO `YYYY-MM-DD` release day, e.g. «۱۵ آبان ۱۴۰۵». */
export function formatReleaseDate(isoDate: string): string {
  return RELEASE_DATE.format(new Date(`${isoDate}T00:00:00Z`));
}
