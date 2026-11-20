import type {
  AdminConversationLogChannel,
  AdminConversationLogStatus,
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
