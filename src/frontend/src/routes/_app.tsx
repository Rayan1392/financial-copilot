import { createFileRoute, Outlet, redirect } from "@tanstack/react-router";
import { isAuthenticated } from "@/integrations/financial-copilot/auth";
import { ConversationSidebar } from "@/components/app/sidebar";
import { ContextPanel } from "@/components/app/context-panel";
import { ChatHeader } from "@/components/app/chat-header";

export const Route = createFileRoute("/_app")({
  beforeLoad: async () => {
    if (typeof window === "undefined") return;
    if (!(await isAuthenticated())) throw redirect({ to: "/auth" });
  },
  component: AppLayout,
});

function AppLayout() {
  return (
    <div dir="rtl" className="flex h-screen w-full overflow-hidden bg-background text-foreground">
      <ConversationSidebar />
      <main className="relative flex min-w-0 flex-1 flex-col overflow-hidden">
        <ChatHeader />
        <div className="min-h-0 flex-1 overflow-y-auto scrollbar-thin">
          <Outlet />
        </div>
      </main>
      <ContextPanel />
    </div>
  );
}
