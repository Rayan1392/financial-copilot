import { useEffect, useState } from "react";
import { createFileRoute, Link, useNavigate } from "@tanstack/react-router";
import { GetCompanyIdPage } from "@/components/admin/get-company-id-page";
import { canAccessAdmin } from "@/integrations/financial-copilot/admin-permissions";
import { getAuthenticatedUser, type AuthUser } from "@/integrations/financial-copilot/auth";

export const Route = createFileRoute("/admin_/getCompanyId")({
  component: GetCompanyIdRoute,
});

type AdminState =
  | { status: "loading" }
  | { status: "unauthenticated" }
  | { status: "denied"; user: AuthUser }
  | { status: "ready" };

function GetCompanyIdRoute() {
  const navigate = useNavigate();
  const [state, setState] = useState<AdminState>({ status: "loading" });

  useEffect(() => {
    getAuthenticatedUser().then((user) => {
      if (!user) {
        setState({ status: "unauthenticated" });
      } else if (!canAccessAdmin(user)) {
        setState({ status: "denied", user });
      } else {
        setState({ status: "ready" });
      }
    });
  }, []);

  useEffect(() => {
    if (state.status === "unauthenticated") navigate({ to: "/auth" });
  }, [state.status, navigate]);

  if (state.status === "loading" || state.status === "unauthenticated") {
    return (
      <div className="flex min-h-screen items-center justify-center bg-background">
        <div className="flex gap-1.5">
          <div className="size-2 animate-bounce rounded-full bg-emerald [animation-delay:0ms]" />
          <div className="size-2 animate-bounce rounded-full bg-emerald [animation-delay:150ms]" />
          <div className="size-2 animate-bounce rounded-full bg-emerald [animation-delay:300ms]" />
        </div>
      </div>
    );
  }

  if (state.status === "denied") {
    return (
      <div dir="rtl" className="flex min-h-screen items-center justify-center bg-background px-4">
        <div className="text-center">
          <h1 className="mb-2 text-lg font-bold">دسترسی مجاز نیست</h1>
          <p className="mb-6 text-sm text-muted-foreground">
            حساب کاربری شما دسترسی مدیریتی ندارد.
          </p>
          <Link to="/admin" className="rounded-lg bg-primary px-4 py-2 text-sm font-medium text-primary-foreground">
            بازگشت به مدیریت
          </Link>
        </div>
      </div>
    );
  }

  return <GetCompanyIdPage />;
}
