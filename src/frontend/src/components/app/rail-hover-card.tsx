import type { ReactNode } from "react";

/**
 * Hover/focus card for an icon in a collapsed side rail. The trigger and the card share a
 * `group/rail` wrapper, so the card shows on pointer hover and on keyboard focus without JS.
 * `opensTo` is the screen side the card appears on: the right rail opens to the left and the
 * left rail opens to the right, so the card always points toward the content area.
 */
export function RailHoverCard({
  id,
  opensTo,
  align = "center",
  icon,
  title,
  subtitle,
  badge,
  trigger,
  children,
  footer,
}: {
  id: string;
  opensTo: "left" | "right";
  /** "start" pins the card's top to the trigger — use for icons near the top of the rail so the card never clips above the viewport. */
  align?: "center" | "start";
  icon: ReactNode;
  title: string;
  subtitle?: string;
  badge?: ReactNode;
  trigger: ReactNode;
  children: ReactNode;
  footer?: ReactNode;
}) {
  const placement = [
    opensTo === "left" ? "right-full mr-3 translate-x-2" : "left-full ml-3 -translate-x-2",
    align === "start" ? "-top-1" : "top-1/2 -translate-y-1/2",
  ].join(" ");
  return (
    <div className="group/rail relative">
      {trigger}
      <div
        id={id}
        role="tooltip"
        dir="rtl"
        className={`invisible pointer-events-none absolute z-50 w-64 rounded-2xl border border-hairline bg-surface p-4 text-right font-sans text-foreground opacity-0 shadow-2xl ring-1 ring-black/5 transition-all duration-150 group-hover/rail:visible group-hover/rail:translate-x-0 group-hover/rail:opacity-100 group-focus-within/rail:visible group-focus-within/rail:translate-x-0 group-focus-within/rail:opacity-100 ${placement}`}
      >
        <div className="mb-3 flex items-center gap-2.5">
          <span className="flex size-9 shrink-0 items-center justify-center rounded-xl bg-emerald-soft/70 text-emerald">
            {icon}
          </span>
          <div className="min-w-0 flex-1">
            <p className="text-sm font-bold">{title}</p>
            {subtitle && <p className="mt-0.5 text-[11px] text-muted-foreground">{subtitle}</p>}
          </div>
          {badge}
        </div>
        {children}
        {footer && <div className="mt-3 text-[10px] text-muted-foreground">{footer}</div>}
      </div>
    </div>
  );
}

/** Signed-percent pill for rail cards: emerald when up, rose when down, muted when unknown. */
export function ChangePill({ value, label }: { value?: number | null; label: string }) {
  const tone =
    value == null
      ? "bg-muted text-muted-foreground"
      : value >= 0
        ? "bg-emerald-soft/70 text-emerald"
        : "bg-rose/10 text-rose";
  return (
    <span
      dir="ltr"
      className={`shrink-0 rounded-full px-2 py-0.5 text-[11px] font-semibold tabular-nums ${tone}`}
    >
      {label}
    </span>
  );
}
