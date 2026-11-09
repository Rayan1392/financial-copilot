import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { MessageList } from "../message-list";
import { SymbolSnapshotCard } from "../symbol-snapshot-card";
import type { AssistantChatBlock, SymbolSnapshotResult } from "@/lib/chat.functions";

vi.mock("@/components/app/follow-symbol-button", () => ({
  FollowSymbolButton: ({ symbol, followed }: { symbol: string; followed?: boolean }) => (
    <button type="button">{followed ? `دنبال‌شده ${symbol}` : `دنبال کردن ${symbol}`}</button>
  ),
}));

const snapshot: SymbolSnapshotResult = {
  symbol: "اخابر",
  companyName: "مخابرات ایران",
  marketName: "بورس",
  groupName: "مخابرات",
  introFa: "خلاصهٔ آخرین داده‌های «اخابر».",
  price: {
    status: "Available",
    latestPrice: 3450,
    dailyChangePercent: -2.1,
    tradingDateJalali: "1405/08/18",
  },
  monthlySales: {
    status: "Available",
    periodLabelFa: "مهر 1405",
    unitLabelFa: "میلیارد ریال",
    salesAmount: 12340,
    monthOverMonthPercent: 4,
    yearOverYearPercent: 31,
  },
  quarterlyReport: {
    status: "Available",
    periodLabelFa: "فصل دوم 1405",
    isAudited: false,
    rows: [
      { labelFa: "فروش", unitLabelFa: "میلیون ریال", value: 1000, yearOverYearPercent: 12 },
      { labelFa: "سود هر سهم", unitLabelFa: "ریال", value: null, yearOverYearPercent: null },
    ],
  },
};

const card = () => screen.getByRole("region", { name: "خلاصهٔ نماد اخابر" });

describe("SymbolSnapshotCard", () => {
  it("shows the three sections with their periods", () => {
    render(<SymbolSnapshotCard data={snapshot} />);

    const region = within(card());
    expect(region.getByRole("heading", { name: /آخرین قیمت/ })).toBeInTheDocument();
    expect(region.getByRole("heading", { name: /فروش ماهانه.*مهر ۱۴۰۵/ })).toBeInTheDocument();
    expect(
      region.getByRole("heading", { name: /گزارش فصلی.*فصل دوم ۱۴۰۵.*حسابرسی‌نشده/ }),
    ).toBeInTheDocument();
    expect(region.getByText("۱۴۰۵/۰۸/۱۸")).toBeInTheDocument();
    expect(region.getByText(/۳٬۴۵۰/)).toBeInTheDocument();
  });

  it("keeps the sign of a change in text and shows a dash for a missing value", () => {
    render(<SymbolSnapshotCard data={snapshot} />);

    expect(screen.getByText("-۲٫۱۰٪")).toBeInTheDocument();
    expect(screen.getByText("—")).toBeInTheDocument();
  });

  it("replaces a missing section with its notice and keeps the others", () => {
    render(
      <SymbolSnapshotCard
        data={{
          ...snapshot,
          monthlySales: { status: "NoData", noticeFa: "گزارش ماهانه‌ای ثبت نشده است." },
        }}
      />,
    );

    expect(screen.getByText("گزارش ماهانه‌ای ثبت نشده است.")).toBeInTheDocument();
    expect(screen.queryByText("نسبت به ماه قبل")).not.toBeInTheDocument();
    expect(screen.getByRole("heading", { name: /آخرین قیمت/ })).toBeInTheDocument();
  });

  it("omits the audit label when the report status is unknown", () => {
    render(
      <SymbolSnapshotCard
        data={{ ...snapshot, quarterlyReport: { ...snapshot.quarterlyReport, isAudited: null } }}
      />,
    );

    expect(screen.queryByText(/حسابرسی/)).not.toBeInTheDocument();
  });
});

describe("MessageList with a symbol snapshot", () => {
  const block: AssistantChatBlock = {
    message: "متن تلگرام و تاریخچه",
    intent: "SymbolSnapshot",
    replyLanguage: "fa",
    creditsUsed: 1,
    suggestedQuestions: [],
    suggestedActions: [
      {
        id: "a1",
        kind: "RunRelatedCapability",
        label: "روند فروش ماهانه",
        message: "روند فروش ماهانه اخابر را نشان بده",
      },
    ] as unknown as AssistantChatBlock["suggestedActions"],
    filters: [],
    citations: [],
    symbolSnapshotResult: snapshot,
  };

  const renderBlock = (onSuggested = vi.fn(), followedSymbols?: ReadonlySet<string>) => {
    render(
      <MessageList
        messages={[
          { id: "m1", role: "assistant", content: block, created_at: "2026-11-09T00:00:00Z" },
        ]}
        loading={false}
        streaming={false}
        onSuggested={onSuggested}
        followedSymbols={followedSymbols}
      />,
    );
    return onSuggested;
  };

  it("shows the card instead of the text", () => {
    renderBlock();

    expect(card()).toBeInTheDocument();
    expect(screen.queryByText("متن تلگرام و تاریخچه")).not.toBeInTheDocument();
  });

  it("sends the suggested action when a button is pressed", () => {
    const onSuggested = renderBlock();

    fireEvent.click(screen.getByRole("button", { name: "روند فروش ماهانه" }));

    expect(onSuggested).toHaveBeenCalledWith("روند فروش ماهانه اخابر را نشان بده", "a1");
  });

  it("marks a followed symbol", () => {
    renderBlock(vi.fn(), new Set(["اخابر"]));

    expect(screen.getByRole("button", { name: "دنبال‌شده اخابر" })).toBeInTheDocument();
  });
});
