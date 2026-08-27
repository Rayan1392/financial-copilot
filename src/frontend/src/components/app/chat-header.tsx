import { Link } from "@tanstack/react-router";
import { useEffect, useState } from "react";
import { FileSearch, Settings } from "lucide-react";
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

export function ChatHeader() {
  const [showAdmin, setShowAdmin] = useState(false);
  const [showMonthlyBackfill, setShowMonthlyBackfill] = useState(false);

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
    <header className="h-14 border-b border-hairline flex items-center justify-between gap-3 px-4 sm:px-6 flex-shrink-0">
      <div className="flex items-center gap-4">
        <span className="text-sm font-medium text-muted-foreground">تحلیل لحظه‌ای بازار</span>
        <div className="hidden sm:flex items-center gap-1.5 px-2 py-0.5 rounded-full bg-surface ring-1 ring-hairline">
          <div className="size-1.5 rounded-full bg-emerald animate-pulse" />
          <span className="text-[10px] text-emerald">متصل به نوآوران‌امین</span>
          <div className="size-1.5 rounded-full bg-emerald animate-pulse" />
          <span className="text-[10px] text-emerald">متصل به تحلیل‌اپ</span>

        </div>
      </div>
      <div className="flex items-center gap-2">
        {showMonthlyBackfill && (
          <Link
            to="/admin/getCompanyId"
            className="rounded-lg p-2 text-muted-foreground transition hover:bg-surface hover:text-foreground"
            aria-label="Monthly production and sales"
            title="Monthly production and sales"
          >
            <FileSearch className="size-4" />
          </Link>
        )}
        {showAdmin && (
          <Link
            to="/admin"
            className="rounded-lg p-2 text-muted-foreground transition hover:bg-surface hover:text-foreground"
            aria-label="پنل مدیریت"
            title="پنل مدیریت"
          >
            <Settings className="size-4" />
          </Link>
        )}
        <ThemeToggle />
      </div>
    </header>
  );
}
