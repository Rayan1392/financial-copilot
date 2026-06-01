import { createFileRoute, redirect } from "@tanstack/react-router";
import { isAuthenticated } from "@/integrations/financial-copilot/auth";

export const Route = createFileRoute("/")({
  beforeLoad: async () => {
    if (typeof window === "undefined") return;
    if (!(await isAuthenticated())) throw redirect({ to: "/auth" });
    throw redirect({ to: "/chat" });
  },
  component: () => null,
});
