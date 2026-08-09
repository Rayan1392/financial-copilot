using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Application.FinancialData.Features;
using FinancialCopilot.Infrastructure.Financial.FundPortfolio;
using FinancialCopilot.Domain.Financial.Features;
using FinancialCopilot.Domain.Financial.Periods;
using FinancialCopilot.Domain.Financial.FundPortfolio;

namespace FinancialCopilot.UnitTests;

public sealed class FundPortfolioAnalyticsFoundationTests
{
    [Fact]
    public void Completeness_ReportsSixIndependentDimensions()
    {
        var completeness = new FundPortfolioInputCompleteness(true, true, false, true, false, true);

        Assert.Equal(4, completeness.CompletedDimensions);
        Assert.Equal(4m / 6m, completeness.Score);
        Assert.True(completeness.IsComplete(FundPortfolioCompletenessDimension.Equity));
        Assert.False(completeness.IsComplete(FundPortfolioCompletenessDimension.MarketLiquidity));
    }

    [Fact]
    public void Ordering_UsesSubjectAndIdAsStableTieBreakers()
    {
        var first = new TestPosition(Guid.Parse("00000000-0000-0000-0000-000000000002"), "A", 10m);
        var second = new TestPosition(Guid.Parse("00000000-0000-0000-0000-000000000001"), "A", 10m);
        var third = new TestPosition(Guid.Parse("00000000-0000-0000-0000-000000000003"), "B", 10m);

        var ordered = FundPortfolioAnalyticsOrdering.OrderDeterministically(
            [third, first, second],
            position => position.Amount,
            position => position.Subject,
            position => position.Id).ToArray();

        Assert.Equal([second.Id, first.Id, third.Id], ordered.Select(position => position.Id));
    }

    [Fact]
    public void SignalKey_IncludesVersionedIdentity()
    {
        var key = FundPortfolioAnalyticsCalculationPolicy.SignalDeduplicationKey(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            FundPortfolioSignalType.NewPosition,
            "COMPANY-1");

        Assert.EndsWith("|fund-portfolio-analytics-v1", key, StringComparison.Ordinal);
        Assert.Contains("|NewPosition|COMPANY-1|", key, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Calculator_DeduplicatesAndOrdersSignalsWithVersionedConfidence()
    {
        var snapshot = new FundPortfolioAnalyticsSnapshot(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 31), null,
            null, null, null, null, null, null, null, null, null, null, null, 0, 0,
            FundPortfolioRiskPosture.Unknown, FundPortfolioLiquidityRiskStatus.Unavailable,
            FundPortfolioValuationQualityStatus.Unknown,
            new FundPortfolioInputCompleteness(true, true, false, false, false, false),
            0.9m, FundPortfolioAnalyticsCalculationPolicy.CalculationVersion, "{}");
        var duplicate = new FundPortfolioSignal(Guid.NewGuid(), snapshot.Id, FundPortfolioSignalType.NewPosition,
            "B", null, 2m, 0.5m, 0.5m, "low", "low", "{}", "b");
        var winner = duplicate with { Id = Guid.NewGuid(), ImportanceScore = 0.8m, Title = "winner" };
        var other = duplicate with { Id = Guid.NewGuid(), DeduplicationKey = "a", ExternalCompanyId = "A" };

        var result = await new DeterministicFundPortfolioAnalyticsCalculator().CalculateAsync(
            new FundPortfolioAnalyticsCalculationContext(snapshot, [duplicate, winner, other]),
            CancellationToken.None);

        Assert.Equal(2, result.Signals.Count);
        Assert.Equal("A", result.Signals.First().ExternalCompanyId);
        Assert.Equal("winner", result.Signals.Single(signal => signal.DeduplicationKey == "b").Title);
        Assert.Equal(2m / 6m, result.Snapshot.ConfidenceScore);
    }

    [Fact]
    public async Task Feature016Calculator_UsesStableCompletenessFingerprint()
    {
        var definition = FundPortfolioAnalyticsFeatureDefinition.Current;
        var period = FiscalPeriod.Closed(FiscalPeriodType.Monthly, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));
        var inputs = definition.Dependencies.Take(2)
            .Select(dependency => new FeatureInputObservation(dependency, 1m, period, dependency.Code + "-evidence"))
            .ToArray();
        var context = new FeatureCalculationContext("FUND-1", definition, period, inputs);

        var first = await new FundPortfolioAnalyticsFeatureCalculator().CalculateAsync(context, CancellationToken.None);
        var second = await new FundPortfolioAnalyticsFeatureCalculator().CalculateAsync(context, CancellationToken.None);

        Assert.Equal(1m / 3m, first.Value);
        Assert.Equal(first.InputFingerprint, second.InputFingerprint);
        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task ComparableSelector_ChoosesHighestAcceptedRevisionFromImmediatelyPriorAvailablePeriod()
    {
        var current = Candidate(Guid.Parse("00000000-0000-0000-0000-000000000010"), new DateOnly(2026, 7, 31), 1, FundPortfolioParseStatus.Parsed);
        var priorLowRevision = Candidate(Guid.Parse("00000000-0000-0000-0000-000000000011"), new DateOnly(2026, 5, 31), 1, FundPortfolioParseStatus.Parsed);
        var priorHighRevision = Candidate(Guid.Parse("00000000-0000-0000-0000-000000000012"), new DateOnly(2026, 5, 31), 2, FundPortfolioParseStatus.Parsed);
        var failed = Candidate(Guid.Parse("00000000-0000-0000-0000-000000000013"), new DateOnly(2026, 6, 30), 99, FundPortfolioParseStatus.Failed);
        var selector = new FundComparablePeriodSelector(new InMemoryComparableReader(current, [current, priorLowRevision, priorHighRevision, failed]));

        var result = await selector.SelectAsync(current.ReportId, CancellationToken.None);

        Assert.Equal(FundComparablePeriodSelectionOutcome.Selected, result.Outcome);
        Assert.Equal(priorHighRevision.ReportId, result.PreviousComparableReportId);
        Assert.Equal(61, result.PeriodGapDays);
        Assert.Contains(FundPortfolioAnalyticsCalculationPolicy.SelectionPolicyVersion, result.EvidenceJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComparableSelector_HandlesFirstReportAndNeverComparesCumulativeBlocks()
    {
        var current = Candidate(Guid.NewGuid(), new DateOnly(2026, 7, 31), 1, FundPortfolioParseStatus.Parsed);
        var selector = new FundComparablePeriodSelector(new InMemoryComparableReader(current, [current]));

        var result = await selector.SelectAsync(current.ReportId, CancellationToken.None);

        Assert.Equal(FundComparablePeriodSelectionOutcome.FirstAcceptedReport, result.Outcome);
        Assert.False(result.HasComparableReport);
        Assert.True(FundComparablePeriodSelectionPolicy.CanCompareBlocks(FundWorkbookPeriodContext.CurrentPeriod, FundWorkbookPeriodContext.CurrentPeriod));
        Assert.False(FundComparablePeriodSelectionPolicy.CanCompareBlocks(FundWorkbookPeriodContext.CurrentPeriod, FundWorkbookPeriodContext.FiscalYearToDate));
    }

    [Fact]
    public async Task ComparableSelector_DoesNotUseFailedOrReviewReportsAsComparablePeriods()
    {
        var current = Candidate(Guid.NewGuid(), new DateOnly(2026, 7, 31), 1, FundPortfolioParseStatus.Parsed);
        var review = Candidate(Guid.NewGuid(), new DateOnly(2026, 6, 30), 2, FundPortfolioParseStatus.NeedsReview);
        var selector = new FundComparablePeriodSelector(new InMemoryComparableReader(current, [current, review]));

        var result = await selector.SelectAsync(current.ReportId, CancellationToken.None);

        Assert.Equal(FundComparablePeriodSelectionOutcome.NoPriorAcceptedReport, result.Outcome);
        Assert.Null(result.PreviousComparableReportId);
    }

    private static FundComparableReportCandidate Candidate(Guid id, DateOnly periodEnd, int revision, FundPortfolioParseStatus status) =>
        new(id, Guid.Parse("00000000-0000-0000-0000-000000000099"), "Provider-A", FundPortfolioReportType.MonthlyPortfolio, periodEnd, status, revision, new DateTimeOffset(periodEnd.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), null);

    private sealed class InMemoryComparableReader(
        FundComparableReportCandidate current,
        IReadOnlyCollection<FundComparableReportCandidate> candidates) : IFundComparablePeriodReportReader
    {
        public Task<FundComparableReportCandidate?> GetAsync(Guid reportId, CancellationToken cancellationToken) =>
            Task.FromResult<FundComparableReportCandidate?>(reportId == current.ReportId ? current : null);

        public Task<IReadOnlyCollection<FundComparableReportCandidate>> ListComparableCandidatesAsync(FundComparableReportIdentity identity, CancellationToken cancellationToken) =>
            Task.FromResult(candidates);
    }

    private sealed record TestPosition(Guid Id, string Subject, decimal Amount);
}
