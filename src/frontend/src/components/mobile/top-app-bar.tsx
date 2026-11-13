import { Link, useRouter } from "@tanstack/react-router";
import type { ReactNode } from "react";
import { AccountInitial } from "@/components/mobile/account-initial";
import { CreditIcon, NewChatIcon, SendIcon } from "@/components/mobile/icons";
import { computeCreditProgress } from "@/lib/credit-progress";
import { formatNumber } from "@/lib/format/persian";
import { officialDisplayName } from "@/lib/profile-display-name";
import { useOwnProfile, useUsage } from "@/lib/workspace/queries";

/** A 44 px icon button in the app bar; exported so screens style their own bar actions the same way. */
export const APP_BAR_ICON_BUTTON =
  "flex size-11 shrink-0 items-center justify-center rounded-xl text-muted-foreground transition-colors hover:text-foreground active:bg-surface-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40";

function AccountAvatar() {
  const { data: profile } = useOwnProfile();
  const name = officialDisplayName(profile?.displayName ?? null);
  return (
    <Link to="/m/account" aria-label="حساب کاربری" className={APP_BAR_ICON_BUTTON}>
      <AccountInitial name={name} className="size-9 text-m-title" />
    </Link>
  );
}

function BackButton({ fallback }: { fallback: string }) {
  const router = useRouter();
  return (
    <button
      type="button"
      aria-label="بازگشت"
      className={APP_BAR_ICON_BUTTON}
      onClick={() => {
        if (router.history.canGoBack()) router.history.back();
        else void router.navigate({ href: fallback, replace: true });
      }}
    >
      {/* Back points right in RTL: the up arrow turned a quarter. */}
      <SendIcon weight="bold" className="size-6 rotate-90" aria-hidden="true" />
    </button>
  );
}

export function NewChatButton() {
  return (
    <Link
      to="/m"
      state={{ newChat: true }}
      aria-label="گفتگوی جدید"
      className={APP_BAR_ICON_BUTTON}
    >
      <NewChatIcon className="size-6" aria-hidden="true" />
    </Link>
  );
}

/** Remaining AI credit at a glance on every tab; opens the account screen with plan and purchase guidance. */
function CreditChip() {
  const { data } = useUsage();
  if (!data) return null;
  const unlimited = data.creditQuotaMode === "Unlimited";
  const credit = unlimited
    ? null
    : computeCreditProgress(data.availableSpendingCapacity, data.planIncludedCredits);
  const label = unlimited ? "نامحدود" : formatNumber(credit?.remaining ?? 0);
  const tone =
    credit?.tone === "danger"
      ? "mobile-danger-text ring-rose/30"
      : credit?.tone === "warning"
        ? "text-gold ring-gold/30"
        : "text-foreground ring-hairline";
  return (
    // The link is 44 px tall for touch; the visible pill inside it is smaller.
    <Link
      to="/m/account"
      aria-label={`اعتبار هوش مصنوعی: ${label}. مشاهدهٔ حساب`}
      className="group flex min-h-11 shrink-0 items-center px-1 focus-visible:outline-none"
    >
      <span
        className={`flex h-9 items-center gap-1.5 rounded-full bg-surface px-3 text-m-sub font-bold tabular-nums ring-1 transition-colors group-active:bg-surface-2 group-focus-visible:ring-2 group-focus-visible:ring-emerald/40 ${tone}`}
      >
        <CreditIcon weight="duotone" className="size-[18px]" aria-hidden="true" />
        {label}
      </span>
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

/**
 * Top app bar: start = avatar or back, then the title, end = credit (tab screens), actions, new chat. The admin
 * entry lives on the account screen (AdminPanelLink), so the bar holds at most four items.
 */
export function TopAppBar({ title, backFallback, actions, hideNewChat }: TopAppBarProps) {
  return (
    <header className="shrink-0 border-b border-hairline bg-background/95 pt-[env(safe-area-inset-top)] backdrop-blur">
      <div className="flex h-14 items-center gap-1 px-[max(6px,env(safe-area-inset-left))]">
        {backFallback ? <BackButton fallback={backFallback} /> : <AccountAvatar />}
        <h1 className="min-w-0 flex-1 truncate px-1 text-m-head font-bold text-foreground">
          {title}
        </h1>
        {!backFallback && <CreditChip />}
        {actions}
        {!hideNewChat && <NewChatButton />}
      </div>
    </header>
  );
}
