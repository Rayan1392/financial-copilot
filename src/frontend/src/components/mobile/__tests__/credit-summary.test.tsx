import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { UsageSummary } from "@/lib/market-view.functions";

const getUsage = vi.fn<() => Promise<UsageSummary>>();
vi.mock("@tanstack/react-start", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@tanstack/react-start")>()),
  useServerFn: (fn: unknown) => fn,
}));
vi.mock("@/lib/market-view.functions", () => ({
  getUsage: () => getUsage(),
  getWatchlist: vi.fn(),
  getMarketSummary: vi.fn(),
}));

const { CreditSummary, PURCHASE_GUIDANCE_TEXT } = await import("../credit-summary");

function renderSummary() {
  return render(
    <QueryClientProvider client={new QueryClient()}>
      <CreditSummary />
    </QueryClientProvider>,
  );
}

const usage = (overrides: Partial<UsageSummary>): UsageSummary => ({
  balance: 0,
  reservedCredits: 0,
  availableSpendingCapacity: 1200,
  walletUpdatedAt: "2026-11-03T00:00:00Z",
  planName: "حرفه‌ای",
  planIncludedCredits: 2600,
  creditQuotaMode: "Finite",
  ...overrides,
});

describe("CreditSummary (AC-07, AC-19, D-Q6)", () => {
  it("shows finite credit with a progress bar and text-only purchase guidance", async () => {
    getUsage.mockResolvedValue(usage({}));
    const { container } = renderSummary();
    expect(await screen.findByText("۱,۲۰۰ از ۲,۶۰۰")).toBeInTheDocument();
    expect(
      screen.getByRole("progressbar", { name: "اعتبار باقی‌مانده هوش مصنوعی" }),
    ).toHaveAttribute("aria-valuenow", "46");
    expect(screen.getByText(PURCHASE_GUIDANCE_TEXT)).toBeInTheDocument();
    expect(PURCHASE_GUIDANCE_TEXT).toBe(
      "برای خرید یا تمدید اعتبار، از طریق حساب کاربری خود در تحلیل‌اپ اقدام کنید.",
    );
    expect(container.querySelector("a")).toBeNull();
    expect(container.innerHTML).not.toContain("tahlilapp.com");
  });

  it("shows «نامحدود» and the expiry for unlimited plans", async () => {
    getUsage.mockResolvedValue(
      usage({
        creditQuotaMode: "Unlimited",
        subscriptionExpiresAtUtc: "2026-12-20T00:00:00Z",
        planName: "Max",
      }),
    );
    renderSummary();
    expect(await screen.findByText("نامحدود")).toBeInTheDocument();
    expect(screen.queryByRole("progressbar")).not.toBeInTheDocument();
    expect(screen.getByText(/^تا /)).toBeInTheDocument();
  });

  it("offers a retry when usage cannot be loaded", async () => {
    getUsage.mockRejectedValue(new Error("down"));
    renderSummary();
    expect(await screen.findByText("اعتبار در دسترس نیست")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "تلاش مجدد" })).toBeInTheDocument();
  });
});
