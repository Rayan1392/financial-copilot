import { Link, useLocation } from "@tanstack/react-router";
import {
  NavChatIcon,
  NavHistoryIcon,
  NavMarketIcon,
  NavWatchlistIcon,
  type MobileIcon,
} from "@/components/mobile/icons";
import { cn } from "@/lib/utils";

interface Tab {
  to: "/m" | "/m/history" | "/m/market" | "/m/watchlist";
  label: string;
  icon: MobileIcon;
}

/** DOM order equals visual right-to-left order, so گفتگو is the rightmost tab. */
const MOBILE_TABS: readonly Tab[] = [
  { to: "/m", label: "گفتگو", icon: NavChatIcon },
  { to: "/m/history", label: "تاریخچه", icon: NavHistoryIcon },
  { to: "/m/market", label: "بازار", icon: NavMarketIcon },
  { to: "/m/watchlist", label: "دیده‌بان", icon: NavWatchlistIcon },
];

function isActive(pathname: string, to: Tab["to"]): boolean {
  const path = pathname.replace(/\/+$/, "") || "/";
  return path === to;
}

/**
 * Four-tab bottom navigation. The active tab shows a filled icon in a tinted pill and a bold label, so it does
 * not depend on color alone. Re-tapping the active tab scrolls the screen to the top.
 */
export function BottomNav({ onReselect }: { onReselect: () => void }) {
  const { pathname } = useLocation();
  return (
    <nav
      aria-label="ناوبری اصلی"
      className="shrink-0 border-t border-hairline bg-background/95 pb-[max(6px,env(safe-area-inset-bottom))] backdrop-blur"
    >
      <ul className="mx-auto grid max-w-xl grid-cols-4 px-[env(safe-area-inset-left)]">
        {MOBILE_TABS.map((tab) => {
          const active = isActive(pathname, tab.to);
          return (
            <li key={tab.to}>
              <Link
                to={tab.to}
                // Exact matching: otherwise /m (گفتگو) would also count as active on every /m/* tab.
                activeOptions={{ exact: true, includeSearch: false }}
                aria-current={active ? "page" : undefined}
                onClick={(event) => {
                  if (!active) return;
                  event.preventDefault();
                  onReselect();
                }}
                className={cn(
                  "group flex min-h-14 flex-col items-center justify-center gap-0.5 pt-1.5 text-m-cap transition-colors focus-visible:outline-none",
                  active ? "font-bold text-emerald" : "font-medium text-muted-foreground",
                )}
              >
                <span
                  aria-hidden="true"
                  className={cn(
                    "flex h-8 w-16 items-center justify-center rounded-full transition-colors group-focus-visible:ring-2 group-focus-visible:ring-emerald/40",
                    active ? "bg-emerald-soft" : "group-active:bg-surface-2",
                  )}
                >
                  <tab.icon weight={active ? "fill" : "regular"} className="size-[26px]" />
                </span>
                {tab.label}
              </Link>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}
