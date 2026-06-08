using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CodalDb;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.CodalDb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace FinancialCopilot.UnitTests;

public sealed class CodalDbRatioNormalizerTests
{
    private const string ProviderName = ProviderSources.NoavaranArchiveSqlName;
    private const string ExternalCompanyId = "3001";

    // ROE (ItemId=4138) + CURRENT_RATIO (ItemId=65) for company 3001,
    // one period (12-month, PeriodEnd=2025-03-20, FiscalYearEnd=2025-03-20), audited+consolidated.
    private static readonly string TwoRatiosJson = MakeRatiosJson(
        MakeRow(id: 1, itemId: 4138, itemValue: 18.5, periodType: 12),
        MakeRow(id: 2, itemId: 65, itemValue: 2.3, periodType: 12));

    // Two variants for the same (Period, PeriodType, ItemId): audited vs unaudited.
    private static readonly string DuplicateVariantsJson = MakeRatiosJson(
        MakeRow(id: 10, itemId: 4138, itemValue: 18.5, periodType: 12, isAudited: true),
        MakeRow(id: 11, itemId: 4138, itemValue: 15.0, periodType: 12, isAudited: false));

    // Unmapped ratio (ItemId=99999).
    private static readonly string UnmappedRatioJson = MakeRatiosJson(
        MakeRow(id: 20, itemId: 99999, itemValue: 0.5, periodType: 3));

    private static FinancialIngestionDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static CodalDbRatioNormalizer CreateNormalizer(FinancialIngestionDbContext db,
        bool preferConsolidated = true) =>
        new(db, Options.Create(new CodalDbProviderOptions
        {
            PreferConsolidatedStatements = preferConsolidated
        }));

    private static ProviderRawPayload MakePayload(string json, string? externalRef = ExternalCompanyId) =>
        new(Guid.NewGuid(), ProviderName, ProviderDataset.FinancialRatios,
            $"codaldb://financial-ratios/{externalRef}", externalRef ?? ExternalCompanyId,
            json, "checksum-" + Guid.NewGuid(), DateTimeOffset.UtcNow);

    private static async Task SeedSymbolAsync(FinancialIngestionDbContext db, string externalCompanyId)
    {
        var companyId = Guid.NewGuid();
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = companyId,
            Name = "Test Co",
            ProviderName = ProviderName,
            ExternalCompanyId = externalCompanyId,
            LastSynchronizedAt = DateTimeOffset.UtcNow
        });
        db.Symbols.Add(new NormalizedSymbolRow
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            ProviderName = ProviderName,
            ExternalSymbolId = externalCompanyId,
            SymbolCode = $"SYM{externalCompanyId}",
            LastSynchronizedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Normalize_MappedRatios_CreateDerivedMetricRowsWithCodalPolicyVersion()
    {
        await using var db = CreateDb();
        await SeedSymbolAsync(db, ExternalCompanyId);

        var count = await CreateNormalizer(db).NormalizeAsync(MakePayload(TwoRatiosJson), default);

        Assert.Equal(2, count);
        var metrics = await db.DerivedMetrics.ToListAsync();
        Assert.Equal(2, metrics.Count);
        Assert.All(metrics, m => Assert.Equal("codal-ratio-source-v1", m.CalculationPolicyVersion));
        Assert.All(metrics, m => Assert.Equal("v1", m.MetricVersion));
        Assert.Contains(metrics, m => m.MetricCode == "RETURN_ON_EQUITY");
        Assert.Contains(metrics, m => m.MetricCode == "CURRENT_RATIO");
    }

    [Fact]
    public async Task Normalize_VendorSourceEvidence_ContainsCodalDbSourceAndRatioItemId()
    {
        await using var db = CreateDb();
        await SeedSymbolAsync(db, ExternalCompanyId);

        await CreateNormalizer(db).NormalizeAsync(MakePayload(TwoRatiosJson), default);

        var roe = await db.DerivedMetrics.SingleAsync(m => m.MetricCode == "RETURN_ON_EQUITY");
        Assert.Contains(ProviderSources.NoavaranArchiveSqlName, roe.SourceEvidenceJson);
        Assert.Contains("4138", roe.SourceEvidenceJson);         // RatioItemId
        Assert.Contains("vendorPrecomputed", roe.SourceEvidenceJson); // camelCase from Web defaults
    }

    [Fact]
    public async Task Normalize_CanonicalVariantSelection_AuditedPreferredOverUnaudited()
    {
        await using var db = CreateDb();
        await SeedSymbolAsync(db, ExternalCompanyId);

        await CreateNormalizer(db).NormalizeAsync(MakePayload(DuplicateVariantsJson), default);

        var metric = await db.DerivedMetrics.SingleAsync(m => m.MetricCode == "RETURN_ON_EQUITY");
        // Audited row (id=10) has itemValue=18.5; unaudited row (id=11) has 15.0
        Assert.Equal(18.5m, metric.Value);
    }

    [Fact]
    public async Task Normalize_PercentageValue_StoredAsPercentScaleNotFraction()
    {
        // CodalDB stores 18.5 for 18.5% ROE. Must persist as 18.5, not 0.185.
        await using var db = CreateDb();
        await SeedSymbolAsync(db, ExternalCompanyId);

        await CreateNormalizer(db).NormalizeAsync(MakePayload(TwoRatiosJson), default);

        var roe = await db.DerivedMetrics.SingleAsync(m => m.MetricCode == "RETURN_ON_EQUITY");
        Assert.Equal(18.5m, roe.Value);
        Assert.Equal("Percentage", roe.Unit);
    }

    [Fact]
    public async Task Normalize_DistinctPolicyVersion_DoesNotCollideWithEngineCalculatedRows()
    {
        await using var db = CreateDb();
        await SeedSymbolAsync(db, ExternalCompanyId);
        var symbol = await db.Symbols.SingleAsync(s => s.ExternalSymbolId == ExternalCompanyId);

        // Seed an engine-calculated ROE row with a different policy version.
        db.DerivedMetrics.Add(new DerivedMetricRow
        {
            Id = Guid.NewGuid(),
            SymbolId = symbol.Id,
            MetricCode = "RETURN_ON_EQUITY",
            MetricVersion = "v1",
            CalculationPolicyVersion = "roe-engine-v1", // different policy
            PeriodType = "TwelveMonths",
            PeriodStart = new DateOnly(2024, 3, 21),
            PeriodEnd = new DateOnly(2025, 3, 20),
            Value = 20.0m,
            Unit = "Percentage",
            ObservedAt = DateTimeOffset.UtcNow,
            LastSynchronizedAt = DateTimeOffset.UtcNow,
            WarningsJson = "[]", SourceEvidenceJson = "[]", DependencyEvidenceJson = "[]"
        });
        await db.SaveChangesAsync();

        await CreateNormalizer(db).NormalizeAsync(MakePayload(TwoRatiosJson), default);

        // Two separate rows: engine-calculated (roe-engine-v1) + vendor (codal-ratio-source-v1)
        var roeRows = await db.DerivedMetrics
            .Where(m => m.MetricCode == "RETURN_ON_EQUITY")
            .ToListAsync();
        Assert.Equal(2, roeRows.Count);
        Assert.Contains(roeRows, m => m.CalculationPolicyVersion == "roe-engine-v1");
        Assert.Contains(roeRows, m => m.CalculationPolicyVersion == "codal-ratio-source-v1");
    }

    [Fact]
    public async Task Normalize_IdempotentRerun_NoDuplicateRowsCreated()
    {
        await using var db = CreateDb();
        await SeedSymbolAsync(db, ExternalCompanyId);
        var normalizer = CreateNormalizer(db);
        var payload = MakePayload(TwoRatiosJson);

        await normalizer.NormalizeAsync(payload, default);
        await normalizer.NormalizeAsync(payload, default);

        Assert.Equal(2, await db.DerivedMetrics.CountAsync());
    }

    [Fact]
    public async Task Normalize_UnmappedRatioItemId_IsIgnored()
    {
        await using var db = CreateDb();
        await SeedSymbolAsync(db, ExternalCompanyId);

        var count = await CreateNormalizer(db).NormalizeAsync(MakePayload(UnmappedRatioJson), default);

        Assert.Equal(0, count);
        Assert.Equal(0, await db.DerivedMetrics.CountAsync());
    }

    [Fact]
    public async Task Normalize_SymbolNotYetSynced_ReturnsZeroAndSkipsAllRows()
    {
        await using var db = CreateDb();
        // No symbol seeded for company "9999"

        var count = await CreateNormalizer(db).NormalizeAsync(MakePayload(TwoRatiosJson, "9999"), default);

        Assert.Equal(0, count);
        Assert.Equal(0, await db.DerivedMetrics.CountAsync());
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    private static string MakeRatiosJson(params object[] rows) =>
        JsonSerializer.Serialize(rows, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static object MakeRow(
        long id, int itemId, double itemValue, int periodType,
        bool? isAudited = true, bool? isRepresented = null, bool? isComposing = true) =>
        new
        {
            id,
            companyId = 3001,
            fiscalYearEnd = "2025-03-20T00:00:00Z",
            jalaliFiscalYearEnd = "1403/12/29",
            periodEnd = "2025-03-20T00:00:00Z",
            jalaliPeriodEnd = "1403/12/29",
            periodType,
            isAudited,
            isRepresented,
            isComposing,
            itemId,
            itemValue,
            modifiedDateTime = (string?)null
        };
}
