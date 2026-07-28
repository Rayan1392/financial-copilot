using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Domain.Financial.Periods;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Spec 029: verifies the new <c>(ProviderName, ExternalStatementId, StatementType)</c> unique
/// key shape — income and balance rows can share the same <c>ExternalStatementId</c> while a
/// duplicate triple is rejected.
/// </summary>
public sealed class FinancialStatementSchemaTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-31T09:00:00Z");

    [Fact]
    public async Task IncomeAndBalance_SharingExternalStatementId_AreBothPersisted()
    {
        await using var db = NewDb();

        db.FinancialStatements.AddRange(
            MakeStatement("CodalDb", "12345", FinancialStatementType.IncomeStatement),
            MakeStatement("CodalDb", "12345", FinancialStatementType.BalanceSheet));
        await db.SaveChangesAsync();

        var rows = await db.FinancialStatements.ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.StatementType == "IncomeStatement");
        Assert.Contains(rows, r => r.StatementType == "BalanceSheet");
    }

    [Fact]
    public void UniqueIndex_IsConfiguredOnTheNewTriple()
    {
        // EF in-memory does not enforce unique-index violations at SaveChanges time, so we verify
        // the relational metadata directly. The configured unique index becomes a PostgreSQL
        // UNIQUE constraint at migration time, which is what actually rejects duplicates in
        // production.
        using var db = NewDb();

        var entityType = db.Model.FindEntityType(typeof(NormalizedFinancialStatementRow))!;
        var uniqueIndexes = entityType.GetIndexes().Where(i => i.IsUnique).ToList();

        Assert.Contains(uniqueIndexes, index =>
            index.Properties.Select(p => p.Name).SequenceEqual(
                new[] { nameof(NormalizedFinancialStatementRow.ProviderName),
                        nameof(NormalizedFinancialStatementRow.ExternalStatementId),
                        nameof(NormalizedFinancialStatementRow.StatementType) }));

        // The OLD two-column unique index must be gone.
        Assert.DoesNotContain(uniqueIndexes, index =>
            index.Properties.Count == 2 &&
            index.Properties[0].Name == nameof(NormalizedFinancialStatementRow.ProviderName) &&
            index.Properties[1].Name == nameof(NormalizedFinancialStatementRow.ExternalStatementId));
    }

    [Fact]
    public async Task DifferentProviders_SameExternalStatementId_PermittedSideBySide()
    {
        // (ProviderName, ExternalStatementId, StatementType) — different ProviderName means
        // different uniqueness group, so two providers can each report income statement "100".
        await using var db = NewDb();

        db.FinancialStatements.AddRange(
            MakeStatement("CodalDb",       "100", FinancialStatementType.IncomeStatement),
            MakeStatement("CyclicalWaves", "100", FinancialStatementType.IncomeStatement));
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.FinancialStatements.CountAsync());
    }

    private static NormalizedFinancialStatementRow MakeStatement(
        string providerName,
        string externalStatementId,
        FinancialStatementType statementType) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProviderName = providerName,
            ExternalCompanyId = "co-1",
            ExternalStatementId = externalStatementId,
            StatementType = statementType.ToString(),
            PeriodType = nameof(FiscalPeriodType.ThreeMonths),
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 3, 31),
            SourcePayloadChecksum = "x",
            LastSynchronizedAt = Now
        };

    private static FinancialIngestionDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
