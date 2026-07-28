using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CodalDb;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.CodalDb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class CodalDbFinancialStatementNormalizerTests
{
    private const string ProviderName = ProviderSources.NoavaranArchiveSqlName;
    private const string CompanyId = "5001";

    // One Q1 statement (PeriodType=3), audited, consolidated.
    // Income: NET_PROFIT (143), REVENUE (15), unmapped item (999).
    // Balance: TOTAL_EQUITY (147).
    private const string SingleStatementJson = """
        [
          {
            "id": 1,
            "stmtId": 100,
            "companyId": 5001,
            "periodType": 3,
            "fiscalYearEnd": "2025-03-20T00:00:00Z",
            "fiscalYearEndJalali": "1403/12/29",
            "periodEnd": "2024-06-22T00:00:00Z",
            "periodEndJalali": "1403/03/31",
            "announcementDate": "2024-07-15T00:00:00Z",
            "isAudited": true,
            "isRepresented": true,
            "isComposing": true,
            "modifiedDateTime": null,
            "incomeItems": [
              { "itemId": 143, "itemTitleEn": "Net income", "amount": 500000 },
              { "itemId": 15,  "itemTitleEn": "Revenue",    "amount": 2000000 },
              { "itemId": 999, "itemTitleEn": "Unmapped",   "amount": 100 }
            ],
            "balanceItems": [
              { "itemId": 147, "itemTitleEn": "Total equity", "amount": 8000000 }
            ]
          }
        ]
        """;

    // Two distinct periods (Q1 and Q2) — should produce 4 statement rows total (2×INC+BS).
    private const string TwoPeriodsJson = """
        [
          {
            "id": 1, "stmtId": 100, "companyId": 5001, "periodType": 3,
            "fiscalYearEnd": "2025-03-20T00:00:00Z", "fiscalYearEndJalali": null,
            "periodEnd": "2024-06-22T00:00:00Z", "periodEndJalali": null,
            "announcementDate": "2024-07-15T00:00:00Z",
            "isAudited": true, "isRepresented": true, "isComposing": true, "modifiedDateTime": null,
            "incomeItems": [{ "itemId": 143, "itemTitleEn": "Net income", "amount": 100 }],
            "balanceItems": []
          },
          {
            "id": 2, "stmtId": 200, "companyId": 5001, "periodType": 6,
            "fiscalYearEnd": "2025-03-20T00:00:00Z", "fiscalYearEndJalali": null,
            "periodEnd": "2024-09-21T00:00:00Z", "periodEndJalali": null,
            "announcementDate": "2024-10-05T00:00:00Z",
            "isAudited": true, "isRepresented": true, "isComposing": true, "modifiedDateTime": null,
            "incomeItems": [{ "itemId": 143, "itemTitleEn": "Net income", "amount": 200 }],
            "balanceItems": []
          }
        ]
        """;

    private static FinancialIngestionDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static CodalDbFinancialStatementNormalizer CreateNormalizer(FinancialIngestionDbContext db,
        bool preferConsolidated = true) =>
        new(db, Options.Create(new CodalDbProviderOptions
        {
            PreferConsolidatedStatements = preferConsolidated
        }));

    private static ProviderRawPayload MakePayload(string json, string? companyId = CompanyId) =>
        new(Guid.NewGuid(), ProviderName, ProviderDataset.FinancialStatements,
            $"codaldb://statements/{companyId}", companyId ?? CompanyId,
            json, "checksum-" + Guid.NewGuid(), DateTimeOffset.UtcNow);

    [Fact]
    public async Task Normalize_SingleStatement_ProducesIncomeAndBalanceRowsSharingExternalId()
    {
        await using var db = CreateDb();
        var outcome = await CreateNormalizer(db).NormalizeAsync(MakePayload(SingleStatementJson), default);

        Assert.Equal(1, outcome.ProcessedRecords);
        Assert.Equal(2, await db.FinancialStatements.CountAsync()); // income + balance, same ExternalStatementId
        var inc = await db.FinancialStatements.SingleAsync(s =>
            s.ExternalStatementId == "100" && s.StatementType == "IncomeStatement");
        var bs = await db.FinancialStatements.SingleAsync(s =>
            s.ExternalStatementId == "100" && s.StatementType == "BalanceSheet");
        Assert.Equal("ThreeMonths", inc.PeriodType);
        Assert.Equal("ThreeMonths", bs.PeriodType);
        Assert.Equal(inc.PeriodStart, bs.PeriodStart);
        Assert.Equal(inc.PeriodEnd,   bs.PeriodEnd);
        // Spec 029: no more :INC / :BS suffix mangling on ExternalStatementId.
        Assert.DoesNotContain(":INC", inc.ExternalStatementId);
        Assert.DoesNotContain(":BS",  bs.ExternalStatementId);
    }

    [Fact]
    public async Task Normalize_OnlyMappedItemsWritten_UnmappedItemIgnored()
    {
        await using var db = CreateDb();
        await CreateNormalizer(db).NormalizeAsync(MakePayload(SingleStatementJson), default);

        var incomeStmt = await db.FinancialStatements.SingleAsync(s =>
            s.ExternalStatementId == "100" && s.StatementType == "IncomeStatement");
        var incomeItems = await db.FinancialStatementLineItems
            .Where(li => li.FinancialStatementId == incomeStmt.Id)
            .ToListAsync();

        // Only mapped income items: NET_PROFIT (143) + REVENUE (15); unmapped (999) excluded
        Assert.Equal(2, incomeItems.Count);
        Assert.Contains(incomeItems, li => li.MetricCode == "NET_PROFIT");
        Assert.Contains(incomeItems, li => li.MetricCode == "REVENUE");
        Assert.DoesNotContain(incomeItems, li => li.MetricCode == "UNMAPPED");
    }

    [Fact]
    public async Task Normalize_NetIncomeItem143_MapsToNetProfit()
    {
        await using var db = CreateDb();
        await CreateNormalizer(db).NormalizeAsync(MakePayload(SingleStatementJson), default);

        var stmt = await db.FinancialStatements.SingleAsync(s =>
            s.ExternalStatementId == "100" && s.StatementType == "IncomeStatement");
        var netProfit = await db.FinancialStatementLineItems.SingleAsync(
            li => li.FinancialStatementId == stmt.Id && li.MetricCode == "NET_PROFIT");

        Assert.Equal(500000m, netProfit.Value);
    }

    [Fact]
    public async Task Normalize_BalanceSheet_TotalEquityWrittenCorrectly()
    {
        await using var db = CreateDb();
        await CreateNormalizer(db).NormalizeAsync(MakePayload(SingleStatementJson), default);

        var stmt = await db.FinancialStatements.SingleAsync(s =>
            s.ExternalStatementId == "100" && s.StatementType == "BalanceSheet");
        var equity = await db.FinancialStatementLineItems.SingleAsync(
            li => li.FinancialStatementId == stmt.Id && li.MetricCode == "TOTAL_EQUITY");

        Assert.Equal(8000000m, equity.Value);
    }

    [Fact]
    public async Task Normalize_IsIdempotent_NoDuplicateRowsOnSecondRun()
    {
        await using var db = CreateDb();
        var normalizer = CreateNormalizer(db);
        var payload = MakePayload(SingleStatementJson);

        await normalizer.NormalizeAsync(payload, default);
        await normalizer.NormalizeAsync(payload, default);

        Assert.Equal(2, await db.FinancialStatements.CountAsync());
        Assert.Equal(3, await db.FinancialStatementLineItems.CountAsync()); // NET_PROFIT + REVENUE + TOTAL_EQUITY
    }

    [Fact]
    public async Task Normalize_SelectionFlagsRecordedInWarningsJson()
    {
        await using var db = CreateDb();
        await CreateNormalizer(db).NormalizeAsync(MakePayload(SingleStatementJson), default);

        var stmt = await db.FinancialStatements.SingleAsync(s =>
            s.ExternalStatementId == "100" && s.StatementType == "IncomeStatement");
        Assert.Contains("CodalStatementSelection", stmt.WarningsJson);
        Assert.Contains("MillionRials", stmt.WarningsJson);
        Assert.Contains("1403/03/31", stmt.WarningsJson); // PeriodEndJalali retained
    }

    [Fact]
    public async Task Normalize_TwoDistinctPeriods_ProducesFourStatementRows()
    {
        await using var db = CreateDb();
        var outcome = await CreateNormalizer(db).NormalizeAsync(MakePayload(TwoPeriodsJson), default);

        Assert.Equal(2, outcome.ProcessedRecords);
        Assert.Equal(4, await db.FinancialStatements.CountAsync()); // 2 periods × (income + balance)
    }

    [Fact]
    public async Task Normalize_PeriodTypeParsedAsFiscalPeriodTypeEnumName()
    {
        await using var db = CreateDb();
        await CreateNormalizer(db).NormalizeAsync(MakePayload(TwoPeriodsJson), default);

        var q1 = await db.FinancialStatements.FirstAsync(s =>
            s.ExternalStatementId == "100" && s.StatementType == "IncomeStatement");
        var q2 = await db.FinancialStatements.FirstAsync(s =>
            s.ExternalStatementId == "200" && s.StatementType == "IncomeStatement");

        // These must be parseable back to FiscalPeriodType via Enum.Parse (MetricInputSource requirement)
        Assert.Equal("ThreeMonths", q1.PeriodType);
        Assert.Equal("SixMonths",   q2.PeriodType);
    }

    [Fact]
    public async Task Normalize_EmptyBalanceItems_BalanceRowExistsWithNoLineItems()
    {
        await using var db = CreateDb();
        await CreateNormalizer(db).NormalizeAsync(MakePayload(TwoPeriodsJson), default);

        var bs = await db.FinancialStatements.SingleAsync(s =>
            s.ExternalStatementId == "100" && s.StatementType == "BalanceSheet");
        var items = await db.FinancialStatementLineItems
            .Where(li => li.FinancialStatementId == bs.Id)
            .ToListAsync();

        Assert.Empty(items); // no balance items in the Q1 row of TwoPeriodsJson
    }
}
