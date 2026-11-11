import { describe, expect, it } from "vitest";
import type { ChatMessage } from "@/lib/chat.functions";
import { PENDING_QUESTION_ID, withPendingQuestion } from "@/lib/chat/pending-question";

const persisted: ChatMessage[] = [
  { id: "u1", role: "user", created_at: "", content: { text: "اول" } },
];

describe("withPendingQuestion (BUG-023)", () => {
  it("appends the pending question as the last user message", () => {
    const result = withPendingQuestion(persisted, "دوم");

    expect(result).toHaveLength(2);
    expect(result[0]).toBe(persisted[0]);
    expect(result[1]).toMatchObject({
      id: PENDING_QUESTION_ID,
      role: "user",
      content: { text: "دوم" },
    });
  });

  it("shows the first question of an empty conversation", () => {
    expect(withPendingQuestion([], "گزارش فصلی فملی")).toEqual([
      {
        id: PENDING_QUESTION_ID,
        role: "user",
        created_at: "",
        content: { text: "گزارش فصلی فملی" },
      },
    ]);
  });

  it("leaves the conversation unchanged when nothing is pending", () => {
    expect(withPendingQuestion(persisted, null)).toEqual(persisted);
    expect(withPendingQuestion(persisted, "")).toEqual(persisted);
  });
});
