using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Spec 029: the configured-HTTP-provider normalizer must validate the two enum-shaped string
/// fields (<c>Period</c> and <c>StatementType</c>) at ingestion time so a future HTTP adapter
/// cannot silently write garbage that the metric engine can't parse downstream.
/// </summary>
public sealed class ConfiguredFinancialProviderNormalizerTests
{
    private const string ProviderName = "ConfiguredFinancialProvider";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-31T09:00:00Z");

    [Fact]
    public async Task ValidPayload_PersistsRowWithBothFields()
    {
        await using var db = NewDb();
        var normalizer = new FinancialStatementPayloadNormalizer(db);
        var payload = MakePayload(MakeJson(
            statementId: "S1", companyId: "C1",
            period: "ThreeMonths", statementType: "IncomeStatement"));

        var count = await normalizer.NormalizeAsync(payload, CancellationToken.None);

        Assert.Equal(1, count);
        var row = await db.FinancialStatements.SingleAsync();
        Assert.Equal("ThreeMonths", row.PeriodType);
        Assert.Equal("IncomeStatement", row.StatementType);
        Assert.Equal("C1", row.ExternalCompanyId);
    }

    [Fact]
    public async Task InvalidPeriod_ThrowsFinancialProviderException()
    {
        await using var db = NewDb();
        var normalizer = new FinancialStatementPayloadNormalizer(db);
        var payload = MakePayload(MakeJson(
            statementId: "S2", companyId: "C2",
            period: "IncomeStatement", // wrong: this would be the spec-020 bug
            statementType: "IncomeStatement"));

        var ex = await Assert.ThrowsAsync<FinancialProviderException>(() =>
            normalizer.NormalizeAsync(payload, CancellationToken.None));
        Assert.Equal(FinancialProviderErrorCode.InvalidResponse, ex.Code);
        Assert.Contains("PeriodType", ex.Message);
        Assert.Equal(0, await db.FinancialStatements.CountAsync());
    }

    [Fact]
    public async Task InvalidStatementType_ThrowsFinancialProviderException()
    {
        await using var db = NewDb();
        var normalizer = new FinancialStatementPayloadNormalizer(db);
        var payload = MakePayload(MakeJson(
            statementId: "S3", companyId: "C3",
            period: "ThreeMonths",
            statementType: "NotARealType"));

        var ex = await Assert.ThrowsAsync<FinancialProviderException>(() =>
            normalizer.NormalizeAsync(payload, CancellationToken.None));
        Assert.Equal(FinancialProviderErrorCode.InvalidResponse, ex.Code);
        Assert.Contains("StatementType", ex.Message);
        Assert.Equal(0, await db.FinancialStatements.CountAsync());
    }

    // ---- Helpers ----

    private static FinancialIngestionDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static string MakeJson(
        string statementId,
        string companyId,
        string period,
        string statementType) =>
        $$"""
        {
          "statementId": "{{statementId}}",
          "companyId": "{{companyId}}",
          "netProfit": 1000,
          "period": "{{period}}",
          "statementType": "{{statementType}}",
          "periodStart": "2026-01-01",
          "periodEnd": "2026-03-31"
        }
        """;

    private static ProviderRawPayload MakePayload(string json) =>
        new(
            Guid.NewGuid(),
            ProviderName,
            ProviderDataset.FinancialStatements,
            "/test",
            "ext-ref",
            json,
            "checksum-" + Guid.NewGuid().ToString("N"),
            Now);
}
