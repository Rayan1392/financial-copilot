import { useEffect, useState } from "react";
import { useNavigate, Link } from "@tanstack/react-router";
import {
  adminPermissions,
  hasPermission,
} from "@/integrations/financial-copilot/admin-permissions";
import { getAuthenticatedUser, type AuthUser } from "@/integrations/financial-copilot/auth";
import { DataManagementShell } from "./data-management-shell";

type GuardState =
  | { status: "loading" }
  | { status: "unauthenticated" }
  | { status: "denied"; user: AuthUser }
  | { status: "ready"; user: AuthUser };

export function DataManagementGuard({ children }: { children: (user: AuthUser) => React.ReactNode }) {
  const navigate = useNavigate();
  const [state, setState] = useState<GuardState>({ status: "loading" });

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
            The <code>data.sync.manage</code> permission is required.
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
    <DataManagementShell>
      {children(state.user)}
    </DataManagementShell>
  );
}
