import { createFileRoute, Outlet, redirect } from "@tanstack/react-router";
import { supabase } from "@/integrations/supabase/client";
import { ConversationSidebar } from "@/components/app/sidebar";
import { ContextPanel } from "@/components/app/context-panel";
import { ChatHeader } from "@/components/app/chat-header";

export const Route = createFileRoute("/_app")({
  beforeLoad: async () => {
    if (typeof window === "undefined") return;
    const { data } = await supabase.auth.getUser();
    if (!data.user) throw redirect({ to: "/auth" });
  },
  component: AppLayout,
});

function AppLayout() {
  return (
    <div dir="rtl" className="flex h-screen w-full overflow-hidden bg-background text-foreground">
      <ConversationSidebar />
      <main className="flex-1 flex flex-col relative min-w-0">
        <ChatHeader />
        <Outlet />
      </main>
      <ContextPanel />
    </div>
  );
}
