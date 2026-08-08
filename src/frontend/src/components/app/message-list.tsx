import { ChevronRight, ChevronLeft, Loader2, ShieldCheck } from "lucide-react";
import type { AssistantChatBlock, ChatMessage, DisclosureListingResult, ScannerTable } from "@/lib/chat.functions";
import { formatNumber, formatPercent, toPersianDigits } from "@/lib/format/persian";
import { replaceProviderDisplayNames } from "@/lib/format/provider-display";
import { MarkdownMessage } from "@/components/app/markdown-message";
import { OrchestrationDiagnosticsPanel } from "@/components/app/orchestration-diagnostics-panel";
import { MonthlyActivityTrendChart } from "@/components/app/monthly-activity-trend-chart";
import { PsGauge } from "@/components/app/ps-gauge";
import { FollowSymbolButton } from "@/components/app/follow-symbol-button";
import { consolidationLabel, disclosureTypeLabels, formatDisclosurePublicationDate, formatDisclosureReceiptDate } from "@/lib/format/disclosure";

interface Props {
  messages: ChatMessage[];
  loading: boolean;
  streaming: boolean;
  onSuggested: (q: string, actionId?: string) => void;
  onPageChange?: (page: number, originalQuery?: string, pageSize?: number) => void;
  onDisclosurePageChange?: (page: number, originalQuery: string) => void;
  showDiagnostics?: boolean;
  followedSymbols?: ReadonlySet<string>;
}

export function MessageList({
  messages,
  loading,
  streaming,
  onSuggested,
  onPageChange,
  onDisclosurePageChange,
  showDiagnostics,
  followedSymbols,
}: Props) {
  if (loading) return <div className="p-8 text-sm text-muted-foreground">در حال بارگذاری...</div>;
  return (
    <div className="p-6 md:p-8 space-y-10 max-w-4xl mx-auto w-full">
      {messages.map((message, index) => {
        const originalQuery = [...messages.slice(0, index)].reverse().find((item) => item.role === "user");
        return (
        <div key={message.id} className="animate-fade-up">
          {message.role === "user" ? (
            <UserBubble text={(message.content as { text: string }).text} />
          ) : (
            <AssistantBlock
              block={message.content as AssistantChatBlock}
              onSuggested={onSuggested}
              actionsDisabled={streaming}
              onPageChange={onPageChange}
              onDisclosurePageChange={onDisclosurePageChange}
              disclosureOriginalQuery={originalQuery && "text" in originalQuery.content ? originalQuery.content.text : undefined}
              showDiagnostics={showDiagnostics}
              followedSymbols={followedSymbols}
            />
          )}
        </div>
        );
      })}
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
  actionsDisabled,
  onPageChange,
  onDisclosurePageChange,
  disclosureOriginalQuery,
  showDiagnostics,
  followedSymbols,
}: {
  block: AssistantChatBlock;
  onSuggested: (q: string, actionId?: string) => void;
  actionsDisabled: boolean;
  onPageChange?: (page: number, originalQuery?: string, pageSize?: number) => void;
  onDisclosurePageChange?: (page: number, originalQuery: string) => void;
  disclosureOriginalQuery?: string;
  showDiagnostics?: boolean;
  followedSymbols?: ReadonlySet<string>;
}) {
  const tableMetadataLabel = block.tableMetadataLabel ?? getMonthlySalesMetadataLabel(block);
  const message = tableMetadataLabel && isTechnicalMonthlySalesUnitNote(block.message)
    ? ""
    : replaceProviderDisplayNames(block.message);
  const isRtlMessage = block.replyLanguage === "fa" ||
    (!block.replyLanguage && containsPersianText(message));

  return (
    <div className="flex gap-4" dir="ltr">
      <div className="size-7 rounded-lg bg-emerald-soft ring-1 ring-emerald/30 flex-shrink-0 flex items-center justify-center text-[10px] text-emerald font-mono">
        AI
      </div>
      <div
        className={`flex-1 space-y-5 min-w-0 ${isRtlMessage ? "text-right" : "text-left"}`}
        dir={isRtlMessage ? "rtl" : "ltr"}
      >
        {message.trim().length > 0 && (
          <MarkdownMessage content={message} direction={isRtlMessage ? "rtl" : "ltr"} />
        )}

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

        {block.table && (block.table.rows.length > 0 || isRenderableEmptyTable(block.table)) && (
          <ScannerResultTable
            table={block.table}
            metadataLabel={tableMetadataLabel}
            originalQuery={disclosureOriginalQuery}
            onPageChange={onPageChange}
            followedSymbols={followedSymbols}
          />
        )}

        {block.monthlyActivityTrendResult && (
          <MonthlyActivityTrendChart data={block.monthlyActivityTrendResult} />
        )}

        {block.psVisualizationResult && !block.monthlyActivityTrendResult && (
          <PsGauge data={block.psVisualizationResult} />
        )}

        {block.disclosureListingResult && (
          <DisclosureListingTable
            result={block.disclosureListingResult}
            originalQuery={disclosureOriginalQuery}
            onPageChange={onDisclosurePageChange}
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

        {(block.suggestedActions ?? []).length > 0 && (
          <div className="flex flex-wrap gap-2 pt-1" role="group" aria-label={block.replyLanguage === "fa" ? "پیشنهادهای ادامه گفتگو" : "Suggested next actions"}>
            {(block.suggestedActions ?? []).map((action) => (
              <button
                key={action.id}
                type="button"
                disabled={actionsDisabled}
                onClick={() => !actionsDisabled && onSuggested(action.message, action.id)}
                className="px-3 py-1.5 rounded-full ring-1 ring-emerald/30 bg-emerald-soft text-[11px] text-emerald hover:bg-emerald/15 focus-visible:outline-none focus-visible:ring-2 transition disabled:opacity-50 disabled:cursor-not-allowed"
              >
                {action.label}
              </button>
            ))}
          </div>
        )}

        {block.suggestedQuestions.length > 0 && (
          <div className="flex flex-wrap gap-2 pt-1">
            {block.suggestedQuestions.map((question) => (
              <button
                key={question}
                disabled={actionsDisabled}
                onClick={() => !actionsDisabled && onSuggested(question)}
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

function DisclosureListingTable({ result, originalQuery, onPageChange }: { result: DisclosureListingResult; originalQuery?: string; onPageChange?: (page: number, originalQuery: string) => void }) {
  const stale = result.freshnessReasonCode.toLowerCase().includes("stale");
  return <section dir="rtl" className="space-y-3 rounded-xl border border-hairline p-3">
    {(stale || result.coverageStatus !== "Complete") && <p role="status" className="text-xs text-amber-500">{stale ? "بخشی از داده‌ها ممکن است به‌روز نباشند." : "پوشش اطلاعیه‌ها کامل نیست."}</p>}
    <div className="overflow-x-auto"><table className="min-w-[680px] w-full text-right text-xs"><thead><tr className="border-b border-hairline"><th className="p-2">نماد</th><th className="p-2">عنوان</th><th className="p-2">نوع</th><th className="p-2">انتشار</th><th className="p-2">دریافت</th></tr></thead><tbody>{result.items.map((item) => <tr key={item.disclosureId} className="border-b border-hairline/60"><td className="p-2"><bdi>{item.symbol ?? item.companyName ?? "—"}</bdi></td><td className="p-2">{item.title}{item.isRevised ? " (اصلاحی)" : ""} ({consolidationLabel(item.isComposing)})</td><td className="p-2">{disclosureTypeLabels[item.type] ?? item.type}</td><td className="p-2">{formatDisclosurePublicationDate(item.publishedAt)}</td><td className="p-2">{formatDisclosureReceiptDate(item.receivedAt)}</td></tr>)}</tbody></table></div>
    {result.items.length === 0 && <p className="text-sm text-muted-foreground">اطلاعیه‌ای یافت نشد.</p>}
    <nav aria-label="صفحه‌بندی اطلاعیه‌ها" className="flex items-center justify-between text-xs"><button type="button" disabled={!result.hasPreviousPage || !originalQuery} onClick={() => originalQuery && onPageChange?.(result.page - 1, originalQuery)}>صفحه قبل</button><span>صفحه {result.page} از {result.totalPages || 1}</span><button type="button" disabled={!result.hasNextPage || !originalQuery} onClick={() => originalQuery && onPageChange?.(result.page + 1, originalQuery)}>صفحه بعد</button></nav>
  </section>;
}

function ScannerResultTable({
  table,
  metadataLabel,
  originalQuery,
  onPageChange,
  followedSymbols,
}: {
  table: ScannerTable;
  metadataLabel?: string;
  originalQuery?: string;
  onPageChange?: (page: number, originalQuery?: string, pageSize?: number) => void;
  followedSymbols?: ReadonlySet<string>;
}) {
  const isPersianTable = isRtlFinancialTable(table);
  const { page, pageSize, totalPages, matchingSymbolCount } = table.executionFacts;
  const hasPagination = totalPages > 1;
  const isSalesGrowth = Boolean(table.salesGrowthMetadata);

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

        {isSalesGrowth && <SalesGrowthTableStatus table={table} />}
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
                      {formatScannerCell(cell, column.identifier, isSalesGrowth)}
                      {shouldShowFreshnessStatus(cell?.freshnessStatus) && (
                        <span className="block text-[9px] text-muted-foreground">
                          {cell.freshnessStatus}
                        </span>
                      )}
                      {column.identifier.toUpperCase() === "SYMBOL" && row.symbolCode && (
                        <span className="mt-1 block">
                          <FollowSymbolButton
                            symbol={row.symbolCode}
                            compact
                            followed={followedSymbols?.has(row.symbolCode) ?? false}
                          />
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
              onClick={() => onPageChange?.(page - 1, originalQuery, pageSize)}
              className="p-1 rounded-md hover:bg-white/10 disabled:opacity-30 disabled:cursor-not-allowed transition"
              aria-label="صفحه قبل"
            >
              <ChevronRight className="size-3.5" />
            </button>
            <button
              disabled={page >= totalPages || !onPageChange}
              onClick={() => onPageChange?.(page + 1, originalQuery, pageSize)}
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

function isRenderableEmptyTable(table: ScannerTable) {
  return Boolean(table.salesGrowthMetadata || table.missingDataWarnings.length > 0);
}

function SalesGrowthTableStatus({ table }: { table: ScannerTable }) {
  const metadata = table.salesGrowthMetadata;
  const rowMetadata = table.rows[0]?.salesGrowthMetadata;
  if (!metadata) return null;

  const statusLabel = metadata.selectionStatus === "Available"
    ? "قابل استفاده"
    : metadata.selectionStatus === "Partial"
      ? "ناقص"
      : "در دسترس نیست";
  const baseline = rowMetadata?.baselinePeriod
    ? `مبنای مقایسه: ${formatPeriod(rowMetadata.baselinePeriod)}`
    : rowMetadata?.baselineWindow.length
      ? `پنجره مبنا: ${formatPeriod(rowMetadata.baselineWindow[0])} تا ${formatPeriod(rowMetadata.baselineWindow[rowMetadata.baselineWindow.length - 1])}`
      : undefined;

  return (
    <div className="space-y-1 px-4 py-2 text-[11px] text-muted-foreground" dir="rtl" data-testid="sales-growth-table-status">
      <div>
        دوره هدف: {formatPeriod(metadata.targetCommonPeriod)} · پوشش: {toPersianDigits(metadata.coverageNumerator)} از {toPersianDigits(metadata.coverageDenominator)} ({toPersianDigits(metadata.coveragePercent)}٪) · وضعیت: {statusLabel}
      </div>
      {baseline && <div>{baseline}</div>}
      {rowMetadata?.freshnessSource && <div>منبع تازگی: {rowMetadata.freshnessSource}</div>}
      {rowMetadata?.latestObservedAtUtc && <div>آخرین مشاهده: {formatPeriod(rowMetadata.latestObservedAtUtc)}</div>}
      {metadata.mixedPeriods && <div className="text-amber-500">هشدار: نتایج دوره‌های ترکیبی دارند.</div>}
      {table.missingDataWarnings.map((warning) => (
        <div key={warning} className="text-amber-500" role="status">{warning}</div>
      ))}
      {table.rows.length === 0 && <div>داده‌ای برای نمایش وجود ندارد.</div>}
    </div>
  );
}

function formatPeriod(value: string) {
  const match = value.match(/^(\d{4})-(\d{2})-(\d{2})/u);
  return toPersianDigits(match ? `${match[1]}/${match[2]}` : value);
}

function formatScannerCell(
  cell: ScannerTable["rows"][number]["cells"][string] | undefined,
  columnIdentifier: string,
  isSalesGrowth: boolean,
) {
  if (!cell || (cell.formattedValue === undefined && cell.value === undefined)) {
    return isSalesGrowth ? "در دسترس نیست" : "—";
  }
  if (cell.formattedValue !== undefined) return cell.formattedValue;
  if (cell.value === undefined) return isSalesGrowth ? "در دسترس نیست" : "—";

  const key = columnIdentifier.toUpperCase();
  return key.includes("GROWTH_PERCENT") || key.includes("CHANGE_PCT")
    ? formatPercent(cell.value, false)
    : formatNumber(cell.value, { maximumFractionDigits: 2 });
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

  const salesGrowthLabels: Record<string, string> = {
    MONTHLY_SALES: "فروش آخرین دوره",
    MONTHLY_SALES_BASELINE_PREVIOUS_MONTH: "فروش ماه قبل",
    MONTHLY_SALES_BASELINE_SAME_MONTH_PREVIOUS_YEAR: "فروش ماه مشابه سال قبل",
    MONTHLY_SALES_BASELINE_AVERAGE_PREVIOUS_12_MONTHS: "میانگین فروش ۱۲ ماه قبل",
    MONTHLY_SALES_GROWTH_PERCENT: "درصد رشد",
    MONTHLY_SALES_GROWTH_MULTIPLE: "نسبت فروش",
  };

  return salesGrowthLabels[key] ?? localizedLabels[key] ?? localizedLabels[displayName] ?? column.displayName;
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
