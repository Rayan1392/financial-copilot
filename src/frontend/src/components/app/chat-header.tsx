import { Link } from "@tanstack/react-router";
import { useEffect, useState } from "react";
import { Bell, Eye, FileSearch, Home, Settings, Smartphone } from "lucide-react";
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

export function ChatHeader({ historyCollapsed }: { historyCollapsed: boolean }) {
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
    <header className="h-16 border-b border-hairline flex items-center justify-between gap-3 px-4 sm:px-6 flex-shrink-0">
      <div className="flex min-w-0 items-center gap-4">
        {historyCollapsed && (
          <Link
            to="/chat"
            className="flex min-h-11 shrink-0 items-center gap-2 rounded-xl px-2 text-foreground transition hover:bg-emerald-soft/50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40"
            aria-label="ساپیو - صفحه اصلی"
          >
            <SapioMark className="size-8 text-emerald" />
            <span className="hidden text-base font-bold sm:inline">ساپیو</span>
          </Link>
        )}
        <HeaderAccount />
        <span className="hidden min-w-0 truncate text-sm font-medium text-muted-foreground xl:inline">
          تحلیل بازار، ساده‌تر از همیشه
        </span>
        <div className="hidden sm:flex items-center gap-1.5 px-2 py-0.5 rounded-full bg-surface ring-1 ring-hairline">
          <div className="size-1.5 rounded-full bg-emerald animate-pulse" />
          <span className="text-[10px] text-emerald">متصل به نوآوران‌امین</span>
          <div className="size-1.5 rounded-full bg-emerald animate-pulse" />
          <span className="text-[10px] text-emerald">متصل به تحلیل‌اپ</span>

        </div>
      </div>
      <div className="flex shrink-0 items-center gap-1">
        {!historyCollapsed && (
          <Link
            to="/chat"
            className="flex min-h-11 shrink-0 items-center gap-2 rounded-xl px-2 text-foreground transition hover:bg-emerald-soft/50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40"
            aria-label="ساپیو - صفحه اصلی"
          >
            <SapioMark className="size-8 text-emerald" />
            <span className="hidden text-base font-bold sm:inline">ساپیو</span>
          </Link>
        )}
        <span aria-hidden="true" className="mx-1 hidden h-7 w-px bg-hairline sm:block" />
        <Link
          to="/chat"
          className="group flex size-10 items-center justify-center rounded-xl text-muted-foreground transition hover:bg-emerald-soft/60 hover:text-emerald focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40"
          aria-label="صفحه اصلی"
          title="صفحه اصلی"
        >
          <Home className="size-5 transition-transform group-hover:scale-105" />
        </Link>
        <Link
          to="/followed-symbols"
          className="group flex size-10 items-center justify-center rounded-xl text-muted-foreground transition hover:bg-emerald-soft/60 hover:text-emerald focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40"
          aria-label="دیده‌بان"
          title="دیده‌بان"
        >
          <Eye className="size-5 transition-transform group-hover:scale-105" />
        </Link>
        <Link
          to="/disclosures"
          className="group flex size-10 items-center justify-center rounded-xl text-muted-foreground transition hover:bg-emerald-soft/60 hover:text-emerald focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40"
          aria-label="اطلاعیه‌ها"
          title="اطلاعیه‌ها"
        >
          <Bell className="size-5 transition-transform group-hover:scale-105" />
        </Link>
        {showMonthlyBackfill && (
          <Link
            to="/getCompanyId"
            className="group flex size-10 items-center justify-center rounded-xl text-muted-foreground transition hover:bg-emerald-soft/60 hover:text-emerald focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40"
            aria-label="دریافت تولید و فروش"
            title="دریافت تولید و فروش"
          >
            <FileSearch className="size-5 transition-transform group-hover:scale-105" />
          </Link>
        )}
        {showAdmin && (
          <Link
            to="/admin"
            className="group flex size-10 items-center justify-center rounded-xl text-muted-foreground transition hover:bg-emerald-soft/60 hover:text-emerald focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40"
            aria-label="پنل مدیریت"
            title="پنل مدیریت"
          >
            <Settings className="size-5 transition-transform group-hover:rotate-12" />
          </Link>
        )}
        <button
          type="button"
          onClick={switchToMobile}
          className="group flex size-10 items-center justify-center rounded-xl text-muted-foreground transition hover:bg-emerald-soft/60 hover:text-emerald focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40"
          aria-label="نسخه موبایل"
          title="نسخه موبایل"
        >
          <Smartphone className="size-5 transition-transform group-hover:scale-105" />
        </button>
        <ThemeToggle />
      </div>
    </header>
  );
}
