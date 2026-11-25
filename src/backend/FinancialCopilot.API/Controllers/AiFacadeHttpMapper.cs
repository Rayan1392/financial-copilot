using FinancialCopilot.API.Contracts;
using FinancialCopilot.API.Security;
using FinancialCopilot.Application.AI.GroundedAnalysis;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Billing;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Memory;
using FinancialCopilot.Application.Notifications;
using FinancialCopilot.Application.Scanner;

namespace FinancialCopilot.API.Controllers;

/// <summary>
/// Maps AI facade application results to their HTTP contracts. Moved verbatim out of <see cref="AiFacadeController"/>
/// (Feature 164) so the admin conversation logs show a stored answer exactly as the chat's messages endpoint does.
/// </summary>
public static class AiFacadeHttpMapper
{
    internal static AiQueryHttpResponse MapQueryResponse(AiQueryResponse result) =>
        new(
            result.ConversationId,
            result.MessageId,
            result.AssistantMessageId,
            result.Intent.ToString(),
            result.ClarificationRequired,
            result.ClarificationMessage,
            result.TextAnswer,
            result.ScannerPlan is null ? null : new ScannerPlanResponse(
                result.ScannerPlan.PlanId,
                result.ScannerPlan.Conditions.Count,
                result.ScannerPlan.ClarificationRequired,
                result.ScannerPlan.ClarificationMessage,
                result.ScannerPlan.ColumnOverflowWarnings),
            MapScannerTable(result.ScannerTable),
            MapSymbolLookupTable(result.SymbolLookupTable),
            MapExplainableAnswer(result.ExplainableAnswer),
            MapConfidenceScore(ShouldSuppressSymbolLookupConfidence(result.SymbolLookupTable)
                ? null
                : result.ConfidenceScore),
            result.Usage is null ? null : new UsageAccountingResponse(
                result.Usage.OperationCode,
                result.Usage.CompletionStatus,
                result.Usage.CreditsCharged,
                result.Usage.RemainingSpendingCapacity,
                result.Usage.PricingPolicyVersion,
                result.Usage.Cached,
                result.Usage.ProviderName,
                result.Usage.ModelName,
                result.Usage.PromptTokens,
                result.Usage.CompletionTokens,
                result.Usage.TotalTokens,
                result.Usage.EstimatedCost),
            result.MemoryDisclosures?.Select(d => new MemoryDisclosureResponse(
                d.Type.ToString(), d.Purpose.ToString(), d.Explanation)).ToList(),
            result.AiOrchestrationMode,
            result.WorkflowVersion,
            result.ProviderSelection,
            result.ProviderFallbackOccurred,
            result.WorkflowCorrelationId,
            MapComprehensiveAnalysisResult(result.ComprehensiveAnalysisResult),
            MapFinancialStatementAnalysisResult(result.FinancialStatementAnalysisResult),
            MapFinancialStatementTableResult(result.FinancialStatementTableResult),
            MapMonthlyActivityTrendResult(result.MonthlyActivityTrendResult),
            MapMonthlySalesQualityRankingResult(result.MonthlySalesQualityRankingResult),
             result.DisclosureListingResult,
             result.PsVisualizationResult,
             result.Outcome.ToString(),
             result.OutcomeReasonCode,
             result.ReplyLanguage,
             result.LanguageGuardApplied,
             MapSuggestedActions(result.SuggestedActions),
             result.SemanticCapabilityCode,
             result.SemanticRegistryVersion,
             MapMonthlyProductComparisonResult(result.MonthlyProductComparisonResult),
             FinancialStatementValueSearchResult: result.FinancialStatementValueSearchResult,
             MonthlyProductTrendResult: MapMonthlyProductTrendResult(result.MonthlyProductTrendResult),
             SymbolSnapshotResult: result.SymbolSnapshotResult,
             GroundedAnalysis: MapGroundedAnalysisHint(result.GroundedAnalysis),
             Sources: MapExternalSources(result.Sources));

    internal static string DepthName(AnalysisDepth depth) => depth == AnalysisDepth.Simple ? "simple" : "deep";

    internal static GroundedAnalysisHttpResponse? MapGroundedAnalysisHint(GroundedAnalysisHint? hint) =>
        hint is null
            ? null
            : new GroundedAnalysisHttpResponse(
                hint.Available,
                hint.PricingVersion,
                hint.Prices.Select(price => new GroundedAnalysisPriceHttpResponse(
                    DepthName(price.Depth), price.WebSearch, price.Credits)).ToList(),
                DepthName(hint.Defaults.Depth),
                hint.Defaults.WebSearch,
                hint.Unlimited,
                hint.Offer is null
                    ? null
                    : new GroundedAnalysisOfferHttpResponse(
                        hint.Offer.OfferId,
                        hint.Offer.Symbol,
                        hint.Offer.ExpiresAt,
                        hint.Offer.SufficientBalance,
                        hint.Offer.ConfirmActionId,
                        hint.Offer.CancelActionId),
                hint.UseReportDefaults);

    internal static IReadOnlyList<ExternalSourceHttpResponse>? MapExternalSources(IReadOnlyList<ExternalSource>? sources) =>
        sources is not { Count: > 0 }
            ? null
            : sources.Select(source => new ExternalSourceHttpResponse(
                source.Id, source.Title, source.Url, source.PublishedAt)).ToList();

    internal static ScannerTableResponse? MapSymbolLookupTable(SymbolLookupTableResult? table)
    {
        if (table is null) return null;
        if (table.Rows.Count == 0) return null;

        return new ScannerTableResponse(
            table.LookupId,
            table.Columns.Select(c => new ScannerTableColumnResponse(
                c.Identifier, c.DisplayName, c.ColumnType.ToString(), c.MetricCode)).ToList(),
            table.Rows.Select(r => new ScannerTableRowResponse(
                r.SymbolCode,
                r.CompanyName,
                MapCells(table.Columns, r),
                r.Score,
                r.MatchedConditionMetrics,
                MapSalesGrowthRowMetadata(r.SalesGrowthMetadata))).ToList(),
            new ScannerExecutionFactsResponse(
                table.ExecutionFacts.ExecutedAt,
                table.ExecutionFacts.Duration,
                table.ExecutionFacts.TotalSymbolsEvaluated,
                table.ExecutionFacts.MatchingSymbolCount,
                table.ExecutionFacts.FromCache,
                table.ExecutionFacts.Page,
                table.ExecutionFacts.PageSize,
                table.ExecutionFacts.TotalPages,
                table.ExecutionFacts.EligibleSymbolCount,
                table.ExecutionFacts.EvaluatedSymbolCount,
                table.ExecutionFacts.ExcludedByReason),
            table.MissingDataWarnings.Concat(
                table.UnresolvedSymbols.Select(s => $"Symbol '{s}' could not be resolved.")).ToList());
    }

    internal static bool ShouldSuppressSymbolLookupConfidence(SymbolLookupTableResult? table) =>
        table is not null && table.Rows.Count == 0;

    internal static ScannerTableResponse? MapScannerTable(ScannerTableResult? table)
    {
        if (table is null) return null;

        return new ScannerTableResponse(
            table.PlanId,
            table.Columns.Select(c => new ScannerTableColumnResponse(
                c.Identifier, c.DisplayName, c.ColumnType.ToString(), c.MetricCode)).ToList(),
            table.Rows.Select(r => new ScannerTableRowResponse(
                r.SymbolCode,
                r.CompanyName,
                MapCells(table.Columns, r),
                r.Score,
                r.MatchedConditionMetrics)).ToList(),
            new ScannerExecutionFactsResponse(
                table.ExecutionFacts.ExecutedAt,
                table.ExecutionFacts.Duration,
                table.ExecutionFacts.TotalSymbolsEvaluated,
                table.ExecutionFacts.MatchingSymbolCount,
                table.ExecutionFacts.FromCache,
                table.ExecutionFacts.Page,
                table.ExecutionFacts.PageSize,
                table.ExecutionFacts.TotalPages,
                table.ExecutionFacts.EligibleSymbolCount,
                table.ExecutionFacts.EvaluatedSymbolCount,
                table.ExecutionFacts.ExcludedByReason),
            table.MissingDataWarnings,
            MapSalesGrowthTableMetadata(table.SalesGrowthMetadata));
    }

    internal static SalesGrowthRowMetadataResponse? MapSalesGrowthRowMetadata(
        SalesGrowthRowMetadata? metadata) =>
        metadata is null
            ? null
            : new SalesGrowthRowMetadataResponse(
                metadata.CurrentPeriod,
                metadata.BaselinePeriod,
                metadata.BaselineWindow,
                metadata.Unit,
                metadata.Scale,
                metadata.Evidence.Select(evidence => new SalesGrowthEvidenceResponse(
                    evidence.ExternalCompanyId,
                    evidence.Period.Year,
                    evidence.Period.Month,
                    evidence.SalesAmount,
                    evidence.SourceName,
                    evidence.EvidenceId,
                    evidence.ObservedAtUtc)).ToArray(),
                metadata.LatestObservedAtUtc,
                metadata.FreshnessSource,
                metadata.Threshold,
                metadata.Operator.ToString(),
                metadata.Origin.ToString(),
                metadata.Policies.TargetPeriod.Value,
                metadata.Policies.Calculation.Value,
                metadata.MatchReason);

    internal static SalesGrowthTableMetadataResponse? MapSalesGrowthTableMetadata(
        SalesGrowthTableMetadata? metadata) =>
        metadata is null
            ? null
            : new SalesGrowthTableMetadataResponse(
                metadata.TargetCommonPeriod,
                metadata.CoverageNumerator,
                metadata.CoverageDenominator,
                metadata.CoveragePercent,
                metadata.SelectionStatus.ToString(),
                metadata.TargetPeriodPolicyVersion.Value,
                metadata.CalculationPolicyVersion.Value,
                metadata.MixedPeriods,
                metadata.SelectionReason);

    internal static IReadOnlyDictionary<string, ScannerTableCellResponse> MapCells(
        IReadOnlyCollection<ScannerTableColumn> columns,
        ScannerTableRow row) =>
        columns.ToDictionary(
            column => column.Identifier,
            column =>
            {
                row.Cells.TryGetValue(column.Identifier, out var cell);
                cell = NormalizeDisplayCell(column, row, cell);
                return new ScannerTableCellResponse(
                    cell.Value,
                    cell.FormattedValue,
                    cell.FreshnessStatus.ToString(),
                    cell.SourceTimestamp,
                    cell.TradingDate,
                    cell.TradingDatePersian,
                    cell.SourceLabel is null ? null : ProviderSources.GetDisplayName(cell.SourceLabel));
            });

    internal static ScannerTableCell NormalizeDisplayCell(
        ScannerTableColumn column,
        ScannerTableRow row,
        ScannerTableCell? cell)
    {
        var existing = cell ?? new ScannerTableCell(null, null, CellFreshnessStatus.Persisted, null);
        return column.ColumnType switch
        {
            ScannerColumnType.Symbol when string.IsNullOrWhiteSpace(existing.FormattedValue) =>
                existing with { FormattedValue = row.SymbolCode },

            ScannerColumnType.CompanyName when string.IsNullOrWhiteSpace(existing.FormattedValue) =>
                existing with { FormattedValue = row.CompanyName },

            _ => existing
        };
    }

    internal static ExplainableAnswerResponse? MapExplainableAnswer(ExplainableAnswer? answer)
    {
        if (answer is null) return null;

        return new ExplainableAnswerResponse(
            answer.FilterChips.Select(chip => new ConditionFilterChipResponse(
                chip.MetricCode,
                chip.MetricDisplayName,
                chip.OperatorSymbol,
                chip.OperatorLabel,
                chip.Threshold,
                chip.ThresholdFormatted,
                chip.FilterOrigin,
                chip.IsInferred,
                chip.InferredReason)).ToList(),
            answer.MetricEvidence.Select(ev => new MetricEvidenceSummaryResponse(
                ev.MetricCode,
                ev.MetricVersion,
                ev.CalculationPolicyVersion,
                ev.MetricDisplayName,
                ev.Unit,
                ev.ActualValue,
                ev.FormattedValue,
                ev.PeriodType,
                ev.ObservedAt)).ToList(),
            answer.DataCitations.Select(c => new DataCitationResponse(
                c.SymbolCode,
                c.MetricCode,
                c.ObservedAt,
                c.FreshnessStatus,
                c.SourceProvider is null ? null : ProviderSources.GetDisplayName(c.SourceProvider))).ToList(),
            new ConfidenceScoreResponse(
                answer.Confidence.Score,
                new ConfidenceFactorsResponse(
                    answer.Confidence.Factors.InterpretationCertainty,
                    answer.Confidence.Factors.EvidenceCompleteness,
                    answer.Confidence.Factors.SourceFreshness,
                    answer.Confidence.Factors.WarningPenalty),
                answer.Confidence.PolicyVersion),
            answer.SuggestedFollowUpQuestions,
            answer.ExplanationText);
    }

    internal static ConversationSummaryResponse MapConversationSummary(ConversationSummary summary) =>
        new(summary.ConversationId, summary.StartedAt, summary.UpdatedAt, summary.MessageCount, summary.Title);

    internal static MessageResponse MapMessage(MessageRecord message) =>
        new(
            message.MessageId,
            message.Role.ToString(),
            message.Content,
            message.ScannerQueryPlanJson is not null,
            message.CreatedAt,
            MapAssistantContent(message.AssistantPayload));

    public static AssistantMessageContentResponse? MapAssistantContent(AssistantMessagePayload? payload) =>
        payload is null
            ? null
            : new AssistantMessageContentResponse(
                payload.Version,
                payload.Intent.ToString(),
                payload.ClarificationRequired,
                payload.ClarificationMessage,
                payload.TextAnswer,
                payload.ScannerPlan is null ? null : new ScannerPlanResponse(
                    payload.ScannerPlan.PlanId,
                    payload.ScannerPlan.Conditions.Count,
                    payload.ScannerPlan.ClarificationRequired,
                    payload.ScannerPlan.ClarificationMessage,
                    payload.ScannerPlan.ColumnOverflowWarnings),
                MapScannerTable(payload.ScannerTable),
                MapSymbolLookupTable(payload.SymbolLookupTable),
                MapExplainableAnswer(payload.ExplainableAnswer),
                MapConfidenceScore(ShouldSuppressSymbolLookupConfidence(payload.SymbolLookupTable)
                    ? null
                    : payload.ConfidenceScore),
                payload.Usage is null ? null : new UsageAccountingResponse(
                    payload.Usage.OperationCode,
                    payload.Usage.CompletionStatus,
                    payload.Usage.CreditsCharged,
                    payload.Usage.RemainingSpendingCapacity,
                    payload.Usage.PricingPolicyVersion,
                    payload.Usage.Cached),
                payload.MemoryDisclosures?.Select(d => new MemoryDisclosureResponse(
                    d.Type.ToString(), d.Purpose.ToString(), d.Explanation)).ToList(),
                MapComprehensiveAnalysisResult(payload.ComprehensiveAnalysisResult),
                MapFinancialStatementAnalysisResult(payload.FinancialStatementAnalysisResult),
                MapFinancialStatementTableResult(payload.FinancialStatementTableResult),
                MapMonthlyActivityTrendResult(payload.MonthlyActivityTrendResult),
                MapMonthlySalesQualityRankingResult(payload.MonthlySalesQualityRankingResult),
                payload.DisclosureListingResult,
                payload.PsVisualizationResult,
                payload.Outcome.ToString(),
                payload.OutcomeReasonCode,
                payload.ReplyLanguage,
                payload.LanguageGuardApplied,
                MapSuggestedActions(payload.SuggestedActions),
                payload.SemanticCapabilityCode,
                payload.SemanticRegistryVersion,
                MapMonthlyProductComparisonResult(payload.MonthlyProductComparisonResult),
                FinancialStatementValueSearchResult: payload.FinancialStatementValueSearchResult,
                MonthlyProductTrendResult: MapMonthlyProductTrendResult(payload.MonthlyProductTrendResult),
                SymbolSnapshotResult: payload.SymbolSnapshotResult,
                Sources: MapExternalSources(payload.ExternalSources));

    internal static MonthlyProductComparisonHttpResponse? MapMonthlyProductComparisonResult(MonthlyProductComparisonResponse? result)
    {
        if (result is null) return null;
        return new(
            result.State.ToString(), result.CompanyText, result.ExternalCompanyId,
            result.CurrentPeriod?.ToString(), result.ComparisonPeriod?.ToString(),
            result.Totals?.Current, result.Totals?.Comparison, result.Totals?.Change, result.Totals?.ChangePercent,
            result.PrimaryDriver?.ToString(),
            result.Products.Take(20).Select(item => new MonthlyProductComparisonHttpItem(
                item.DisplayTitle, item.RawUnit, item.Lifecycle.ToString(), item.Current?.SalesAmount,
                item.Comparison?.SalesAmount, item.SalesChange, item.ContributionPercent,
                item.QuantityEffect, item.PriceEffect, item.Residual, item.Driver.ToString(),
                item.Warnings.Select(w => w.ToString()).ToArray())).ToArray(),
            result.Warnings.Select(w => w.ToString()).ToArray(),
            result.Evidence.Select(e => new MonthlyProductComparisonHttpEvidence(e.ReportId, e.RowId, e.ProviderName, e.ExternalReportId, e.Period.ToString())).ToArray(),
            result.BlockingReason?.ToString(), result.ClarificationMessage);
    }

    internal static MonthlyProductTrendHttpResponse? MapMonthlyProductTrendResult(MonthlyProductTrendResult? result)
    {
        if (result is null) return null;
        return new(
            result.ResultDiscriminator,
            result.ResultVersion,
            result.ResolutionState.ToString(),
            result.CompanyText,
            result.ExternalCompanyId,
            result.CompanyName,
            result.CompanySymbol,
            result.ProductKey,
            result.ProviderProductCode,
            result.ProviderProductId,
            result.ProductTitle,
            result.ProductUnit,
            result.Points.Select(point => new MonthlyProductTrendHttpPoint(
                point.Period.ToString(), point.FiscalLabel, point.ProductKey, point.ProductTitle,
                point.ProductUnit, point.ProductionQuantity, point.SaleQuantity,
                point.SalesValueMillionRial, point.SalesValueBillionToman,
                point.CalculatedSaleRateToman, point.RateStatus.ToString(), point.IsGap)).ToArray(),
            result.Candidates.Select(candidate => new MonthlyProductTrendHttpCandidate(
                candidate.DisplayTitle, candidate.Unit, candidate.ProductKey,
                candidate.ProviderProductCode, candidate.ProviderProductId)).ToArray(),
            result.Evidence.Select(e => new MonthlyProductComparisonHttpEvidence(
                e.ReportId, e.RowId, e.ProviderName, e.ExternalReportId, e.Period.ToString())).ToArray(),
            result.BlockingReason,
            result.Message);
    }

    internal static IReadOnlyCollection<SuggestedActionHttpResponse>? MapSuggestedActions(
        IReadOnlyCollection<SuggestedAction>? actions) =>
        actions?.Select(action => new SuggestedActionHttpResponse(
            action.Id,
            action.Kind.ToString(),
            action.LocalizedLabel,
            action.Message,
            action.CapabilityCode,
            action.PresetSlots,
            action.RelevanceReason,
            action.RegistryVersion)).ToArray();

    internal static ComprehensiveAnalysisResultResponse? MapComprehensiveAnalysisResult(
        ComprehensiveAnalysisQueryResponse? result)
    {
        if (result is null) return null;

        return new ComprehensiveAnalysisResultResponse(
            result.Items.Select(i => new ComprehensiveAnalysisItemResponse(
                i.AnalysisId,
                i.Title,
                i.PersianCreatedAt,
                i.AuthorName,
                i.PlainTextSummary,
                i.TagNames,
                i.SyncedAt)).ToList(),
            result.UnresolvedSymbols,
            result.HasResults);
    }

    internal static FinancialStatementAnalysisHttpResponse? MapFinancialStatementAnalysisResult(
        FinancialStatementAnalysisResponse? result)
    {
        if (result is null) return null;

        return new FinancialStatementAnalysisHttpResponse(
            result.CompanySymbol,
            result.CompanyName,
            result.SelectedPeriodMonths,
            result.SelectedPeriodType,
            result.JalaliPeriodEnd,
            result.JalaliFiscalYearEnd,
            result.SelectedVariant,
            result.SelectedAuditedStatus,
            result.SummaryBullets,
            result.Sections.Select(section => new FinancialStatementAnalysisSectionResponse(
                section.TitleFa,
                section.SummaryBullets,
                section.Metrics.Select(metric => new FinancialStatementMetricComparisonResponse(
                    metric.MetricCode,
                    metric.LabelFa,
                    metric.CurrentValue,
                    metric.CurrentFormattedValue,
                    metric.PreviousValue,
                    metric.PreviousFormattedValue,
                    metric.ChangePercent,
                    metric.ChangeDirectionFa,
                    metric.Indicator,
                    metric.IsUnavailable,
                    metric.Warning)).ToList())).ToList(),
            result.SourceReferences.Select(source => new FinancialStatementSourceReferenceResponse(
                source.StatementType,
                source.StatementId,
                source.ExternalStatementId,
                ProviderSources.GetDisplayName(source.ProviderName),
                source.PeriodType,
                source.PeriodMonths,
                source.JalaliPeriodEnd,
                source.JalaliFiscalYearEnd,
                source.JalaliAnnouncementDate,
                source.IsAudited,
                source.IsRepresented,
                source.IsComposing)).ToList(),
            result.Warnings,
            result.ConfidenceScore,
            result.GeneratedAtUtc);
    }

    internal static FinancialStatementTableHttpResponse? MapFinancialStatementTableResult(
        FinancialStatementTableResult? result)
    {
        if (result is null) return null;

        return new FinancialStatementTableHttpResponse(
            result.Source is null ? null : new FinancialStatementTableSourceHttpResponse(
                result.Source.StatementId,
                result.Source.ExternalStatementId,
                ProviderSources.GetDisplayName(result.Source.ProviderName),
                result.Source.ExternalCompanyId,
                result.Source.CompanySymbol,
                result.Source.CompanyName,
                result.Source.StatementType.ToString(),
                result.Source.PeriodType,
                result.Source.PeriodMonths,
                result.Source.PeriodStart,
                result.Source.PeriodEnd,
                result.Source.AnnouncementDate,
                result.Source.JalaliPeriodEnd,
                result.Source.JalaliFiscalYearEnd,
                result.Source.JalaliAnnouncementDate,
                result.Source.IsAudited,
                result.Source.IsRepresented,
                result.Source.IsComposing,
                result.Source.Unit),
            result.LineItems.Select(MapFinancialStatementTableLineItem).ToList(),
            result.BalanceSheetRows.Select(row => new BalanceSheetTableRowHttpResponse(
                row.Asset is null ? null : MapFinancialStatementTableLineItem(row.Asset),
                row.LiabilityOrEquity is null ? null : MapFinancialStatementTableLineItem(row.LiabilityOrEquity))).ToList(),
            result.Warnings,
            result.GeneratedAtUtc,
            result.QuarterlyResult,
            result.BalanceSheetReport,
            result.CashFlowReport);
    }

    internal static FinancialStatementTableLineItemHttpResponse MapFinancialStatementTableLineItem(
        FinancialStatementTableLineItem item) =>
        new(
            item.RowNumber,
            item.SourceItemId,
            item.TitleFa,
            item.TitleEn,
            item.MetricCode,
            item.Value,
            item.FormattedValue,
            item.Unit,
            item.Side.ToString());

    internal static MonthlyActivityTrendChartResponse? MapMonthlyActivityTrendResult(
        MonthlyActivityTrendResponse? result)
    {
        if (result is null) return null;

        return new MonthlyActivityTrendChartResponse(
            result.CompanySymbol,
            result.CompanyName,
            result.LatestReportYear,
            result.LatestReportMonth,
            result.UnitLabelFa,
            result.LatestMonthlySalesAmount,
            result.SameMonthPreviousYearSalesAmount,
            result.Average12MonthSalesAmount,
            result.SalesAmountYoYGrowthPercent,
            result.SalesVsAverage12MonthPercent,
            result.YtdSalesAmount,
            result.YtdPreviousMonthSalesAmount,
            result.ChartPoints.Select(p => new MonthlyActivityTrendChartPointResponse(
                p.FiscalMonthIndex,
                p.FiscalMonthNameFa,
                p.PreviousFiscalYear,
                p.PreviousFiscalYearSalesAmount,
                p.CurrentFiscalYear,
                p.CurrentFiscalYearSalesAmount,
                p.Average12MonthSalesAmount,
                p.IsCurrentYearReported,
                p.IsPreviousYearReported)).ToList(),
            result.Insights.Select(i => new MonthlyActivityTrendInsightResponse(
                i.Kind.ToString(),
                i.TextFa)).ToList(),
            result.MissingDataPoints.Select(m => new MonthlyActivityTrendMissingDataPointResponse(
                m.Year,
                m.Month,
                m.ReasonFa)).ToList(),
            ProviderSources.GetDisplayName(result.SourceProviderName),
            result.CalculatedAtUtc,
            result.PreviousMonthSalesAmount,
            result.SalesAmountMomGrowthPercent);
    }

    internal static MonthlySalesQualityRankingHttpResponse? MapMonthlySalesQualityRankingResult(
        MonthlySalesQualityRankingResponse? result)
    {
        if (result is null) return null;

        return new MonthlySalesQualityRankingHttpResponse(
            result.ReportYear,
            result.ReportMonth,
            result.Scope.ToString(),
            result.Direction.ToString(),
            result.TotalEligibleCompanies,
            result.GeneratedAtUtc,
            result.Items.Select(item => new MonthlySalesQualityRankingItemHttpResponse(
                item.Rank,
                item.Symbol,
                item.CompanyName,
                item.IndustryTitle,
                item.QualityScore,
                item.QualityLabel,
                item.ConfidenceScore,
                item.MonthlySalesAmount,
                item.Avg12MonthSalesAmount,
                item.SalesVsAvg12MPercent,
                item.SalesMonthOverMonthPercent,
                item.SalesYearOverYearPercent,
                item.DimensionScores is null ? null : new MonthlySalesQualityDimensionScoresHttpResponse(
                    item.DimensionScores.SalesGrowthVs12M,
                    item.DimensionScores.QuantityGrowthQuality,
                    item.DimensionScores.RateGrowthQuality,
                    item.DimensionScores.ProductMixStrength,
                    item.DimensionScores.PersistenceTrend,
                    item.DimensionScores.IndustryRelativeStrength),
                item.PositiveDrivers,
                item.NegativeDrivers,
                new MonthlySalesQualityDataCoverageHttpResponse(
                    item.DataCoverage.HistoryMonths,
                    item.DataCoverage.HasProductLineItems,
                    item.DataCoverage.HasProductMix,
                    item.DataCoverage.IndustryPeerCount),
                ProviderSources.GetDisplayName(item.SourceProviderName),
                item.CalculatedAtUtc)).ToList());
    }

    internal static ConfidenceScoreResponse? MapConfidenceScore(ConfidenceScoreResult? confidence)
    {
        if (confidence is null) return null;

        return new ConfidenceScoreResponse(
            confidence.Score,
            new ConfidenceFactorsResponse(
                confidence.Factors.InterpretationCertainty,
                confidence.Factors.EvidenceCompleteness,
                confidence.Factors.SourceFreshness,
                confidence.Factors.WarningPenalty),
            confidence.PolicyVersion);
    }
}
