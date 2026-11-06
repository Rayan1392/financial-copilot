import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  createMemoryHistory,
  createRootRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router";
import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

const onSignOut = vi.fn();

vi.mock("@/integrations/financial-copilot/auth", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/integrations/financial-copilot/auth")>()),
  // A user without a TahlilApp profile: the header chip hides, the menu must still work.
  getOwnProfile: () => Promise.reject(new Error("no TahlilApp profile")),
  getStoredAuthenticatedUser: () => ({
    userId: "u1",
    email: "user@example.com",
    tenantId: "t1",
    roles: [],
    permissions: [],
  }),
}));

const { AccountMenu } = await import("../account-menu");

function renderMenu() {
  const router = createRouter({
    routeTree: createRootRoute({
      component: () => (
        <>
          <span data-testid="router-ready" />
          <AccountMenu
            variant="full"
            creditSummary="۶۶۴ از ۲٬۶۰۰"
            showAdmin={false}
            onSignOut={onSignOut}
          />
        </>
      ),
    }),
    history: createMemoryHistory({ initialEntries: ["/chat"] }),
  });
  return render(
    <QueryClientProvider client={new QueryClient()}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
}

describe("AccountMenu", () => {
  it("keeps profile, Telegram, credit and sign-out reachable without a TahlilApp display name", async () => {
    renderMenu();
    await screen.findByTestId("router-ready");

    const trigger = screen.getByRole("button", { name: "منوی حساب: حساب کاربری" });
    fireEvent.keyDown(trigger, { key: "Enter" });

    expect(await screen.findByRole("menuitem", { name: "پروفایل حساب" })).toHaveAttribute(
      "href",
      "/profile",
    );
    expect(screen.getByRole("menuitem", { name: "اتصال تلگرام" })).toHaveAttribute(
      "href",
      "/telegram-link",
    );
    expect(screen.queryByRole("menuitem", { name: "پنل مدیریت" })).not.toBeInTheDocument();
    expect(screen.getByText("۶۶۴ از ۲٬۶۰۰")).toBeInTheDocument();
    // The credit row leads to the account page (plan, expiry, purchases, top-up guidance).
    expect(screen.getByRole("menuitem", { name: /اعتبار هوش مصنوعی/ })).toHaveAttribute(
      "href",
      "/profile",
    );
    expect(screen.getAllByText("user@example.com").length).toBeGreaterThan(0);

    fireEvent.click(screen.getByRole("menuitem", { name: "خروج از حساب" }));
    expect(onSignOut).toHaveBeenCalledTimes(1);
  });
});
