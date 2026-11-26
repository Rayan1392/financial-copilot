import type {
  AssistantChatBlock,
  AssistantContentResponse,
  ScannerTable,
} from "@/lib/chat.functions";

/*
 * Maps a stored or live assistant answer (the API's `assistantContent`) to the block the chat renders. Client-safe:
 * shared by the chat server functions and the admin conversation-log drawer (Feature 164), which must show an answer
 * exactly as the user saw it.
 */

export function mapAssistantBlock(
  content: AssistantContentResponse | undefined,
  fallbackMessage: string,
): AssistantChatBlock {
  const explanation = content?.explainableAnswer;
  const financialStatementAnalysisResult = content?.financialStatementAnalysisResult;
  // Prefer symbolLookupTable over scannerTable; both render via ScannerResultTable.
  const table = normalizeRenderableTable(content?.symbolLookupTable ?? content?.scannerTable);
  const rawMessage = financialStatementAnalysisResult
    ? ""
    : (explanation?.explanationText ??
      content?.clarificationMessage ??
      content?.textAnswer ??
      fallbackMessage);
  const tableMetadataLabel = isMonthlySalesTechnicalUnitNote(rawMessage, table)
    ? "واحد: میلیون ریال"
    : undefined;
  return {
    message: tableMetadataLabel ? "" : rawMessage,
    intent: content?.intent ?? "Unknown",
    outcome: content?.outcome,
    outcomeReasonCode: content?.outcomeReasonCode,
    replyLanguage: content?.replyLanguage,
    languageGuardApplied: content?.languageGuardApplied,
    confidence: content?.confidenceScore?.score ?? explanation?.confidence?.score,
    creditsUsed: content?.usage?.creditsCharged ?? 0,
    suggestedQuestions: explanation?.suggestedFollowUpQuestions ?? [],
    suggestedActions: content?.suggestedActions ?? [],
    filters:
      explanation?.filterChips.map((chip) => ({
        label: chip.metricDisplayName,
        value: `${chip.operatorSymbol} ${chip.thresholdFormatted}`,
      })) ?? [],
    table,
    tableMetadataLabel,
    monthlyActivityTrendResult: content?.monthlyActivityTrendResult,
    monthlyProductComparisonResult: content?.monthlyProductComparisonResult,
    monthlyProductTrendResult: content?.monthlyProductTrendResult,
    symbolSnapshotResult: content?.symbolSnapshotResult,
    valuationGaugeCards: content?.valuationGaugeCards ?? undefined,
    disclosureListingResult: content?.disclosureListingResult,
    psVisualizationResult: content?.psVisualizationResult,
    financialStatementAnalysisResult,
    financialStatementTableResult: content?.financialStatementTableResult,
    sources: content?.sources ?? undefined,
    groundedAnalysis: content?.groundedAnalysis ?? undefined,
    citations: explanation?.dataCitations ?? [],
    orchestration:
      content?.aiOrchestrationMode || content?.workflowCorrelationId
        ? {
            mode: content?.aiOrchestrationMode,
            workflowVersion: content?.workflowVersion,
            providerSelection: content?.providerSelection,
            providerFallbackOccurred: content?.providerFallbackOccurred,
            correlationId: content?.workflowCorrelationId,
          }
        : undefined,
  };
}

function normalizeRenderableTable(table: ScannerTable | undefined): ScannerTable | undefined {
  if (!table) return undefined;
  if (
    table.rows.length === 0 &&
    !table.salesGrowthMetadata &&
    table.missingDataWarnings.length === 0
  ) {
    return undefined;
  }
  return table;
}

function isMonthlySalesTechnicalUnitNote(message: string | undefined, table?: ScannerTable) {
  if (message?.trim() !== "Unit: million Rials") return false;
  return Boolean(
    table?.columns.some((column) => column.identifier.toUpperCase().startsWith("MONTHLY_SALES")),
  );
}
