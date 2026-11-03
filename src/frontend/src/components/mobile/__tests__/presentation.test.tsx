import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { SignedChange } from "../signed-change";
import { BottomNav } from "../bottom-nav";
import { renderInRouter } from "./test-router";
import { MessageList } from "@/components/app/message-list";
import type { ChatMessage } from "@/lib/chat.functions";

describe("SignedChange (AC-14, AC-32)", () => {
  it("adds a direction glyph and isolates the signed number left-to-right", () => {
    const { container } = render(<SignedChange value={1.234} />);
    expect(container).toHaveTextContent("▲");
    const number = container.querySelector("bdi");
    expect(number).toHaveAttribute("dir", "ltr");
    expect(number).toHaveTextContent("+۱٫۲۳٪".replace("٫", "."));
    expect(container).toHaveTextContent("افزایش");
  });

  it("uses ▼ for falls and «–» for unknown values", () => {
    expect(render(<SignedChange value={-0.5} />).container).toHaveTextContent("▼");
    expect(render(<SignedChange value={null} />).container).toHaveTextContent("–");
  });
});

describe("BottomNav (AC-01)", () => {
  it("has four labelled tabs, گفتگو first in DOM (rightmost in RTL), and marks the active tab", async () => {
    await renderInRouter(<BottomNav onReselect={vi.fn()} />, { path: "/m/market" });
    const nav = screen.getByRole("navigation", { name: "ناوبری اصلی" });
    const links = Array.from(nav.querySelectorAll("a"));
    expect(links.map((link) => link.textContent)).toEqual([
      "گفتگو",
      "تاریخچه",
      "بازار",
      "دیده‌بان",
    ]);
    expect(screen.getByRole("link", { name: "بازار" })).toHaveAttribute("aria-current", "page");
    expect(screen.getByRole("link", { name: "گفتگو" })).not.toHaveAttribute("aria-current");
    for (const link of links) expect(link).toHaveClass("min-h-14");
  });

  it("re-tapping the active tab scrolls to the top instead of navigating", async () => {
    const onReselect = vi.fn();
    await renderInRouter(<BottomNav onReselect={onReselect} />, { path: "/m/history" });
    screen.getByRole("link", { name: "تاریخچه" }).click();
    expect(onReselect).toHaveBeenCalledOnce();
  });
});

describe("MessageList density (T10)", () => {
  const messages: ChatMessage[] = [
    { id: "u1", role: "user", content: { text: "سلام" }, created_at: "" },
  ];
  const renderList = (density?: "default" | "compact") =>
    render(
      <QueryClientProvider client={new QueryClient()}>
        <MessageList
          messages={messages}
          loading={false}
          streaming={false}
          onSuggested={vi.fn()}
          density={density}
        />
      </QueryClientProvider>,
    );

  it("keeps the desktop layout by default", () => {
    const { container } = renderList();
    expect(container.firstElementChild).toHaveAttribute("data-density", "default");
    expect(container.firstElementChild).toHaveClass("p-6");
  });

  it("uses tighter padding and wider bubbles when compact", () => {
    const { container } = renderList("compact");
    expect(container.firstElementChild).toHaveClass("px-4");
    expect(container.querySelector('[data-message-id="u1"]')).toHaveAttribute("data-role", "user");
    expect(screen.getByText("سلام")).toHaveClass("max-w-[85%]");
  });
});
