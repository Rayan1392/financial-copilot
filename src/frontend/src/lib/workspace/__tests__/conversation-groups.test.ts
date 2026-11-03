import { describe, expect, it } from "vitest";
import { groupConversationsByRecency, recencyGroup } from "@/lib/workspace/conversation-groups";
import type { ChatThread } from "@/lib/chat.functions";

const now = new Date(2026, 10, 3, 10, 0, 0);
const at = (daysAgo: number, hour = 9) => new Date(2026, 10, 3 - daysAgo, hour, 0, 0).toISOString();
const thread = (id: string, updatedAt: string): ChatThread => ({
  id,
  title: id,
  created_at: updatedAt,
  updated_at: updatedAt,
});

describe("recencyGroup (AC-10)", () => {
  it.each([
    [at(0), "today"],
    [at(1), "yesterday"],
    [at(1, 0), "yesterday"],
    [at(2), "last7"],
    [at(7), "last7"],
    [at(8), "older"],
    ["not-a-date", "older"],
  ] as const)("%s → %s", (updatedAt, expected) => {
    expect(recencyGroup(updatedAt, now)).toBe(expected);
  });
});

describe("groupConversationsByRecency", () => {
  it("keeps server order inside groups, labels them in Persian, and omits empty groups", () => {
    const groups = groupConversationsByRecency(
      [thread("a", at(0, 9)), thread("b", at(0, 8)), thread("c", at(30))],
      now,
    );
    expect(groups.map((group) => [group.label, group.threads.map((item) => item.id)])).toEqual([
      ["امروز", ["a", "b"]],
      ["قدیمی‌تر", ["c"]],
    ]);
  });
});
