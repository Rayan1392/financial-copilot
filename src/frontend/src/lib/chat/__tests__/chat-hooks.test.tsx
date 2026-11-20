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

describe("analysis options (change «sol-deep-analysis-web-search», T12)", () => {
  const hint = (offer?: boolean) => ({
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
    offer: offer
      ? {
          offerId: "o1",
          symbol: "فولاد",
          expiresAt: "2026-11-20T10:00:00Z",
          sufficientBalance: true,
          confirmActionId: "ga1:o1:confirm",
          cancelActionId: "ga1:o1:cancel",
        }
      : null,
  });
  const withHint = (id: string, offer?: boolean): ChatMessage => {
    const message = assistant(id);
    return {
      ...message,
      content: { ...(message.content as object), groundedAnalysis: hint(offer) },
    } as ChatMessage;
  };
  const before = [user("u1", "عملکرد مالی فصلی فولاد را بررسی کن")];

  beforeEach(() => localStorage.clear());

  async function threadAfterReport(offer = false) {
    getThreadMessages.mockResolvedValueOnce(before).mockResolvedValue([...before, assistant("a1")]);
    sendChatMessage.mockResolvedValueOnce({
      ok: true,
      result: { threadId: THREAD, aiMsg: withHint("a1", offer) },
    });
    const { result } = renderHook(() => useChatThread(THREAD), {
      wrapper: wrapper(new QueryClient()),
    });
    await waitFor(() => expect(result.current.messages).toHaveLength(1));
    act(() => result.current.submit("گزارش"));
    await waitFor(() => expect(result.current.analysisHint).not.toBeNull());
    await waitFor(() => expect(result.current.isPending).toBe(false));
    sendChatMessage.mockResolvedValue({ ok: true, result: { threadId: THREAD } });
    return result;
  }

  it("the question after a discussable report carries the default choice and the shown price version, never an amount", async () => {
    const result = await threadAfterReport();
    expect(sendChatMessage.mock.calls[0][0].data.analysisOptions).toBeUndefined();

    act(() => result.current.submit("حاشیه سود چرا افت کرد؟"));

    await waitFor(() => expect(sendChatMessage).toHaveBeenCalledTimes(2));
    const data = sendChatMessage.mock.calls[1][0].data;
    expect(data.analysisOptions).toEqual({ depth: "deep", webSearch: true, pricingVersion: "v1" });
    expect(JSON.stringify(data)).not.toMatch(/credit|amount|"price"/i);
  });

  it("a changed choice is sent and remembered for this viewer", async () => {
    const result = await threadAfterReport();

    act(() => result.current.setAnalysisChoice({ depth: "simple", webSearch: false }));
    act(() => result.current.submit("حاشیه سود چرا افت کرد؟"));

    await waitFor(() => expect(sendChatMessage).toHaveBeenCalledTimes(2));
    expect(sendChatMessage.mock.calls[1][0].data.analysisOptions).toEqual({
      depth: "simple",
      webSearch: false,
      pricingVersion: "v1",
    });
    expect(JSON.parse(localStorage.getItem("sapio.analysis-choice.v1")!)).toEqual({
      depth: "simple",
      webSearch: false,
    });
  });

  it("«تحلیل کن» sends the offer's confirm id with the choice; «انصراف» sends the cancel id alone", async () => {
    const result = await threadAfterReport(true);

    act(() => result.current.submit("تحلیل کن", "ga1:o1:confirm"));
    await waitFor(() => expect(sendChatMessage).toHaveBeenCalledTimes(2));
    expect(sendChatMessage.mock.calls[1][0].data).toMatchObject({
      suggestedActionId: "ga1:o1:confirm",
      analysisOptions: { depth: "deep", webSearch: true },
    });
    expect(sendChatMessage.mock.calls[1][0].data.analysisOptions.pricingVersion).toBeUndefined();
    await waitFor(() => expect(result.current.isPending).toBe(false));

    act(() => result.current.submit("انصراف", "ga1:o1:cancel"));
    await waitFor(() => expect(sendChatMessage).toHaveBeenCalledTimes(3));
    expect(sendChatMessage.mock.calls[2][0].data.suggestedActionId).toBe("ga1:o1:cancel");
    expect(sendChatMessage.mock.calls[2][0].data.analysisOptions).toBeUndefined();
  });

  it("the hint applies only while its answer is the latest one", async () => {
    getThreadMessages
      .mockResolvedValueOnce(before)
      .mockResolvedValueOnce([...before, assistant("a1")])
      .mockResolvedValue([...before, assistant("a1"), user("u2", "x"), assistant("a2")]);
    sendChatMessage
      .mockResolvedValueOnce({ ok: true, result: { threadId: THREAD, aiMsg: withHint("a1") } })
      .mockResolvedValue({ ok: true, result: { threadId: THREAD, aiMsg: assistant("a2") } });
    const { result } = renderHook(() => useChatThread(THREAD), {
      wrapper: wrapper(new QueryClient()),
    });
    await waitFor(() => expect(result.current.messages).toHaveLength(1));
    act(() => result.current.submit("گزارش"));
    await waitFor(() => expect(result.current.analysisHint).not.toBeNull());
    await waitFor(() => expect(result.current.isPending).toBe(false));

    act(() => result.current.submit("x"));

    await waitFor(() => expect(result.current.messages).toHaveLength(4));
    expect(result.current.analysisHint).toBeNull();
  });

  it("a reloaded conversation uses the hint saved on its latest answer (OQ-113 a)", async () => {
    getThreadMessages.mockResolvedValue([...before, withHint("a1", true)]);
    sendChatMessage.mockResolvedValue({ ok: true, result: { threadId: THREAD } });
    const { result } = renderHook(() => useChatThread(THREAD), {
      wrapper: wrapper(new QueryClient()),
    });

    await waitFor(() => expect(result.current.analysisHint?.offer?.offerId).toBe("o1"));
    expect(result.current.analysisHintMessageId).toBe("a1");

    act(() => result.current.submit("حاشیه سود چرا افت کرد؟"));
    await waitFor(() => expect(sendChatMessage).toHaveBeenCalledOnce());
    expect(sendChatMessage.mock.calls[0][0].data.analysisOptions).toEqual({
      depth: "deep",
      webSearch: true,
      pricingVersion: "v1",
    });
  });

  it("the first answer's hint reaches the conversation the new-chat page opens", async () => {
    const client = new QueryClient();
    sendChatMessage.mockResolvedValue({
      ok: true,
      result: { threadId: THREAD, aiMsg: withHint("a1") },
    });
    const navigateToThread = vi.fn();
    const start = renderHook(() => useStartChat({ navigateToThread }), {
      wrapper: wrapper(client),
    });
    act(() => start.result.current.submit("عملکرد مالی فصلی فولاد را بررسی کن"));
    await waitFor(() => expect(navigateToThread).toHaveBeenCalledWith(THREAD));

    getThreadMessages.mockResolvedValue([...before, assistant("a1")]);
    const { result } = renderHook(() => useChatThread(THREAD), { wrapper: wrapper(client) });

    await waitFor(() => expect(result.current.analysisHint?.pricingVersion).toBe("v1"));
    expect(result.current.analysisHintMessageId).toBe("a1");
  });
});
