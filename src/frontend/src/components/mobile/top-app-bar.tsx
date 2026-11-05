import { Link, useRouter } from "@tanstack/react-router";
import { ArrowRight, Settings, SquarePen, UserRound } from "lucide-react";
import { useEffect, useState, type ReactNode } from "react";
import { canAccessAdmin } from "@/integrations/financial-copilot/admin-permissions";
import {
  getStoredAuthenticatedUser,
  subscribeToAuthChanges,
} from "@/integrations/financial-copilot/auth";
import { officialDisplayName } from "@/lib/profile-display-name";
import { useOwnProfile } from "@/lib/workspace/queries";

const ICON_BUTTON =
  "flex size-11 shrink-0 items-center justify-center rounded-xl text-muted-foreground transition-colors hover:text-foreground active:bg-surface-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40";

function AccountAvatar() {
  const { data: profile } = useOwnProfile();
  const name = officialDisplayName(profile?.displayName ?? null);
  return (
    <Link to="/m/account" aria-label="حساب کاربری" className={ICON_BUTTON}>
      {name ? (
        <span
          aria-hidden="true"
          className="flex size-8 items-center justify-center rounded-full bg-emerald-soft text-sm font-semibold text-emerald ring-1 ring-emerald/30"
        >
          {Array.from(name)[0]}
        </span>
      ) : (
        <UserRound className="size-5" aria-hidden="true" />
      )}
    </Link>
  );
}

function BackButton({ fallback }: { fallback: string }) {
  const router = useRouter();
  return (
    <button
      type="button"
      aria-label="بازگشت"
      className={ICON_BUTTON}
      onClick={() => {
        if (router.history.canGoBack()) router.history.back();
        else void router.navigate({ href: fallback, replace: true });
      }}
    >
      <ArrowRight className="size-5" aria-hidden="true" />
    </button>
  );
}

export function NewChatButton() {
  return (
    <Link to="/m" state={{ newChat: true }} aria-label="گفتگوی جدید" className={ICON_BUTTON}>
      <SquarePen className="size-5" aria-hidden="true" />
    </Link>
  );
}

/**
 * Entry to the admin panel, rendered only for users holding an admin read permission (same rule as the desktop
 * header). The check is a UI convenience; `/admin` and its APIs enforce access on their own.
 */
export function AdminSettingsButton() {
  const [visible, setVisible] = useState(false);

  useEffect(() => {
    const update = () => setVisible(canAccessAdmin(getStoredAuthenticatedUser()));
    update();
    return subscribeToAuthChanges(update);
  }, []);

  if (!visible) return null;
  return (
    <Link to="/admin" aria-label="پنل مدیریت" className={ICON_BUTTON}>
      <Settings className="size-5" aria-hidden="true" />
    </Link>
  );
}

export interface TopAppBarProps {
  title: string;
  /** Tab screens show the account avatar; pushed screens show a back button with this fallback target. */
  backFallback?: string;
  /** Extra actions before «گفتگوی جدید» (for example a ⋯ menu). */
  actions?: ReactNode;
  hideNewChat?: boolean;
}

/** Top app bar (Feature 153 design §2): start = avatar or back, center = title, end = actions + new chat. */
export function TopAppBar({ title, backFallback, actions, hideNewChat }: TopAppBarProps) {
  return (
    <header className="shrink-0 border-b border-hairline bg-background/95 pt-[env(safe-area-inset-top)] backdrop-blur">
      <div className="flex h-14 items-center gap-1 px-[max(4px,env(safe-area-inset-left))]">
        {backFallback ? <BackButton fallback={backFallback} /> : <AccountAvatar />}
        <h1 className="min-w-0 flex-1 truncate px-1 text-lg font-bold text-foreground">{title}</h1>
        {actions}
        {!hideNewChat && <NewChatButton />}
      </div>
    </header>
  );
}
