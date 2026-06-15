using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Spec 050 — all-index coverage normalizer: persists every vendor index (mapped and unmapped) into
/// the non-scannable staging table, flags governed candidates, never writes DerivedMetrics, applies
/// deterministic variant selection, and is idempotent.
/// </summary>
public sealed class NadpcoFundamentalIndexCoverageTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-09T10:00:00Z");

    // Company 4: index 65 (curated/governed candidate) + index 99999 (unmapped) for one 12-month period.
    private const string TwoIndexesJson = """
        [
          {
            "comBS_ID": 5001,
            "comId": 4,
            "comTitle": "آبین",
            "periodType": 12,
            "jalaliFiscalYearEnd": "1403/12/29",
            "jalaliPeriodEnd": "1403/12/29",
            "jalaliAnouncementDate": "1404/02/10",
            "isAudited": true,
            "isRepresented": false,
            "isComposing": true,
            "indexes": [
              { "companyIndexId": 65, "companyIndexTitle": "Current Ratio", "companyIndexGroupId": 1, "companyIndexGroupTitle": "Liquidity", "companyIndexValue": 1.03, "companyIndexUnit": "Ratio" },
              { "companyIndexId": 99999, "companyIndexTitle": "Vendor Experimental Index", "companyIndexGroupId": 9, "companyIndexGroupTitle": "Other", "companyIndexValue": 7.5, "companyIndexUnit": "X" }
            ]
          }
        ]
        """;

    // Same (company, index, period type, period end) twice: unaudited vs audited — audited must win.
    private const string DuplicateVariantsJson = """
        [
          { "comBS_ID": 10, "comId": 4, "comTitle": "آبین", "periodType": 12,
            "jalaliFiscalYearEnd": "1403/12/29", "jalaliPeriodEnd": "1403/12/29", "jalaliAnouncementDate": "1404/01/01",
            "isAudited": false, "isRepresented": false, "isComposing": true,
            "indexes": [ { "companyIndexId": 65, "companyIndexTitle": "CR", "companyIndexGroupId": 1, "companyIndexGroupTitle": "L", "companyIndexValue": 1.50, "companyIndexUnit": "Ratio" } ] },
          { "comBS_ID": 11, "comId": 4, "comTitle": "آبین", "periodType": 12,
            "jalaliFiscalYearEnd": "1403/12/29", "jalaliPeriodEnd": "1403/12/29", "jalaliAnouncementDate": "1404/02/01",
            "isAudited": true, "isRepresented": false, "isComposing": true,
            "indexes": [ { "companyIndexId": 65, "companyIndexTitle": "CR", "companyIndexGroupId": 1, "companyIndexGroupTitle": "L", "companyIndexValue": 1.03, "companyIndexUnit": "Ratio" } ] }
        ]
        """;

    [Fact]
    public async Task Normalize_PersistsEveryVendorIndex_IncludingUnmapped_WithoutDerivedMetrics()
    {
        await using var db = CreateDb();

        var outcome = await CreateNormalizer(db).NormalizeAsync(MakePayload(TwoIndexesJson), default);

        Assert.Equal(2, outcome.ProcessedRecords);
        var observations = await db.NadpcoFundamentalIndexObservations.ToListAsync();
        Assert.Equal(2, observations.Count);

        var governed = observations.Single(o => o.CompanyIndexId == 65);
        Assert.True(governed.IsGovernedCandidate);          // curated 041 allowlist contains 65
        Assert.Equal(1.03m, governed.CompanyIndexValue);

        var unmapped = observations.Single(o => o.CompanyIndexId == 99999);
        Assert.False(unmapped.IsGovernedCandidate);          // not in the allowlist, still persisted
        Assert.Equal("Vendor Experimental Index", unmapped.CompanyIndexTitle);

        // The all-index coverage path must never write governed scanner metrics.
        Assert.Empty(await db.DerivedMetrics.ToListAsync());
    }

    [Fact]
    public async Task Normalize_PreservesProvenance_AndConvertsJalaliPeriod()
    {
        await using var db = CreateDb();

        await CreateNormalizer(db).NormalizeAsync(MakePayload(TwoIndexesJson, "chk-1"), default);

        var row = await db.NadpcoFundamentalIndexObservations.FirstAsync(o => o.CompanyIndexId == 65);
        Assert.Equal("4", row.ExternalCompanyId);
        Assert.Equal(5001, row.ExternalStatementId);
        Assert.Equal("آبین", row.CompanyTitle);
        Assert.Equal(12, row.PeriodType);
        Assert.True(row.IsAudited);
        Assert.False(row.IsRepresented);
        Assert.Equal("1404/02/10", row.JalaliAnnouncementDate);
        Assert.Equal("chk-1", row.SourcePayloadChecksum);
        Assert.Equal(new DateOnly(2025, 3, 19), row.PeriodEnd); // 1403/12/29 Jalali (year-end) -> 2025-03-19
    }

    [Fact]
    public async Task Normalize_SelectsAuditedVariant_Deterministically()
    {
        await using var db = CreateDb();

        await CreateNormalizer(db).NormalizeAsync(MakePayload(DuplicateVariantsJson), default);

        var row = Assert.Single(await db.NadpcoFundamentalIndexObservations.ToListAsync());
        Assert.Equal(1.03m, row.CompanyIndexValue); // audited variant (ComBS_ID 11) wins over unaudited 1.50
        Assert.True(row.IsAudited);
    }

    [Fact]
    public async Task Normalize_IsIdempotent_OnRerun()
    {
        await using var db = CreateDb();
        var normalizer = CreateNormalizer(db);

        await normalizer.NormalizeAsync(MakePayload(TwoIndexesJson), default);
        await normalizer.NormalizeAsync(MakePayload(TwoIndexesJson, "chk-2"), default);

        Assert.Equal(2, await db.NadpcoFundamentalIndexObservations.CountAsync());
        // The re-run updates provenance to the latest payload checksum.
        Assert.All(
            await db.NadpcoFundamentalIndexObservations.ToListAsync(),
            o => Assert.Equal("chk-2", o.SourcePayloadChecksum));
    }

    private static NadpcoApiFundamentalIndexCoverageNormalizer CreateNormalizer(FinancialIngestionDbContext db) =>
        new(db);

    private static ProviderRawPayload MakePayload(string json, string checksum = "chk") =>
        new(Guid.NewGuid(), ProviderSources.NoavaranCurrentApiName, ProviderDataset.FundamentalIndexCoverage,
            "api/v2/CompanyFundamentalIndex/Values?fromYear=1403&toYear=1405", "4", json, checksum, Now);

    private static FinancialIngestionDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
