import { describe, expect, it } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { OptionComparisonCard } from "../option-comparison-card";
import {
  OPTION_ADVICE_NOTE,
  OPTION_BOOK_NOT_CURRENT_NOTE,
  OPTION_COMPARISON_WORDING,
  OPTION_VIEW_NOTE,
  formatOptionDate,
  formatOptionDistance,
  formatOptionMoney,
  formatOptionPercent,
  initialOptionExpiry,
  initialOptionView,
  optionFeesText,
  optionViewsWithRows,
  type OptionComparison,
  type OptionComparisonRow,
} from "@/lib/chat/option-comparison";
import { homeStarterPrompts } from "@/lib/chat/starter-prompts";
import { symbolAnalysisPrompts } from "@/lib/chat/symbol-prompts";

const row = (
  overrides: Partial<OptionComparisonRow> & { symbol: string },
): OptionComparisonRow => ({
  strikePrice: 1000,
  contractSize: 1000,
  openInterest: 100,
  state: "Fillable",
  fillPrice: 544,
  availableVolume: 10,
  lastPrice: 540,
  isNearestToMoney: false,
  isRequestedContract: false,
  highlights: [],
  ...overrides,
});

const comparison = (overrides: Partial<OptionComparison> = {}): OptionComparison => ({
  version: 1,
  underlyingSymbol: "اخابر",
  underlyingClass: "Share",
  underlyingPrice: 1489,
  underlyingTradingDate: "2026-11-28",
  underlyingObservedAt: "2026-11-28T06:30:00Z",
  quantityInContracts: 1,
  requestedView: "Call",
  availableExpiryDates: ["2026-12-09", "2027-01-13"],
  fees: { trade: 0.001, exercise: 0.0005, shareTransferTax: 0.005 },
  orderBookIsCurrent: true,
  orderBookObservedAt: "2026-11-28T06:30:00Z",
  openInterestObservedAt: "2026-11-28T06:30:00Z",
  views: [
    {
      view: "Call",
      expiries: [
        {
          expiryDate: "2026-12-09",
          daysToExpiry: 11,
          rows: [
            row({
              symbol: "ضمخا9019",
              strikePrice: 800,
              fillPrice: 730,
              effectivePurchaseCost: 1531.13,
              breakEvenDistance: 0.0283,
              timeValue: 41,
              impliedAnnualRate: 1.7,
              leverage: 2.04,
              cashAtExpiry: 800400,
              highlights: ["lowest_effective_purchase_cost"],
            }),
            row({
              symbol: "ضمخا9021",
              effectivePurchaseCost: 1545.044,
              breakEvenDistance: 0.0376,
              timeValue: 55,
              impliedAnnualRate: 1.825,
              leverage: 2.74,
              cashAtExpiry: 1000500,
              isNearestToMoney: true,
            }),
            row({
              symbol: "ضمخا9027",
              strikePrice: 1600,
              state: "NoOrder",
              fillPrice: null,
              availableVolume: 0,
            }),
            row({
              symbol: "ضمخا9028",
              strikePrice: 1700,
              state: "NotFillable",
              fillPrice: null,
              availableVolume: 3,
            }),
          ],
        },
        {
          expiryDate: "2027-01-13",
          daysToExpiry: 46,
          rows: [
            row({
              symbol: "ضمخا1051",
              strikePrice: 1200,
              fillPrice: 454,
              effectivePurchaseCost: 1655,
            }),
          ],
        },
      ],
    },
    {
      view: "Put",
      expiries: [
        {
          expiryDate: "2026-12-09",
          daysToExpiry: 11,
          rows: [
            row({
              symbol: "طمخا9025",
              strikePrice: 1400,
              fillPrice: 20,
              effectiveSalePrice: 1372.28,
              distanceFromMarket: -0.0784,
              protectionCost: 0.0134,
            }),
          ],
        },
      ],
    },
    { view: "CoveredCall", expiries: [] },
  ],
  ...overrides,
});

describe("option comparison formatting", () => {
  it("formats money, percentages, distances and dates in the product's Persian style", () => {
    expect(formatOptionMoney(1545.044)).toBe("۱٬۵۴۵");
    expect(formatOptionMoney(null)).toBe("—");
    expect(formatOptionPercent(0.0376)).toBe("۳٫۸٪");
    expect(formatOptionPercent(1.825, 0)).toBe("۱۸۳٪");
    expect(formatOptionDistance(0.0376)).toBe("۳٫۸٪ بالاتر");
    expect(formatOptionDistance(-0.0784)).toBe("۷٫۸٪ پایین‌تر");
    expect(formatOptionDate("2026-12-09")).toBe("۱۴۰۵/۰۹/۱۸");
  });

  it("offers only the views that have rows and opens on the asked one", () => {
    const data = comparison();
    expect(optionViewsWithRows(data).map((item) => item.view)).toEqual(["Call", "Put"]);
    expect(initialOptionView(data)).toBe("Call");
    expect(initialOptionView(comparison({ requestedView: "CoveredCall" }))).toBe("Call");
    expect(initialOptionView(comparison({ requestedView: "Put" }))).toBe("Put");
    expect(initialOptionExpiry(data.views[0])).toBe("2026-12-09");
  });

  it("states the transfer tax only where the underlying is delivered, and none for fund units", () => {
    expect(optionFeesText(comparison(), "Call")).toBe("کارمزدها: معامله ۰٫۱۰٪، اعمال ۰٫۰۵٪");
    expect(optionFeesText(comparison(), "Put")).toContain("مالیات نقل‌وانتقال ۰٫۵۰٪");
    expect(optionFeesText(comparison({ underlyingClass: "FundUnit" }), "Put")).toContain(
      "مالیات نقل‌وانتقال ۰٫۰۰٪",
    );
  });

  it("never uses recommending words", () => {
    for (const text of OPTION_COMPARISON_WORDING)
      for (const banned of ["بهترین", "مناسب", "پیشنهاد", "فرصت"])
        expect(text).not.toContain(banned);
  });
});

describe("OptionComparisonCard", () => {
  it("shows the asked view with fillable rows, highlights, notes and data time", () => {
    render(<OptionComparisonCard data={comparison()} />);

    const card = screen.getByTestId("option-comparison-card");
    expect(
      within(card).getByRole("heading", {
        name: "مقایسهٔ هزینهٔ خرید از مسیر اختیار خرید — اخابر",
      }),
    ).toBeTruthy();
    expect(within(card).getByText("۱٬۴۸۹")).toBeTruthy();
    const rows = within(card).getAllByRole("row").slice(1);
    expect(rows).toHaveLength(2);
    expect(within(rows[0]).getByText("ضمخا۹۰۱۹")).toBeTruthy();
    expect(within(rows[0]).getByText("کمترین هزینهٔ مؤثر")).toBeTruthy();
    expect(within(rows[1]).getByText("۱٬۵۴۵")).toBeTruthy();
    expect(within(rows[1]).getByText("۳٫۸٪ بالاتر")).toBeTruthy();
    expect(within(rows[1]).getByText("نزدیک به قیمت سهم")).toBeTruthy();
    expect(within(card).getByText(OPTION_VIEW_NOTE.Call)).toBeTruthy();
    expect(within(card).getByText(OPTION_ADVICE_NOTE)).toBeTruthy();
    expect(within(card).getByText("کارمزدها: معامله ۰٫۱۰٪، اعمال ۰٫۰۵٪")).toBeTruthy();
    expect(within(card).getByText(/زمان عمق سفارش: ۱۴۰۵\/۰۹\/۰۷/)).toBeTruthy();
    expect(within(card).getByText(/مبلغ لازم در سررسید برای اعمال یک قرارداد/)).toBeTruthy();
  });

  it("keeps rows without orders collapsed until asked", () => {
    render(<OptionComparisonCard data={comparison()} />);

    const toggle = screen.getByRole("button", { name: /نمایش بدون سفارش‌ها/ });
    expect(toggle.getAttribute("aria-expanded")).toBe("false");
    expect(screen.queryByText("ضمخا۹۰۲۷")).toBeNull();

    fireEvent.click(toggle);

    expect(screen.getByText("ضمخا۹۰۲۷")).toBeTruthy();
    expect(screen.getByText("بدون سفارش")).toBeTruthy();
    expect(screen.getByText("حجم ناکافی (۳)")).toBeTruthy();
    expect(screen.getByRole("button", { name: /پنهان‌کردن بدون سفارش‌ها/ })).toBeTruthy();
  });

  it("switches view and expiry with tabs", () => {
    render(<OptionComparisonCard data={comparison()} />);

    fireEvent.click(screen.getByRole("tab", { name: /۱۴۰۵\/۱۰\/۲۳/ }));
    expect(screen.getByText("ضمخا۱۰۵۱")).toBeTruthy();
    expect(screen.queryByText("ضمخا۹۰۲۱")).toBeNull();

    fireEvent.click(screen.getByRole("tab", { name: "اختیار فروش" }));
    expect(
      screen.getByRole("heading", { name: "مقایسهٔ قیمت فروش از مسیر اختیار فروش — اخابر" }),
    ).toBeTruthy();
    expect(screen.getByText("طمخا۹۰۲۵")).toBeTruthy();
    expect(screen.getByText("۷٫۸٪ پایین‌تر")).toBeTruthy();
    expect(screen.getByText(OPTION_VIEW_NOTE.Put)).toBeTruthy();
    expect(screen.queryByRole("tab", { name: "کاورد کال" })).toBeNull();
  });

  it("shows trade prices only when the order book is not current", () => {
    render(<OptionComparisonCard data={comparison({ orderBookIsCurrent: false })} />);

    expect(screen.getByRole("status").textContent).toBe(OPTION_BOOK_NOT_CURRENT_NOTE);
    expect(screen.getByRole("columnheader", { name: "آخرین معامله" })).toBeTruthy();
    expect(screen.queryByRole("columnheader", { name: "هزینهٔ مؤثر" })).toBeNull();
    expect(screen.getAllByRole("row").slice(1)).toHaveLength(4);
    expect(screen.queryByRole("button", { name: /بدون سفارش‌ها/ })).toBeNull();
  });

  it("opens on the expiry of the asked contract and marks its row", () => {
    const data = comparison();
    data.views[0].expiries[1].rows[0].isRequestedContract = true;

    render(<OptionComparisonCard data={data} />);

    const requested = screen.getByText("ضمخا۱۰۵۱").closest("tr");
    expect(requested?.getAttribute("data-requested")).toBe("true");
  });

  it("renders nothing when no view has rows", () => {
    const { container } = render(<OptionComparisonCard data={comparison({ views: [] })} />);
    expect(container.firstChild).toBeNull();
  });
});

describe("option entry points", () => {
  it("offers the starter card only while option answers are available", () => {
    expect(homeStarterPrompts().some((prompt) => prompt.id === "option-calls")).toBe(false);
    const card = homeStarterPrompts({ optionComparison: true }).find(
      (prompt) => prompt.id === "option-calls",
    );
    expect(card?.kind).toBe("symbol");
    expect(card?.kind === "symbol" ? card.forSymbol("اخابر") : null).toBe("اختیار خرید اخابر");
  });

  it("adds the symbol shortcut only for symbols that have listed options", () => {
    expect(symbolAnalysisPrompts("اخابر").some((prompt) => prompt.id === "option-calls")).toBe(
      false,
    );
    expect(
      symbolAnalysisPrompts("اخابر", { optionCalls: true }).find(
        (prompt) => prompt.id === "option-calls",
      )?.text,
    ).toBe("اختیار خرید اخابر");
  });
});
