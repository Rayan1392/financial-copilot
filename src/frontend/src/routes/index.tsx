import { createFileRoute, redirect } from "@tanstack/react-router";
import { isAuthenticated } from "@/integrations/financial-copilot/auth";
import { clientMobileRedirectTarget } from "@/lib/experience/client";
import { AuthScreen } from "./auth";

export const Route = createFileRoute("/")({
  beforeLoad: async () => {
    if (typeof window === "undefined") return;
    if (await isAuthenticated()) {
      const mobileTarget = clientMobileRedirectTarget("/chat");
      throw mobileTarget
        ? redirect({ href: mobileTarget, replace: true })
        : redirect({ to: "/chat" });
    }
  },
  component: IndexRoute,
});

function IndexRoute() {
  // The site root is where first-time visitors land, so it introduces the product beside the sign-in form.
  return <AuthScreen redirect="/chat" withIntro />;
}
