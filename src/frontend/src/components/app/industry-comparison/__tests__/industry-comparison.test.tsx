import { describe, expect, it } from "vitest";
import { render, within } from "@testing-library/react";
import { AnswerDensityContext } from "../../answer-density";
import { MarkdownMessage } from "../../markdown-message";
import { parseIndustryComparison, parsePriceEvidence } from "../industry-comparison-model";

const PRICE_ANSWER = [
  "### مقایسه نماد با هم‌گروهی‌ها",
  "**گروه صنعتی:** استخراج سنگ معدن های فلزی آهنی",
  "",
  "| نماد | P/E | P/S | قیمت به تعادلی | رتبه | قیمت و تاریخ معامله |",
  "|---|---:|---:|---:|---:|---|",
  "| کچاد | ۱۱۲٫۳۱٪ | ۵۳٫۵٪ | ۴۷٫۵۷٪ | ۱ از ۴ | ۳۴۴۰ ریال (روزانه، ۱۴۰۵/۰۸/۳۰)، تعادلی ۷۲۳۲٫۱۷ ریال |",
  "| کگهر | ۷۱٫۱۶٪ | ۴۱٫۶٪ | ۶۹٫۹٪ | ۲ از ۴ | ۱۰۵۰۰ ریال (درون‌روزی، ۱۴۰۵/۰۹/۰۳)، تعادلی ۱۵۰۲۱٫۱۳ ریال |",
  "| کنور | ۷۹٫۶٪ | ۶۷٫۸۴٪ | ۷۴٫۳۹٪ | ۳ از ۴ | — |",
  "| کگل | ۲۱۶٫۵۷٪ | ۵۸٫۱۵٪ | ۵۵٫۷۷٪ | ۴ از ۴ | ۴۱۲۰ ریال (روزانه، ۱۴۰۵/۰۸/۳۰)، تعادلی ۷۳۸۶٫۹۷ ریال |",
  "| میانگین گروه | ۱۱۹٫۹۱٪ | ۵۵٫۲۷٪ | ۶۱٫۹۱٪ | — | — |",
  "این اطلاعات توصیه به سرمایه گذاری نیست",
].join("\n");

const LEGACY_ANSWER = [
  "| نماد | P/E | P/S | قیمت به تعادلی |",
  "|---|---:|---:|---:|",
  "| شگل | ۹۰٪ | ۱۲۰٪ | — |",
  "| میانگین صنعت | ۱۰۰٪ | ۱۰۰٪ | ۹۰٪ |",
].join("\n");

describe("industry comparison model", () => {
  it("keeps member order, separates the group average and classifies each ratio against it", () => {
    const model = parseIndustryComparison(PRICE_ANSWER)!;

    expect(model.hasPriceEvidence).toBe(true);
    expect(model.members.map((row) => row.symbol)).toEqual(["کچاد", "کگهر", "کنور", "کگل"]);
    expect(model.benchmark).toEqual({
      label: "میانگین گروه",
      metrics: ["۱۱۹٫۹۱٪", "۵۵٫۲۷٪", "۶۱٫۹۱٪"],
    });
    expect(model.members[0].statuses).toEqual(["below", "below", "below"]);
    expect(model.members[3].statuses).toEqual(["above", "above", "below"]);
    expect(model.members[2].evidence).toBeUndefined();
    expect(model.after).toBe("این اطلاعات توصیه به سرمایه گذاری نیست");
  });

  it("splits trade evidence into price, source, date and equilibrium, and keeps unknown text whole", () => {
    expect(parsePriceEvidence("۸۰۰ ریال (روزانه، ۱۴۰۵/۰۸/۳۰)، تعادلی ۱۰۰۰ ریال")).toEqual({
      raw: "۸۰۰ ریال (روزانه، ۱۴۰۵/۰۸/۳۰)، تعادلی ۱۰۰۰ ریال",
      price: "۸۰۰ ریال",
      source: "روزانه",
      date: "۱۴۰۵/۰۸/۳۰",
      equilibrium: "۱۰۰۰ ریال",
    });
    expect(parsePriceEvidence("قیمت نامشخص")).toEqual({ raw: "قیمت نامشخص" });
    expect(parsePriceEvidence("—")).toBeUndefined();
  });

  it("reads a stored four-column answer, leaving missing values neutral", () => {
    const model = parseIndustryComparison(LEGACY_ANSWER)!;

    expect(model.hasPriceEvidence).toBe(false);
    expect(model.members).toHaveLength(1);
    expect(model.members[0].statuses).toEqual(["below", "above", "neutral"]);
    expect(model.benchmark?.label).toBe("میانگین صنعت");
  });
});

describe("IndustryComparison in a web chat answer", () => {
  it("renders the six-column and four-column tables without a scroll container or fixed minimum width", () => {
    const { container, getByTestId } = render(
      <MarkdownMessage content={PRICE_ANSWER} direction="rtl" />,
    );
    const comparison = getByTestId("industry-comparison");

    const wide = comparison.querySelector("[data-layout='wide']") as HTMLElement;
    expect(
      within(wide)
        .getAllByRole("columnheader")
        .map((header) => header.textContent),
    ).toEqual(["نماد", "P/E", "P/S", "قیمت به تعادلی", "رتبه", "قیمت و تاریخ معامله"]);
    expect(
      within(wide)
        .getAllByRole("rowheader")
        .map((header) => header.textContent),
    ).toEqual(["کچاد", "کگهر", "کنور", "کگل", "میانگین گروه"]);
    expect(within(wide).getAllByText("روزانه، ۱۴۰۵/۰۸/۳۰")).toHaveLength(2);
    expect(within(wide).getByText("تعادلی ۷۲۳۲٫۱۷ ریال")).toBeTruthy();

    const compactTable = comparison.querySelector("[data-layout='compact-table']") as HTMLElement;
    expect(within(compactTable).getAllByRole("columnheader")).toHaveLength(4);
    expect(within(compactTable).getAllByText("قیمت تعادلی:")).toHaveLength(3);

    expect(container.innerHTML).not.toMatch(/overflow-x-auto|min-w-\[/);
    expect(container.querySelector("[role='region'][tabindex]")).toBeNull();
  });

  it("marks every coloured ratio with a non-colour cue and screen-reader text", () => {
    const { getByTestId } = render(<MarkdownMessage content={PRICE_ANSWER} direction="rtl" />);
    const wide = getByTestId("industry-comparison").querySelector(
      "[data-layout='wide']",
    ) as HTMLElement;
    const firstRow = within(wide).getByRole("rowheader", { name: "کچاد" }).closest("tr")!;

    expect(within(firstRow).getAllByText("کمتر از میانگین گروه")).toHaveLength(3);
    expect(firstRow.textContent).toContain("▼");
    expect(firstRow.innerHTML).not.toMatch(/slate-|emerald-100|rose-100/);
  });

  it("shows only symbol and average cards in the phone workspace", () => {
    const { getByTestId } = render(
      <AnswerDensityContext.Provider value="compact">
        <MarkdownMessage content={PRICE_ANSWER} direction="rtl" />
      </AnswerDensityContext.Provider>,
    );
    const comparison = getByTestId("industry-comparison");

    expect(comparison.querySelector("table")).toBeNull();
    const cards = within(comparison).getAllByRole("listitem");
    expect(cards.map((card) => card.firstElementChild?.textContent)).toEqual([
      "کچادرتبه ۱ از ۴",
      "کگهررتبه ۲ از ۴",
      "کنوررتبه ۳ از ۴",
      "کگلرتبه ۴ از ۴",
      "میانگین گروه",
    ]);
    expect(within(cards[1]).getByText("۱۴۰۵/۰۹/۰۳، درون‌روزی")).toBeTruthy();
    expect(within(cards[2]).getByText("—")).toBeTruthy();
  });

  it("renders the stored four-column format as a table and cards without trade details", () => {
    const { getByTestId } = render(<MarkdownMessage content={LEGACY_ANSWER} direction="rtl" />);
    const comparison = getByTestId("industry-comparison");

    expect(comparison.querySelector("[data-layout='wide']")).toBeNull();
    const table = comparison.querySelector("[data-layout='compact-table']") as HTMLElement;
    expect(
      within(table)
        .getAllByRole("rowheader")
        .map((header) => header.textContent),
    ).toEqual(["شگل", "میانگین صنعت"]);
    expect(within(table).queryByText("رتبه:")).toBeNull();
  });
});
