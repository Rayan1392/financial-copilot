import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { StarterCardGrid } from "@/components/app/starter-card-grid";
import { MobileStarterList } from "@/components/mobile/mobile-starter-list";
import { homeStarterPrompts, starterCoverage } from "@/lib/chat/starter-prompts";

// Changes «valuation-gauge-starter-card» and «starter-cards-symbol-picker».
const withGauge = homeStarterPrompts({ valuationGaugeCards: true });
const withoutGauge = homeStarterPrompts();
const GAUGE_LABEL = "گیج‌های ارزش‌گذاری نماد";
const RANKING_TEXT = "رتبه‌بندی گزارش‌های ماهانه را نشان بده";
const FIXED_SYMBOLS = ["گدنا", "شاراک", "فولاد", "فاذر", "اخابر"];

describe("StarterCardGrid", () => {
  it("opens with the wide featured gauge card above the eight two-column cards", () => {
    render(<StarterCardGrid prompts={withGauge} disabled={false} onSelect={() => {}} />);

    const buttons = within(screen.getByTestId("starter-card-grid")).getAllByRole("button");
    expect(buttons).toHaveLength(9);
    expect(buttons[0]).toHaveAttribute("data-featured");
    expect(buttons[0].className).toContain("md:col-span-2");
    expect(buttons.slice(1).every((button) => !button.className.includes("col-span-2"))).toBe(true);
    expect(buttons[0]).toHaveAccessibleName(expect.stringContaining(GAUGE_LABEL));
    expect(within(buttons[0]).getByTestId("starter-card-detail").textContent).toBe(
      "P/E · P/S · قیمت تعادلی",
    );
    expect(within(buttons[0]).getByTestId("starter-card-dial")).toHaveAttribute(
      "aria-hidden",
      "true",
    );
  });

  it("shows the «نماد» slot instead of a fixed symbol on the six symbol cards", () => {
    render(<StarterCardGrid prompts={withGauge} disabled={false} onSelect={() => {}} />);

    const grid = screen.getByTestId("starter-card-grid");
    const symbolCards = within(grid)
      .getAllByRole("button")
      .filter((button) => button.getAttribute("aria-haspopup") === "dialog");
    expect(symbolCards).toHaveLength(6);
    for (const card of symbolCards) {
      expect(within(card).getByTestId("starter-symbol-slot")).toHaveTextContent("نماد");
      for (const symbol of FIXED_SYMBOLS) expect(card).not.toHaveTextContent(symbol);
    }
    expect(grid).toHaveTextContent("مقایسه نماد با صنعت");
  });

  it("hands the selected card to the page, which sends or asks for the symbol", () => {
    const onSelect = vi.fn();
    render(<StarterCardGrid prompts={withGauge} disabled={false} onSelect={onSelect} />);

    fireEvent.click(screen.getByRole("button", { name: new RegExp(GAUGE_LABEL) }));
    fireEvent.click(screen.getByRole("button", { name: new RegExp(RANKING_TEXT) }));

    expect(onSelect.mock.calls.map(([prompt]) => [prompt.id, prompt.kind])).toEqual([
      ["valuation-gauges", "symbol"],
      ["ranking", "direct"],
    ]);
  });

  it("marks only the two fixed-symbol cards as «نمونه»", () => {
    render(<StarterCardGrid prompts={withGauge} disabled={false} onSelect={() => {}} />);

    const marked = screen
      .getAllByTestId("starter-example-mark")
      .map((mark) => mark.closest("button")?.textContent);
    expect(marked).toHaveLength(2);
    expect(marked[0]).toContain("ترکیب فروش محصولات کگهر را بررسی کن");
    expect(marked[1]).toContain("روند فروش گندله کگهر در ۱۲ ماهه اخیر چطور بوده؟");
    expect(screen.getByRole("button", { name: new RegExp(RANKING_TEXT) })).not.toHaveAttribute(
      "aria-haspopup",
    );
  });

  it("shows eight cards with no featured card while gauge cards are unavailable", () => {
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
  it("puts the gauge row first with its icon, slot and detail line", () => {
    const onSelect = vi.fn();
    render(<MobileStarterList prompts={withGauge} disabled={false} onSelect={onSelect} />);

    const rows = screen.getAllByRole("listitem");
    expect(rows).toHaveLength(9);
    expect(rows[0]).toHaveTextContent(GAUGE_LABEL);
    expect(rows[0]).not.toHaveTextContent("گدنا");
    expect(rows[0].querySelector("svg")).not.toBeNull();
    expect(within(rows[0]).getByTestId("starter-symbol-slot")).toBeInTheDocument();
    expect(within(rows[0]).getByTestId("starter-card-detail").textContent).toBe(
      "P/E · P/S · قیمت تعادلی",
    );
    fireEvent.click(within(rows[0]).getByRole("button"));
    expect(onSelect).toHaveBeenCalledWith(expect.objectContaining({ id: "valuation-gauges" }));
  });

  it("shows six symbol rows and marks the two fixed-symbol rows as «نمونه»", () => {
    render(<MobileStarterList prompts={withGauge} disabled={false} onSelect={() => {}} />);

    expect(screen.getAllByTestId("starter-symbol-slot")).toHaveLength(6);
    expect(screen.getAllByTestId("starter-example-mark")).toHaveLength(2);
  });

  it("writes the sales topics in the emerald ink that keeps 4.5:1 in the light theme (BUG-049)", () => {
    render(<MobileStarterList prompts={withoutGauge} disabled={false} onSelect={() => {}} />);

    expect(screen.getByText("ترکیب فروش")).toHaveClass("emerald-ink-text");
    expect(screen.getByText("ترکیب فروش")).not.toHaveClass("text-emerald");
  });

  it("keeps eight rows while gauge cards are unavailable", () => {
    render(<MobileStarterList prompts={withoutGauge} disabled={false} onSelect={() => {}} />);

    expect(screen.getAllByRole("listitem")).toHaveLength(8);
    expect(screen.queryByText("ارزش‌گذاری")).not.toBeInTheDocument();
  });
});
