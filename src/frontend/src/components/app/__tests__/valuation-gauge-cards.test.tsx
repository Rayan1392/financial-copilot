import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { MessageList } from "../message-list";
import { ValuationGaugeCards } from "../valuation-gauge-cards";
import type { AssistantChatBlock, AssistantContentResponse } from "@/lib/chat.functions";
import { mapAssistantBlock } from "@/lib/chat/assistant-block-mapper";
import {
  dialPoint,
  formatExactDecimal,
  formatPercent,
  formatRatio2,
  formatRial0,
  formatTehranDate,
  formatTehranDateTime,
  roundDecimalString,
} from "@/lib/chat/valuation-gauges";
import {
  equilibriumCard,
  gaugeResult,
  ratioCard,
  unavailableCard,
} from "./valuation-gauge-fixtures";

vi.mock("@/components/app/follow-symbol-button", () => ({
  FollowSymbolButton: () => null,
}));

describe("valuation gauge formatting", () => {
  it("formats exact decimals in Persian without rounding and keeps the minus sign attached", () => {
    expect(formatExactDecimal("4312.6801")).toBe("۴٬۳۱۲٫۶۸۰۱");
    expect(formatExactDecimal("-12.5")).toBe("‎−۱۲٫۵");
    expect(formatExactDecimal("6")).toBe("۶");
  });

  it.each([
    ["6.999044944", 2, "7.00"],
    ["5.880866117458263", 2, "5.88"],
    ["3.3487316736139543", 2, "3.35"],
    ["7", 2, "7.00"],
    ["1.005", 2, "1.01"],
    ["-0.004", 2, "0.00"],
    ["4120.5", 0, "4121"],
    ["4120.4", 0, "4120"],
    ["-12.5", 0, "-13"],
    [".5", 0, "1"],
    ["123456789012345678901234567890.125", 2, "123456789012345678901234567890.13"],
    ["n/a", 2, "n/a"],
  ] as const)("rounds %s to %i decimals half away from zero", (value, decimals, expected) => {
    expect(roundDecimalString(value, decimals)).toBe(expected);
  });

  it("shows P/E and P/S with two decimals and prices in whole rials", () => {
    expect(formatRatio2("6.999044944")).toBe("۷٫۰۰");
    expect(formatRatio2("59.613521843")).toBe("۵۹٫۶۱");
    expect(formatRial0("4120.5")).toBe("۴٬۱۲۱");
    expect(formatRial0("-12.5")).toBe("‎−۱۳");
  });

  it("signs only positive distances", () => {
    expect(formatPercent("19.0", true)).toBe("‎+۱۹٫۰٪");
    expect(formatPercent("-6.4", true)).toBe("‎−۶٫۴٪");
    expect(formatPercent("0.0", true)).toBe("۰٫۰٪");
    expect(formatPercent("57.4")).toBe("۵۷٫۴٪");
  });

  it("shows Tehran dates in the Persian calendar, with and without the time", () => {
    expect(formatTehranDateTime("2026-11-26T08:10:00Z")).toBe("۱۴۰۵/۰۹/۰۵ ۱۱:۴۰");
    expect(formatTehranDate("2026-11-26T08:10:00Z")).toBe("۱۴۰۵/۰۹/۰۵");
  });

  it("puts 0° at the left end, 90° on top and 180° at the right end", () => {
    const left = dialPoint(0, 100);
    const top = dialPoint(90, 100);
    const right = dialPoint(180, 100);
    expect(left.x).toBeCloseTo(50);
    expect(left.y).toBeCloseTo(142);
    expect(top.x).toBeCloseTo(150);
    expect(top.y).toBeCloseTo(42);
    expect(right.x).toBeCloseTo(250);
  });
});

describe("valuation gauge mapping", () => {
  const content = (extra: Partial<AssistantContentResponse>): AssistantContentResponse =>
    ({
      intent: "ValuationGauges",
      clarificationRequired: false,
      textAnswer: "متن تلگرام و تاریخچه",
      ...extra,
    }) as AssistantContentResponse;

  it("maps the live and the stored answer to the same block", () => {
    const cards = gaugeResult([ratioCard("Pe")]);
    const live = mapAssistantBlock(content({ valuationGaugeCards: cards }), "");
    const stored = mapAssistantBlock(
      JSON.parse(JSON.stringify(content({ valuationGaugeCards: cards }))),
      "متن تلگرام و تاریخچه",
    );
    expect(live.valuationGaugeCards).toEqual(cards);
    expect(stored.valuationGaugeCards).toEqual(live.valuationGaugeCards);
    expect(stored.valuationGaugeCards?.cards[0].insights?.currentBand).toBe(4);
  });

  it("leaves an old answer without cards on its text", () => {
    const block = mapAssistantBlock(content({ valuationGaugeCards: null }), "");
    expect(block.valuationGaugeCards).toBeUndefined();
    expect(block.message).toBe("متن تلگرام و تاریخچه");
  });
});

describe("ValuationGaugeCards ratio card", () => {
  it("draws one P/E card with rounded values, the derived tiles and the history sentence", () => {
    render(<ValuationGaugeCards data={gaugeResult([ratioCard("Pe")])} />);

    const card = screen.getByTestId("valuation-gauge-card-Pe");
    expect(within(card).getByRole("heading", { level: 3 })).toHaveTextContent(
      "گیج P/E شاراک · پتروشیمی شازند",
    );
    expect(screen.queryByRole("heading", { level: 2 })).not.toBeInTheDocument();
    expect(
      within(card).getByRole("img", { name: "P/E شاراک: ۶٫۴۳، بین میانگین و حد بالا" }),
    ).toBeInTheDocument();
    expect(within(card).getByTestId("valuation-gauge-hero")).toHaveTextContent("۶٫۴۳");
    expect(within(card).getByTestId("valuation-gauge-position")).toHaveTextContent(
      "بین میانگین و حد بالا",
    );
    expect(card).toHaveTextContent("میانگین تاریخی۶٫۰۰");
    expect(card).toHaveTextContent("فاصله تا میانگین‎+۷٫۲٪گران‌تر از میانگین");
    expect(card).toHaveTextContent("روزهای ارزان‌تر۵۵٫۴٪زیر بازهٔ امروز");
    expect(within(card).getByTestId("valuation-gauge-history-sentence")).toHaveTextContent(
      "در ۵۵٫۴٪ روزها P/E پایین‌تر از بازهٔ امروز بوده و ۳۱٫۳٪ روزها در همین بازه.",
    );
    expect(card).not.toHaveTextContent("۶٫۴۲۹۱");
  });

  it("raises the band that holds the needle and labels the bounds in their colours", () => {
    render(<ValuationGaugeCards data={gaugeResult([ratioCard("Pe")])} />);

    const bands = screen.getAllByTestId("valuation-gauge-band");
    expect(bands.map((band) => band.hasAttribute("data-active"))).toEqual([
      false,
      false,
      false,
      true,
      false,
      false,
    ]);
    expect(bands[2]).toHaveTextContent("۳۶٫۱٪");
    const min = screen.getByTestId("valuation-gauge-marker-min");
    const average = screen.getByTestId("valuation-gauge-marker-average");
    const max = screen.getByTestId("valuation-gauge-marker-max");
    expect(min).toHaveAttribute("data-color", "var(--gauge-low)");
    expect(average).toHaveAttribute("data-color", "var(--foreground)");
    expect(max).toHaveAttribute("data-color", "var(--gauge-high)");
    expect(min).toHaveTextContent("حد پایین P/E۴٫۰۰");
    expect(average).toHaveTextContent("میانگین P/E۶٫۰۰");
    expect(max).toHaveTextContent("حد بالا P/E۸٫۰۰");
  });

  it("puts the average marker at the top of the dial", () => {
    render(<ValuationGaugeCards data={gaugeResult([ratioCard("Pe")])} />);
    const stem = screen.getByTestId("valuation-gauge-marker-average").querySelector("line");
    expect(Number(stem?.getAttribute("x1"))).toBeCloseTo(170);
    expect(Number(stem?.getAttribute("x2"))).toBeCloseTo(170);
  });

  it("shows a tapped band's range and share in text", () => {
    render(<ValuationGaugeCards data={gaugeResult([ratioCard("Pe")])} />);

    fireEvent.click(screen.getByRole("button", { name: "بازهٔ ۲: ۱۴٫۵٪ روزها" }));
    expect(screen.getByTestId("valuation-gauge-card-Pe")).toHaveTextContent(
      "بازهٔ ۲ · ۴٫۰۰ تا ۵٫۰۰ · ۱۴٫۵٪ روزها",
    );
  });

  it("keeps the guide and every band in the details table", () => {
    render(<ValuationGaugeCards data={gaugeResult([ratioCard("Pe")])} />);

    const card = screen.getByTestId("valuation-gauge-card-Pe");
    const details = card.querySelector("details")!;
    expect(details).not.toHaveAttribute("open");
    expect(within(details).getByText("راهنمای خواندن گیج و جزئیات عددی")).toBeInTheDocument();
    const rows = within(details).getAllByRole("row");
    expect(rows).toHaveLength(7);
    expect(rows[4]).toHaveTextContent("بازهٔ ۴۶٫۰۰ تا ۷٫۰۰۲۶۰۳۱٫۳٪");
    expect(details).toHaveTextContent("محور گیج از ۲٫۰۰ تا ۱۰٫۰۰");
  });

  it("names تحلیل‌اپ with the fetch date only and shows no chip for an available card", () => {
    render(<ValuationGaugeCards data={gaugeResult([ratioCard("Pe")])} />);

    const card = screen.getByTestId("valuation-gauge-card-Pe");
    expect(within(card).getByTestId("valuation-gauge-source")).toHaveTextContent(
      "تحلیل‌اپ · دریافت ۱۴۰۵/۰۹/۰۵",
    );
    expect(card).not.toHaveTextContent("CyclicalWaves");
    expect(card).not.toHaveTextContent("۱۱:۴۰");
    expect(card).toHaveTextContent("P/E یک نسبت بنیادی است، نه توصیهٔ خرید یا فروش.");
    for (const chip of ["ناقص", "قدیمی", "بدون داده"]) {
      expect(within(card).queryByText(chip)).not.toBeInTheDocument();
    }
  });

  it("shows «—» and no needle when there is no current value", () => {
    render(<ValuationGaugeCards data={gaugeResult([ratioCard("Ps", { current: null })])} />);

    const card = screen.getByTestId("valuation-gauge-card-Ps");
    expect(card).toHaveAttribute("data-state", "Partial");
    expect(within(card).getByText("ناقص")).toBeInTheDocument();
    expect(within(card).queryByTestId("valuation-gauge-needle")).not.toBeInTheDocument();
    expect(within(card).getByTestId("valuation-gauge-hero")).toHaveTextContent("—");
    expect(card).toHaveTextContent("P/S امروز ثبت نشده");
    expect(within(card).queryByTestId("valuation-gauge-history-sentence")).not.toBeInTheDocument();
    expect(card).not.toHaveTextContent("فاصله تا میانگین");
  });

  it("says in text when the needle is pinned to an end of the axis", () => {
    render(
      <ValuationGaugeCards
        data={gaugeResult([
          ratioCard("Pe", { current: "11.2", needleAngle: 180, side: "PinnedAbove" }),
        ])}
      />,
    );
    const card = screen.getByTestId("valuation-gauge-card-Pe");
    expect(card).toHaveTextContent("نشانگر روی انتهای محور نگه داشته شده است");
    expect(within(card).getByTestId("valuation-gauge-needle")).toHaveAttribute(
      "data-side",
      "PinnedAbove",
    );
    expect(card).toHaveTextContent("‎+۸۶٫۷٪");
    expect(within(card).queryByTestId("valuation-gauge-history-sentence")).not.toBeInTheDocument();
  });

  it("renders a card stored by v1.29.2 without the derived figures", () => {
    render(<ValuationGaugeCards data={gaugeResult([ratioCard("Pe", { legacy: true })])} />);

    const card = screen.getByTestId("valuation-gauge-card-Pe");
    expect(within(card).getByTestId("valuation-gauge-hero")).toHaveTextContent("۶٫۴۳");
    expect(card).toHaveTextContent("میانگین تاریخی۶٫۰۰");
    expect(card).not.toHaveTextContent("فاصله تا میانگین");
    expect(card).not.toHaveTextContent("روزهای ارزان‌تر");
    expect(within(card).queryByTestId("valuation-gauge-history-sentence")).not.toBeInTheDocument();
    // The active band still follows the needle angle; the table drops the range column.
    expect(screen.getAllByTestId("valuation-gauge-band")[3]).toHaveAttribute("data-active");
    expect(within(card.querySelector("details")!).queryByText("محدوده")).not.toBeInTheDocument();
  });
});

describe("ValuationGaugeCards equilibrium card", () => {
  it("shows the price band with the quote, the comparisons and both source lines", () => {
    render(<ValuationGaugeCards data={gaugeResult([equilibriumCard()], "اخابر")} />);

    const card = screen.getByTestId("valuation-gauge-card-Equilibrium");
    expect(within(card).getByTestId("valuation-gauge-needle")).toHaveTextContent("۴٬۱۲۰ ریال");
    expect(
      within(card).getByRole("img", {
        name: "بازهٔ قیمت تعادلی اخابر: قیمت بازار ۴٬۱۲۰ ریال، در نیمهٔ پایین بازه",
      }),
    ).toBeInTheDocument();
    expect(card).toHaveTextContent("حد پایین۳٬۸۰۰");
    expect(card).toHaveTextContent("میانه۴٬۴۰۰");
    expect(card).toHaveTextContent("حد بالا۵٬۰۰۰");
    expect(card).toHaveTextContent("جایگاهدر نیمهٔ پایین بازه");
    expect(card).toHaveTextContent("تا میانهٔ بازه‎−۶٫۴٪");
    expect(card).toHaveTextContent("بالای حد پایین‎+۸٫۴٪");
    // Owner, 2026-11-26: no provider name on the price line and no «بازه:» prefix on the range line.
    expect(within(card).getByTestId("valuation-gauge-quote-source").textContent).toBe(
      "قیمت: آخرین روز معاملاتی · ۱۴۰۵/۰۹/۰۴ ۱۲:۳۰",
    );
    expect(within(card).getByTestId("valuation-gauge-source").textContent).toBe(
      "تحلیل‌اپ · دریافت ۱۴۰۵/۰۹/۰۵",
    );
    expect(card).not.toHaveTextContent("NoavaranAmin");
    expect(card).not.toHaveTextContent("نوآوران امین");
  });

  it("places the pin at the server angle on the linear track", () => {
    render(<ValuationGaugeCards data={gaugeResult([equilibriumCard()], "اخابر")} />);
    // 48° of 180° → 26.67٪ along the track.
    expect(screen.getByTestId("valuation-gauge-needle").style.left).toContain(
      String(48 / 180).slice(0, 6),
    );
  });

  it("rests the pin at the upper end with a dashed stem when the price is above the range", () => {
    const base = equilibriumCard();
    const card = {
      ...base,
      equilibrium: {
        ...base.equilibrium!,
        quote: { ...base.equilibrium!.quote!, price: "5600" },
        position: "AboveRange" as const,
      },
      geometry: {
        ...base.geometry!,
        needle: { angle: 180, value: "5600", side: "PinnedAbove" as const },
      },
      insights: { priceVsMidpointPercent: "27.3", priceVsLowerPercent: "47.4" },
    };
    render(<ValuationGaugeCards data={gaugeResult([card], "اخابر")} />);

    const pin = screen.getByTestId("valuation-gauge-needle");
    expect(pin).toHaveAttribute("data-side", "PinnedAbove");
    // The full track length (factor 1); jsdom may reorder the calc() terms.
    expect(pin.style.left).toMatch(/(\+ 1 \* \(100% - 1\.5rem\)|\* 1\))\)?$/);
    expect(pin).toHaveTextContent("۵٬۶۰۰ ریال");
    expect(pin.querySelector(".border-dashed")).not.toBeNull();
    expect(screen.getByTestId("valuation-gauge-card-Equilibrium")).toHaveTextContent(
      "جایگاهبالاتر از بازهٔ تعادلی",
    );
  });

  // OQ-137 (owner, 2026-11-26): below the lower bound the tile reads «زیر حد پایین» with the absolute value.
  it("says «زیر حد پایین» with the absolute value when the price is below the range", () => {
    const base = equilibriumCard();
    const card = {
      ...base,
      equilibrium: {
        ...base.equilibrium!,
        quote: { ...base.equilibrium!.quote!, price: "3610" },
        position: "BelowRange" as const,
      },
      geometry: {
        ...base.geometry!,
        needle: { angle: 0, value: "3610", side: "PinnedBelow" as const },
      },
      insights: { priceVsMidpointPercent: "-18.0", priceVsLowerPercent: "-5.0" },
    };
    render(<ValuationGaugeCards data={gaugeResult([card], "اخابر")} />);

    const tile = screen.getByTestId("valuation-gauge-card-Equilibrium");
    expect(tile).toHaveTextContent("زیر حد پایین۵٫۰٪");
    expect(tile).not.toHaveTextContent("بالای حد پایین");
    expect(tile).not.toHaveTextContent("حد پایین‎−");
  });

  it("shows the range only when the quote is unavailable", () => {
    render(<ValuationGaugeCards data={gaugeResult([equilibriumCard(false)], "اخابر")} />);

    const card = screen.getByTestId("valuation-gauge-card-Equilibrium");
    expect(within(card).queryByTestId("valuation-gauge-needle")).not.toBeInTheDocument();
    expect(card).toHaveTextContent("در دسترس نیست؛ فقط بازه نمایش داده شده است");
    expect(within(card).queryByTestId("valuation-gauge-quote-source")).not.toBeInTheDocument();
    expect(within(card).getByText("ناقص")).toBeInTheDocument();
  });
});

describe("ValuationGaugeCards answer layout", () => {
  const trio = () =>
    gaugeResult([
      ratioCard("Pe"),
      unavailableCard("Ps", "Stale", "stale_snapshot"),
      unavailableCard("Equilibrium"),
    ]);

  it("keeps the requested order and names the card that has no data", () => {
    render(<ValuationGaugeCards data={trio()} />);

    const cards = screen.getAllByTestId(/^valuation-gauge-card-/);
    expect(cards.map((card) => card.dataset.testid)).toEqual([
      "valuation-gauge-card-Pe",
      "valuation-gauge-card-Ps",
      "valuation-gauge-card-Equilibrium",
    ]);
    expect(screen.getByRole("heading", { level: 2 })).toHaveTextContent(
      "گیج‌های ارزش‌گذاری شاراک پتروشیمی شازند",
    );
    // The shared heading names the company once; the cards do not repeat it.
    expect(within(cards[0]).getByRole("heading", { level: 3 })).toHaveTextContent(/^گیج P\/E$/);
    expect(cards[1]).toHaveTextContent("دادهٔ منبع قدیمی است");
    expect(cards[1]).toHaveTextContent("تحلیل‌اپ · دریافت ۱۴۰۵/۰۸/۲۹");
    expect(within(cards[1]).getByText("قدیمی")).toBeInTheDocument();
    expect(cards[2]).toHaveTextContent("داده‌ای ثبت نشده است");
    expect(within(cards[1]).queryByRole("img")).not.toBeInTheDocument();
  });

  it("offers a segmented control that shows one card at a time on a narrow message", () => {
    render(<ValuationGaugeCards data={trio()} />);

    const tabs = within(screen.getByRole("tablist", { name: "نوع گیج" })).getAllByRole("tab");
    expect(tabs.map((tab) => tab.textContent)).toEqual(["P/E", "P/S", "قیمت تعادلی"]);
    expect(tabs.map((tab) => tab.getAttribute("aria-selected"))).toEqual([
      "true",
      "false",
      "false",
    ]);
    const panels = screen.getAllByRole("tabpanel", { hidden: true });
    expect(panels.map((panel) => panel.classList.contains("hidden"))).toEqual([false, true, true]);
    expect(panels[1]).toHaveAttribute("aria-labelledby", tabs[1].id);

    fireEvent.click(tabs[2]);
    expect(tabs[2]).toHaveAttribute("aria-selected", "true");
    expect(panels.map((panel) => panel.classList.contains("hidden"))).toEqual([true, true, false]);

    // Right to left: the left arrow moves to the next tab and wraps around.
    fireEvent.keyDown(tabs[2], { key: "ArrowLeft" });
    expect(tabs[0]).toHaveAttribute("aria-selected", "true");
    expect(tabs[0]).toHaveFocus();
    fireEvent.keyDown(tabs[0], { key: "ArrowRight" });
    expect(tabs[2]).toHaveAttribute("aria-selected", "true");
    expect(tabs.map((tab) => tab.tabIndex)).toEqual([-1, -1, 0]);
  });
});

describe("MessageList with valuation gauge cards", () => {
  const block = (extra: Partial<AssistantChatBlock>): AssistantChatBlock => ({
    message: "متن تلگرام و تاریخچه",
    intent: "ValuationGauges",
    replyLanguage: "fa",
    creditsUsed: 1,
    suggestedQuestions: [],
    suggestedActions: [],
    filters: [],
    citations: [],
    ...extra,
  });

  const renderBlock = (content: AssistantChatBlock) =>
    render(
      <MessageList
        messages={[{ id: "m1", role: "assistant", content, created_at: "2026-11-26T00:00:00Z" }]}
        loading={false}
        streaming={false}
        onSuggested={vi.fn()}
      />,
    );

  it("shows the cards instead of their text", () => {
    renderBlock(block({ valuationGaugeCards: gaugeResult([ratioCard("Pe")]) }));

    expect(screen.getByTestId("valuation-gauge-cards")).toBeInTheDocument();
    expect(screen.queryByText("متن تلگرام و تاریخچه")).not.toBeInTheDocument();
  });

  it("shows the text of an answer without cards", () => {
    renderBlock(block({}));

    expect(screen.queryByTestId("valuation-gauge-cards")).not.toBeInTheDocument();
    expect(screen.getByText("متن تلگرام و تاریخچه")).toBeInTheDocument();
  });
});
