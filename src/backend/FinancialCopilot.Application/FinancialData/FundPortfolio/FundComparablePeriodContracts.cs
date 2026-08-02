using System.Text.Json;
using FinancialCopilot.Domain.Financial.FundPortfolio;

namespace FinancialCopilot.Application.FinancialData.FundPortfolio;

public sealed record FundComparableReportIdentity(
    Guid FundId,
    string ProviderName,
    FundPortfolioReportType ReportType);

public sealed record FundComparableReportCandidate(
    Guid ReportId,
    Guid FundId,
    string ProviderName,
    FundPortfolioReportType ReportType,
    DateOnly? PeriodEndDate,
    FundPortfolioParseStatus ParseStatus,
    int SourceRevision,
    DateTimeOffset ImportedAtUtc,
    Guid? SupersedesReportId);

public enum FundComparablePeriodSelectionOutcome
{
    Selected,
    FirstAcceptedReport,
    CurrentReportNotFound,
    CurrentReportNotAccepted,
    CurrentPeriodUnavailable,
    NoPriorAcceptedReport
}

public sealed record FundComparablePeriodSelection(
    Guid CurrentReportId,
    Guid? PreviousComparableReportId,
    DateOnly? CurrentPeriodEndDate,
    DateOnly? PreviousPeriodEndDate,
    int? PeriodGapDays,
    FundComparablePeriodSelectionOutcome Outcome,
    string SelectionPolicyVersion,
    string EvidenceJson)
{
    public bool HasComparableReport => PreviousComparableReportId is not null;
}

public interface IFundComparablePeriodReportReader
{
    Task<FundComparableReportCandidate?> GetAsync(Guid reportId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<FundComparableReportCandidate>> ListComparableCandidatesAsync(
        FundComparableReportIdentity identity,
        CancellationToken cancellationToken);
}

public interface IFundComparablePeriodSelector
{
    Task<FundComparablePeriodSelection> SelectAsync(
        Guid currentReportId,
        CancellationToken cancellationToken);
}

public sealed class FundComparablePeriodSelector(
    IFundComparablePeriodReportReader reportReader) : IFundComparablePeriodSelector
{
    public async Task<FundComparablePeriodSelection> SelectAsync(
        Guid currentReportId,
        CancellationToken cancellationToken)
    {
        var current = await reportReader.GetAsync(currentReportId, cancellationToken);
        if (current is null)
        {
            return CreateResult(currentReportId, null, null, null, null,
                FundComparablePeriodSelectionOutcome.CurrentReportNotFound);
        }

        if (!FundComparablePeriodSelectionPolicy.IsAccepted(current))
        {
            return CreateResult(current.ReportId, null, current.PeriodEndDate, null, null,
                FundComparablePeriodSelectionOutcome.CurrentReportNotAccepted);
        }

        if (current.PeriodEndDate is not { } currentPeriodEnd)
        {
            return CreateResult(current.ReportId, null, null, null, null,
                FundComparablePeriodSelectionOutcome.CurrentPeriodUnavailable);
        }

        var candidates = await reportReader.ListComparableCandidatesAsync(
            new FundComparableReportIdentity(current.FundId, current.ProviderName, current.ReportType),
            cancellationToken);
        var priorCandidates = candidates
            .Where(candidate => candidate.ReportId != current.ReportId)
            .Where(candidate => candidate.PeriodEndDate is { } periodEnd && periodEnd < currentPeriodEnd)
            .ToArray();
        var previous = priorCandidates
            .Where(FundComparablePeriodSelectionPolicy.IsAccepted)
            .GroupBy(candidate => candidate.PeriodEndDate!.Value)
            .Select(group => group
                .OrderByDescending(candidate => candidate.SourceRevision)
                .ThenByDescending(candidate => candidate.ImportedAtUtc)
                .ThenByDescending(candidate => candidate.ReportId)
                .First())
            .OrderByDescending(candidate => candidate.PeriodEndDate)
            .ThenByDescending(candidate => candidate.SourceRevision)
            .ThenByDescending(candidate => candidate.ImportedAtUtc)
            .ThenByDescending(candidate => candidate.ReportId)
            .FirstOrDefault();

        if (previous is null)
        {
            return CreateResult(current.ReportId, null, currentPeriodEnd, null, null,
                priorCandidates.Length == 0
                    ? FundComparablePeriodSelectionOutcome.FirstAcceptedReport
                    : FundComparablePeriodSelectionOutcome.NoPriorAcceptedReport);
        }

        var previousPeriodEnd = previous.PeriodEndDate!.Value;
        return CreateResult(
            current.ReportId,
            previous.ReportId,
            currentPeriodEnd,
            previousPeriodEnd,
            currentPeriodEnd.DayNumber - previousPeriodEnd.DayNumber,
            FundComparablePeriodSelectionOutcome.Selected,
            previous.SourceRevision);
    }

    private static FundComparablePeriodSelection CreateResult(
        Guid currentReportId,
        Guid? previousReportId,
        DateOnly? currentPeriodEnd,
        DateOnly? previousPeriodEnd,
        int? periodGapDays,
        FundComparablePeriodSelectionOutcome outcome,
        int? previousSourceRevision = null) =>
        new(
            currentReportId,
            previousReportId,
            currentPeriodEnd,
            previousPeriodEnd,
            periodGapDays,
            outcome,
            FundPortfolioAnalyticsCalculationPolicy.SelectionPolicyVersion,
            JsonSerializer.Serialize(new
            {
                currentReportId,
                previousComparableReportId = previousReportId,
                currentPeriodEndDate = currentPeriodEnd,
                previousPeriodEndDate = previousPeriodEnd,
                periodGapDays,
                previousSourceRevision,
                outcome,
                selectionPolicyVersion = FundPortfolioAnalyticsCalculationPolicy.SelectionPolicyVersion,
                acceptedStatuses = new[] { FundPortfolioParseStatus.Parsed.ToString() },
                comparisonBlock = FundWorkbookPeriodContext.CurrentPeriod.ToString()
            }));
}

public static class FundComparablePeriodSelectionPolicy
{
    public static bool IsAccepted(FundComparableReportCandidate candidate) =>
        candidate.ParseStatus == FundPortfolioParseStatus.Parsed;

    public static bool CanCompareBlocks(
        FundWorkbookPeriodContext currentBlock,
        FundWorkbookPeriodContext previousBlock) =>
        currentBlock == FundWorkbookPeriodContext.CurrentPeriod &&
        previousBlock == FundWorkbookPeriodContext.CurrentPeriod;
}
