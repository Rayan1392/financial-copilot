import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { StarterCardGrid } from "@/components/app/starter-card-grid";
import { MobileStarterList } from "@/components/mobile/mobile-starter-list";
import { homeStarterPrompts, starterCoverage } from "@/lib/chat/starter-prompts";

// Change «valuation-gauge-starter-card» T2.
const withGauge = homeStarterPrompts([], { valuationGaugeCards: true }).prompts;
const withoutGauge = homeStarterPrompts([]).prompts;
const GAUGE_TEXT = "گیج‌های ارزش‌گذاری گدنا";

describe("StarterCardGrid", () => {
  it("opens with the wide featured gauge card above the eight two-column cards", () => {
    render(<StarterCardGrid prompts={withGauge} disabled={false} onSelect={() => {}} />);

    const buttons = within(screen.getByTestId("starter-card-grid")).getAllByRole("button");
    expect(buttons).toHaveLength(9);
    expect(buttons[0]).toHaveAttribute("data-featured");
    expect(buttons[0].className).toContain("md:col-span-2");
    expect(buttons.slice(1).every((button) => !button.className.includes("col-span-2"))).toBe(true);
    expect(buttons[0]).toHaveAccessibleName(expect.stringContaining(GAUGE_TEXT));
    expect(within(buttons[0]).getByTestId("starter-card-detail").textContent).toBe(
      "P/E · P/S · قیمت تعادلی",
    );
    expect(within(buttons[0]).getByTestId("starter-card-dial")).toHaveAttribute(
      "aria-hidden",
      "true",
    );
  });

  it("sends exactly the card's question", () => {
    const onSelect = vi.fn();
    render(<StarterCardGrid prompts={withGauge} disabled={false} onSelect={onSelect} />);

    fireEvent.click(screen.getByRole("button", { name: new RegExp(GAUGE_TEXT) }));

    expect(onSelect).toHaveBeenCalledWith(GAUGE_TEXT);
  });

  it("shows today's eight cards with no featured card while gauge cards are unavailable", () => {
    render(<StarterCardGrid prompts={withoutGauge} disabled={false} onSelect={() => {}} />);

    const buttons = within(screen.getByTestId("starter-card-grid")).getAllByRole("button");
    expect(buttons).toHaveLength(8);
    expect(screen.queryByTestId("starter-card-dial")).not.toBeInTheDocument();
  });
});

describe("starterCoverage", () => {
  it("names valuation only while the gauge card is shown (OQ-142)", () => {
    expect(starterCoverage(withGauge).badge).toBe(
      "پوشش فعلی: تولید، فروش، گزارش‌های مالی و ارزش‌گذاری",
    );
    expect(starterCoverage(withGauge).sentence).toContain(
      "جریان وجوه نقد و گیج‌های ارزش‌گذاری نمادها",
    );
    expect(starterCoverage(withoutGauge).badge).toBe("پوشش فعلی: تولید، فروش و گزارش‌های مالی");
    expect(starterCoverage(withoutGauge).sentence).not.toContain("ارزش‌گذاری");
  });
});

describe("MobileStarterList", () => {
  it("puts the gauge row first with its icon and detail line", () => {
    const onSelect = vi.fn();
    render(<MobileStarterList prompts={withGauge} disabled={false} onSelect={onSelect} />);

    const rows = screen.getAllByRole("listitem");
    expect(rows).toHaveLength(9);
    expect(rows[0]).toHaveTextContent(GAUGE_TEXT);
    expect(rows[0].querySelector("svg")).not.toBeNull();
    expect(within(rows[0]).getByTestId("starter-card-detail").textContent).toBe(
      "P/E · P/S · قیمت تعادلی",
    );
    fireEvent.click(within(rows[0]).getByRole("button"));
    expect(onSelect).toHaveBeenCalledWith(GAUGE_TEXT);
  });

  it("writes the sales topics in the emerald ink that keeps 4.5:1 in the light theme (BUG-049)", () => {
    render(<MobileStarterList prompts={withoutGauge} disabled={false} onSelect={() => {}} />);

    expect(screen.getByText("ترکیب فروش")).toHaveClass("emerald-ink-text");
    expect(screen.getByText("ترکیب فروش")).not.toHaveClass("text-emerald");
  });

  it("keeps today's eight rows while gauge cards are unavailable", () => {
    render(<MobileStarterList prompts={withoutGauge} disabled={false} onSelect={() => {}} />);

    expect(screen.getAllByRole("listitem")).toHaveLength(8);
    expect(screen.queryByText(GAUGE_TEXT)).not.toBeInTheDocument();
  });
});
