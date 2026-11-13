import type { ReactNode } from "react";
import { IconTile, type IconTileTone } from "@/components/mobile/icon-tile";
import { CloseIcon, type MobileIcon } from "@/components/mobile/icons";
import {
  Drawer,
  DrawerClose,
  DrawerContent,
  DrawerDescription,
  DrawerTitle,
} from "@/components/ui/drawer";
import { cn } from "@/lib/utils";

interface BottomSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description?: string;
  children: ReactNode;
  /** Full-height sheets hold a search list (add symbol). */
  fullHeight?: boolean;
}

/**
 * Bottom sheet for contextual actions (Feature 153 design §2), built on the existing vaul drawer. It dismisses by
 * swipe, the scrim, Escape, or the close button, and returns focus to the trigger.
 */
export function BottomSheet({
  open,
  onOpenChange,
  title,
  description,
  children,
  fullHeight,
}: BottomSheetProps) {
  return (
    <Drawer open={open} onOpenChange={onOpenChange} shouldScaleBackground={false}>
      <DrawerContent
        dir="rtl"
        // Without a description, opt out explicitly instead of repeating the title to screen readers.
        {...(description ? {} : { "aria-describedby": undefined })}
        className={cn(
          "mx-auto max-w-xl border-hairline bg-background pb-[max(12px,env(safe-area-inset-bottom))] text-right",
          fullHeight ? "h-[92dvh]" : "max-h-[85dvh]",
        )}
      >
        <div className="flex items-start gap-2 px-4 pt-3">
          <div className="min-w-0 flex-1">
            <DrawerTitle className="text-m-head font-bold">{title}</DrawerTitle>
            {description && (
              <DrawerDescription className="mt-0.5 text-m-title">{description}</DrawerDescription>
            )}
          </div>
          <DrawerClose
            aria-label="بستن"
            className="flex size-11 shrink-0 items-center justify-center rounded-xl text-muted-foreground hover:bg-surface-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
          >
            <CloseIcon weight="bold" className="size-5" aria-hidden="true" />
          </DrawerClose>
        </div>
        <div className="min-h-0 flex-1 overflow-y-auto px-2 pb-2 scrollbar-thin">{children}</div>
      </DrawerContent>
    </Drawer>
  );
}

/** A full-width 56 px sheet row, with an optional icon tile at the start. */
export function SheetAction({
  children,
  onClick,
  tone = "default",
  disabled,
  trailing,
  icon,
  iconTone,
}: {
  children: ReactNode;
  onClick: () => void;
  tone?: "default" | "danger";
  disabled?: boolean;
  trailing?: ReactNode;
  icon?: MobileIcon;
  iconTone?: IconTileTone;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      className={cn(
        "flex min-h-14 w-full items-center gap-3 rounded-xl px-3 py-1.5 text-right text-m-body transition-colors active:bg-surface-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-emerald/40 disabled:opacity-50",
        tone === "danger" ? "mobile-danger-text" : "text-foreground",
      )}
    >
      {icon && (
        <IconTile icon={icon} tone={iconTone ?? (tone === "danger" ? "danger" : "neutral")} />
      )}
      <span className="min-w-0 flex-1">{children}</span>
      {trailing}
    </button>
  );
}
