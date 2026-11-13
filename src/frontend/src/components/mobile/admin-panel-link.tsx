import { Link } from "@tanstack/react-router";
import { useEffect, useState } from "react";
import { IconTile } from "@/components/mobile/icon-tile";
import { AdminIcon, ChevronIcon } from "@/components/mobile/icons";
import { canAccessAdmin } from "@/integrations/financial-copilot/admin-permissions";
import {
  getStoredAuthenticatedUser,
  subscribeToAuthChanges,
} from "@/integrations/financial-copilot/auth";

/**
 * Entry to the admin panel as a row of the account screen's list, rendered only for users holding an admin read
 * permission (same rule as the desktop header). It renders its own list item, so the list has no empty row for
 * other users. The check is a UI convenience; `/admin` and its APIs enforce access on their own.
 */
export function AdminPanelLink({ className }: { className?: string }) {
  const [visible, setVisible] = useState(false);

  useEffect(() => {
    const update = () => setVisible(canAccessAdmin(getStoredAuthenticatedUser()));
    update();
    return subscribeToAuthChanges(update);
  }, []);

  if (!visible) return null;
  return (
    <li>
      <Link to="/admin" className={className}>
        <IconTile icon={AdminIcon} tone="neutral" />
        <span className="flex-1">پنل مدیریت</span>
        <ChevronIcon className="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
      </Link>
    </li>
  );
}
