import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { MessageList } from "../message-list";
import { ValuationGaugeCards } from "../valuation-gauge-cards";
import type { AssistantChatBlock, AssistantContentResponse } from "@/lib/chat.functions";
import { mapAssistantBlock } from "@/lib/chat/assistant-block-mapper";
import { dialPoint, formatExactDecimal, formatTehranDateTime } from "@/lib/chat/valuation-gauges";
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

  it("shows Tehran time in the Persian calendar", () => {
    expect(formatTehranDateTime("2026-11-26T08:10:00Z")).toBe("۱۴۰۵/۰۹/۰۵ ۱۱:۴۰");
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
  });

  it("leaves an old answer without cards on its text", () => {
    const block = mapAssistantBlock(content({ valuationGaugeCards: null }), "");
    expect(block.valuationGaugeCards).toBeUndefined();
    expect(block.message).toBe("متن تلگرام و تاریخچه");
  });
});

describe("ValuationGaugeCards", () => {
  it("draws one P/E card with the needle and lists every value as text", () => {
    render(<ValuationGaugeCards data={gaugeResult([ratioCard("Pe")])} />);

    const card = screen.getByTestId("valuation-gauge-card-Pe");
    expect(within(card).getByRole("heading", { name: /گیج P\/E/ })).toBeInTheDocument();
    expect(within(card).getByTestId("valuation-gauge-needle")).toBeInTheDocument();
    expect(
      within(card).getByRole("img", { name: /نسبت فعلی ۶٫۴۲۹۱، بین میانگین و حد بالا/ }),
    ).toBeInTheDocument();
    expect(within(card).getByText("نسبت فعلی")).toBeInTheDocument();
    expect(within(card).getByText("بین میانگین و حد بالا")).toBeInTheDocument();
    expect(within(card).getAllByRole("row")).toHaveLength(7);
    expect(within(card).getAllByText("۳۶٫۱٪")).toHaveLength(2);
    expect(card).toHaveTextContent("زمان دریافت داده: ۱۴۰۵/۰۹/۰۵ ۱۱:۴۰");
  });

  it("puts the average marker at the top of the dial", () => {
    render(<ValuationGaugeCards data={gaugeResult([ratioCard("Pe")])} />);
    const lines = screen.getByTestId("valuation-gauge-dial").querySelectorAll("g line");
    const average = Array.from(lines).find((line) => line.getAttribute("stroke-width") === "2");
    expect(Number(average?.getAttribute("x1"))).toBeCloseTo(150);
    expect(Number(average?.getAttribute("x2"))).toBeCloseTo(150);
  });

  it("draws no needle when there is no current value", () => {
    render(<ValuationGaugeCards data={gaugeResult([ratioCard("Ps", { current: null })])} />);

    const card = screen.getByTestId("valuation-gauge-card-Ps");
    expect(within(card).queryByTestId("valuation-gauge-needle")).not.toBeInTheDocument();
    expect(within(card).getByText("در دسترس نیست")).toBeInTheDocument();
    expect(card).toHaveAttribute("data-state", "Partial");
  });

  it("says in text when the needle is pinned to an end of the axis", () => {
    render(
      <ValuationGaugeCards
        data={gaugeResult([
          ratioCard("Pe", { current: "11.2", needleAngle: 180, side: "PinnedAbove" }),
        ])}
      />,
    );
    expect(screen.getByTestId("valuation-gauge-card-Pe")).toHaveTextContent(
      "نشانگر روی انتهای محور نگه داشته شده است",
    );
  });

  it("shows the equilibrium range with the quote and its own source and time", () => {
    render(<ValuationGaugeCards data={gaugeResult([equilibriumCard()], "اخابر")} />);

    const card = screen.getByTestId("valuation-gauge-card-Equilibrium");
    expect(within(card).getByTestId("valuation-gauge-needle")).toBeInTheDocument();
    expect(card).toHaveTextContent("۴٬۱۲۰");
    expect(card).toHaveTextContent("در نیمهٔ پایین بازه");
    expect(within(card).getByTestId("valuation-gauge-quote-source")).toHaveTextContent(
      "قیمت: آخرین روز معاملاتی · نوآوران امین ·",
    );
  });

  it("shows the range only when the quote is unavailable", () => {
    render(<ValuationGaugeCards data={gaugeResult([equilibriumCard(false)], "اخابر")} />);

    const card = screen.getByTestId("valuation-gauge-card-Equilibrium");
    expect(within(card).queryByTestId("valuation-gauge-needle")).not.toBeInTheDocument();
    expect(card).toHaveTextContent("در دسترس نیست؛ فقط بازه نمایش داده شده است");
    expect(within(card).queryByTestId("valuation-gauge-quote-source")).not.toBeInTheDocument();
  });

  it("keeps the requested order and names the card that has no data", () => {
    render(
      <ValuationGaugeCards
        data={gaugeResult([
          ratioCard("Pe"),
          unavailableCard("Ps", "Stale", "stale_snapshot"),
          unavailableCard("Equilibrium"),
        ])}
      />,
    );

    const cards = screen.getAllByTestId(/^valuation-gauge-card-/);
    expect(cards.map((card) => card.dataset.testid)).toEqual([
      "valuation-gauge-card-Pe",
      "valuation-gauge-card-Ps",
      "valuation-gauge-card-Equilibrium",
    ]);
    expect(screen.getByRole("heading", { level: 2 })).toHaveTextContent("گیج‌های ارزش‌گذاری");
    expect(cards[1]).toHaveTextContent("دادهٔ منبع قدیمی است");
    expect(cards[1]).toHaveTextContent("زمان دریافت داده: ۱۴۰۵/۰۸/۲۹ ۱۱:۴۰");
    expect(cards[1]).toHaveTextContent("قدیمی");
    expect(cards[2]).toHaveTextContent("داده‌ای ثبت نشده است");
    expect(within(cards[1]).queryByRole("img")).not.toBeInTheDocument();
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
