import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useState, type ReactElement } from "react";
import type { AssistantChatBlock, ChatMessage, GroundedAnalysisHint } from "@/lib/chat.functions";
import type { AnalysisChoice } from "@/lib/chat/analysis-choice";
import { AnalysisOfferCard, AnalysisSettingsBar } from "../analysis-settings";
import { MessageList } from "../message-list";

/** Change «sol-deep-analysis-web-search» (T12): the analysis controls, the offer card and the sources list. */
const hint = (overrides: Partial<GroundedAnalysisHint> = {}): GroundedAnalysisHint => ({
  available: true,
  pricingVersion: "v1",
  prices: [
    { depth: "simple", webSearch: false, credits: 8 },
    { depth: "simple", webSearch: true, credits: 12 },
    { depth: "deep", webSearch: false, credits: 16 },
    { depth: "deep", webSearch: true, credits: 25 },
  ],
  defaultDepth: "deep",
  defaultWebSearch: true,
  unlimited: false,
  offer: null,
  ...overrides,
});
const offer = (sufficientBalance = true) => ({
  offerId: "o1",
  symbol: "فولاد",
  expiresAt: "2026-11-20T10:00:00Z",
  sufficientBalance,
  confirmActionId: "ga1:o1:confirm",
  cancelActionId: "ga1:o1:cancel",
});
const DEEP_WEB: AnalysisChoice = { depth: "deep", webSearch: true };

function StatefulBar({ value }: { value: GroundedAnalysisHint }) {
  const [choice, setChoice] = useState(DEEP_WEB);
  return <AnalysisSettingsBar hint={value} choice={choice} onChange={setChoice} />;
}

describe("AnalysisSettingsBar (162)", () => {
  it("shows the backend price of the chosen tier and updates it live", () => {
    render(<StatefulBar value={hint()} />);
    expect(screen.getByText(/۲۵ اعتبار مصرف می‌شود/)).toBeInTheDocument();
    expect(screen.getByRole("radio", { name: "عمیق" })).toHaveAttribute("aria-checked", "true");
    const web = screen.getByRole("button", { name: "جست‌وجوی وب" });
    expect(web).toHaveAttribute("aria-pressed", "true");

    fireEvent.click(screen.getByRole("radio", { name: "ساده" }));
    expect(screen.getByText(/۱۲ اعتبار مصرف می‌شود/)).toBeInTheDocument();

    fireEvent.click(web);
    expect(web).toHaveAttribute("aria-pressed", "false");
    expect(screen.getByText(/۸ اعتبار مصرف می‌شود/)).toBeInTheDocument();
  });

  it("a Max account sees that nothing is deducted", () => {
    render(<StatefulBar value={hint({ unlimited: true })} />);
    expect(screen.getByText("با اشتراک Max اعتباری کسر نمی‌شود.")).toBeInTheDocument();
    expect(screen.queryByText(/اعتبار مصرف/)).not.toBeInTheDocument();
  });

  it("phone layout uses 44 px touch targets and wraps instead of scrolling", () => {
    const { container } = render(
      <AnalysisSettingsBar hint={hint()} choice={DEEP_WEB} onChange={vi.fn()} compact />,
    );
    for (const control of screen.getAllByRole("radio"))
      expect(control.className).toContain("min-h-11");
    expect(screen.getByRole("button", { name: "جست‌وجوی وب" }).className).toContain("min-h-11");
    expect(container.querySelector("section")!.className).toContain("flex-wrap");
  });
});

describe("AnalysisOfferCard (163)", () => {
  function renderCard(value: GroundedAnalysisHint, choice = DEEP_WEB, disabled = false) {
    const onConfirm = vi.fn();
    const onCancel = vi.fn();
    const view = render(
      <AnalysisOfferCard
        hint={value}
        choice={choice}
        onChange={vi.fn()}
        disabled={disabled}
        onConfirm={onConfirm}
        onCancel={onCancel}
        creditHref="/profile"
      />,
    );
    return { onConfirm, onCancel, ...view };
  }

  it("confirms once and locks after the click", () => {
    const { onConfirm, onCancel } = renderCard(hint({ offer: offer() }));
    expect(
      screen.getByText("این تحلیل با تنظیمات انتخاب‌شده ۲۵ اعتبار مصرف می‌کند."),
    ).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "تحلیل کن" }));
    fireEvent.click(screen.getByRole("button", { name: "تحلیل کن" }));
    fireEvent.click(screen.getByRole("button", { name: "انصراف" }));

    expect(onConfirm).toHaveBeenCalledOnce();
    expect(onCancel).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "تحلیل کن" })).toBeDisabled();
    expect(screen.getByRole("radio", { name: "ساده" })).toBeDisabled();
  });

  it("unlocks when the request ends without replacing the offer (for example a short balance)", () => {
    const { onCancel, rerender } = renderCard(hint({ offer: offer() }), DEEP_WEB, false);
    fireEvent.click(screen.getByRole("button", { name: "انصراف" }));
    const props = {
      hint: hint({ offer: offer() }),
      choice: DEEP_WEB,
      onChange: vi.fn(),
      onConfirm: vi.fn(),
      onCancel,
      creditHref: "/profile",
    };
    rerender(<AnalysisOfferCard {...props} disabled />);
    rerender(<AnalysisOfferCard {...props} disabled={false} />);

    expect(screen.getByRole("button", { name: "انصراف" })).toBeEnabled();
  });

  it("a short balance disables «تحلیل کن» for the default tier and links to credit", () => {
    renderCard(hint({ offer: offer(false) }));
    expect(screen.getByRole("button", { name: "تحلیل کن" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "انصراف" })).toBeEnabled();
    expect(screen.getByRole("link", { name: "خرید اعتبار" })).toHaveAttribute("href", "/profile");
  });

  it("a cheaper tier stays possible with a short balance", () => {
    renderCard(hint({ offer: offer(false) }), { depth: "simple", webSearch: false });
    expect(screen.getByRole("button", { name: "تحلیل کن" })).toBeEnabled();
  });

  it("a Max account is told nothing is deducted", () => {
    renderCard(hint({ offer: offer(false), unlimited: true }));
    expect(screen.getByText("با اشتراک Max اعتباری کسر نمی‌شود.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "تحلیل کن" })).toBeEnabled();
  });

  it("uses theme tokens only, so it follows light and dark", () => {
    const { container } = renderCard(hint({ offer: offer() }));
    expect(container.innerHTML).not.toMatch(
      /#[0-9a-f]{3,6}|rgb\(|bg-white|bg-black|text-white|text-black/i,
    );
  });
});

describe("MessageList with an analysis offer and sources", () => {
  const answer = (id: string, overrides: Partial<AssistantChatBlock> = {}): ChatMessage => ({
    id,
    role: "assistant",
    created_at: "2026-11-20T00:00:00Z",
    content: {
      message: "پاسخ",
      intent: "x",
      replyLanguage: "fa",
      creditsUsed: 0,
      suggestedQuestions: [],
      suggestedActions: [],
      filters: [],
      citations: [],
      ...overrides,
    },
  });
  function renderList(ui: ReactElement) {
    return render(<QueryClientProvider client={new QueryClient()}>{ui}</QueryClientProvider>);
  }

  it("shows the offer card only on the answer that carries it and sends its action ids", () => {
    const onSuggested = vi.fn();
    renderList(
      <MessageList
        messages={[answer("old"), answer("a1")]}
        loading={false}
        streaming={false}
        onSuggested={onSuggested}
        analysis={{
          hint: hint({ offer: offer() }),
          messageId: "a1",
          choice: DEEP_WEB,
          onChoiceChange: vi.fn(),
        }}
      />,
    );
    expect(screen.getAllByRole("button", { name: "تحلیل کن" })).toHaveLength(1);
    expect(document.querySelector('[data-message-id="a1"]')!.textContent).toContain("تحلیل فولاد");

    fireEvent.click(screen.getByRole("button", { name: "تحلیل کن" }));
    expect(onSuggested).toHaveBeenCalledWith("تحلیل کن", "ga1:o1:confirm");
  });

  it("no offer card when the latest answer carries no offer", () => {
    renderList(
      <MessageList
        messages={[answer("a1")]}
        loading={false}
        streaming={false}
        onSuggested={vi.fn()}
        analysis={{ hint: hint(), messageId: "a1", choice: DEEP_WEB, onChoiceChange: vi.fn() }}
      />,
    );
    expect(screen.queryByRole("button", { name: "تحلیل کن" })).not.toBeInTheDocument();
  });

  it("links the consulted sources at the end of the paragraph that used them, with no list at the end", () => {
    const text =
      "تحلیل حاشیه سود [W1]\n\nبند بدون منبع.\n\n**منابع**\n- [W1] خبر فولاد (1405/08/01): https://example.com/news";
    renderList(
      <MessageList
        messages={[
          answer("a1", {
            message: text,
            sources: [
              {
                id: "W1",
                title: "خبر فولاد",
                url: "https://example.com/news",
                publishedAt: "1405/08/01",
              },
              { id: "W2", title: "بد", url: "javascript:alert(1)", publishedAt: "1405/08/02" },
            ],
          }),
        ]}
        loading={false}
        streaming={false}
        onSuggested={vi.fn()}
      />,
    );
    const link = screen.getByRole("link", { name: "خبر فولاد" });
    expect(link).toHaveAttribute("href", "https://example.com/news");
    expect(link).toHaveAttribute("target", "_blank");
    expect(link.getAttribute("rel")).toContain("noopener");
    expect(screen.queryByRole("link", { name: "بد" })).not.toBeInTheDocument();
    expect(link.closest("p")!.textContent).toBe("تحلیل حاشیه سود — منبع: خبر فولاد");
    expect(screen.queryByRole("region", { name: "منابع" })).not.toBeInTheDocument();
    expect(document.body.textContent).not.toContain("https://example.com/news");
    expect(document.body.textContent).not.toContain("[W1]");
  });
});
