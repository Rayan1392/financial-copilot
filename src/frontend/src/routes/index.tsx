import { createFileRoute, redirect } from "@tanstack/react-router";
import { isAuthenticated } from "@/integrations/financial-copilot/auth";

export const Route = createFileRoute("/")({
  beforeLoad: async () => {
    if (typeof window === "undefined") return;
    if (!(await isAuthenticated())) throw redirect({ to: "/auth" });
    throw redirect({ to: "/chat" });
  },
  component: SplashPage,
});

function SplashPage() {
  return (
    <div className="min-h-screen flex items-center justify-center bg-background">
      <div className="flex gap-1.5">
        <div className="size-2 rounded-full bg-emerald animate-bounce [animation-delay:0ms]" />
        <div className="size-2 rounded-full bg-emerald animate-bounce [animation-delay:150ms]" />
        <div className="size-2 rounded-full bg-emerald animate-bounce [animation-delay:300ms]" />
      </div>
    </div>
  );
}
