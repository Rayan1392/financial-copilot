import type {
  AdminConversationLogChannel,
  AdminConversationLogStatus,
  AdminConversationLogUser,
} from "@/integrations/financial-copilot/admin-client";
import { formatDisclosureReceiptDate } from "@/lib/format/disclosure";

// Feature 164: labels and formats shared by the conversation-log list and drawer (spec 164, "UI").

export const conversationLogStatusLabels: Record<AdminConversationLogStatus, string> = {
  Answered: "پاسخ داده شد",
  PartialAnswer: "پاسخ ناقص",
  ClarificationNeeded: "نیاز به توضیح",
  DisambiguationNeeded: "نیاز به ابهام‌زدایی",
  NoData: "داده‌ای یافت نشد",
  Unsupported: "پشتیبانی نمی‌شود",
  TemporarilyUnavailable: "موقتاً در دسترس نیست",
  Failed: "ناموفق",
  NoResponse: "بدون پاسخ",
};

export const channelLabels: Record<AdminConversationLogChannel, string> = {
  Web: "وب",
  Telegram: "تلگرام",
};

/** Jalali date and time in Tehran, e.g. «۱۴۰۵/۰۸/۱۰، ۱۴:۰۵». */
export function formatTehranDateTime(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "—" : formatDisclosureReceiptDate(value);
}

/**
 * The label of an actor without a user account in the tenant: the configured API client, or an unknown ID.
 * Only an explicit `hasUserAccount: true` is a user account, so a response without `actorKind` stays unknown.
 */
export function unresolvedActorLabel(user: AdminConversationLogUser) {
  if (user.actorKind !== "ApiClient") return "شناسهٔ ناشناخته";
  return user.apiClientName ? `کلاینت API: ${user.apiClientName}` : "کلاینت API";
}

/** The first eight characters of an ID, for table cells; the full ID goes in a tooltip or dialog. */
export function shortActorId(id: string) {
  return id.length > 8 ? `${id.slice(0, 8)}…` : id;
}
