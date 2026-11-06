import { Link } from "@tanstack/react-router";
import { useQuery } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { ChevronDown, LogOut, Send, Settings, UserRound, Wallet } from "lucide-react";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import {
  getOwnProfile,
  getStoredAuthenticatedUser,
  subscribeToAuthChanges,
} from "@/integrations/financial-copilot/auth";
import { officialDisplayName } from "@/lib/profile-display-name";
import { OWN_PROFILE_QUERY_KEY } from "@/lib/workspace/query-keys";

const ITEM_CLASS =
  "flex min-h-9 cursor-pointer items-center gap-2.5 rounded-lg px-2.5 text-[13px] focus:bg-emerald-soft/50 focus:text-foreground";

/**
 * The always-available account entry point of the desktop shell: avatar, name/email, remaining credit and the
 * account destinations (profile, Telegram, admin) plus sign-out. It does not depend on a TahlilApp display name
 * (the header chip hides without one), so the profile page is always reachable.
 */
export function AccountMenu({
  variant,
  creditSummary,
  showAdmin,
  onSignOut,
}: {
  variant: "full" | "rail";
  /** Already formatted remaining-credit text, e.g. «۶۶۴ از ۲٬۶۰۰»; omitted while unknown. */
  creditSummary?: string | null;
  showAdmin: boolean;
  onSignOut: () => void;
}) {
  const [email, setEmail] = useState<string | null>(null);
  useEffect(() => {
    const update = () => setEmail(getStoredAuthenticatedUser()?.email ?? null);
    update();
    return subscribeToAuthChanges(update);
  }, []);
  // Same query as the header chip, so both share one cached request.
  const { data: displayName } = useQuery({
    queryKey: OWN_PROFILE_QUERY_KEY,
    queryFn: getOwnProfile,
    select: (profile) => officialDisplayName(profile.displayName),
    staleTime: Infinity,
    retry: false,
    throwOnError: false,
    refetchOnWindowFocus: false,
  });
  const primary = displayName ?? "حساب کاربری";
  const initial = Array.from(displayName ?? email ?? "؟")[0]?.toUpperCase() ?? "؟";
  const avatar = (size: string) => (
    <span
      aria-hidden="true"
      className={`flex ${size} shrink-0 items-center justify-center rounded-full bg-emerald-soft text-sm font-semibold text-emerald ring-1 ring-emerald/30`}
    >
      {initial}
    </span>
  );

  return (
    <DropdownMenu dir="rtl">
      <DropdownMenuTrigger asChild>
        {variant === "full" ? (
          <button
            type="button"
            className="flex w-full items-center gap-2.5 rounded-xl px-2 py-2 text-right transition hover:bg-white/5 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/50 data-[state=open]:bg-white/5"
            aria-label={`منوی حساب: ${primary}`}
          >
            {avatar("size-9")}
            <span className="min-w-0 flex-1">
              <span className="block truncate text-sm font-medium text-foreground">{primary}</span>
              {email && (
                <span
                  dir="ltr"
                  className="block truncate text-right text-[11px] text-muted-foreground"
                >
                  {email}
                </span>
              )}
            </span>
            <ChevronDown className="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
          </button>
        ) : (
          <button
            type="button"
            className="flex size-11 items-center justify-center rounded-xl transition hover:bg-white/5 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/50 data-[state=open]:bg-white/5"
            aria-label={`منوی حساب: ${primary}`}
            title="حساب کاربری"
          >
            {avatar("size-8")}
          </button>
        )}
      </DropdownMenuTrigger>
      <DropdownMenuContent
        side={variant === "full" ? "top" : "left"}
        align={variant === "full" ? "start" : "end"}
        sideOffset={8}
        className="w-64 rounded-2xl border-hairline bg-surface p-1.5 text-right shadow-2xl"
      >
        <DropdownMenuLabel className="flex items-center gap-2.5 px-2.5 py-2 font-normal">
          {avatar("size-9")}
          <span className="min-w-0 flex-1">
            <span className="block truncate text-sm font-semibold text-foreground">{primary}</span>
            {email && (
              <span
                dir="ltr"
                className="block truncate text-right text-[11px] text-muted-foreground"
              >
                {email}
              </span>
            )}
          </span>
        </DropdownMenuLabel>
        {creditSummary && (
          <div className="mx-1 mb-1 flex items-center justify-between gap-2 rounded-lg bg-background/70 px-2.5 py-2 text-[12px]">
            <span className="flex items-center gap-1.5 text-muted-foreground">
              <Wallet className="size-3.5" aria-hidden="true" />
              اعتبار هوش مصنوعی
            </span>
            <span className="font-semibold tabular-nums text-foreground">{creditSummary}</span>
          </div>
        )}
        <DropdownMenuSeparator className="bg-hairline" />
        <DropdownMenuItem asChild className={ITEM_CLASS}>
          <Link to="/profile">
            <UserRound className="size-4 text-muted-foreground" aria-hidden="true" />
            پروفایل حساب
          </Link>
        </DropdownMenuItem>
        <DropdownMenuItem asChild className={ITEM_CLASS}>
          <Link to="/telegram-link">
            <Send className="size-4 text-muted-foreground" aria-hidden="true" />
            اتصال تلگرام
          </Link>
        </DropdownMenuItem>
        {showAdmin && (
          <DropdownMenuItem asChild className={ITEM_CLASS}>
            <Link to="/admin">
              <Settings className="size-4 text-muted-foreground" aria-hidden="true" />
              پنل مدیریت
            </Link>
          </DropdownMenuItem>
        )}
        <DropdownMenuSeparator className="bg-hairline" />
        <DropdownMenuItem
          onSelect={onSignOut}
          className={`${ITEM_CLASS} text-rose focus:bg-rose/10 focus:text-rose`}
        >
          <LogOut className="size-4" aria-hidden="true" />
          خروج از حساب
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
