using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.FinancialData.ProfessionalScanners;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.ProfessionalScanners;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.ProfessionalScanners;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.UnitTests;

public sealed class GovernedProfessionalFilterCatalogTests
{
    private readonly GovernedProfessionalFilterCatalog _catalog = new();

    [Fact]
    public void Catalog_HasStableUniqueDefinitionsAcrossEverySupportedFamily()
    {
        var page = _catalog.List(new ProfessionalCatalogQuery(PageSize: 100));

        Assert.Equal(9, page.TotalCount);
        Assert.Equal(page.Items.Count, page.Items.Select(item => (item.Code, item.Version)).Distinct().Count());
        Assert.All(Enum.GetValues<ProfessionalFilterCategory>(), category =>
            Assert.Contains(page.Items, item => item.Category == category));
        Assert.All(page.Items, item =>
        {
            Assert.NotEmpty(item.RequiredDatasets);
            Assert.False(string.IsNullOrWhiteSpace(item.Ranking));
            Assert.False(string.IsNullOrWhiteSpace(item.TieBreaker));
            Assert.Equal(GovernedProfessionalFilterCatalog.EntitlementCode, item.EntitlementCode);
        });
        Assert.Contains(page.UnsupportedFilters, item => item.RequestedFilter.Contains("RSI"));
        Assert.Contains(page.UnsupportedFilters, item => item.Reason.Contains("prohibited", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("قدرت خریدار حقیقی", "BUYER_POWER_SURGE")]
    [InlineData("حجم غیرعادی", "VOLUME_ANOMALY")]
    [InlineData("P/E پایین و رشد فروش", "GROWTH_AT_VALUE")]
    public void ResolveAlias_UsesGovernedPersianAliases(string alias, string expectedCode)
    {
        var resolution = _catalog.ResolveAlias(alias);
        Assert.True(resolution.Resolved);
        Assert.Equal(expectedCode, resolution.Definition!.Code);
    }

    [Fact]
    public void ValidateParameters_NormalizesAliasesAndRejectsExpressions()
    {
        var definition = _catalog.Get("LOW_PE");
        var parameters = _catalog.ValidateParameters(definition,
            new Dictionary<string, string> { ["حداکثر پی ای"] = "4.5" });
        Assert.Equal("4.5", parameters["maxPe"]);

        Assert.Throws<ProfessionalScannerValidationException>(() => _catalog.ValidateParameters(definition,
            new Dictionary<string, string> { ["maxPe"] = "1; drop table Companies" }));
        Assert.Throws<ProfessionalScannerValidationException>(() => _catalog.ValidateParameters(definition,
            new Dictionary<string, string> { ["maxPe"] = "101" }));
    }
}

public sealed class SavedFilterDomainTests
{
    [Fact]
    public void SavedFilter_IsVersionedActorOwnedCatalogReference()
    {
        var now = DateTimeOffset.Parse("2026-07-14T08:00:00Z");
        var actor = new SavedFilterActor(Guid.NewGuid(), Guid.NewGuid(), "User");
        var value = SavedFilter.Create(actor, "ارزنده‌ها", "LOW_PE", "1.0.0", "{\"maxPe\":\"5\"}", now);

        value.Update(1, "ارزنده و رشدی", "GROWTH_AT_VALUE", "1.0.0",
            "{\"maxPe\":\"7\",\"minGrowthPercent\":\"30\"}", now.AddMinutes(1));

        Assert.Equal(2, value.Version);
        Assert.Equal("GROWTH_AT_VALUE", value.FilterCode);
        Assert.Throws<SavedFilterValidationException>(() => value.Remove(1, now.AddMinutes(2)));
        value.Remove(2, now.AddMinutes(2));
        Assert.True(value.IsRemoved);
    }
}

public sealed class ProfessionalScannerDeterminismTests
{
    [Fact]
    public async Task EveryGovernedFilterFamily_ExecutesAgainstDeterministicFixtures()
    {
        var now = DateTimeOffset.Parse("2026-07-14T10:00:00Z");
        await using var db = new FinancialIngestionDbContext(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var catalog = new GovernedProfessionalFilterCatalog();
        var definitions = catalog.List(new ProfessionalCatalogQuery(PageSize: 100)).Items;
        foreach (var definition in definitions.Where(item => item.InsightType.HasValue))
        {
            var row = Event(definition.Code, definition.Code, 80m, now.AddMinutes(-1), definition.Code);
            row.InsightType = definition.InsightType!.Value.ToString();
            row.IndustryCode = "IND";
            db.InsightEvents.Add(row);
        }
        foreach (var code in definitions.SelectMany(item => item.Conditions).Select(item => item.MetricCode).Distinct())
        {
            db.DerivedMetrics.Add(new DerivedMetricRow
            {
                Id = Guid.NewGuid(), ExternalCompanyId = "COMPANY", MetricCode = code,
                MetricVersion = "v1", CalculationPolicyVersion = $"{code}_v1", PeriodType = "LatestMonth",
                PeriodStart = new DateOnly(2026, 6, 1), PeriodEnd = new DateOnly(2026, 6, 30), Value = 10,
                Unit = "Ratio", ObservedAt = now.AddMinutes(-5), LastSynchronizedAt = now.AddMinutes(-5)
            });
        }
        await db.SaveChangesAsync();
        var service = new ProfessionalScannerUseCases(catalog, new MemorySavedFilters(), new AllowEntitlement(),
            new DeterministicScanner(now), new NoBilling(), db, new FixedTimeProvider(now),
            NullLogger<ProfessionalScannerUseCases>.Instance);
        var actor = new CurrentActor(ActorType.User, Guid.NewGuid(), Guid.NewGuid(), AuthenticationMode.WebAppUser);

        foreach (var definition in definitions)
        {
            var scope = definition.Category == ProfessionalFilterCategory.Industry
                ? new ProfessionalScannerScope("IND") : new ProfessionalScannerScope();
            var result = await service.ExecuteAsync(new ProfessionalExecuteCommand(actor, definition.Code,
                definition.Version, null, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 14), scope,
                1, 20, $"family-{definition.Code}"), CancellationToken.None);

            Assert.Equal(ProfessionalExecutionStatus.Complete, result.Status);
            Assert.Single(result.Rows);
            Assert.NotEmpty(result.Rows.Single().Reasons);
            Assert.Equal(definition.Version, result.FilterVersion);
        }
    }

    [Fact]
    public async Task InsightFilter_SameSnapshotProducesSameOrderReasonsAndEvidenceHash()
    {
        var now = DateTimeOffset.Parse("2026-07-14T10:00:00Z");
        await using var db = new FinancialIngestionDbContext(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.InsightEvents.AddRange(
            Event("A", "الف", 75m, now.AddMinutes(-5), "reason-A"),
            Event("B", "ب", 90m, now.AddMinutes(-10), "reason-B"),
            Event("C", "پ", 75m, now.AddMinutes(-1), "reason-C"));
        await db.SaveChangesAsync();
        var service = new ProfessionalScannerUseCases(new GovernedProfessionalFilterCatalog(),
            new MemorySavedFilters(), new AllowEntitlement(), new RejectingScanner(), new NoBilling(), db,
            new FixedTimeProvider(now), NullLogger<ProfessionalScannerUseCases>.Instance);
        var actor = new CurrentActor(ActorType.User, Guid.NewGuid(), Guid.NewGuid(), AuthenticationMode.WebAppUser);
        var command = new ProfessionalExecuteCommand(actor, "VOLUME_ANOMALY", null, null,
            new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 14), new ProfessionalScannerScope(),
            1, 20, "same-snapshot");

        var first = await service.ExecuteAsync(command, CancellationToken.None);
        var second = await service.ExecuteAsync(command, CancellationToken.None);

        Assert.Equal(ProfessionalExecutionStatus.Complete, first.Status);
        Assert.Equal(["ب", "پ", "الف"], first.Rows.Select(row => row.Symbol).ToArray());
        Assert.Equal(first.EvidenceHash, second.EvidenceHash);
        Assert.Equal(first.Rows.Select(row => row.Reasons.Single().Text),
            second.Rows.Select(row => row.Reasons.Single().Text));
    }

    [Fact]
    public async Task MissingRequiredEventDataset_ReturnsUnavailableWithoutMatches()
    {
        var now = DateTimeOffset.Parse("2026-07-14T10:00:00Z");
        await using var db = new FinancialIngestionDbContext(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var service = new ProfessionalScannerUseCases(new GovernedProfessionalFilterCatalog(),
            new MemorySavedFilters(), new AllowEntitlement(), new RejectingScanner(), new NoBilling(), db,
            new FixedTimeProvider(now), NullLogger<ProfessionalScannerUseCases>.Instance);
        var actor = new CurrentActor(ActorType.User, Guid.NewGuid(), Guid.NewGuid(), AuthenticationMode.WebAppUser);

        var result = await service.ExecuteAsync(new ProfessionalExecuteCommand(actor, "LARGE_TRADE_ACTIVITY", null,
            null, null, null, null, 1, 20, "missing-data"), CancellationToken.None);

        Assert.Equal(ProfessionalExecutionStatus.Unavailable, result.Status);
        Assert.Empty(result.Rows);
        Assert.Contains(result.DatasetMessages, message => message.Contains("unavailable"));
    }

    private static InsightEventRow Event(string company, string symbol, decimal importance,
        DateTimeOffset detected, string reason) => new()
    {
        Id = Guid.NewGuid(), ExternalCompanyId = company, Symbol = symbol,
        InsightType = "TradingVolumeAnomaly", Severity = "Important", ImportanceScore = importance,
        ConfidenceScore = 85m, Title = symbol, Summary = reason, Reason = reason,
        EvidenceJson = "[]", SourceProviderName = "fixture", SourceEntityType = "MarketMicrostructureObservation",
        DetectedAtUtc = detected, DeduplicationKey = $"{company}:{detected:O}", SuggestedActionsJson = "[]"
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => now; }

    private sealed class AllowEntitlement : IProfessionalScannerEntitlementPolicy
    {
        public Task<ProfessionalAccessMode> ValidateExecuteAsync(CurrentActor actor, CancellationToken cancellationToken) =>
            Task.FromResult(ProfessionalAccessMode.Unlimited);
        public Task ValidateSaveAsync(CurrentActor actor, int currentSavedCount, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RejectingScanner : IScannerExecutionService
    {
        public Task<ScannerTableResult> ExecuteAsync(ScannerExecutionRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Metric scanner should not be called by an insight-event filter.");
    }

    private sealed class DeterministicScanner(DateTimeOffset freshness) : IScannerExecutionService
    {
        public Task<ScannerTableResult> ExecuteAsync(ScannerExecutionRequest request, CancellationToken cancellationToken)
        {
            var columns = request.Plan.Conditions.Select(condition => new ScannerTableColumn(
                condition.MetricReference.MetricCode.Value, condition.MetricReference.MetricCode.Value,
                ScannerColumnType.Metric, condition.MetricReference.MetricCode.Value)).ToArray();
            var cells = request.Plan.Conditions.ToDictionary(condition => condition.MetricReference.MetricCode.Value,
                condition => new ScannerTableCell(condition.Operator is ConditionOperator.LessThan or ConditionOperator.LessThanOrEqual
                    ? condition.Threshold - 1 : condition.Threshold + 1, null, CellFreshnessStatus.Persisted, freshness));
            ScannerTableRow[] rows = [new("FIX", "Fixture", cells, 1, cells.Keys.ToArray(), "fixture", "COMPANY")];
            return Task.FromResult(new ScannerTableResult(request.Plan.PlanId, columns, rows,
                new ScannerExecutionFacts(freshness, TimeSpan.Zero, 1, 1, false, request.Page, request.PageSize, 1), []));
        }
    }

    private sealed class NoBilling : IBillingFacadeHook
    {
        public Task<BillingReservationHandle?> TryReserveAsync(BillingReservationRequest request, CancellationToken cancellationToken) => Task.FromResult<BillingReservationHandle?>(null);
        public Task<UsageAccountingResult?> FinalizeAsync(BillingReservationHandle handle, BillingFinalizationRequest request, CancellationToken cancellationToken) => Task.FromResult<UsageAccountingResult?>(null);
        public Task ReleaseAsync(BillingReservationHandle handle, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class MemorySavedFilters : ISavedFilterRepository
    {
        public Task<IReadOnlyCollection<SavedFilter>> ListAsync(SavedFilterActor actor, int page, int pageSize, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<SavedFilter>>([]);
        public Task<int> CountAsync(SavedFilterActor actor, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<SavedFilter?> FindAsync(SavedFilterActor actor, Guid id, bool includeRemoved, CancellationToken cancellationToken) => Task.FromResult<SavedFilter?>(null);
        public Task SaveAsync(SavedFilter value, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
