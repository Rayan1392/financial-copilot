import { fireEvent, render, screen, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { UserAnalyticsPanel } from "../user-analytics-panel";
import {
  adminApi,
  type AdminUserAnalyticsCube,
} from "@/integrations/financial-copilot/admin-client";

vi.mock("@/integrations/financial-copilot/admin-client", () => ({
  adminApi: { userAnalytics: vi.fn(), subscriptionUsers: vi.fn() },
}));

const cube: AdminUserAnalyticsCube = {
  cells: [
    {
      acquisitionSource: "tahlilapp",
      plan: "Free",
      status: "enabled",
      activity: "active7d",
      registeredOn: "2026-11-01",
      users: 10,
    },
    {
      acquisitionSource: "tahlilapp",
      plan: "Plus",
      status: "enabled",
      activity: "dormant",
      registeredOn: "2026-11-02",
      users: 4,
    },
    {
      acquisitionSource: "native",
      plan: "none",
      status: "enabled",
      activity: "never",
      registeredOn: null,
      users: 6,
    },
  ],
  plans: [
    { code: "Free", name: "Free" },
    { code: "Plus", name: "Plus" },
  ],
  subscriptionBreakdownAvailable: true,
  reportingTimeZone: "Asia/Tehran",
  asOfUtc: "2026-11-09T08:00:00Z",
};

describe("UserAnalyticsPanel", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(adminApi.userAnalytics).mockResolvedValue(cube);
  });

  it("drills a source down into its plans", async () => {
    render(<UserAnalyticsPanel />);

    const drill = (await screen.findByText("تحلیل سلسله‌مراتبی (Drill-down)")).closest("section")!;
    const tahlilApp = within(drill).getByRole("button", { name: /تحلیل‌اپ/ });
    expect(tahlilApp).toHaveAttribute("aria-expanded", "false");

    fireEvent.click(tahlilApp);

    expect(tahlilApp).toHaveAttribute("aria-expanded", "true");
    expect(within(drill).getByRole("button", { name: /رایگان/ })).toBeInTheDocument();
    expect(within(drill).getByRole("button", { name: /پلاس/ })).toBeInTheDocument();
  });

  it("cross-filters the whole page from a slicer", async () => {
    render(<UserAnalyticsPanel />);

    await screen.findByRole("region", { name: "شاخص‌های کلیدی" });
    const usersKpi = () =>
      screen.getByText("کاربران (با فیلتر فعلی)").closest("div")!.parentElement!;
    expect(within(usersKpi()).getByText("۲۰")).toBeInTheDocument();

    const slicers = screen.getByRole("region", { name: "تفکیک‌ها" });
    fireEvent.click(within(slicers).getByRole("button", { name: /تحلیل‌اپ/ }));

    expect(within(usersKpi()).getByText("۱۴")).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "حذف فیلتر منبع جذب: تحلیل‌اپ" }),
    ).toBeInTheDocument();
  });

  it("opens a paginated user list for the selected plan", async () => {
    vi.mocked(adminApi.subscriptionUsers)
      .mockResolvedValueOnce({
        items: [
          {
            userId: "user-1",
            customerAccountId: "account-1",
            email: "plus@example.test",
            displayName: "Plus User",
            isEnabled: true,
            isLockedOut: false,
            planCode: "Plus",
            effectiveFromUtc: "2026-10-01T08:00:00Z",
            effectiveToUtc: null,
          },
        ],
        nextCursor: "next-plus-page",
        hasMore: true,
        asOfUtc: "2026-11-09T08:00:00Z",
      })
      .mockResolvedValueOnce({
        items: [
          {
            userId: "user-2",
            customerAccountId: "account-2",
            email: "plus-two@example.test",
            displayName: "Second Plus User",
            isEnabled: false,
            isLockedOut: false,
            planCode: "Plus",
            effectiveFromUtc: null,
            effectiveToUtc: null,
          },
        ],
        nextCursor: null,
        hasMore: false,
        asOfUtc: "2026-11-09T08:00:00Z",
      });

    render(<UserAnalyticsPanel />);
    fireEvent.click(await screen.findByRole("button", { name: /Plus.*مشاهدهٔ فهرست/ }));

    expect(await screen.findByText("plus@example.test")).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "تاریخ فعال‌سازی" })).toBeInTheDocument();
    expect(adminApi.subscriptionUsers).toHaveBeenNthCalledWith(1, "Plus", {
      pageSize: 20,
      cursor: null,
    });

    fireEvent.click(screen.getByRole("button", { name: "صفحهٔ بعد" }));
    expect(await screen.findByText("plus-two@example.test")).toBeInTheDocument();
    expect(screen.getByText("نامشخص")).toBeInTheDocument();
    expect(adminApi.subscriptionUsers).toHaveBeenNthCalledWith(2, "Plus", {
      pageSize: 20,
      cursor: "next-plus-page",
    });
  });

  it("explains a missing subscription permission instead of showing plans", async () => {
    vi.mocked(adminApi.userAnalytics).mockResolvedValue({
      ...cube,
      cells: cube.cells.map((cell) => ({ ...cell, plan: "restricted" })),
      plans: [],
      subscriptionBreakdownAvailable: false,
    });

    render(<UserAnalyticsPanel />);

    expect(await screen.findByText(/مجوز «خواندن اشتراک‌ها» لازم است/)).toBeInTheDocument();
    expect(screen.queryByRole("option", { name: "اشتراک" })).not.toBeInTheDocument();
  });
});
