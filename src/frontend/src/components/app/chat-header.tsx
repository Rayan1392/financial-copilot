import { Link, type LinkProps } from "@tanstack/react-router";
import { useEffect, useState, type ReactNode } from "react";
import { Eye, FileSearch, FileText, Home, Settings, Smartphone } from "lucide-react";
import { HeaderAccount } from "@/components/app/header-account";
import { SapioMark } from "@/components/app/sapio-mark";
import { ThemeToggle } from "@/components/app/theme-toggle";
import {
  adminPermissions,
  canAccessAdmin,
  hasPermission,
} from "@/integrations/financial-copilot/admin-permissions";
import {
  getStoredAuthenticatedUser,
  subscribeToAuthChanges,
} from "@/integrations/financial-copilot/auth";
import { useExperienceSwitch } from "@/lib/experience/use-experience-switch";

const NAV_ITEM_BASE =
  "group flex h-10 min-w-10 items-center justify-center gap-2 rounded-xl px-2.5 text-sm font-medium transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40";
const NAV_ITEM_IDLE = "text-muted-foreground hover:bg-emerald-soft/60 hover:text-emerald";
const NAV_ITEM_ACTIVE = "bg-emerald-soft/70 text-emerald";
const ICON_CLASS = "size-5 shrink-0 transition-transform group-hover:scale-105";

/**
 * One header destination. The visible label and the accessible name are the same text, so the
 * spoken name always matches what is on screen. The label shows when the header itself is at least
 * 48rem wide (a container query, not the viewport): with both side panels open a 1024px viewport
 * leaves the header only ~416px. TanStack sets aria-current="page" on the active link, and
 * activeProps gives it the highlighted style.
 */
function HeaderNavLink({
  to,
  label,
  icon,
  showLabel = false,
  exact = false,
}: {
  to: LinkProps["to"];
  label: string;
  icon: ReactNode;
  showLabel?: boolean;
  exact?: boolean;
}) {
  return (
    <Link
      to={to}
      activeOptions={{ exact, includeSearch: false }}
      className={NAV_ITEM_BASE}
      inactiveProps={{ className: NAV_ITEM_IDLE }}
      activeProps={{ className: NAV_ITEM_ACTIVE }}
      aria-label={label}
      title={label}
    >
      {icon}
      {showLabel && <span className="hidden whitespace-nowrap @3xl:inline">{label}</span>}
    </Link>
  );
}

/**
 * Desktop top bar. Where the conversation sidebar is shown (md and up inside the app layout) the
 * brand lives only in the sidebar; the header shows it only where no sidebar exists.
 */
export function ChatHeader({ withSidebar = false }: { withSidebar?: boolean }) {
  const [showAdmin, setShowAdmin] = useState(false);
  const [showMonthlyBackfill, setShowMonthlyBackfill] = useState(false);
  const { switchToMobile } = useExperienceSwitch();

  useEffect(() => {
    const updateAccess = () => {
      const user = getStoredAuthenticatedUser();
      setShowAdmin(canAccessAdmin(user));
      setShowMonthlyBackfill(
        hasPermission(user, adminPermissions.noavaranMonthlyBackfillExecute) ||
          hasPermission(user, adminPermissions.dataSyncManage),
      );
    };
    updateAccess();
    return subscribeToAuthChanges(updateAccess);
  }, []);

  return (
    <header className="@container h-16 border-b border-hairline flex items-center justify-between gap-3 px-4 sm:px-6 flex-shrink-0">
      <div className="flex min-w-0 items-center gap-4">
        <HeaderAccount />
        <Link
          to="/chat"
          data-testid="header-brand"
          className={`min-h-11 shrink-0 items-center gap-2 rounded-xl px-2 text-foreground transition hover:bg-emerald-soft/50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40 ${withSidebar ? "flex md:hidden" : "flex"}`}
          aria-label="ساپیو"
        >
          <SapioMark className="size-8 text-emerald" />
          <span className="hidden text-base font-bold sm:inline">ساپیو</span>
        </Link>
      </div>
      <div className="flex shrink-0 items-center gap-1">
        <nav aria-label="ناوبری اصلی" className="flex items-center gap-1">
          <HeaderNavLink
            to="/chat"
            exact
            label="خانه"
            showLabel
            icon={<Home className={ICON_CLASS} />}
          />
          <HeaderNavLink
            to="/followed-symbols"
            label="دیده‌بان"
            showLabel
            icon={<Eye className={ICON_CLASS} />}
          />
          <HeaderNavLink
            to="/disclosures"
            label="اطلاعیه‌های کدال"
            showLabel
            icon={<FileText className={ICON_CLASS} />}
          />
          {showMonthlyBackfill && (
            <HeaderNavLink
              to="/getCompanyId"
              label="دریافت تولید و فروش"
              icon={<FileSearch className={ICON_CLASS} />}
            />
          )}
          {showAdmin && (
            <HeaderNavLink
              to="/admin"
              label="پنل مدیریت"
              icon={
                <Settings className="size-5 shrink-0 transition-transform group-hover:rotate-12" />
              }
            />
          )}
        </nav>
        <span aria-hidden="true" className="mx-1 hidden h-7 w-px bg-hairline sm:block" />
        <button
          type="button"
          onClick={switchToMobile}
          className={`${NAV_ITEM_BASE} ${NAV_ITEM_IDLE}`}
          aria-label="نسخه موبایل"
          title="نسخه موبایل"
        >
          <Smartphone className={ICON_CLASS} />
        </button>
        <ThemeToggle />
      </div>
    </header>
  );
}
