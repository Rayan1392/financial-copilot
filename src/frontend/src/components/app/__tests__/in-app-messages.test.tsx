import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MessageConversation, MessageInbox } from "../in-app-messages";

const mocks = vi.hoisted(() => ({
  session: { tenantId: "tenant", userId: "recipient" } as {
    tenantId: string;
    userId: string;
  } | null,
  subscribers: new Set<() => void>(),
  list: vi.fn(),
  detail: vi.fn(),
  read: vi.fn(),
  reply: vi.fn(),
  startSupport: vi.fn(),
}));

vi.mock("@/integrations/financial-copilot/auth", () => ({
  getStoredAuthenticatedUser: () => mocks.session,
  subscribeToAuthChanges: (fn: () => void) => {
    mocks.subscribers.add(fn);
    return () => mocks.subscribers.delete(fn);
  },
}));

vi.mock("@/integrations/financial-copilot/in-app-messages", () => ({
  messageApi: {
    list: mocks.list,
    detail: mocks.detail,
    read: mocks.read,
    reply: mocks.reply,
    startSupport: mocks.startSupport,
  },
}));

const now = "2026-11-22T12:00:00Z";
const conversation = {
  conversationId: "thread",
  subject: "پیام آزمایشی",
  latestActivityUtc: now,
  unread: true,
  repliesEnabled: true,
  pendingSinceUtc: null,
};

describe("in-app messages", () => {
  beforeEach(() => {
    mocks.session = { tenantId: "tenant", userId: "recipient" };
    mocks.list.mockReset().mockResolvedValue({ items: [conversation], nextCursor: null });
    mocks.detail.mockReset().mockResolvedValue({
      conversation,
      revision: 1,
      messages: {
        items: [
          {
            id: "entry",
            fromStaff: true,
            body: "<b>متن خصوصی</b>",
            sentAtUtc: now,
            readAtUtc: null,
          },
        ],
        nextCursor: null,
      },
    });
    mocks.read.mockReset().mockResolvedValue(undefined);
    mocks.reply.mockReset();
    mocks.startSupport.mockReset();
    vi.spyOn(document, "visibilityState", "get").mockReturnValue("visible");
  });

  afterEach(() => vi.restoreAllMocks());

  it("validates, preserves the draft and retry key, and opens the new request after success", async () => {
    mocks.startSupport
      .mockRejectedValueOnce(new Error("lost response"))
      .mockResolvedValueOnce({ conversationId: "request" });
    const onOpen = vi.fn();
    render(
      <QueryClientProvider client={new QueryClient()}>
        <MessageInbox onOpen={onOpen} />
      </QueryClientProvider>,
    );

    fireEvent.click(screen.getByRole("button", { name: "درخواست جدید" }));
    const subject = screen.getByRole("textbox", { name: "موضوع" });
    const body = screen.getByRole("textbox", { name: "متن پیام" });
    const send = screen.getByRole("button", { name: "ارسال به پشتیبانی" });
    expect(subject).toHaveFocus();
    fireEvent.click(send);
    expect(subject).toHaveAccessibleDescription("موضوع را وارد کنید.");
    expect(mocks.startSupport).not.toHaveBeenCalled();

    fireEvent.change(subject, { target: { value: "  مشکل ورود  " } });
    fireEvent.change(body, { target: { value: "  متن درخواست  " } });
    fireEvent.click(send);
    expect(await screen.findByText(/ثبت درخواست تأیید نشد/)).toBeInTheDocument();
    expect(body).toHaveValue("  متن درخواست  ");
    fireEvent.click(send);
    await waitFor(() => expect(onOpen).toHaveBeenCalledWith("request"));
    const [first, second] = mocks.startSupport.mock.calls;
    expect(first[0]).toEqual({
      subject: "مشکل ورود",
      body: "متن درخواست",
      idempotencyKey: expect.any(String),
    });
    expect(second[0]).toEqual(first[0]);
  });

  it("renders request status separately from the unread badge and leaves legacy threads unlabeled", async () => {
    mocks.list.mockResolvedValue({
      items: [
        conversation,
        { ...conversation, conversationId: "request", supportStatus: "waitingForSupport" },
      ],
      nextCursor: null,
    });
    const client = new QueryClient();
    const view = render(
      <QueryClientProvider client={client}>
        <MessageInbox onOpen={vi.fn()} />
      </QueryClientProvider>,
    );
    expect(await screen.findByText("در انتظار پاسخ")).toBeInTheDocument();
    expect(screen.getAllByText("خوانده‌نشده", { selector: "span" })).toHaveLength(2);

    const detail = await mocks.detail();
    mocks.detail.mockResolvedValue({
      ...detail,
      conversation: { ...conversation, supportStatus: "answered" },
    });
    view.rerender(
      <QueryClientProvider client={client}>
        <MessageConversation id="request" />
      </QueryClientProvider>,
    );
    expect(await screen.findByText("پاسخ داده شد")).toBeInTheDocument();
  });

  it("acknowledges staff entries only after opening the thread and renders message bodies as text", async () => {
    const client = new QueryClient();
    const onOpen = vi.fn();
    const view = render(
      <QueryClientProvider client={client}>
        <MessageInbox onOpen={onOpen} />
      </QueryClientProvider>,
    );
    fireEvent.click(await screen.findByRole("button", { name: /پیام آزمایشی/ }));
    expect(onOpen).toHaveBeenCalledWith("thread");
    expect(mocks.read).not.toHaveBeenCalled();

    view.rerender(
      <QueryClientProvider client={client}>
        <MessageConversation id="thread" />
      </QueryClientProvider>,
    );
    expect(await screen.findByText("<b>متن خصوصی</b>")).toBeInTheDocument();
    await waitFor(() => expect(mocks.read).toHaveBeenCalledExactlyOnceWith("thread", ["entry"]));
  });

  it("clears the old conversation after a session change", async () => {
    const client = new QueryClient();
    render(
      <QueryClientProvider client={client}>
        <MessageConversation id="thread" />
      </QueryClientProvider>,
    );
    expect(await screen.findByText("<b>متن خصوصی</b>")).toBeInTheDocument();

    mocks.detail.mockRejectedValue(new Error("not found"));
    act(() => {
      mocks.session = { tenantId: "tenant", userId: "other" };
      mocks.subscribers.forEach((fn) => fn());
    });

    expect(screen.queryByText("<b>متن خصوصی</b>")).not.toBeInTheDocument();
    expect(
      client.getQueryData(["in-app-messages", "tenant:recipient", "detail", false, "thread"]),
    ).toBeUndefined();
  });
});
