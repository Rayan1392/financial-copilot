import { describe, expect, it } from "vitest";
import type { ChatMessage } from "@/lib/chat.functions";
import {
  initialRecovery,
  reconcileAfterRefetch,
  recoveryActions,
  restoresDraftImmediately,
  type SendAttempt,
  type SendRecovery,
} from "@/lib/chat/send-recovery";

const user = (id: string, text: string): ChatMessage => ({
  id,
  role: "user",
  content: { text },
  created_at: "2026-11-03T08:00:00Z",
});
const assistant = (id: string): ChatMessage => ({
  id,
  role: "assistant",
  created_at: "2026-11-03T08:00:01Z",
  content: {
    message: "answer",
    intent: "Unknown",
    creditsUsed: 1,
    suggestedQuestions: [],
    suggestedActions: [],
    filters: [],
    citations: [],
  },
});

const firstMessage: SendAttempt = {
  draft: "گزارش فصلی فملی",
  conversationId: null,
  knownMessageIds: [],
};
const followUp: SendAttempt = {
  draft: "گزارش فصلی فملی",
  conversationId: "c6f1c7d2-5a59-4c55-9c39-6c3c8d2f4c11",
  knownMessageIds: ["u1", "a1"],
};

describe("initialRecovery (spec AC-06a / AC-06b)", () => {
  it("leaves a failed first message indeterminate: the client cannot look up the conversation", () => {
    expect(initialRecovery(firstMessage, "network")).toEqual({ kind: "indeterminate" });
    expect(initialRecovery(firstMessage, "server")).toEqual({ kind: "indeterminate" });
  });

  it("reconciles a failed follow-up before offering anything", () => {
    expect(initialRecovery(followUp, "network")).toEqual({ kind: "reconciling" });
  });

  it("treats refusals as definite: the request was not processed", () => {
    expect(initialRecovery(firstMessage, "insufficient_credit")).toEqual({
      kind: "definite",
      code: "insufficient_credit",
    });
    expect(initialRecovery(followUp, "cooldown")).toEqual({ kind: "definite", code: "cooldown" });
  });
});

describe("reconcileAfterRefetch", () => {
  it("finds the answer the failed request produced", () => {
    const messages = [
      user("u1", "x"),
      assistant("a1"),
      user("u2", "  گزارش   فصلی فملی "),
      assistant("a2"),
    ];
    expect(reconcileAfterRefetch(followUp, messages)).toEqual({ kind: "answered" });
  });

  it("does not mistake an earlier identical question for the answer", () => {
    const messages = [user("u1", "گزارش فصلی فملی"), assistant("a1")];
    expect(reconcileAfterRefetch(followUp, messages)).toEqual({ kind: "not_found" });
  });

  it("requires the assistant reply, not just the question", () => {
    const messages = [user("u1", "x"), assistant("a1"), user("u2", "گزارش فصلی فملی")];
    expect(reconcileAfterRefetch(followUp, messages)).toEqual({ kind: "not_found" });
  });
});

describe("recoveryActions: no state offers a retry of the original request", () => {
  const states: SendRecovery[] = [
    { kind: "indeterminate" },
    { kind: "reconciling" },
    { kind: "answered" },
    { kind: "not_found" },
    { kind: "reconcile_failed" },
    { kind: "definite", code: "insufficient_credit" },
    { kind: "definite", code: "cooldown" },
  ];

  it("first-message failure: only the history link, no resend", () => {
    expect(recoveryActions({ kind: "indeterminate" })).toEqual({
      editAndResend: false,
      recheck: false,
      history: true,
      viewCredit: false,
    });
  });

  it("follow-up: resend only after a successful refetch without the answer", () => {
    expect(recoveryActions({ kind: "reconciling" }).editAndResend).toBe(false);
    expect(recoveryActions({ kind: "reconcile_failed" })).toMatchObject({
      editAndResend: false,
      recheck: true,
    });
    expect(recoveryActions({ kind: "not_found" })).toMatchObject({
      editAndResend: true,
      recheck: false,
    });
  });

  it("credit and cooldown refusals offer no retry; credit links to the account", () => {
    expect(recoveryActions({ kind: "definite", code: "insufficient_credit" }).viewCredit).toBe(
      true,
    );
    expect(recoveryActions({ kind: "definite", code: "cooldown" })).toEqual({
      editAndResend: false,
      recheck: false,
      history: false,
      viewCredit: false,
    });
  });

  it.each(states)("%o exposes no retry action", (state) => {
    expect(Object.keys(recoveryActions(state))).not.toContain("retry");
  });

  it("restores the draft at once only when no reconciliation is pending", () => {
    expect(restoresDraftImmediately({ kind: "indeterminate" })).toBe(true);
    expect(restoresDraftImmediately({ kind: "definite", code: "validation" })).toBe(true);
    expect(restoresDraftImmediately({ kind: "reconciling" })).toBe(false);
  });
});
