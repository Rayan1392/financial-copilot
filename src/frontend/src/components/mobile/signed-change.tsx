import { formatPercent } from "@/lib/format/persian";
import { cn } from "@/lib/utils";

/**
 * A signed percentage with a ▲/▼ glyph in addition to color, isolated left-to-right so the sign stays in front
 * of the number in RTL text (design §1, AC-32). Unknown values render «–».
 */
export function SignedChange({
  value,
  className,
}: {
  value: number | null | undefined;
  className?: string;
}) {
  if (value == null || !Number.isFinite(value)) {
    return <span className={cn("text-muted-foreground", className)}>–</span>;
  }
  const tone = value > 0 ? "text-emerald" : value < 0 ? "text-rose" : "text-muted-foreground";
  const glyph = value > 0 ? "▲" : value < 0 ? "▼" : "";
  const direction = value > 0 ? "افزایش" : value < 0 ? "کاهش" : "بدون تغییر";
  return (
    <span className={cn("inline-flex items-center gap-1 tabular-nums", tone, className)}>
      {glyph && <span aria-hidden="true">{glyph}</span>}
      <bdi dir="ltr">{formatPercent(value)}</bdi>
      <span className="sr-only">{direction}</span>
    </span>
  );
}
