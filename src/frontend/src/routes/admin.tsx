import { createFileRoute, redirect } from "@tanstack/react-router";
import { AdminPage } from "@/components/admin/admin-page";
import { canAccessAdmin } from "@/integrations/financial-copilot/admin-permissions";
import { getAuthenticatedUser } from "@/integrations/financial-copilot/auth";

export const Route = createFileRoute("/admin")({
  beforeLoad: async () => {
    if (typeof window === "undefined") return;
    const user = await getAuthenticatedUser();
    if (!user) throw redirect({ to: "/auth" });
    if (!canAccessAdmin(user)) throw redirect({ to: "/chat" });
  },
  loader: async () => (typeof window === "undefined" ? null : getAuthenticatedUser()),
  component: AdminRoute,
});

function AdminRoute() {
  const user = Route.useLoaderData();
  return user ? <AdminPage user={user} /> : null;
}
