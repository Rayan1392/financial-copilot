import type { ReactNode } from "react";
import { IconTile } from "@/components/mobile/icon-tile";
import type { MobileIcon } from "@/components/mobile/icons";

/** A screen with nothing to show yet: a large icon, one sentence, and at most one action. */
export function EmptyState({
  icon,
  title,
  action,
}: {
  icon: MobileIcon;
  title: string;
  /** A button or link; the caller owns its behavior. Use `stateActionClass` for the look. */
  action?: ReactNode;
}) {
  return (
    <div className="flex flex-col items-center gap-4 px-6 py-14 text-center">
      <IconTile icon={icon} tone="neutral" size="lg" />
      <p className="text-m-body font-semibold text-foreground">{title}</p>
      {action}
    </div>
  );
}

/** Shared look of the single action in an empty or error state. */
export const stateActionClass =
  "flex min-h-12 items-center gap-2 rounded-xl bg-emerald px-5 text-m-title font-semibold text-primary-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40 focus-visible:ring-offset-2 focus-visible:ring-offset-background";
