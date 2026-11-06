import { Link } from "@tanstack/react-router";
import { CURRENT_RELEASE } from "@/lib/release-notes/release-history";
import { cn } from "@/lib/utils";

/** «نسخهٔ 1.0.0 · تاریخچهٔ نسخه‌ها», linking to /releases. Renders nothing before the first release. */
export function ReleaseVersionLink({ className }: { className?: string }) {
  if (!CURRENT_RELEASE) return null;
  return (
    <Link
      to="/releases"
      className={cn(
        "inline-block rounded-md py-1.5 text-xs leading-5 text-muted-foreground transition hover:text-foreground hover:underline focus-visible:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40",
        className,
      )}
    >
      {/* Real spaces, not flex gaps, so screen readers hear «نسخهٔ 1.0.0 تاریخچهٔ نسخه‌ها». */}
      نسخهٔ <bdi dir="ltr">{CURRENT_RELEASE.version}</bdi> <span aria-hidden="true">·</span>{" "}
      تاریخچهٔ نسخه‌ها
    </Link>
  );
}
