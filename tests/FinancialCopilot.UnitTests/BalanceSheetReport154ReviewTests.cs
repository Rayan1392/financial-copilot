using FinancialCopilot.Application.FinancialData.BalanceSheetReport;
using FinancialCopilot.Application.FinancialData.Ingestion;
using static FinancialCopilot.UnitTests.BalanceSheet154Fixtures;

namespace FinancialCopilot.UnitTests;

/// <summary>Regression tests for review findings F154-R-07, R-09 and R-10.</summary>
public sealed class BalanceSheetReport154ReviewTests
{
    private static BalanceSheetReportQueryUseCase UseCase(FakeBalanceSheetReader reader) =>
        new(reader, new FakeCompanyResolution(), new FakeClassification(new() { ["452"] = [BankGroup] }),
            new BankingFinancialReportsConfiguration(BankGroup), TimeProvider.System);

    [Fact] // F154-R-07: a transitional (rebased) anchor year keeps a truthful fiscal-year-end baseline.
    public void TransitionalAnchorYear_FiscalYearEndUsesLatestEarlierYearEnd_OtherFiscalKindsSayCalendarChanged()
    {
        var anchor = Header("t", J(1405, 6, 31), "SixMonths", "1405/09/30");
        var evidence = new[]
        {
            new BalanceSheetFiscalEvidence(J(1405, 6, 31), "1405/09/30"),
            new BalanceSheetFiscalEvidence(J(1404, 12, 29), "1404/12/29"),
            new BalanceSheetFiscalEvidence(J(1404, 9, 30), "1404/12/29"),
            new BalanceSheetFiscalEvidence(J(1405, 3, 31), "1405/09/30")
        };
        var targets = BalanceSheetBaselineResolver.ResolveStandardTargets(anchor,
            [J(1405, 6, 31), J(1405, 3, 31), J(1404, 12, 29), J(1404, 9, 30)], evidence).ToDictionary(target => target.Kind);
        Assert.Equal(J(1404, 12, 29), targets[BalanceSheetBaselineKinds.FiscalYearEnd].Date);
        Assert.Null(targets[BalanceSheetBaselineKinds.FiscalYearEnd].Reason);
        Assert.Equal(BalanceSheetBaselineResolver.FiscalCalendarChanged, targets[BalanceSheetBaselineKinds.SameStagePreviousYear].Reason);
        Assert.Equal(BalanceSheetBaselineResolver.FiscalCalendarChanged, targets[BalanceSheetBaselineKinds.PreviousFiscalQuarterEnd].Reason);
        Assert.Equal(J(1405, 3, 31), targets[BalanceSheetBaselineKinds.PreviousReport].Date);
    }

    [Fact] // F154-R-07 end to end: previous_report is offered when «فصل قبل» is unavailable for a transitional year.
    public async Task TransitionalAnchorYear_UseCaseOffersPreviousReport()
    {
        var reader = new FakeBalanceSheetReader();
        reader.Add(Header("a", J(1405, 6, 31), "SixMonths", "1405/09/30"), Totals(100m, 60m, 40m));
        reader.Add(Header("p", J(1405, 3, 31), "ThreeMonths", "1405/09/30"), Totals(90m, 55m, 35m));
        reader.Add(Header("y", J(1404, 12, 29), "TwelveMonths", "1404/12/29"), Totals(80m, 50m, 30m));
        var result = await UseCase(reader).ExecuteAsync(new BalanceSheetReportRequest("وبملت"));
        var candidates = result.Comparison!.Candidates.ToDictionary(item => item.Kind);
        Assert.Equal("available", candidates[BalanceSheetBaselineKinds.FiscalYearEnd].State);
        Assert.Equal(BalanceSheetBaselineResolver.FiscalCalendarChanged, candidates[BalanceSheetBaselineKinds.PreviousFiscalQuarterEnd].Reason);
        Assert.Equal(BalanceSheetBaselineKinds.PreviousReport, candidates[BalanceSheetBaselineKinds.PreviousFiscalQuarterEnd].Alternatives[0]);
        Assert.NotEqual(BalanceSheetBaselineResolver.MissingFiscalMetadata, candidates[BalanceSheetBaselineKinds.SameStagePreviousYear].Reason);
    }

    [Theory] // F154-R-09: every requested date and the actual filter are named with a fitting rationale.
    [InlineData("تلفیقی", "تلفیقی")]
    [InlineData("حسابرسی‌شده", "حسابرسی")]
    [InlineData("تجدید ارائه‌شده", "تجدید ارائه")]
    [InlineData("12 ماهه", "طول دوره")]
    [InlineData("همه اقلام", "جدول کامل")]
    public async Task UnsupportedDateWithVariant_NamesEveryDateAndTheFilter(string filter, string rationale)
    {
        var result = await UseCase(new FakeBalanceSheetReader()).ExecuteAsync(new BalanceSheetReportRequest("وبملت",
            BalanceSheetViews.Comparison, Dates: [J(1405, 6, 31), J(1405, 3, 31)], UnsupportedVariantFilter: filter));
        Assert.Equal(BalanceSheetOutcomes.UnsupportedDateWithVariant, result.Outcome);
        Assert.Contains("۱۴۰۵/۰۶/۳۱", result.OutcomeMessage);
        Assert.Contains("۱۴۰۵/۰۳/۳۱", result.OutcomeMessage);
        Assert.Contains($"«{filter}»", result.OutcomeMessage);
        Assert.Contains(rationale, result.OutcomeMessage);
        if (filter != "تلفیقی") Assert.DoesNotContain("صورت تلفیقی فقط", result.OutcomeMessage);
    }

    [Fact] // F154-R-10: more equally ranked versions than the read bound fails closed with a typed outcome.
    public async Task AnchorTieGroupBeyondReadBound_FailsClosed()
    {
        var reader = new FakeBalanceSheetReader();
        for (var index = 0; index < 10; index++)
            reader.Add(Header($"v{index}", J(1405, 6, 31), "SixMonths", "1405/12/29"), Totals(100m, 60m, 40m));
        var result = await UseCase(reader).ExecuteAsync(new BalanceSheetReportRequest("وبملت"));
        Assert.Equal(BalanceSheetOutcomes.TooManyReportVersions, result.Outcome);
        Assert.Empty(result.Snapshots);
        Assert.Empty(reader.LineItemRequests);
    }

    [Fact] // F154-R-10: the line-item read never exceeds 9 statements even with peer versions.
    public async Task PeerVersions_NeverExceedNineLineItemStatements()
    {
        var reader = new FakeBalanceSheetReader();
        var date = J(1405, 6, 31);
        for (var month = 0; month < 6; month++)
            for (var version = 0; version < 3; version++)
                reader.Add(Header($"d{month}-{version}", date.AddMonths(-3 * month), month == 0 ? "SixMonths" : "ThreeMonths",
                    "1405/12/29"), Totals(100m, 60m, 40m));
        var result = await UseCase(reader).ExecuteAsync(new BalanceSheetReportRequest("وبملت"));
        Assert.Equal(BalanceSheetOutcomes.Available, result.Outcome);
        Assert.True(reader.LineItemRequests.Single().Count <= BalanceSheetReportContract.MaxLineItemStatements);
        Assert.True(result.Snapshots.Count <= BalanceSheetReportContract.MaxSnapshots);
    }

    [Fact] // F154-R-10: a truncated header window that splits a date's versions re-reads that date with all versions.
    public async Task SplitBoundaryDate_IsReReadWithAllVersions()
    {
        var reader = new FakeBalanceSheetReader();
        var newest = J(1405, 6, 31);
        for (var index = 0; index < BalanceSheetReportQueryUseCase.HeaderWindow - 1; index++)
            reader.Add(Header($"n{index}", newest.AddDays(-index), "SixMonths", "1405/12/29"), Totals(100m, 60m, 40m));
        var boundary = newest.AddDays(-(BalanceSheetReportQueryUseCase.HeaderWindow - 1));
        reader.Add(Header("unaudited", boundary, "SixMonths", "1405/12/29"), Totals(10m, 6m, 4m));
        var audited = reader.Add(Header("audited", boundary, "SixMonths", "1405/12/29", audited: true), Totals(20m, 12m, 8m));
        var result = await UseCase(reader).ExecuteAsync(new BalanceSheetReportRequest("وبملت", BalanceSheetViews.SingleDate, Dates: [boundary]));
        Assert.Equal(BalanceSheetOutcomes.Available, result.Outcome);
        Assert.Equal(audited.StatementId.ToString("D"), result.AnchorStatementId);
    }
}
