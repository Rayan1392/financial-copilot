import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, renderHook, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { ChatMessage } from "@/lib/chat.functions";

const sendChatMessage = vi.fn();
const getThreadMessages = vi.fn();
const createThread = vi.fn();

// Server functions are plain async functions here; useServerFn just returns them.
vi.mock("@tanstack/react-start", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@tanstack/react-start")>()),
  useServerFn: (fn: unknown) => fn,
}));
vi.mock("@/lib/chat.functions", () => ({
  sendChatMessage: (input: unknown) => sendChatMessage(input),
  getThreadMessages: (input: unknown) => getThreadMessages(input),
  createThread: () => createThread(),
}));
vi.mock("@/lib/followed-symbols.functions", () => ({
  getFollowedSymbols: async () => ({ symbols: [] }),
}));
vi.mock("@/integrations/financial-copilot/auth", () => ({
  getAuthenticatedUser: async () => null,
}));

const { useStartChat } = await import("@/lib/chat/use-start-chat");
const { useChatThread } = await import("@/lib/chat/use-chat-thread");

const THREAD = "c6f1c7d2-5a59-4c55-9c39-6c3c8d2f4c11";
const user = (id: string, text: string): ChatMessage => ({
  id,
  role: "user",
  content: { text },
  created_at: "",
});
const assistant = (id: string): ChatMessage => ({
  id,
  role: "assistant",
  created_at: "",
  content: {
    message: "a",
    intent: "x",
    creditsUsed: 1,
    suggestedQuestions: [],
    suggestedActions: [],
    filters: [],
    citations: [],
  },
});

function wrapper(client: QueryClient) {
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
}

beforeEach(() => {
  sendChatMessage.mockReset();
  getThreadMessages.mockReset();
  createThread.mockReset();
});

describe("useStartChat (first message, spec AC-04 / AC-06a)", () => {
  it("sends once with a GUID key and opens the new conversation", async () => {
    sendChatMessage.mockResolvedValue({ ok: true, result: { threadId: THREAD } });
    const navigateToThread = vi.fn();
    const { result } = renderHook(() => useStartChat({ navigateToThread }), {
      wrapper: wrapper(new QueryClient()),
    });

    act(() => result.current.submit("گزارش فصلی فملی"));

    await waitFor(() => expect(navigateToThread).toHaveBeenCalledWith(THREAD));
    expect(sendChatMessage).toHaveBeenCalledOnce();
    expect(sendChatMessage.mock.calls[0][0].data.idempotencyKey).toMatch(/^[0-9a-f-]{36}$/);
    expect(sendChatMessage.mock.calls[0][0].data.threadId).toBeUndefined();
    expect(createThread).not.toHaveBeenCalled();
  });

  it("a network failure is indeterminate, keeps the draft, refreshes history, and never resends", async () => {
    sendChatMessage.mockRejectedValue(new TypeError("Failed to fetch"));
    const client = new QueryClient();
    const invalidate = vi.spyOn(client, "invalidateQueries");
    const { result } = renderHook(() => useStartChat({ navigateToThread: vi.fn() }), {
      wrapper: wrapper(client),
    });

    act(() => result.current.submit("گزارش فصلی فملی"));

    await waitFor(() => expect(result.current.failure).not.toBeNull());
    expect(result.current.failure).toMatchObject({
      code: "network",
      draft: "گزارش فصلی فملی",
      recovery: { kind: "indeterminate" },
    });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ["threads"] });
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(sendChatMessage).toHaveBeenCalledOnce();
  });

  it("a structured refusal is definite", async () => {
    sendChatMessage.mockResolvedValue({
      ok: false,
      failure: { code: "insufficient_credit", message: "no credit" },
    });
    const { result } = renderHook(() => useStartChat({ navigateToThread: vi.fn() }), {
      wrapper: wrapper(new QueryClient()),
    });
    act(() => result.current.submit("x"));
    await waitFor(() =>
      expect(result.current.failure?.recovery).toEqual({
        kind: "definite",
        code: "insufficient_credit",
      }),
    );
  });
});

describe("useChatThread (follow-up, spec AC-06b)", () => {
  const before = [user("u1", "اول"), assistant("a1")];

  async function failFollowUp(afterRefetch: () => Promise<ChatMessage[]>) {
    getThreadMessages.mockResolvedValueOnce(before).mockImplementation(afterRefetch);
    sendChatMessage.mockRejectedValue(new TypeError("Failed to fetch"));
    const { result } = renderHook(() => useChatThread(THREAD, { reconcileOnFailure: true }), {
      wrapper: wrapper(new QueryClient()),
    });
    await waitFor(() => expect(result.current.messages).toHaveLength(2));
    act(() => result.current.submit("دوم"));
    return result;
  }

  it("shows the persisted answer and drops the error when the refetch finds it", async () => {
    const result = await failFollowUp(async () => [...before, user("u2", "دوم"), assistant("a2")]);
    await waitFor(() => expect(result.current.messages).toHaveLength(4));
    await waitFor(() => expect(result.current.failure).toBeNull());
    expect(sendChatMessage).toHaveBeenCalledOnce();
  });

  it("offers a deliberate resend only after a successful refetch without the answer", async () => {
    const result = await failFollowUp(async () => before);
    await waitFor(() => expect(result.current.failure?.recovery).toEqual({ kind: "not_found" }));
    expect(sendChatMessage).toHaveBeenCalledOnce();
  });

  it("when the refetch fails too, «بررسی دوباره» repeats only the refetch", async () => {
    const result = await failFollowUp(async () => {
      throw new TypeError("Failed to fetch");
    });
    await waitFor(() =>
      expect(result.current.failure?.recovery).toEqual({ kind: "reconcile_failed" }),
    );
    const fetchesBefore = getThreadMessages.mock.calls.length;

    getThreadMessages.mockImplementation(async () => [
      ...before,
      user("u2", "دوم"),
      assistant("a2"),
    ]);
    act(() => result.current.recheck());

    await waitFor(() => expect(result.current.failure).toBeNull());
    expect(getThreadMessages.mock.calls.length).toBe(fetchesBefore + 1);
    expect(sendChatMessage).toHaveBeenCalledOnce();
  });

  it("shows the question while it is sent and until its persisted copy is loaded (BUG-023)", async () => {
    let answerSend!: (value: unknown) => void;
    let loadAnswer!: (value: ChatMessage[]) => void;
    getThreadMessages
      .mockResolvedValueOnce(before)
      .mockImplementationOnce(() => new Promise((resolve) => (loadAnswer = resolve)));
    sendChatMessage.mockImplementation(() => new Promise((resolve) => (answerSend = resolve)));
    const { result } = renderHook(() => useChatThread(THREAD), {
      wrapper: wrapper(new QueryClient()),
    });
    await waitFor(() => expect(result.current.messages).toHaveLength(2));

    act(() => result.current.submit("دوم"));
    expect(result.current.pendingMessage).toBe("دوم");
    await waitFor(() => expect(sendChatMessage).toHaveBeenCalledOnce());
    expect(result.current.isPending).toBe(true);

    // The answer is ready but not loaded yet: the question and the progress indicator stay.
    await act(async () => answerSend({ ok: true, result: { threadId: THREAD } }));
    await waitFor(() => expect(getThreadMessages).toHaveBeenCalledTimes(2));
    expect(result.current.pendingMessage).toBe("دوم");
    expect(result.current.isPending).toBe(true);

    // The persisted copy replaces it: shown once, never doubled.
    await act(async () => loadAnswer([...before, user("u2", "دوم"), assistant("a2")]));
    await waitFor(() => expect(result.current.messages).toHaveLength(4));
    expect(result.current.pendingMessage).toBeNull();
    await waitFor(() => expect(result.current.isPending).toBe(false));
  });

  it("an empty conversation shows its first question while it is sent (BUG-023)", async () => {
    getThreadMessages.mockResolvedValue([]);
    sendChatMessage.mockImplementation(() => new Promise(() => {}));
    const { result } = renderHook(() => useChatThread(THREAD), {
      wrapper: wrapper(new QueryClient()),
    });
    await waitFor(() => expect(result.current.isLoading).toBe(false));

    act(() => result.current.submit("گزارش فصلی فملی"));

    expect(result.current.pendingMessage).toBe("گزارش فصلی فملی");
  });

  it("a failed send drops the pending question and keeps the draft (BUG-023)", async () => {
    getThreadMessages.mockResolvedValue(before);
    sendChatMessage.mockRejectedValue(new TypeError("Failed to fetch"));
    const { result } = renderHook(() => useChatThread(THREAD), {
      wrapper: wrapper(new QueryClient()),
    });
    await waitFor(() => expect(result.current.messages).toHaveLength(2));

    act(() => result.current.submit("دوم"));

    await waitFor(() => expect(result.current.failure?.draft).toBe("دوم"));
    expect(result.current.pendingMessage).toBeNull();
  });

  it("desktop (no reconciliation) keeps its earlier behavior", async () => {
    getThreadMessages.mockResolvedValue(before);
    sendChatMessage.mockRejectedValue(new TypeError("Failed to fetch"));
    const { result } = renderHook(() => useChatThread(THREAD), {
      wrapper: wrapper(new QueryClient()),
    });
    await waitFor(() => expect(result.current.messages).toHaveLength(2));
    act(() => result.current.submit("دوم"));
    await waitFor(() => expect(result.current.failure?.recovery).toEqual({ kind: "reconciling" }));
    expect(getThreadMessages).toHaveBeenCalledOnce();
  });
});
