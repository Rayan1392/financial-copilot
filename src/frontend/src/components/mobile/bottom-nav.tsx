import { Link, useLocation } from "@tanstack/react-router";
import { Eye, History, LineChart, MessageSquare, type LucideIcon } from "lucide-react";
import { cn } from "@/lib/utils";

interface Tab {
  to: "/m" | "/m/history" | "/m/market" | "/m/watchlist";
  label: string;
  icon: LucideIcon;
}

/** DOM order equals visual right-to-left order, so گفتگو is the rightmost tab. */
const MOBILE_TABS: readonly Tab[] = [
  { to: "/m", label: "گفتگو", icon: MessageSquare },
  { to: "/m/history", label: "تاریخچه", icon: History },
  { to: "/m/market", label: "بازار", icon: LineChart },
  { to: "/m/watchlist", label: "دیده‌بان", icon: Eye },
];

function isActive(pathname: string, to: Tab["to"]): boolean {
  const path = pathname.replace(/\/+$/, "") || "/";
  return path === to;
}

/** Four-tab bottom navigation (Feature 153 design §2). Re-tapping the active tab scrolls the screen to the top. */
export function BottomNav({ onReselect }: { onReselect: () => void }) {
  const { pathname } = useLocation();
  return (
    <nav
      aria-label="ناوبری اصلی"
      className="shrink-0 border-t border-hairline bg-background/95 pb-[max(8px,env(safe-area-inset-bottom))] backdrop-blur"
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
                  "relative flex min-h-14 flex-col items-center justify-center gap-1 text-xs font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-emerald/40",
                  active ? "text-emerald" : "text-muted-foreground active:bg-surface-2",
                )}
              >
                {active && (
                  <span
                    aria-hidden="true"
                    className="absolute inset-x-6 top-0 h-[3px] rounded-b-full bg-emerald"
                  />
                )}
                <tab.icon className="size-5" aria-hidden="true" />
                {tab.label}
              </Link>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}
