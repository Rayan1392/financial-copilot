import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { renderInRouter } from "./test-router";
import { BottomNav } from "../bottom-nav";
import { EmptyState } from "../empty-state";
import { ErrorState } from "../error-state";
import { IconTile } from "../icon-tile";
import * as icons from "../icons";
import { SignedChange } from "../signed-change";
import { SymbolRow } from "../symbol-row";
import { STARTER_PROMPTS } from "@/lib/chat/starter-prompts";
import { cn } from "@/lib/utils";

vi.mock("@/lib/workspace/queries", () => ({
  useOwnProfile: () => ({ data: { displayName: "کاربر آزمایشی" } }),
  useUsage: () => ({
    data: { creditQuotaMode: "Finite", availableSpendingCapacity: 1200, planIncludedCredits: 2600 },
  }),
}));

const { TopAppBar } = await import("../top-app-bar");

describe("mobile type scale and class merging", () => {
  it("keeps a text color next to a mobile font-size utility", () => {
    expect(cn("text-emerald", "text-m-sub")).toBe("text-emerald text-m-sub");
    expect(cn("text-m-body", "text-foreground")).toBe("text-m-body text-foreground");
  });

  it("lets a later mobile size replace an earlier size", () => {
    expect(cn("text-xs", "text-m-cap")).toBe("text-m-cap");
    expect(cn("text-m-sub", "text-m-body")).toBe("text-m-body");
  });
});

describe("mobile icon module", () => {
  it("exports every icon as a component under a semantic name", () => {
    const names = Object.keys(icons);
    expect(names.length).toBeGreaterThan(0);
    for (const name of names) {
      expect(name).toMatch(/Icon$/);
      expect((icons as Record<string, unknown>)[name]).toBeTruthy();
    }
  });

  it("IconTile is decorative", () => {
    const { container } = render(<IconTile icon={icons.SalesMixIcon} tone="gold" />);
    expect(container.firstElementChild).toHaveAttribute("aria-hidden", "true");
    expect(container.querySelector("svg")).toBeInTheDocument();
  });
});

describe("SignedChange badge", () => {
  it("keeps the direction glyph, the screen-reader label and the color in a pill", () => {
    const { container } = render(
      <SignedChange value={-4.8} variant="badge" className="text-m-sub" />,
    );
    const badge = container.firstElementChild!;
    expect(badge).toHaveTextContent("▼");
    expect(badge).toHaveTextContent("کاهش");
    expect(badge).toHaveClass("rounded-full", "mobile-danger-text", "text-m-sub");
  });
});

describe("SymbolRow", () => {
  it("shows symbol, company, price and change, and opens on tap", () => {
    const onSelect = vi.fn();
    render(
      <ul>
        <SymbolRow
          symbol="شپنا"
          companyName="پالایش نفت اصفهان"
          price={5420}
          changePercent={5}
          onSelect={onSelect}
        />
      </ul>,
    );
    const row = screen.getByRole("button", { name: /شپنا/ });
    expect(row).toHaveClass("min-h-16");
    expect(row).toHaveTextContent("پالایش نفت اصفهان");
    expect(row).toHaveTextContent(/۵.۴۲۰/);
    expect(row).toHaveTextContent("▲");
    expect(screen.queryByText("داده قدیمی")).not.toBeInTheDocument();
    fireEvent.click(row);
    expect(onSelect).toHaveBeenCalledOnce();
  });

  it("marks a lagging quote and renders «–» for a missing price", () => {
    render(
      <ul>
        <SymbolRow symbol="فولاد" price={null} changePercent={null} stale onSelect={vi.fn()} />
      </ul>,
    );
    expect(screen.getByText("داده قدیمی")).toBeInTheDocument();
    expect(screen.getAllByText("–")).toHaveLength(2);
  });
});

describe("EmptyState and ErrorState", () => {
  it("EmptyState shows one sentence and the given action", () => {
    render(
      <EmptyState
        icon={icons.NavChatIcon}
        title="هنوز گفتگویی ندارید"
        action={<button type="button">شروع گفتگو</button>}
      />,
    );
    expect(screen.getByText("هنوز گفتگویی ندارید")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "شروع گفتگو" })).toBeInTheDocument();
  });

  it("ErrorState is an alert and retries only when a retry is given", () => {
    const onRetry = vi.fn();
    const { rerender } = render(
      <ErrorState message="داده بازار در دسترس نیست" onRetry={onRetry} />,
    );
    expect(screen.getByRole("alert")).toHaveTextContent("داده بازار در دسترس نیست");
    fireEvent.click(screen.getByRole("button", { name: "تلاش مجدد" }));
    expect(onRetry).toHaveBeenCalledOnce();
    rerender(<ErrorState message="دسترسی به داده بازار برای اشتراک شما فعال نیست" />);
    expect(screen.queryByRole("button", { name: "تلاش مجدد" })).not.toBeInTheDocument();
  });
});

describe("BottomNav active state", () => {
  it("fills the active tab's icon and leaves the others regular", async () => {
    await renderInRouter(<BottomNav onReselect={vi.fn()} />, { path: "/m/watchlist" });
    const active = screen.getByRole("link", { name: "دیده‌بان" });
    expect(active).toHaveClass("font-bold", "text-emerald");
    expect(active.querySelector("span[aria-hidden='true']")).toHaveClass("bg-emerald-soft");
    const idle = screen.getByRole("link", { name: "بازار" });
    expect(idle.querySelector("span[aria-hidden='true']")).not.toHaveClass("bg-emerald-soft");
  });
});

describe("TopAppBar", () => {
  it("a tab screen shows account, title, credit and new chat, and no admin entry", async () => {
    await renderInRouter(<TopAppBar title="بازار" />);
    expect(screen.getByRole("heading", { level: 1, name: "بازار" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "حساب کاربری" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /اعتبار هوش مصنوعی: ۱.۲۰۰/ })).toHaveClass("min-h-11");
    expect(screen.getByRole("link", { name: "گفتگوی جدید" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "پنل مدیریت" })).not.toBeInTheDocument();
  });

  it("hides «گفتگوی جدید» on the new-chat screen and shows back on pushed screens", async () => {
    const { unmount } = await renderInRouter(<TopAppBar title="ساپیو" hideNewChat />);
    expect(screen.queryByRole("link", { name: "گفتگوی جدید" })).not.toBeInTheDocument();
    unmount();
    await renderInRouter(<TopAppBar title="گفتگو" backFallback="/m/history" />);
    expect(screen.getByRole("button", { name: "بازگشت" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /اعتبار هوش مصنوعی/ })).not.toBeInTheDocument();
  });
});

describe("starter prompts on the phone cards", () => {
  it("sends the same six questions as before the card redesign", () => {
    expect(STARTER_PROMPTS.map((prompt) => prompt.text)).toEqual([
      "ترکیب فروش محصولات کگهر را بررسی کن",
      "روند فروش ماهانه کگهر را نشان بده",
      "روند فروش گندله کگهر در ۱۲ ماهه اخیر چطور بوده؟",
      "رتبه‌بندی گزارش‌های ماهانه را نشان بده",
      "عملکرد مالی فصلی فولاد را بررسی کن",
      "صورت وضعیت مالی فاذر را بررسی کن",
    ]);
  });

  it("gives every card a unique id and a short topic label", () => {
    expect(new Set(STARTER_PROMPTS.map((prompt) => prompt.id)).size).toBe(STARTER_PROMPTS.length);
    for (const prompt of STARTER_PROMPTS) {
      expect(prompt.topic.length).toBeGreaterThan(0);
      expect(prompt.topic.length).toBeLessThan(prompt.text.length);
    }
  });
});
