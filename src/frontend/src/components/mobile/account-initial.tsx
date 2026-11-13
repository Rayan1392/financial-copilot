import { AccountIcon } from "@/components/mobile/icons";
import { cn } from "@/lib/utils";

interface AccountInitialProps {
  /** Official display name; without one a person icon is shown, never a «؟» that reads as a broken logo. */
  name: string | null;
  /** Size and text classes, for example `size-9 text-m-title`. */
  className?: string;
  iconClassName?: string;
}

/** Round account badge: the first letter of the display name, or a person icon when there is no name. */
export function AccountInitial({ name, className, iconClassName }: AccountInitialProps) {
  const initial = name ? Array.from(name)[0] : null;
  return (
    <span
      aria-hidden="true"
      data-account-initial={initial ? "letter" : "icon"}
      className={cn(
        "flex shrink-0 items-center justify-center rounded-full bg-emerald-soft font-bold text-emerald ring-1 ring-emerald/30",
        className,
      )}
    >
      {initial ?? <AccountIcon weight="duotone" className={cn("size-5", iconClassName)} />}
    </span>
  );
}
