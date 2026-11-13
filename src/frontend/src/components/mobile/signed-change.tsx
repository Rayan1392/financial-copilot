import { formatPercent } from "@/lib/format/persian";
import { cn } from "@/lib/utils";

/**
 * A signed percentage with a ▲/▼ glyph in addition to color, isolated left-to-right so the sign stays in front
 * of the number in RTL text (design §1, AC-32). Unknown values render «–».
 *
 * `variant="badge"` puts it in a tinted pill for list rows, where the change is the figure users scan for.
 */
export function SignedChange({
  value,
  className,
  variant = "text",
}: {
  value: number | null | undefined;
  className?: string;
  variant?: "text" | "badge";
}) {
  const badge = variant === "badge";
  if (value == null || !Number.isFinite(value)) {
    return (
      <span
        className={cn(
          "text-muted-foreground",
          badge && "rounded-full bg-surface-2 px-2.5 py-0.5",
          className,
        )}
      >
        –
      </span>
    );
  }
  const tone =
    value > 0 ? "text-emerald" : value < 0 ? "mobile-danger-text" : "text-muted-foreground";
  const surface = value > 0 ? "bg-emerald-soft" : value < 0 ? "bg-rose/12" : "bg-surface-2";
  const glyph = value > 0 ? "▲" : value < 0 ? "▼" : "";
  const direction = value > 0 ? "افزایش" : value < 0 ? "کاهش" : "بدون تغییر";
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1 tabular-nums",
        tone,
        badge && "rounded-full px-2.5 py-0.5 font-semibold",
        badge && surface,
        className,
      )}
    >
      {glyph && <span aria-hidden="true">{glyph}</span>}
      <bdi dir="ltr">{formatPercent(value)}</bdi>
      <span className="sr-only">{direction}</span>
    </span>
  );
}
