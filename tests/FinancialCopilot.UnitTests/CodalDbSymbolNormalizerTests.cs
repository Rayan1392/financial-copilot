using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Domain.Financial.Services;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CodalDb;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.UnitTests;

public sealed class CodalDbSymbolNormalizerTests
{
    private const string ProviderName = ProviderSources.NoavaranArchiveSqlName;

    // Two companies sharing IndustryID 270 (dimension dedup); the second has no ISIN.
    private const string CompaniesJson = """
        [
          {
            "CoID": 1001,
            "CoName": "فولاد مبارکه",
            "CoNameEnglish": "Mobarakeh Steel",
            "CompanySymbol": "فولاد",
            "CoTSESymbol": "فولاد",
            "GroupID": 27,
            "GroupName": "فلزات اساسی",
            "IndustryID": 270,
            "IndustryName": "فلزات اساسی",
            "InstCode": "46348559193224090",
            "TseCIsinCode": "IRO1FOLD0006",
            "TseSIsinCode": "IRO1FOLD0001",
            "MarketID": 1,
            "MarketName": "بورس",
            "InstrumentRef": "9455D05D-0000-0000-0000-000000000000",
            "ModifiedDateTime": "2026-01-15T10:00:00Z"
          },
          {
            "CoID": 1002,
            "CoName": "ذوب آهن",
            "CoNameEnglish": "Esfahan Steel",
            "CompanySymbol": "ذوب",
            "CoTSESymbol": "ذوب",
            "GroupID": 27,
            "GroupName": "فلزات اساسی",
            "IndustryID": 270,
            "IndustryName": "فلزات اساسی",
            "InstCode": "70289374903549577",
            "TseCIsinCode": null,
            "TseSIsinCode": null,
            "MarketID": 1,
            "MarketName": "بورس",
            "InstrumentRef": "9455D05D-0000-0000-0000-000000000000",
            "ModifiedDateTime": null
          }
        ]
        """;

    private static FinancialIngestionDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static CodalDbSymbolNormalizer CreateNormalizer(FinancialIngestionDbContext db) =>
        new(db, new CanonicalSymbolLinkageResolver(), NullLogger<CodalDbSymbolNormalizer>.Instance);

    private static ProviderRawPayload MakePayload(string json) =>
        new(
            Guid.NewGuid(),
            ProviderName,
            ProviderDataset.Symbols,
            "codaldb://companies",
            "all",
            json,
            "checksum-" + Guid.NewGuid(),
            DateTimeOffset.UtcNow);

    [Fact]
    public async Task Normalize_CreatesCompaniesSymbolsAndDeduplicatedDimensions()
    {
        await using var db = CreateDbContext();
        var outcome = await CreateNormalizer(db).NormalizeAsync(MakePayload(CompaniesJson), default);

        Assert.Equal(2, outcome.ProcessedRecords);
        Assert.Equal(2, await db.Companies.CountAsync());
        Assert.Equal(2, await db.Symbols.CountAsync());
        Assert.Equal(1, await db.Industries.CountAsync()); // shared IndustryID 270
        Assert.Equal(1, await db.IndustryGroups.CountAsync());
        Assert.Equal(1, await db.Markets.CountAsync());
    }

    [Fact]
    public async Task Normalize_PopulatesEnrichedCompanyAttributes()
    {
        await using var db = CreateDbContext();
        await CreateNormalizer(db).NormalizeAsync(MakePayload(CompaniesJson), default);

        var company = await db.Companies.SingleAsync(c => c.ExternalCompanyId == "1001");
        Assert.Equal("فولاد مبارکه", company.Name);
        Assert.Equal("Mobarakeh Steel", company.NameEnglish);
        Assert.Equal("فولاد", company.CompanySymbol);
        Assert.Equal("فولاد", company.TseSymbol);
        Assert.Equal("46348559193224090", company.InstrumentCode);
        Assert.Equal("IRO1FOLD0006", company.CompanyIsin);
        Assert.Equal("IRO1FOLD0001", company.SymbolIsin);
        Assert.NotNull(company.IndustryId);
        Assert.NotNull(company.GroupId);
        Assert.NotNull(company.MarketId);
        Assert.Equal(new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero), company.SourceModifiedAt);
    }

    [Fact]
    public async Task Normalize_ResolvesCanonicalSymbolFromSymbolIsin_WithRecordedBasis()
    {
        await using var db = CreateDbContext();
        await CreateNormalizer(db).NormalizeAsync(MakePayload(CompaniesJson), default);

        var symbol = await db.Symbols.SingleAsync(s => s.ExternalSymbolId == "1001");
        Assert.Equal("IRO1FOLD0001", symbol.SymbolCode);
        Assert.Equal("SymbolIsin", symbol.LinkageBasis);
    }

    [Fact]
    public async Task Normalize_NoIsin_FallsBackToInstrumentCodeBasis()
    {
        await using var db = CreateDbContext();
        await CreateNormalizer(db).NormalizeAsync(MakePayload(CompaniesJson), default);

        var symbol = await db.Symbols.SingleAsync(s => s.ExternalSymbolId == "1002");
        Assert.Equal("70289374903549577", symbol.SymbolCode);
        Assert.Equal("InstrumentCode", symbol.LinkageBasis);
    }

    [Fact]
    public async Task Normalize_StoresInstrumentRefAsNonIdentifyingProvenance()
    {
        await using var db = CreateDbContext();
        await CreateNormalizer(db).NormalizeAsync(MakePayload(CompaniesJson), default);

        // Both companies carry the same constant InstrumentRef; it is stored but never a key,
        // so distinct companies still exist.
        var refs = await db.Companies.Select(c => c.InstrumentRefPlaceholder).Distinct().ToListAsync();
        Assert.Single(refs);
        Assert.Equal("9455D05D-0000-0000-0000-000000000000", refs[0]);
        Assert.Equal(2, await db.Companies.CountAsync());
    }

    [Fact]
    public async Task Normalize_IsIdempotent_NoDuplicateRowsOnSecondRun()
    {
        await using var db = CreateDbContext();
        var normalizer = CreateNormalizer(db);
        var payload = MakePayload(CompaniesJson);

        await normalizer.NormalizeAsync(payload, default);
        await normalizer.NormalizeAsync(payload, default);

        Assert.Equal(2, await db.Companies.CountAsync());
        Assert.Equal(2, await db.Symbols.CountAsync());
        Assert.Equal(1, await db.Industries.CountAsync());
        Assert.Equal(1, await db.IndustryGroups.CountAsync());
        Assert.Equal(1, await db.Markets.CountAsync());
    }

    [Fact]
    public async Task Normalize_CompanyWithoutAnyIdentifier_CreatesNoSymbolRow()
    {
        const string json = """
            [
              {
                "CoID": 2001,
                "CoName": "بدون نماد",
                "CompanySymbol": null,
                "CoTSESymbol": null,
                "InstCode": null,
                "TseCIsinCode": null,
                "TseSIsinCode": null
              }
            ]
            """;

        await using var db = CreateDbContext();
        var outcome = await CreateNormalizer(db).NormalizeAsync(MakePayload(json), default);

        Assert.Equal(1, outcome.ProcessedRecords);
        Assert.Equal(1, await db.Companies.CountAsync());
        Assert.Equal(0, await db.Symbols.CountAsync());
    }
}
