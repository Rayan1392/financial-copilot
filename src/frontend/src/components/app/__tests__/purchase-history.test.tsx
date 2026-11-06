import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { MyTopUpPurchase } from "@/lib/market-view.functions";

const getMyTopUps =
  vi.fn<(input: { data: { limit: number } }) => Promise<{ items: MyTopUpPurchase[] }>>();
vi.mock("@tanstack/react-start", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@tanstack/react-start")>()),
  useServerFn: (fn: unknown) => fn,
}));
vi.mock("@/lib/market-view.functions", () => ({
  getMyTopUps: (input: { data: { limit: number } }) => getMyTopUps(input),
  getUsage: vi.fn(),
  getWatchlist: vi.fn(),
  getMarketSummary: vi.fn(),
}));

const { PurchaseHistory } = await import("../purchase-history");

function renderHistory() {
  return render(
    <QueryClientProvider
      client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}
    >
      <PurchaseHistory />
    </QueryClientProvider>,
  );
}

describe("PurchaseHistory", () => {
  it("lists purchases with credits, date and a readable status", async () => {
    getMyTopUps.mockResolvedValue({
      items: [
        {
          invoiceId: "inv-2",
          creditsPurchased: 500,
          status: "granted",
          paidAtUtc: "2026-11-02T08:00:00Z",
          grantedAtUtc: "2026-11-02T08:00:05Z",
          expiresAtUtc: "2026-12-01T00:00:00Z",
        },
        {
          invoiceId: "inv-1",
          creditsPurchased: 100,
          status: "expired-unfulfilled",
          paidAtUtc: "2026-10-01T08:00:00Z",
        },
      ],
    });
    renderHistory();

    expect(await screen.findByText("۵۰۰ اعتبار")).toBeInTheDocument();
    expect(screen.getByText("به حساب اضافه شد")).toBeInTheDocument();
    expect(screen.getByText("۱۰۰ اعتبار")).toBeInTheDocument();
    expect(screen.getByText("اعمال نشد · پایان اشتراک")).toBeInTheDocument();
    expect(screen.getByText(/معتبر تا/)).toBeInTheDocument();
    expect(getMyTopUps).toHaveBeenCalledWith({ data: { limit: 10 } });
  });

  it("says so when the account has no purchases", async () => {
    getMyTopUps.mockResolvedValue({ items: [] });
    renderHistory();

    expect(
      await screen.findByText("هنوز خرید اعتباری برای این حساب ثبت نشده است."),
    ).toBeInTheDocument();
  });

  it("offers a retry when the history cannot be loaded", async () => {
    getMyTopUps.mockRejectedValue(new Error("down"));
    renderHistory();

    expect(await screen.findByRole("alert")).toHaveTextContent("تاریخچهٔ خرید در دسترس نیست.");
    expect(screen.getByRole("button", { name: "تلاش مجدد" })).toBeInTheDocument();
  });
});
