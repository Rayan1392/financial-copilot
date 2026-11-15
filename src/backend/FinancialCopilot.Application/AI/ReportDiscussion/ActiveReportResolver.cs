using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.FinancialData.BalanceSheetReport;
using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Application.AI.ReportDiscussion;

/// <summary>
/// Derives the conversation's active report from persisted assistant payloads (D6, FR-2). Walking from the newest
/// assistant message back: a payload that holds a supported report is the active report; a discussion turn (answer,
/// zero-credit non-answer or a hand-back for different data) keeps looking further back; an <c>OutsideReport</c>
/// hand-back or any other assistant answer means there is no active report.
/// </summary>
public static class ActiveReportResolver
{
    public static ActiveReport? Resolve(IReadOnlyList<MessageRecord> messagesOldestFirst)
    {
        for (var index = messagesOldestFirst.Count - 1; index >= 0; index--)
        {
            var message = messagesOldestFirst[index];
            if (message.Role != MessageRole.Assistant) continue;
            if (message.AssistantPayload is not { } payload) return null;

            if (TryDescribe(message.MessageId, payload) is { } report) return report;
            if (payload.ReportDiscussion is not { } marker || marker.DeactivatesReport) return null;
        }

        return null;
    }

    /// <summary>The supported report a payload holds (D1), or null. Only answered turns count.</summary>
    public static ActiveReport? TryDescribe(Guid assistantMessageId, AssistantMessagePayload payload)
    {
        if (payload.Outcome is not (DialogueOutcome.Answered or DialogueOutcome.PartialAnswer)) return null;

        if (payload.FinancialStatementTableResult?.BalanceSheetReport is { } balance &&
            balance.Outcome == BalanceSheetOutcomes.Available && balance.Snapshots.Count > 0)
        {
            return new ActiveReport(assistantMessageId, ReportFamily.BalanceSheet,
                balance.Company.Symbol, balance.Company.Name, balance.Company.ProviderName, payload);
        }

        if (payload.FinancialStatementTableResult?.QuarterlyResult is { } quarterly &&
            quarterly.Outcome is QuarterlyFinancialReportOutcome.SuccessfulTable or QuarterlyFinancialReportOutcome.PartialTable &&
            quarterly.Rows.Count > 0 && quarterly.PeriodsNewestFirst.Count > 0)
        {
            return new ActiveReport(assistantMessageId, ReportFamily.QuarterlyIncomeStatement,
                quarterly.CompanySymbol, quarterly.CompanyName, quarterly.ProviderName, payload);
        }

        if (payload.MonthlyActivityTrendResult is { } monthly &&
            (monthly.LatestMonthlySalesAmount is not null || monthly.ChartPoints.Count > 0))
        {
            return new ActiveReport(assistantMessageId, ReportFamily.MonthlySales,
                monthly.CompanySymbol, monthly.CompanyName, monthly.SourceProviderName, payload);
        }

        return null;
    }
}
