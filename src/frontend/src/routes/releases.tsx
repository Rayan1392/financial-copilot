import { createFileRoute, Link } from "@tanstack/react-router";
import { ArrowRight } from "lucide-react";
import { SapioMark } from "@/components/app/sapio-mark";
import {
  CHANGE_KIND_LABELS,
  formatReleaseDate,
  RELEASES,
} from "@/lib/release-notes/release-history";
import type { Release } from "@/lib/release-notes/changelog";

/** Public release history, built from the repository CHANGELOG.md (docs/release/release-process.md). */
export const Route = createFileRoute("/releases")({
  head: () => ({ meta: [{ title: "تاریخچهٔ نسخه‌ها — ساپیو" }] }),
  component: ReleasesPage,
});

function ReleasesPage() {
  return (
    <main
      dir="rtl"
      className="min-h-screen bg-background px-4 pt-[max(1.5rem,env(safe-area-inset-top))] pb-[max(2rem,env(safe-area-inset-bottom))] text-foreground sm:px-6"
    >
      <div className="mx-auto max-w-2xl">
        <Link
          to="/"
          className="inline-flex min-h-11 items-center gap-2 rounded-lg text-sm text-muted-foreground hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40"
        >
          <ArrowRight className="size-4" aria-hidden="true" />
          بازگشت به ساپیو
        </Link>
        <header className="mt-4 mb-8 flex items-center gap-3">
          <SapioMark className="size-10 shrink-0" />
          <div>
            <h1 className="text-xl font-bold">تاریخچهٔ نسخه‌ها</h1>
            <p className="mt-1 text-sm text-muted-foreground">
              امکانات تازه، بهبودها و رفع اشکال‌های هر نسخهٔ ساپیو.
            </p>
          </div>
        </header>
        {RELEASES.length === 0 ? (
          <p className="text-sm text-muted-foreground">هنوز نسخه‌ای منتشر نشده است.</p>
        ) : (
          <ol className="space-y-4">
            {RELEASES.map((release, index) => (
              <li key={release.version}>
                <ReleaseCard release={release} current={index === 0} />
              </li>
            ))}
          </ol>
        )}
      </div>
    </main>
  );
}

function ReleaseCard({ release, current }: { release: Release; current: boolean }) {
  const headingId = `release-${release.version.replaceAll(".", "-")}`;
  return (
    <article
      aria-labelledby={headingId}
      className="rounded-2xl bg-surface p-5 ring-1 ring-hairline"
    >
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
        <h2 id={headingId} className="text-base font-semibold">
          نسخهٔ <bdi dir="ltr">{release.version}</bdi>
        </h2>
        {current && (
          <span className="rounded-full bg-emerald-soft px-2.5 py-0.5 text-xs font-medium text-emerald">
            نسخهٔ جاری
          </span>
        )}
        <time dateTime={release.date} className="text-sm text-muted-foreground">
          {formatReleaseDate(release.date)}
        </time>
      </div>
      {release.summary && (
        <p className="mt-2 text-sm leading-7 text-muted-foreground">{release.summary}</p>
      )}
      {release.sections.map((section) => (
        <section key={section.kind} className="mt-4">
          <h3 className="text-sm font-semibold text-foreground">
            {CHANGE_KIND_LABELS[section.kind]}
          </h3>
          <ul className="mt-2 list-disc space-y-1.5 pe-1 ps-5 text-sm leading-7 text-foreground/90 marker:text-emerald">
            {section.items.map((item) => (
              <li key={item}>{item}</li>
            ))}
          </ul>
        </section>
      ))}
    </article>
  );
}
