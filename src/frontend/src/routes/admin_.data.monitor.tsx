import { createFileRoute, useNavigate, Link } from "@tanstack/react-router";
import { ArrowLeft } from "lucide-react";
import { DataSyncMonitorPage } from "@/components/admin/data-sync-monitor-page";
import {
  adminPermissions,
  hasPermission,
} from "@/integrations/financial-copilot/admin-permissions";
import { getAuthenticatedUser, type AuthUser } from "@/integrations/financial-copilot/auth";
import { useEffect, useState } from "react";

export const Route = createFileRoute("/admin_/data/monitor")({
  component: DataSyncMonitorRoute,
});

type RouteState =
  | { status: "loading" }
  | { status: "unauthenticated" }
  | { status: "denied"; user: AuthUser }
  | { status: "ready"; user: AuthUser };

function DataSyncMonitorRoute() {
  const navigate = useNavigate();
  const [state, setState] = useState<RouteState>({ status: "loading" });

  useEffect(() => {
    getAuthenticatedUser().then((user) => {
      if (!user) { setState({ status: "unauthenticated" }); return; }
      if (!hasPermission(user, adminPermissions.dataSyncManage)) {
        setState({ status: "denied", user });
        return;
      }
      setState({ status: "ready", user });
    });
  }, []);

  useEffect(() => {
    if (state.status === "unauthenticated") navigate({ to: "/auth" });
  }, [state.status, navigate]);

  if (state.status === "loading" || state.status === "unauthenticated") {
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

  if (state.status === "denied") {
    return (
      <div className="min-h-screen flex items-center justify-center bg-background px-4">
        <div className="max-w-sm w-full text-center">
          <div className="size-14 mx-auto rounded-2xl bg-destructive/10 ring-1 ring-destructive/20 flex items-center justify-center mb-6">
            <div className="size-5 rounded-full bg-destructive/60" />
          </div>
          <h1 className="text-lg font-bold text-foreground mb-2">Access Denied</h1>
          <p className="text-sm text-muted-foreground mb-6">
            The <code>data.sync.manage</code> permission is required to access this page.
          </p>
          <Link
            to="/admin"
            className="rounded-lg bg-primary px-4 py-2 text-sm font-medium text-primary-foreground"
          >
            Back to Admin
          </Link>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="border-b border-border px-6 py-3 flex items-center gap-4">
        <Link
          to="/admin"
          className="p-1.5 text-muted-foreground hover:text-foreground transition"
          aria-label="Back to admin"
        >
          <ArrowLeft className="size-4" />
        </Link>
        <span className="text-sm text-muted-foreground">Admin</span>
        <span className="text-muted-foreground">/</span>
        <span className="text-sm font-medium">Data Sync Monitor</span>
      </header>
      <main className="mx-auto max-w-7xl px-6 py-8">
        <DataSyncMonitorPage />
      </main>
    </div>
  );
}
