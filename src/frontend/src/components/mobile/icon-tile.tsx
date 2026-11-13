import type { MobileIcon } from "@/components/mobile/icons";
import { cn } from "@/lib/utils";

export type IconTileTone = "emerald" | "gold" | "neutral" | "danger";

const TONES: Record<IconTileTone, string> = {
  emerald: "bg-emerald-soft text-emerald",
  gold: "bg-gold/15 text-gold",
  neutral: "bg-surface-2 text-muted-foreground",
  danger: "bg-rose/12 mobile-danger-text",
};

/** A duotone icon on a tinted square: the phone workspace's icon treatment for cards and list rows. */
export function IconTile({
  icon: Icon,
  tone = "emerald",
  size = "md",
  className,
}: {
  icon: MobileIcon;
  tone?: IconTileTone;
  size?: "md" | "lg";
  className?: string;
}) {
  return (
    <span
      aria-hidden="true"
      className={cn(
        "flex shrink-0 items-center justify-center",
        size === "lg" ? "size-14 rounded-2xl" : "size-10 rounded-xl",
        TONES[tone],
        className,
      )}
    >
      <Icon weight="duotone" className={size === "lg" ? "size-8" : "size-[22px]"} />
    </span>
  );
}
