import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  Outlet,
  RouterProvider,
  useLocation,
} from "@tanstack/react-router";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

const asOf = new Date().toISOString();

vi.mock("@/lib/workspace/queries", () => ({
  useMarketSummary: () => ({
    data: {
      indices: [{ symbol: "TEDPIX", name: "شاخص کل", value: 2_500_000, changePercent: 1.12 }],
      topGainers: [
        {
          symbol: "فولاد",
          name: "فولاد مبارکه اصفهان",
          latestPrice: 5_000,
          changePercent: 4.5,
          asOf,
          isStale: false,
        },
      ],
      topLosers: [
        {
          symbol: "شپنا",
          name: "پالایش نفت اصفهان",
          latestPrice: 7_000,
          changePercent: -3.25,
          asOf,
          isStale: false,
        },
      ],
      asOf,
    },
    isLoading: false,
    isError: false,
  }),
  useWatchlistWithNames: () => ({
    join: {
      rows: [
        {
          symbol: "خودرو",
          latestPrice: 2_100,
          changePercent: -1.12,
          asOf,
          isStale: false,
          externalCompanyId: "c-1",
          companyName: "ایران خودرو",
        },
      ],
      unmatchedSymbols: [],
      ambiguousSymbols: [],
    },
    asOf,
    followed: [],
    isLoading: false,
    isError: false,
    isFetching: false,
    refetch: vi.fn(),
  }),
}));

const { ContextPanel } = await import("../context-panel");

function ChatProbe() {
  const state = useLocation({ select: (location) => location.state });
  return (
    <output data-testid="chat-intent">
      {state.submitQuestion ?? ""}|{state.prefillQuestion ?? ""}
    </output>
  );
}

function renderPanel() {
  const rootRoute = createRootRoute({
    component: () => (
      <>
        <span data-testid="router-ready" />
        <ContextPanel collapsed={false} onToggle={() => {}} />
        <Outlet />
      </>
    ),
  });
  const routeTree = rootRoute.addChildren([
    createRoute({ getParentRoute: () => rootRoute, path: "/", component: () => null }),
    createRoute({ getParentRoute: () => rootRoute, path: "/chat", component: ChatProbe }),
  ]);
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/"] }),
  });
  render(
    <QueryClientProvider client={new QueryClient()}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
  return router;
}

function rowButton(symbol: string) {
  return screen.getAllByRole("button").find((button) => within(button).queryByText(symbol))!;
}

describe("ContextPanel (expanded desktop market column)", () => {
  it("shows the company name and a ▲/▼ change with the sign isolated left-to-right", async () => {
    renderPanel();
    await screen.findByTestId("router-ready");

    const row = rowButton("خودرو");
    expect(within(row).getByText("ایران خودرو")).toBeInTheDocument();
    expect(within(row).getByText("▼")).toBeInTheDocument();
    const percent = within(row).getByText("-۱٫۱۲٪");
    expect(percent.tagName).toBe("BDI");
    expect(percent).toHaveAttribute("dir", "ltr");

    const indexChange = screen.getByText("+۱٫۱۲٪");
    expect(indexChange.tagName).toBe("BDI");
    expect(indexChange).toHaveAttribute("dir", "ltr");
  });

  it("spells «به‌روزرسانی» like the mobile screens", async () => {
    renderPanel();
    await screen.findByTestId("router-ready");

    expect(screen.queryByText(/بروزرسانی/)).not.toBeInTheDocument();
    expect(screen.getAllByText(/به‌روزرسانی/).length).toBe(2);
  });

  it("lists the top gainers and switches to the top losers", async () => {
    renderPanel();
    await screen.findByTestId("router-ready");

    expect(screen.getByRole("button", { name: "بیشترین رشد" })).toHaveAttribute(
      "aria-pressed",
      "true",
    );
    expect(rowButton("فولاد")).toBeDefined();
    expect(screen.queryByText("شپنا")).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "بیشترین افت" }));

    expect(rowButton("شپنا")).toBeDefined();
    expect(screen.queryByText("فولاد")).not.toBeInTheDocument();
  });

  it("starts a conversation about the clicked watchlist symbol on /chat", async () => {
    const router = renderPanel();
    await screen.findByTestId("router-ready");

    fireEvent.keyDown(rowButton("خودرو"), { key: "Enter" });
    fireEvent.click(await screen.findByRole("menuitem", { name: /روند فروش ماهانه خودرو/ }));

    await waitFor(() => expect(router.state.location.pathname).toBe("/chat"));
    expect(screen.getByTestId("chat-intent")).toHaveTextContent(
      "روند فروش ماهانه خودرو را نشان بده|",
    );
  });

  it("seeds the composer when asking another question about a symbol", async () => {
    const router = renderPanel();
    await screen.findByTestId("router-ready");

    fireEvent.keyDown(rowButton("فولاد"), { key: "Enter" });
    fireEvent.click(await screen.findByRole("menuitem", { name: /پرسش دیگری درباره/ }));

    await waitFor(() => expect(router.state.location.pathname).toBe("/chat"));
    expect(screen.getByTestId("chat-intent")).toHaveTextContent("|فولاد");
  });
});
