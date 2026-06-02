import { Loader2, ShieldCheck } from "lucide-react";
import type { AssistantChatBlock, ChatMessage, ScannerTable } from "@/lib/chat.functions";
import { toPersianDigits } from "@/lib/format/persian";

interface Props {
  messages: ChatMessage[];
  loading: boolean;
  streaming: boolean;
  onSuggested: (q: string) => void;
}

export function MessageList({ messages, loading, streaming, onSuggested }: Props) {
  if (loading) return <div className="p-8 text-sm text-muted-foreground">در حال بارگذاری...</div>;
  return (
    <div className="p-6 md:p-8 space-y-10 max-w-4xl mx-auto w-full">
      {messages.map((message) => (
        <div key={message.id} className="animate-fade-up">
          {message.role === "user" ? (
            <UserBubble text={(message.content as { text: string }).text} />
          ) : (
            <AssistantBlock
              block={message.content as AssistantChatBlock}
              onSuggested={onSuggested}
            />
          )}
        </div>
      ))}
      {streaming && <StreamingPlaceholder />}
    </div>
  );
}

function UserBubble({ text }: { text: string }) {
  return (
    <div className="flex justify-end" dir="ltr">
      <div
        className="max-w-[56ch] bg-surface ring-1 ring-hairline rounded-2xl px-5 py-3 text-foreground text-pretty text-sm"
        dir="auto"
      >
        {text}
      </div>
    </div>
  );
}

function AssistantBlock({
  block,
  onSuggested,
}: {
  block: AssistantChatBlock;
  onSuggested: (q: string) => void;
}) {
  return (
    <div className="flex gap-4" dir="ltr">
      <div className="size-7 rounded-lg bg-emerald-soft ring-1 ring-emerald/30 flex-shrink-0 flex items-center justify-center text-[10px] text-emerald font-mono">
        AI
      </div>
      <div className="flex-1 space-y-5 min-w-0" dir="auto">
        <p className="text-foreground/90 leading-relaxed text-pretty text-[15px] max-w-[64ch]" dir="auto">
          {block.message}
        </p>

        {block.filters.length > 0 && (
          <div className="flex flex-wrap gap-2">
            {block.filters.map((filter) => (
              <span
                key={`${filter.label}:${filter.value}`}
                className="text-[11px] px-2.5 py-1 rounded-full bg-emerald-soft ring-1 ring-emerald/20 text-emerald"
              >
                {filter.label}: {filter.value}
              </span>
            ))}
          </div>
        )}

        {block.table && <ScannerResultTable table={block.table} />}

        {block.citations.length > 0 && (
          <div className="text-[11px] text-muted-foreground space-y-1">
            {block.citations.map((citation, index) => (
              <div key={`${citation.symbolCode}:${citation.metricCode}:${index}`}>
                {citation.symbolCode} · {citation.metricCode} · {citation.freshnessStatus}
              </div>
            ))}
          </div>
        )}

        <div className="flex items-center gap-3 text-[10px] text-muted-foreground">
          <span className="flex items-center gap-1">
            <ShieldCheck className="size-3 text-emerald" />
            اطمینان {toPersianDigits(Math.round(block.confidence * 100))}٪
          </span>
          <span>·</span>
          <span>{toPersianDigits(block.creditsUsed)} اعتبار مصرف شد</span>
        </div>

        {block.suggestedQuestions.length > 0 && (
          <div className="flex flex-wrap gap-2 pt-1">
            {block.suggestedQuestions.map((question) => (
              <button
                key={question}
                onClick={() => onSuggested(question)}
                className="px-3 py-1.5 rounded-full ring-1 ring-hairline bg-white/5 text-[11px] text-muted-foreground hover:text-foreground hover:bg-white/10 transition"
              >
                {question}
              </button>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

function ScannerResultTable({ table }: { table: ScannerTable }) {
  const isPersianTable = table.columns.some((column) => containsPersianText(column.displayName));

  return (
    <div
      className="rounded-2xl overflow-x-auto ring-1 ring-hairline bg-surface/40"
      dir={isPersianTable ? "rtl" : "ltr"}
    >
      <table className={`w-full text-sm ${isPersianTable ? "text-right" : "text-left"}`}>
        <thead className="bg-white/5">
          <tr>
            {table.columns.map((column) => (
              <th
                key={column.identifier}
                className={`px-4 py-2.5 font-medium text-muted-foreground text-xs ${
                  isPersianTable ? "text-right" : "text-left"
                }`}
              >
                {column.displayName}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-hairline">
          {table.rows.map((row) => (
            <tr key={row.symbolCode}>
              {table.columns.map((column) => {
                const cell = row.cells[column.identifier];
                return (
                  <td
                    key={column.identifier}
                    className={`px-4 py-2.5 text-foreground/80 mono ${
                      isPersianTable ? "text-right" : "text-left"
                    }`}
                  >
                    {cell?.formattedValue ?? cell?.value ?? "—"}
                    {cell?.freshnessStatus && (
                      <span className="block text-[9px] text-muted-foreground">
                        {cell.freshnessStatus}
                      </span>
                    )}
                  </td>
                );
              })}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function containsPersianText(text: string) {
  return /[\u0600-\u06ff\u0750-\u077f]/u.test(text);
}

function StreamingPlaceholder() {
  return (
    <div className="flex gap-4 animate-fade-up">
      <div className="size-7 rounded-lg bg-emerald-soft ring-1 ring-emerald/30 flex-shrink-0 flex items-center justify-center">
        <Loader2 className="size-3.5 text-emerald animate-spin" />
      </div>
      <div className="text-sm text-muted-foreground pt-1">در حال تحلیل...</div>
    </div>
  );
}
