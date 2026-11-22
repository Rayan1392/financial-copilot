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
}));

vi.mock("@/integrations/financial-copilot/auth", () => ({
  getStoredAuthenticatedUser: () => mocks.session,
  subscribeToAuthChanges: (fn: () => void) => {
    mocks.subscribers.add(fn);
    return () => mocks.subscribers.delete(fn);
  },
}));

vi.mock("@/integrations/financial-copilot/in-app-messages", () => ({
  messageApi: { list: mocks.list, detail: mocks.detail, read: mocks.read, reply: mocks.reply },
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
    vi.spyOn(document, "visibilityState", "get").mockReturnValue("visible");
  });

  afterEach(() => vi.restoreAllMocks());

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
