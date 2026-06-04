import { createFileRoute, useNavigate } from "@tanstack/react-router";
import { AdminPage } from "@/components/admin/admin-page";
import { canAccessAdmin } from "@/integrations/financial-copilot/admin-permissions";
import { getAuthenticatedUser, type AuthUser } from "@/integrations/financial-copilot/auth";
import { useEffect, useState } from "react";

export const Route = createFileRoute("/admin")({
  component: AdminRoute,
});

type AdminState =
  | { status: "loading" }
  | { status: "unauthenticated" }
  | { status: "denied"; user: AuthUser }
  | { status: "ready"; user: AuthUser };

function AdminRoute() {
  const navigate = useNavigate();
  const [state, setState] = useState<AdminState>({ status: "loading" });

  useEffect(() => {
    getAuthenticatedUser().then((user) => {
      if (!user) { setState({ status: "unauthenticated" }); return; }
      if (!canAccessAdmin(user)) { setState({ status: "denied", user }); return; }
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
      <div dir="rtl" className="min-h-screen flex items-center justify-center bg-background px-4">
        <div className="max-w-sm w-full text-center">
          <div className="size-14 mx-auto rounded-2xl bg-destructive/10 ring-1 ring-destructive/20 flex items-center justify-center mb-6">
            <div className="size-5 rounded-full bg-destructive/60" />
          </div>
          <h1 className="text-lg font-bold text-foreground mb-2">دسترسی مجاز نیست</h1>
          <p className="text-sm text-muted-foreground mb-6">
            حساب کاربری شما دسترسی به پنل مدیریت ندارد. برای دریافت دسترسی با مدیر سیستم تماس بگیرید.
          </p>
          <button
            onClick={() => navigate({ to: "/chat" })}
            className="rounded-lg bg-primary px-4 py-2 text-sm font-medium text-primary-foreground"
          >
            بازگشت به گفتگو
          </button>
        </div>
      </div>
    );
  }

  return <AdminPage user={state.user} />;
}
