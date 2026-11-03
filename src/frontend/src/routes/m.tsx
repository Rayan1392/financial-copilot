import { createFileRoute, Outlet, useLocation, useRouter } from "@tanstack/react-router";
import { useEffect, useState } from "react";
import { isAuthenticated, subscribeToAuthChanges } from "@/integrations/financial-copilot/auth";
import { Toaster } from "@/components/ui/sonner";
import { Skeleton } from "@/components/ui/skeleton";
import { consumeExplicitSignOut } from "@/lib/session-end";

/**
 * Mobile workspace layout (Feature 153 T07). Reachable on any device; never redirects to desktop.
 * The `<head>` overrides apply only under `/m`: TanStack keeps the deepest route's meta for each name.
 */
export const Route = createFileRoute("/m")({
  head: () => ({
    meta: [
      {
        name: "viewport",
        content:
          "width=device-width, initial-scale=1, viewport-fit=cover, interactive-widget=resizes-content",
      },
      { name: "robots", content: "noindex" },
    ],
  }),
  component: MobileLayout,
});

type AuthGate = "checking" | "signed-in";

function MobileLayout() {
  const router = useRouter();
  const location = useLocation();
  const [gate, setGate] = useState<AuthGate>("checking");

  // Client-only gate: SSR and the first client render show a skeleton, so navigation never flashes to
  // signed-out users. A lost session (refresh failure) returns to login with the current path.
  useEffect(() => {
    let active = true;
    const toLogin = (reason?: "expired") => {
      const redirect = window.location.pathname + window.location.search;
      void router.navigate({ to: "/auth", search: { redirect, reason }, replace: true });
    };
    void isAuthenticated().then((signedIn) => {
      if (!active) return;
      if (signedIn) setGate("signed-in");
      else toLogin();
    });
    const unsubscribe = subscribeToAuthChanges(() => {
      // A deliberate sign-out navigates on its own and must not be reported as an expired session.
      if (consumeExplicitSignOut()) return;
      void isAuthenticated().then((signedIn) => {
        if (active && !signedIn) toLogin("expired");
      });
    });
    return () => {
      active = false;
      unsubscribe();
    };
  }, [router]);

  if (gate === "checking") return <ShellSkeleton key={location.pathname} />;
  return (
    <>
      <Outlet />
      <Toaster position="top-center" dir="rtl" offset={64} />
    </>
  );
}

function ShellSkeleton() {
  return (
    <div
      dir="rtl"
      className="flex h-screen h-dvh flex-col bg-background"
      aria-busy="true"
      aria-label="در حال بارگذاری"
    >
      <div className="flex h-14 items-center gap-3 border-b border-hairline px-4 pt-[env(safe-area-inset-top)]">
        <Skeleton className="size-8 rounded-full" />
        <Skeleton className="h-5 w-24" />
      </div>
      <div className="flex-1 space-y-3 p-4">
        <Skeleton className="h-14 w-full" />
        <Skeleton className="h-14 w-full" />
        <Skeleton className="h-14 w-full" />
      </div>
      <div className="h-16 border-t border-hairline" />
    </div>
  );
}
