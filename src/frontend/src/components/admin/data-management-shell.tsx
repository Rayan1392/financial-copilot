import { Link, useRouterState } from "@tanstack/react-router";
import {
  Archive,
  Activity,
  BarChart2,
  Database,
  LayoutGrid,
  GitCompare,
  ArrowLeft,
} from "lucide-react";
import { cn } from "@/lib/utils";

type NavItem = {
  label: string;
  to: string;
  icon: React.ComponentType<{ className?: string }>;
  badge?: string;
};

const navItems: NavItem[] = [
  { label: "نمای کلی", to: "/admin/data", icon: LayoutGrid },
  { label: "ورود آرشیو", to: "/admin/data/archive", icon: Archive },
  { label: "رابط فعلی نواوران", to: "/admin/data/noavaran", icon: BarChart2 },
  { label: "پل StockMarketDB", to: "/admin/data/stockmarket", icon: Database, badge: "پل" },
  { label: "پایش زنده", to: "/admin/data/monitor", icon: Activity },
  { label: "تطبیق داده‌ها", to: "/admin/data/reconciliation", icon: GitCompare },
];

export function DataManagementShell({ children }: { children: React.ReactNode }) {
  const routerState = useRouterState();
  const currentPath = routerState.location.pathname;

  return (
    <div dir="rtl" className="min-h-screen bg-background flex flex-col">
      <header className="border-b border-border px-6 py-3 flex items-center gap-4 shrink-0">
        <Link
          to="/admin"
          className="p-1.5 text-muted-foreground hover:text-foreground transition"
          aria-label="بازگشت به مدیریت"
        >
          <ArrowLeft className="size-4" />
        </Link>
        <span className="text-sm text-muted-foreground">مدیریت</span>
        <span className="text-muted-foreground">/</span>
        <span className="text-sm font-medium">عملیات داده</span>
      </header>

      <div className="flex flex-1 min-h-0">
        <nav className="w-56 shrink-0 border-r border-border bg-surface/40 py-4 px-3 flex flex-col gap-1">
          {navItems.map(({ label, to, icon: Icon, badge }) => {
            const isActive = currentPath === to || (to !== "/admin/data" && currentPath.startsWith(to));
            return (
              <Link
                key={to}
                to={to}
                className={cn(
                  "flex items-center gap-2.5 rounded-lg px-3 py-2 text-sm transition-colors",
                  isActive
                    ? "bg-primary/10 text-primary font-medium"
                    : "text-muted-foreground hover:text-foreground hover:bg-muted/50",
                )}
              >
                <Icon className="size-4 shrink-0" />
                <span className="flex-1">{label}</span>
                {badge && (
                  <span className="text-[10px] font-medium px-1.5 py-0.5 rounded bg-amber-500/15 text-amber-600">
                    {badge}
                  </span>
                )}
              </Link>
            );
          })}
        </nav>

        <main className="flex-1 overflow-auto p-6">{children}</main>
      </div>
    </div>
  );
}
