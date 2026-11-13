import { act, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { AuthUser } from "@/integrations/financial-copilot/auth";
import { renderInRouter } from "./test-router";

const authState = vi.hoisted(() => ({ user: null as AuthUser | null }));

vi.mock("@/integrations/financial-copilot/auth", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/integrations/financial-copilot/auth")>()),
  getStoredAuthenticatedUser: () => authState.user,
  subscribeToAuthChanges: (listener: () => void) => {
    window.addEventListener("test-auth-changed", listener);
    return () => window.removeEventListener("test-auth-changed", listener);
  },
}));

import { AdminPanelLink } from "../admin-panel-link";

function user(permissions: string[]): AuthUser {
  return { userId: "u1", email: "u@example.com", tenantId: "t1", roles: [], permissions };
}

afterEach(() => {
  authState.user = null;
});

describe("AdminPanelLink", () => {
  it("links admins to the admin panel", async () => {
    authState.user = user(["admin.users.read"]);
    await renderInRouter(<AdminPanelLink />);
    const link = await screen.findByRole("link", { name: "پنل مدیریت" });
    expect(link).toHaveAttribute("href", "/admin");
  });

  it("stays hidden for users without an admin permission", async () => {
    authState.user = user(["chat.use"]);
    await renderInRouter(<AdminPanelLink />);
    expect(screen.queryByRole("link", { name: "پنل مدیریت" })).not.toBeInTheDocument();
  });

  it("stays hidden when signed out and appears after an admin signs in", async () => {
    await renderInRouter(<AdminPanelLink />);
    expect(screen.queryByRole("link", { name: "پنل مدیریت" })).not.toBeInTheDocument();

    authState.user = user(["admin.plans.read"]);
    act(() => {
      window.dispatchEvent(new Event("test-auth-changed"));
    });
    expect(await screen.findByRole("link", { name: "پنل مدیریت" })).toBeInTheDocument();
  });
});
