import type { AdminConversationLogStatus } from "@/integrations/financial-copilot/admin-client";
import { conversationLogStatusLabels } from "@/components/admin/ai-conversation-log-format";

// Feature 164: the status badge of the conversation-log list and drawer.

const statusTone: Record<AdminConversationLogStatus, string> = {
  Answered: "border-emerald-200 bg-emerald-50 text-emerald-800",
  PartialAnswer: "border-amber-200 bg-amber-50 text-amber-800",
  ClarificationNeeded: "border-sky-200 bg-sky-50 text-sky-800",
  DisambiguationNeeded: "border-sky-200 bg-sky-50 text-sky-800",
  NoData: "border-slate-200 bg-slate-50 text-slate-700",
  Unsupported: "border-slate-200 bg-slate-50 text-slate-700",
  TemporarilyUnavailable: "border-amber-200 bg-amber-50 text-amber-800",
  Failed: "border-rose-200 bg-rose-50 text-rose-800",
  NoResponse: "border-rose-200 bg-white text-rose-700",
};

export function AdminConversationStatusBadge({ status }: { status: AdminConversationLogStatus }) {
  return (
    <span
      className={`inline-flex whitespace-nowrap rounded-full border px-2.5 py-0.5 text-[11px] font-medium ${statusTone[status]}`}
    >
      {conversationLogStatusLabels[status]}
    </span>
  );
}
