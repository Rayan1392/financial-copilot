using System.Security.Cryptography;
using System.Text;
using FinancialCopilot.Application.AI.ReportDiscussion;

namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>
/// Feature 162 change «report-interpretation-suggestion» (owner, 2026-11-16): under a shown quarterly income statement,
/// balance sheet or monthly sales trend, suggest asking for its interpretation. The suggestion is a
/// <see cref="SuggestedActionKind.DiscussReport"/> so that clicking it is answered by Report Discussion
/// (<see cref="ReportDiscussionHandler"/>) instead of re-running the report.
/// </summary>
public sealed class ReportInterpretationFollowUp(bool reportDiscussionEnabled)
{
    public const string CapabilityCode = "report_discussion";
    public const string RelevanceReason = "report_interpretation_follow_up";
    public const string IdPrefix = "report-interpretation:";

    public SuggestedAction? TryCreate(FollowUpSuggestionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!reportDiscussionEnabled || !context.IsAnsweredOrPartial) return null;
        if (ActiveReportResolver.Discussable(context.FinancialStatementTable, context.MonthlyActivityTrend) is not { } report)
            return null;

        var symbol = report.CompanySymbol?.Trim();
        if (string.IsNullOrWhiteSpace(symbol) || symbol.Length > 120) return null;

        var message = Message(report.Family, symbol);
        var idInput = string.Join('\n', RelevanceReason, report.Family, symbol);
        var id = IdPrefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idInput))).ToLowerInvariant();
        return new SuggestedAction(
            id,
            SuggestedActionKind.DiscussReport,
            message,
            message,
            CapabilityCode,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["symbol"] = symbol },
            RelevanceReason,
            0);
    }

    public static string Message(ReportFamily family, string symbol) => family switch
    {
        ReportFamily.QuarterlyIncomeStatement => $"صورت سود و زیان {symbol} را تفسیر کن",
        ReportFamily.BalanceSheet => $"ترازنامه {symbol} را تفسیر کن",
        ReportFamily.MonthlySales => $"روند فروش ماهانه {symbol} را تفسیر کن",
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, null)
    };
}
