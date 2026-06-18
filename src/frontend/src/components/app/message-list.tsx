import { ChevronRight, ChevronLeft, Loader2, ShieldCheck } from "lucide-react";
import type { AssistantChatBlock, ChatMessage, ScannerTable } from "@/lib/chat.functions";
import { toPersianDigits } from "@/lib/format/persian";
import { MarkdownMessage } from "@/components/app/markdown-message";
import { OrchestrationDiagnosticsPanel } from "@/components/app/orchestration-diagnostics-panel";

interface Props {
  messages: ChatMessage[];
  loading: boolean;
  streaming: boolean;
  onSuggested: (q: string) => void;
  onPageChange?: (page: number) => void;
  showDiagnostics?: boolean;
}

export function MessageList({ messages, loading, streaming, onSuggested, onPageChange, showDiagnostics }: Props) {
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
              onPageChange={onPageChange}
              showDiagnostics={showDiagnostics}
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
  onPageChange,
  showDiagnostics,
}: {
  block: AssistantChatBlock;
  onSuggested: (q: string) => void;
  onPageChange?: (page: number) => void;
  showDiagnostics?: boolean;
}) {
  const tableMetadataLabel = block.tableMetadataLabel ?? getMonthlySalesMetadataLabel(block);
  const message = tableMetadataLabel && isTechnicalMonthlySalesUnitNote(block.message)
    ? ""
    : block.message;

  return (
    <div className="flex gap-4" dir="ltr">
      <div className="size-7 rounded-lg bg-emerald-soft ring-1 ring-emerald/30 flex-shrink-0 flex items-center justify-center text-[10px] text-emerald font-mono">
        AI
      </div>
      <div className="flex-1 space-y-5 min-w-0" dir="auto">
        {message.trim().length > 0 && <MarkdownMessage content={message} />}

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

        {block.table && block.table.rows.length > 0 && (
          <ScannerResultTable
            table={block.table}
            metadataLabel={tableMetadataLabel}
            onPageChange={onPageChange}
          />
        )}

        {/* Citations are only shown for non-scanner responses (e.g. single-symbol analysis).
            When a scanner table is present the table's freshness indicators already
            show data provenance per cell — a separate citation list would duplicate every row. */}
        {!block.table && block.citations.length > 0 && (
          <div className="text-[11px] text-muted-foreground space-y-1">
            {block.citations.map((citation, index) => (
              <div key={`${citation.symbolCode}:${citation.metricCode}:${index}`}>
                {citation.symbolCode} · {citation.metricCode} · {citation.freshnessStatus}
              </div>
            ))}
          </div>
        )}

        <div className="flex items-center gap-3 text-[10px] text-muted-foreground">
          {typeof block.confidence === "number" && (
            <>
              <span className="flex items-center gap-1">
                <ShieldCheck className="size-3 text-emerald" />
                اطمینان {toPersianDigits(Math.round(block.confidence * 100))}٪
              </span>
              <span>·</span>
            </>
          )}
          <span>{toPersianDigits(block.creditsUsed)} اعتبار مصرف شد</span>
        </div>

        {showDiagnostics && block.orchestration && (
          <OrchestrationDiagnosticsPanel orchestration={block.orchestration} />
        )}

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

function getMonthlySalesMetadataLabel(block: AssistantChatBlock) {
  if (!isTechnicalMonthlySalesUnitNote(block.message)) return undefined;
  if (!block.table?.columns.some((column) =>
    column.identifier.toUpperCase().startsWith("MONTHLY_SALES"),
  )) {
    return undefined;
  }

  return "واحد: میلیون ریال";
}

function isTechnicalMonthlySalesUnitNote(message: string) {
  return message.trim() === "Unit: million Rials";
}

function ScannerResultTable({
  table,
  metadataLabel,
  onPageChange,
}: {
  table: ScannerTable;
  metadataLabel?: string;
  onPageChange?: (page: number) => void;
}) {
  const isPersianTable = isRtlFinancialTable(table);
  const { page, pageSize, totalPages, matchingSymbolCount } = table.executionFacts;
  const hasPagination = totalPages > 1;

  return (
    <div className="space-y-2">
      <div
        className="rounded-2xl overflow-x-auto ring-1 ring-hairline bg-surface/40"
        dir={isPersianTable ? "rtl" : "ltr"}
      >
        {metadataLabel && (
          <div
            className="flex justify-start px-4 pt-2 text-[10px] text-muted-foreground"
            data-testid="table-metadata"
            dir="ltr"
          >
            <span dir="rtl">{metadataLabel}</span>
          </div>
        )}
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
                  {isPersianTable ? localizeColumnDisplayName(column) : column.displayName}
                </th>
              ))}
            </tr>
          </thead>
          <tbody className="divide-y divide-hairline">
            {table.rows.map((row) => (
              <tr key={row.symbolCode}>
                {table.columns.map((column) => {
                  const cell = getRenderableCell(row, column.identifier);
                  return (
                    <td
                      key={column.identifier}
                      className={`px-4 py-2.5 text-foreground/80 mono ${
                        isPersianTable ? "text-right" : "text-left"
                      }`}
                    >
                      {cell?.formattedValue ?? cell?.value ?? "—"}
                      {shouldShowFreshnessStatus(cell?.freshnessStatus) && (
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

      {hasPagination && (
        <div
          className="flex items-center justify-between px-1 text-xs text-muted-foreground"
          dir="rtl"
        >
          <span>
            {toPersianDigits(matchingSymbolCount)} نتیجه · صفحه{" "}
            {toPersianDigits(page)} از {toPersianDigits(totalPages)}
          </span>
          <div className="flex items-center gap-1">
            <button
              disabled={page <= 1 || !onPageChange}
              onClick={() => onPageChange?.(page - 1)}
              className="p-1 rounded-md hover:bg-white/10 disabled:opacity-30 disabled:cursor-not-allowed transition"
              aria-label="صفحه قبل"
            >
              <ChevronRight className="size-3.5" />
            </button>
            <button
              disabled={page >= totalPages || !onPageChange}
              onClick={() => onPageChange?.(page + 1)}
              className="p-1 rounded-md hover:bg-white/10 disabled:opacity-30 disabled:cursor-not-allowed transition"
              aria-label="صفحه بعد"
            >
              <ChevronLeft className="size-3.5" />
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

function containsPersianText(text: string) {
  return /[\u0600-\u06ff\u0750-\u077f]/u.test(text);
}

function shouldShowFreshnessStatus(status?: string) {
  return Boolean(status && status !== "Persisted");
}

function getRenderableCell(row: ScannerTable["rows"][number], columnIdentifier: string) {
  const key = columnIdentifier.toUpperCase();
  if (key === "SYMBOL") {
    return {
      ...row.cells[columnIdentifier],
      formattedValue: row.symbolCode || row.cells[columnIdentifier]?.formattedValue,
      freshnessStatus: row.cells[columnIdentifier]?.freshnessStatus ?? "Persisted",
    };
  }

  if (key === "COMPANY_NAME" || key === "COMPANY") {
    return {
      ...row.cells[columnIdentifier],
      formattedValue: row.companyName || row.cells[columnIdentifier]?.formattedValue,
      freshnessStatus: row.cells[columnIdentifier]?.freshnessStatus ?? "Persisted",
    };
  }

  return row.cells[columnIdentifier];
}

function localizeColumnDisplayName(column: ScannerTable["columns"][number]) {
  const key = column.identifier.toUpperCase();
  const displayName = column.displayName.toUpperCase();
  if (key === "PE_TTM" || displayName === "PE_TTM") return "PE_TTM";
  if (key === "PS_TTM" || displayName === "PS_TTM") return "PS_TTM";

  const localizedLabels: Record<string, string> = {
    SYMBOL: "نماد",
    COMPANY: "شرکت",
    PE_TTM: "نسبت قیمت به درآمد دوازده‌ماهه",
    PS_TTM: "نسبت قیمت به فروش دوازده‌ماهه",
    LATEST_PRICE: "آخرین قیمت",
    DAILY_CHANGE_PERCENT: "تغییر روزانه %",
    MARKET_CAP: "ارزش بازار",
    AVG_12M_MONTHLY_SALES: "متوسط فروش ۱۲ ماهه",
  };

  return localizedLabels[key] ?? localizedLabels[displayName] ?? column.displayName;
}

function isRtlFinancialTable(table: ScannerTable) {
  if (table.columns.some((column) => containsPersianText(column.displayName))) return true;

  return table.rows.some((row) => {
    if (containsPersianText(row.symbolCode) || containsPersianText(row.companyName ?? "")) {
      return true;
    }

    return Object.values(row.cells).some((cell) =>
      containsPersianText(String(cell?.formattedValue ?? cell?.value ?? "")),
    );
  });
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
