using FinancialCopilot.Application.FinancialData.CashFlowReport;
using FinancialCopilot.Application.FinancialData.Ingestion;
using System.Diagnostics;
using static FinancialCopilot.UnitTests.BalanceSheet154Fixtures;

namespace FinancialCopilot.UnitTests;

public sealed class CashFlowReport161UseCaseTests
{
    private sealed class Reader : ICashFlowStatementReader
    {
        public List<CashFlowStatementHeader> Headers { get; } = [];
        public List<CashFlowFiscalEvidence> Evidence { get; } = [];
        public Dictionary<Guid, IReadOnlyList<CashFlowItemObservation>> Lines { get; } = [];
        public List<CashFlowIncomeStatementObservation> Income { get; } = [];
        public bool Consolidated { get; set; }
        public IReadOnlyCollection<int>? ReadIds { get; private set; }
        public int ReadStatementCount { get; private set; }
        public Task<CashFlowHeaderSet> ReadHeadersAsync(string providerName, string externalCompanyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CashFlowHeaderSet(Headers.ToArray(), Evidence.ToArray()));
        public Task<IReadOnlyList<CashFlowStatementHeader>> ReadHeadersAtDatesAsync(string providerName, string externalCompanyId, IReadOnlyCollection<DateOnly> dates, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CashFlowStatementHeader>>(Headers.Where(x => dates.Contains(x.PeriodEnd)).ToArray());
        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<CashFlowItemObservation>>> ReadLineItemsAsync(string providerName,
            IReadOnlyCollection<Guid> statementIds, IReadOnlyCollection<int> sourceItemIds, CancellationToken cancellationToken = default)
        {
            ReadIds = sourceItemIds; ReadStatementCount = statementIds.Count;
            return Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<CashFlowItemObservation>>>(statementIds.ToDictionary(id => id,
                id => (IReadOnlyList<CashFlowItemObservation>)(Lines.GetValueOrDefault(id) ?? []).Where(item => sourceItemIds.Contains(item.SourceItemId)).ToArray()));
        }
        public Task<IReadOnlyList<CashFlowIncomeStatementObservation>> ReadIncomeStatementsAsync(string providerName, string externalCompanyId,
            IReadOnlyCollection<CashFlowStatementHeader> periods, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CashFlowIncomeStatementObservation>>(Income);
        public Task<bool> ConsolidatedExistsAsync(string providerName, string externalCompanyId, CancellationToken cancellationToken = default) => Task.FromResult(Consolidated);
    }

    private static CashFlowStatementHeader Header(string id = "10", Guid? statementId = null) =>
        new(statementId ?? Guid.NewGuid(), id, J(1405, 1, 1), J(1405, 6, 31), "SixMonths", false, false, false,
            null, null, "1405/12/29", "MillionRials", false);
    private static IReadOnlyList<CashFlowItemObservation> Items(params (int Id, decimal? Value)[] items) =>
        items.Select(item => new CashFlowItemObservation(item.Id, Guid.NewGuid(), item.Value, null)).ToArray();
    private static IReadOnlyList<CashFlowItemObservation> Fixture() => Items(
        (128, 6_918_890), (121, -359_104), (122, 6_559_786), (123, 9_025_064), (124, 15_584_850),
        (33, 6_692_454), (35, 0), (125, 22_277_304), (10, -359_535), (14, 300),
        (23, 24_635_600), (24, -13_908_702), (5, -1_457_520), (6, -244_314), (1, 999));
    private static CashFlowReportQueryUseCase UseCase(Reader reader, Guid?[]? groups = null) =>
        new(reader, new FakeCompanyResolution(), new FakeClassification(new() { ["167"] = groups ?? [ShegolGroup], ["452"] = groups ?? [BankGroup] }),
            new BankingFinancialReportsConfiguration(BankGroup), TimeProvider.System);
    private static Reader ReaderWithFixture()
    {
        var reader = new Reader(); var header = Header(); reader.Headers.Add(header); reader.Lines[header.StatementId] = Fixture(); return reader;
    }

    [Fact]
    public async Task GeneralFixture_ProducesTypedVerifiedReportWithoutExcludedReads()
    {
        var reader = ReaderWithFixture();
        var result = await UseCase(reader).ExecuteAsync(new("شگل", Focus: "analysis", AnalysisTopic: "earnings_quality"));
        Assert.Equal("161.1", result.ContractVersion);
        Assert.Equal("available", result.Outcome);
        Assert.Equal(CashFlowLineRegistry.GeneralTemplate, result.TemplateId);
        Assert.Equal("verified", result.Verification.Status);
        Assert.Equal(6_918_890m, Assert.Single(result.Kpis, x => x.MetricId == "operating_cash_flow" && x.ColumnId == result.Columns[0].ColumnId).Value);
        Assert.Equal(6_559_355m, Assert.Single(result.Kpis, x => x.MetricId == "free_cash_flow" && x.ColumnId == result.Columns[0].ColumnId).Value);
        Assert.NotNull(result.Waterfall);
        Assert.NotNull(result.Analysis);
        Assert.Empty(reader.ReadIds!.Intersect(CashFlowLineRegistry.ExcludedValueItems));
        Assert.InRange(reader.ReadStatementCount, 1, CashFlowReportContract.MaxCashFlowStatements);
    }

    [Fact]
    public async Task BankClassification_UsesOnlyConfiguredGroupAndOmitsIndustrialMetrics()
    {
        var result = await UseCase(ReaderWithFixture()).ExecuteAsync(new("وبملت"));
        Assert.Equal(CashFlowLineRegistry.BankingTemplate, result.TemplateId);
        Assert.DoesNotContain(result.Kpis, item => item.MetricId is "capex" or "free_cash_flow" or "cash_conversion");
        Assert.Single(result.Notices);
    }

    [Fact]
    public async Task SuppliedBankFixture_ReconcilesAndClosesVerifiedDetailSections()
    {
        var reader = ReaderWithFixture();
        var id = reader.Headers[0].StatementId;
        reader.Lines[id] = Items((128, 386_206_543), (121, -43_767_594), (122, 342_438_949), (123, -141_868_091),
            (124, 200_570_858), (33, 2_718_638_069), (35, 617_787_300), (125, 3_536_996_227),
            (10, -43_082_895), (48, -6_849_443), (4, 34_008), (14, 3_095_081), (104, 3_035_655),
            (23, 1_247_174_422), (24, -1_285_839_096), (5, -10_578_244), (6, -76_072_909), (117, -89_299_066), (119, 72_746_802));
        var result = await UseCase(reader).ExecuteAsync(new("وبملت"));
        Assert.Equal("verified", result.Verification.Status);
        Assert.Equal(3_536_996_227m, result.Waterfall!.Steps[^1].Value);
        Assert.Equal(617_787_300m, Assert.Single(result.Waterfall.Steps, step => step.StepId == "fx").Value);
        Assert.DoesNotContain(result.Sections.SelectMany(section => section.Rows), row => row.Kind == "residual");
        Assert.Equal(-16_552_264m, Assert.Single(result.Kpis, item => item.MetricId == "treasury_net" && item.ColumnId == result.Columns[0].ColumnId).Value);
    }

    [Fact]
    public async Task IncomeCompanion_RequiresSamePeriodAndUsesApprovedProfitFallback()
    {
        var reader = ReaderWithFixture();
        var companion = Header("income");
        reader.Income.Add(new(companion, Items((15, 10_000_000), (313, 2_000_000))));
        var result = await UseCase(reader).ExecuteAsync(new("شگل"));
        var conversion = Assert.Single(result.Kpis, item => item.MetricId == "cash_conversion" && item.ColumnId == result.Columns[0].ColumnId);
        Assert.Equal(6_918_890m / 2_000_000m, conversion.Value);
        Assert.Contains(conversion.Components, item => item.SourceType == "income_statement" && item.SourceItemId == 313);
        reader.Income[0] = reader.Income[0] with { Header = companion with { PeriodEnd = companion.PeriodEnd.AddDays(-1) } };
        result = await UseCase(reader).ExecuteAsync(new("شگل"));
        conversion = Assert.Single(result.Kpis, item => item.MetricId == "cash_conversion" && item.ColumnId == result.Columns[0].ColumnId);
        Assert.Equal("income_statement_unavailable", conversion.Reason);
    }

    [Fact]
    public async Task MissingFxThatReconciles_IsExposedAsInferredZeroInBankRow()
    {
        var reader = ReaderWithFixture(); var id = reader.Headers[0].StatementId;
        reader.Lines[id] = reader.Lines[id].Where(item => item.SourceItemId != 35).ToArray();
        var result = await UseCase(reader).ExecuteAsync(new("وبملت"));
        var row = Assert.Single(result.Sections.SelectMany(section => section.Rows), item => item.RowId == "item_35");
        var cell = row.Cells[result.Columns[0].ColumnId];
        Assert.Equal("inferred_zero", cell.State);
        Assert.Equal(0m, cell.Value);
        Assert.Equal("verified", result.Verification.Status);
    }

    [Fact]
    public async Task QuarterlyReport_UsesThreeMonthColumnsAndChecksYtdOpeningContinuity()
    {
        var reader = new Reader();
        var q1 = Header("q1") with { PeriodType = "ThreeMonths", PeriodEnd = J(1405, 3, 31) };
        var q2 = Header("q2");
        reader.Headers.AddRange([q1, q2]);
        reader.Lines[q1.StatementId] = Items((128, 400), (121, -100), (122, 300), (123, 50), (124, 350), (33, 1000), (35, 0), (125, 1350));
        reader.Lines[q2.StatementId] = Items((128, 750), (121, -250), (122, 500), (123, 150), (124, 650), (33, 1000), (35, 20), (125, 1670));
        var result = await UseCase(reader).ExecuteAsync(new("شگل", View: "quarterly"));
        Assert.Equal("available", result.Outcome);
        Assert.Equal("verified", result.Verification.Status);
        Assert.All(result.Columns, column => Assert.Equal(3, column.PeriodMonths));
        var q2Column = Assert.Single(result.Columns, column => column.Quarter == 2);
        Assert.Equal(350m, Assert.Single(result.Kpis, metric => metric.MetricId == "operating_cash_flow" && metric.ColumnId == q2Column.ColumnId).Value);
        Assert.Equal("passed", Assert.Single(result.Verification.Checks, check => check.CheckId == "C2" && check.ColumnId == q2Column.ColumnId).State);
    }

    [Fact]
    public async Task FailedReport_KeepsPublishedValuesAndMarksDependentMetricUnverified()
    {
        var reader = ReaderWithFixture(); var id = reader.Headers[0].StatementId;
        reader.Lines[id] = reader.Lines[id].Select(item => item.SourceItemId == 124 ? item with { Value = 15_584_973m } : item).ToArray();
        var result = await UseCase(reader).ExecuteAsync(new("شگل"));
        Assert.Equal("failed", result.Verification.Status);
        Assert.Equal(22_277_304m, result.Waterfall!.Steps[^1].Value);
        Assert.Equal("unverified", Assert.Single(result.Kpis, metric => metric.MetricId == "free_cash_flow" && metric.ColumnId == result.Columns[0].ColumnId).State);
        Assert.Equal(123m, Assert.Single(result.Verification.Checks, check => check.CheckId == "R1" && check.ColumnId == result.Columns[0].ColumnId).Difference);
    }

    [Fact]
    public async Task UnsupportedComparison_IsUnavailableWithoutHidingValidAnchor()
    {
        var reader = ReaderWithFixture();
        var prior = Header("prior") with { PeriodStart = J(1404, 1, 1), PeriodEnd = J(1404, 6, 31), JalaliFiscalYearEnd = "1404/12/29" };
        reader.Headers.Add(prior); reader.Lines[prior.StatementId] = Fixture().Concat(Items((120, 1))).ToArray();
        var result = await UseCase(reader).ExecuteAsync(new("شگل"));
        Assert.Equal("available", result.Outcome);
        Assert.Equal("available", result.Columns[0].State);
        Assert.Equal("unsupported_presentation", result.Columns[1].Reason);
        Assert.Null(result.Changes);
        Assert.Equal("verified", result.Verification.Status);
    }

    [Fact]
    public async Task ResolutionAndClassificationFailures_ReturnTypedOutcomes()
    {
        var useCase = UseCase(new Reader());
        Assert.Equal("company_unresolved", (await useCase.ExecuteAsync(new("missing"))).Outcome);
        Assert.Equal("company_ambiguous", (await useCase.ExecuteAsync(new("مبهم"))).Outcome);
        Assert.Equal("provider_unavailable", (await useCase.ExecuteAsync(new("شگل", ProviderName: "CyclicalWaves"))).Outcome);
        Assert.Equal("company_classification_unavailable", (await UseCase(new Reader(), []).ExecuteAsync(new("شگل"))).Outcome);
        Assert.Equal("unsupported_request", (await useCase.ExecuteAsync(new("شگل", View: "invalid"))).Outcome);
    }

    [Fact]
    public async Task MissingAndUnsupportedPresentations_ReturnSpecificOutcomes()
    {
        var empty = new Reader { Consolidated = true };
        var missing = await UseCase(empty).ExecuteAsync(new("شگل"));
        Assert.Equal("no_standalone_cash_flow", missing.Outcome);
        Assert.True(missing.ConsolidatedAlternativeAvailable);
        var reader = ReaderWithFixture(); var id = reader.Headers[0].StatementId;
        reader.Lines[id] = Fixture().Concat(Items((120, 1))).ToArray();
        Assert.Equal("unsupported_presentation", (await UseCase(reader).ExecuteAsync(new("شگل"))).Outcome);
        reader.Lines[id] = Fixture().Where(x => x.SourceItemId != 123).ToArray();
        Assert.Equal("codal_totals_missing", (await UseCase(reader).ExecuteAsync(new("شگل"))).Outcome);
        reader.Lines[id] = Fixture().Where(x => x.SourceItemId != 128).ToArray();
        Assert.Equal("operating_total_missing", (await UseCase(reader).ExecuteAsync(new("شگل"))).Outcome);
        reader.Lines[id] = Fixture(); reader.Headers[0] = reader.Headers[0] with { AssumedScale = "Rials" };
        Assert.Equal("ineligible_storage_scale", (await UseCase(reader).ExecuteAsync(new("شگل"))).Outcome);
    }

    [Fact]
    public async Task ConflictingVersions_FailClosedAndMissingPeriodReturnsNearest()
    {
        var reader = ReaderWithFixture(); var other = reader.Headers[0] with { StatementId = Guid.NewGuid(), ExternalStatementId = "11" };
        reader.Headers.Add(other); reader.Lines[other.StatementId] = Fixture().Select(x => x.SourceItemId == 121 ? x with { Value = -1 } : x).ToArray();
        var useCase = UseCase(reader);
        Assert.Equal("conflicting_report_revision", (await useCase.ExecuteAsync(new("شگل"))).Outcome);
        var missing = await useCase.ExecuteAsync(new("شگل", RequestedDate: J(1405, 3, 31)));
        Assert.Equal("no_statement_for_period", missing.Outcome);
        Assert.NotEmpty(missing.NearestPeriods);
    }

    [Fact]
    public async Task TooManyTopPeers_FailsWithoutReadingPartialVersions()
    {
        var reader = new Reader();
        for (var i = 0; i < 13; i++) reader.Headers.Add(Header(i.ToString()));
        var result = await UseCase(reader).ExecuteAsync(new("شگل"));
        Assert.Equal("unsupported_request", result.Outcome);
        Assert.Equal("too_many_report_versions", result.OutcomeReason);
        Assert.Equal(0, reader.ReadStatementCount);
    }

    [Fact]
    public async Task TelemetryCountsReadVersionsAndMeasuresReaderTime()
    {
        using var activity = new Activity("cash-flow-report-test").Start();
        var reader = ReaderWithFixture();
        var header = reader.Headers[0];
        var identical = header with { StatementId = Guid.NewGuid(), ExternalStatementId = "11" };
        reader.Headers.Add(identical); reader.Lines[identical.StatementId] = Fixture();
        var result = await UseCase(reader).ExecuteAsync(new("شگل"));
        Assert.Equal("available", result.Outcome);
        Assert.Equal(2, activity.GetTagItem("feature161.statements_read"));
        Assert.IsType<double>(activity.GetTagItem("feature161.read_latency_ms"));
        Assert.Single(result.Statements);
    }
}
