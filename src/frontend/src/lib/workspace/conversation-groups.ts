import type { ChatThread } from "@/lib/chat.functions";

export type RecencyGroup = "today" | "yesterday" | "last7" | "older";

export const RECENCY_LABELS: Record<RecencyGroup, string> = {
  today: "امروز",
  yesterday: "دیروز",
  last7: "۷ روز گذشته",
  older: "قدیمی‌تر",
};

const ORDER: RecencyGroup[] = ["today", "yesterday", "last7", "older"];
const DAY_MS = 24 * 60 * 60 * 1000;

function startOfLocalDay(date: Date): number {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate()).getTime();
}

export function recencyGroup(updatedAt: string, now: Date): RecencyGroup {
  const time = new Date(updatedAt).getTime();
  if (!Number.isFinite(time)) return "older";
  const today = startOfLocalDay(now);
  if (time >= today) return "today";
  if (time >= today - DAY_MS) return "yesterday";
  if (time >= today - 7 * DAY_MS) return "last7";
  return "older";
}

/**
 * Groups conversations into امروز / دیروز / ۷ روز گذشته / قدیمی‌تر in the device time zone (AC-10), keeping
 * the server's most-recent-first order inside each group. Empty groups are omitted.
 */
export function groupConversationsByRecency(
  threads: readonly ChatThread[],
  now: Date,
): Array<{ group: RecencyGroup; label: string; threads: ChatThread[] }> {
  const buckets = new Map<RecencyGroup, ChatThread[]>();
  for (const thread of threads) {
    const group = recencyGroup(thread.updated_at, now);
    buckets.set(group, [...(buckets.get(group) ?? []), thread]);
  }
  return ORDER.filter((group) => buckets.has(group)).map((group) => ({
    group,
    label: RECENCY_LABELS[group],
    threads: buckets.get(group)!,
  }));
}

/** Time of day for today's rows, the Jalali date otherwise. */
export function conversationTimestamp(updatedAt: string, group: RecencyGroup): string {
  const date = new Date(updatedAt);
  if (!Number.isFinite(date.getTime())) return "";
  return group === "today"
    ? date.toLocaleTimeString("fa-IR", { hour: "2-digit", minute: "2-digit" })
    : date.toLocaleDateString("fa-IR");
}
