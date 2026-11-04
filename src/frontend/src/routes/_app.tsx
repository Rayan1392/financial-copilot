import { createFileRoute, Outlet, redirect } from "@tanstack/react-router";
import { isAuthenticated } from "@/integrations/financial-copilot/auth";
import { ConversationSidebar } from "@/components/app/sidebar";
import { ContextPanel } from "@/components/app/context-panel";
import { ChatHeader } from "@/components/app/chat-header";
import { ExperienceBanner } from "@/components/app/experience-banner";
import { useSidebarPreferences } from "@/components/app/use-sidebar-preferences";
import { clientMobileRedirectTarget } from "@/lib/experience/client";

export const Route = createFileRoute("/_app")({
  beforeLoad: async ({ location }) => {
    if (typeof window === "undefined") return;
    // Keep the requested path so login returns to it; /auth sanitizes the value with safeInternalPath.
    if (!(await isAuthenticated())) throw redirect({ to: "/auth", search: { redirect: location.href } });
    const mobileTarget = clientMobileRedirectTarget(location.href);
    if (mobileTarget) throw redirect({ href: mobileTarget, replace: true });
  },
  component: AppLayout,
});

function AppLayout() {
  const { historyCollapsed, marketCollapsed, toggleHistory, toggleMarket } = useSidebarPreferences();

  return (
    <div dir="rtl" className="flex h-screen w-full overflow-hidden bg-background text-foreground">
      <ConversationSidebar collapsed={historyCollapsed} onToggle={toggleHistory} />
      <main className="relative flex min-w-0 flex-1 flex-col overflow-hidden">
        <ChatHeader />
        <ExperienceBanner />
        <div className="flex min-h-0 flex-1 flex-col overflow-y-auto scrollbar-thin">
          <Outlet />
        </div>
      </main>
      <ContextPanel collapsed={marketCollapsed} onToggle={toggleMarket} />
    </div>
  );
}
