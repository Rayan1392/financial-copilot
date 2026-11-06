import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  createMemoryHistory,
  createRootRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { CodalAlertSubscription } from "@/integrations/financial-copilot/codal-alerts";

const subscription: CodalAlertSubscription = {
  id: "sub-1",
  externalCompanyId: "12795",
  symbol: "کگهر",
  companyName: "معدنی و صنعتی گهر زمین",
  announcementTypes: ["FinancialStatement", "MonthlyActivity"],
  minimumImportance: "Notice",
  rawAlertEnabled: true,
  aiSummaryEnabled: false,
  state: "Active",
  createdAtUtc: "2026-11-01T00:00:00Z",
  updatedAtUtc: "2026-11-01T00:00:00Z",
};
const updateCodalAlert = vi.fn().mockResolvedValue(subscription);
const createCodalAlert = vi.fn().mockResolvedValue(subscription);

vi.mock("@/integrations/financial-copilot/codal-alerts", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/integrations/financial-copilot/codal-alerts")>()),
  getMyCodalAlerts: () => Promise.resolve({ items: [subscription] }),
  updateCodalAlert: (...args: unknown[]) => updateCodalAlert(...args),
  createCodalAlert: (...args: unknown[]) => createCodalAlert(...args),
  deleteCodalAlert: vi.fn().mockResolvedValue(undefined),
}));
vi.mock("@/lib/workspace/queries", () => ({
  useFollowedSymbols: () => ({
    isLoading: false,
    data: {
      symbols: [
        {
          externalCompanyId: "12795",
          symbol: "کگهر",
          companyName: "معدنی و صنعتی گهر زمین",
          followedAtUtc: "",
        },
        {
          externalCompanyId: "290",
          symbol: "فاذر",
          companyName: "صنایع آذر آب",
          followedAtUtc: "",
        },
      ],
    },
  }),
}));

const { CodalAlertSettings } = await import("../codal-alert-settings");

function renderSettings() {
  const router = createRouter({
    routeTree: createRootRoute({ component: () => <CodalAlertSettings /> }),
    history: createMemoryHistory({ initialEntries: ["/telegram-link"] }),
  });
  return render(
    <QueryClientProvider
      client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}
    >
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
}

describe("CodalAlertSettings", () => {
  it("lists subscriptions and pauses one without losing its other settings", async () => {
    renderSettings();

    expect(await screen.findByText("کگهر")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "توقف" }));

    await waitFor(() =>
      expect(updateCodalAlert).toHaveBeenCalledWith("sub-1", {
        announcementTypes: ["FinancialStatement", "MonthlyActivity"],
        minimumImportance: "Notice",
        rawAlertEnabled: true,
        aiSummaryEnabled: false,
        state: "Paused",
      }),
    );
  });

  it("offers only watchlist symbols that have no alert yet", async () => {
    renderSettings();
    await screen.findByText("کگهر");

    const options = screen.getAllByRole("option").map((option) => option.textContent);
    expect(options).toContain("فاذر — صنایع آذر آب");
    expect(options.some((text) => text?.startsWith("کگهر"))).toBe(false);

    fireEvent.change(screen.getByLabelText("انتخاب نماد برای هشدار"), { target: { value: "290" } });
    fireEvent.click(screen.getByRole("button", { name: "افزودن هشدار" }));
    await waitFor(() =>
      expect(createCodalAlert).toHaveBeenCalledWith(
        "290",
        expect.objectContaining({ rawAlertEnabled: true }),
      ),
    );
  });
});
