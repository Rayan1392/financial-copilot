import { useState } from "react";
import { RefreshIcon } from "@/components/mobile/icons";
import { cn } from "@/lib/utils";

const MINIMUM_SPIN_MS = 700;

/** A 44 px refresh control that spins for at least 700 ms, as the desktop credit refresh does. */
export function RefreshButton({
  label,
  onRefresh,
  busy,
}: {
  label: string;
  onRefresh: () => Promise<unknown>;
  busy?: boolean;
}) {
  const [refreshing, setRefreshing] = useState(false);
  const spinning = refreshing || busy;
  return (
    <button
      type="button"
      aria-label={label}
      disabled={spinning}
      onClick={async () => {
        setRefreshing(true);
        await Promise.allSettled([
          onRefresh(),
          new Promise((resolve) => setTimeout(resolve, MINIMUM_SPIN_MS)),
        ]);
        setRefreshing(false);
      }}
      className="flex size-11 shrink-0 items-center justify-center rounded-xl text-muted-foreground active:bg-surface-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40 disabled:opacity-60"
    >
      <RefreshIcon className={cn("size-5", spinning && "animate-spin")} aria-hidden="true" />
    </button>
  );
}
