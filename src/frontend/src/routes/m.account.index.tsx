import { createFileRoute, Link, useRouter } from "@tanstack/react-router";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { MobileScreen } from "@/components/mobile/mobile-screen";
import { AccountInitial } from "@/components/mobile/account-initial";
import { AdminPanelLink } from "@/components/mobile/admin-panel-link";
import { CreditSummary } from "@/components/mobile/credit-summary";
import { IconTile } from "@/components/mobile/icon-tile";
import {
  ChevronIcon,
  DarkThemeIcon,
  DesktopIcon,
  DownloadIcon,
  SecurityIcon,
  SignOutIcon,
  TelegramIcon,
} from "@/components/mobile/icons";
import { IosInstallSheet } from "@/components/mobile/ios-install-sheet";
import { ReleaseVersionLink } from "@/components/release-notes/release-version-link";
import { useTheme } from "@/components/app/theme-toggle";
import { getAccountSummary, logout } from "@/integrations/financial-copilot/auth";
import { officialDisplayName } from "@/lib/profile-display-name";
import { useOwnProfile } from "@/lib/workspace/queries";
import { useExperienceSwitch } from "@/lib/experience/use-experience-switch";
import { useInstallState } from "@/lib/pwa/install";
import { markExplicitSignOut } from "@/lib/session-end";
import { cn } from "@/lib/utils";

export const Route = createFileRoute("/m/account/")({
  head: () => ({ meta: [{ title: "حساب کاربری — ساپیو" }] }),
  component: MobileAccount,
});

const ROW =
  "flex min-h-16 w-full items-center gap-3 px-4 py-2 text-right text-m-body active:bg-surface-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-emerald/40";

const chevron = (
  <ChevronIcon className="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
);

function MobileAccount() {
  const router = useRouter();
  const qc = useQueryClient();
  const { data: profile } = useOwnProfile();
  const account = useQuery({
    queryKey: ["account-summary"],
    queryFn: getAccountSummary,
    retry: false,
    throwOnError: false,
    refetchOnWindowFocus: false,
  });
  const { theme, toggleTheme } = useTheme();
  const { switchToDesktop } = useExperienceSwitch();
  const install = useInstallState();
  const [iosOpen, setIosOpen] = useState(false);
  const [signingOut, setSigningOut] = useState(false);
  const displayName = officialDisplayName(profile?.displayName ?? null);
  const email = account.data?.email ?? null;

  async function signOut() {
    setSigningOut(true);
    markExplicitSignOut();
    try {
      await logout();
    } finally {
      qc.clear();
      void router.navigate({ to: "/auth", search: { redirect: "/m" }, replace: true });
    }
  }

  return (
    <MobileScreen title="حساب کاربری" backFallback="/m" showTabBar={false} hideNewChat>
      <div className="space-y-4 px-4 pb-8 pt-4">
        <section className="flex items-center gap-3">
          <AccountInitial
            name={displayName}
            className="size-14 text-m-hero"
            iconClassName="size-7"
          />
          <div className="min-w-0">
            {displayName && <p className="truncate text-m-head font-bold">{displayName}</p>}
            {email && (
              <p dir="ltr" className="truncate text-right text-m-sub text-muted-foreground">
                {email}
              </p>
            )}
          </div>
        </section>

        <CreditSummary />

        <ul className="overflow-hidden rounded-2xl bg-surface ring-1 ring-hairline [&>li]:border-b [&>li]:border-hairline [&>li:last-child]:border-b-0">
          <li>
            <Link to="/m/account/profile" className={ROW}>
              <IconTile icon={SecurityIcon} />
              <span className="flex-1">پروفایل و امنیت حساب</span>
              {chevron}
            </Link>
          </li>
          <li>
            <Link to="/telegram-link" className={ROW}>
              <IconTile icon={TelegramIcon} />
              <span className="flex-1">اتصال تلگرام</span>
              {chevron}
            </Link>
          </li>
          <li>
            <button
              type="button"
              role="switch"
              aria-checked={theme === "dark"}
              onClick={toggleTheme}
              className={ROW}
            >
              <IconTile icon={DarkThemeIcon} tone="gold" />
              <span className="flex-1">حالت تیره</span>
              <span
                aria-hidden="true"
                className={cn(
                  "flex h-7 w-12 shrink-0 items-center rounded-full p-0.5 transition-colors",
                  theme === "dark"
                    ? "justify-end bg-emerald"
                    : "justify-start bg-muted-foreground/40",
                )}
              >
                <span className="size-6 rounded-full bg-white shadow" />
              </span>
            </button>
          </li>
          {install.installable && (
            <li>
              <button
                type="button"
                onClick={() =>
                  install.mode === "ios" ? setIosOpen(true) : void install.promptInstall()
                }
                className={ROW}
              >
                <IconTile icon={DownloadIcon} />
                <span className="flex-1">نصب برنامه</span>
                {chevron}
              </button>
            </li>
          )}
          <li>
            <button type="button" onClick={switchToDesktop} className={ROW}>
              <IconTile icon={DesktopIcon} tone="neutral" />
              <span className="flex-1">نسخه دسکتاپ</span>
              {chevron}
            </button>
          </li>
          <AdminPanelLink className={ROW} />
        </ul>

        <button
          type="button"
          onClick={() => void signOut()}
          disabled={signingOut}
          className={cn(
            ROW,
            "rounded-2xl bg-surface mobile-danger-text ring-1 ring-hairline disabled:opacity-60",
          )}
        >
          <IconTile icon={SignOutIcon} tone="danger" />
          <span className="flex-1">خروج از حساب</span>
        </button>
        <div className="flex justify-center">
          <ReleaseVersionLink className="text-m-cap" />
        </div>
      </div>
      <IosInstallSheet open={iosOpen} onOpenChange={setIosOpen} />
    </MobileScreen>
  );
}
