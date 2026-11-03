import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { AdminCustomerChatHistory } from "../admin-customer-chat-history";
import { adminApi } from "@/integrations/financial-copilot/admin-client";

vi.mock("@/integrations/financial-copilot/admin-client", () => ({
  adminApi: {
    customerChatHistory: vi.fn(),
    customerChatHistoryTurn: vi.fn(),
  },
}));

const page = {
  items: [
    {
      turnId: "turn-1",
      conversationId: "conversation-1",
      conversationTitle: "Support",
      questionSummary: "پرسش کاربر",
      answerSummary: "پاسخ کوتاه",
      questionCreatedAtUtc: "2026-10-30T10:00:00Z",
      answerCreatedAtUtc: "2026-10-30T10:00:01Z",
      hasAnswer: true,
    },
  ],
  page: 1,
  pageSize: 20,
  totalCount: 21,
  totalPages: 2,
};

describe("AdminCustomerChatHistory", () => {
  beforeEach(() => vi.clearAllMocks());

  it("loads one server page and fetches full content when a row is expanded", async () => {
    vi.mocked(adminApi.customerChatHistory).mockResolvedValue(page);
    vi.mocked(adminApi.customerChatHistoryTurn).mockResolvedValue({
      turnId: "turn-1",
      conversationId: "conversation-1",
      conversationTitle: "Support",
      question: "پرسش کامل",
      questionCreatedAtUtc: "2026-10-30T10:00:00Z",
      answers: [
        {
          messageId: "answer-1",
          content: "پاسخ کامل",
          createdAtUtc: "2026-10-30T10:00:01Z",
          structuredPayload: null,
        },
      ],
    });

    render(<AdminCustomerChatHistory userId="user-1" />);
    expect(await screen.findByText("پاسخ کوتاه")).toBeTruthy();
    expect(adminApi.customerChatHistory).toHaveBeenCalledWith("user-1", 1);
    fireEvent.click(screen.getByRole("button", { name: /پرسش کاربر/ }));
    expect(await screen.findByText("پاسخ کامل")).toBeTruthy();
    expect(adminApi.customerChatHistoryTurn).toHaveBeenCalledWith("user-1", "turn-1");
    expect(screen.getByRole("button", { name: /پرسش کاربر/ }).getAttribute("aria-expanded")).toBe(
      "true",
    );
  });

  it("shows an empty state and reloads from page one for a different user", async () => {
    vi.mocked(adminApi.customerChatHistory).mockResolvedValueOnce({
      ...page,
      items: [],
      totalCount: 0,
      totalPages: 0,
    });
    const view = render(<AdminCustomerChatHistory userId="user-1" />);
    expect(await screen.findByText("گفتگویی برای این کاربر ثبت نشده است.")).toBeTruthy();

    vi.mocked(adminApi.customerChatHistory).mockResolvedValueOnce(page);
    view.rerender(<AdminCustomerChatHistory userId="user-2" />);
    await waitFor(() => expect(adminApi.customerChatHistory).toHaveBeenLastCalledWith("user-2", 1));
  });

  it("uses the server for next-page navigation", async () => {
    vi.mocked(adminApi.customerChatHistory)
      .mockResolvedValueOnce(page)
      .mockResolvedValueOnce({
        ...page,
        page: 2,
        items: [],
      });
    render(<AdminCustomerChatHistory userId="user-1" />);
    await screen.findByText("پاسخ کوتاه");
    fireEvent.click(screen.getByRole("button", { name: "بعدی" }));
    await waitFor(() => expect(adminApi.customerChatHistory).toHaveBeenLastCalledWith("user-1", 2));
  });

  it("shows list errors without hiding the retry action", async () => {
    vi.mocked(adminApi.customerChatHistory).mockRejectedValueOnce(new Error("network unavailable"));
    render(<AdminCustomerChatHistory userId="user-1" />);
    expect(await screen.findByRole("alert")).toHaveTextContent("network unavailable");
    expect(screen.getByRole("button", { name: "تازه‌سازی" })).toBeTruthy();
  });
});
