import { createFileRoute, Link, useRouter } from "@tanstack/react-router";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useState, type ReactNode } from "react";
import { ChevronLeft, Download, LogOut, Monitor, Moon, Send, ShieldCheck, Sun } from "lucide-react";
import { MobileScreen } from "@/components/mobile/mobile-screen";
import { CreditSummary } from "@/components/mobile/credit-summary";
import { IosInstallSheet } from "@/components/mobile/ios-install-sheet";
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
  "flex min-h-[52px] w-full items-center gap-3 px-4 text-right text-[15px] active:bg-surface-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-emerald/40";

function RowIcon({ children }: { children: ReactNode }) {
  return (
    <span className="flex size-5 shrink-0 items-center justify-center text-muted-foreground">
      {children}
    </span>
  );
}

const chevron = (
  <ChevronLeft className="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
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
          <span
            aria-hidden="true"
            className="flex size-12 shrink-0 items-center justify-center rounded-full bg-emerald-soft text-lg font-semibold text-emerald ring-1 ring-emerald/30"
          >
            {displayName ? Array.from(displayName)[0] : "؟"}
          </span>
          <div className="min-w-0">
            {displayName && <p className="truncate text-base font-semibold">{displayName}</p>}
            {email && (
              <p dir="ltr" className="truncate text-right text-sm text-muted-foreground">
                {email}
              </p>
            )}
          </div>
        </section>

        <CreditSummary />

        <ul className="overflow-hidden rounded-2xl bg-surface ring-1 ring-hairline [&>li]:border-b [&>li]:border-hairline [&>li:last-child]:border-b-0">
          <li>
            <Link to="/m/account/profile" className={ROW}>
              <RowIcon>
                <ShieldCheck className="size-5" aria-hidden="true" />
              </RowIcon>
              <span className="flex-1">پروفایل و امنیت حساب</span>
              {chevron}
            </Link>
          </li>
          <li>
            <Link to="/telegram-link" className={ROW}>
              <RowIcon>
                <Send className="size-5" aria-hidden="true" />
              </RowIcon>
              <span className="flex-1">اتصال تلگرام</span>
              {chevron}
            </Link>
          </li>
          <li>
            <button type="button" onClick={toggleTheme} className={ROW}>
              <RowIcon>
                {theme === "dark" ? (
                  <Moon className="size-5" aria-hidden="true" />
                ) : (
                  <Sun className="size-5" aria-hidden="true" />
                )}
              </RowIcon>
              <span className="flex-1">پوسته: {theme === "dark" ? "تیره" : "روشن"}</span>
              <span className="text-sm text-emerald">
                {theme === "dark" ? "تغییر به روشن" : "تغییر به تیره"}
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
                <RowIcon>
                  <Download className="size-5" aria-hidden="true" />
                </RowIcon>
                <span className="flex-1">نصب برنامه</span>
                {chevron}
              </button>
            </li>
          )}
          <li>
            <button type="button" onClick={switchToDesktop} className={ROW}>
              <RowIcon>
                <Monitor className="size-5" aria-hidden="true" />
              </RowIcon>
              <span className="flex-1">نمایش نسخه دسکتاپ</span>
              {chevron}
            </button>
          </li>
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
          <LogOut className="size-5" aria-hidden="true" />
          <span className="flex-1">خروج از حساب</span>
        </button>
      </div>
      <IosInstallSheet open={iosOpen} onOpenChange={setIosOpen} />
    </MobileScreen>
  );
}
