using System.Globalization;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWavesV2;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// BUG-035 (owner, 2026-11-19, «شسپا را بررسی کن»): in production the analysis search and the context lookup share the
/// request's scoped <see cref="FinancialIngestionDbContext"/>. Run in parallel on PostgreSQL, one of them failed at random
/// and a failed search was answered «تحلیلی … پیدا نشد» with 1 credit. EF InMemory finishes its queries synchronously, so
/// only a real provider shows the hazard.
/// </summary>
[Collection(FinancialStatementValueSearch131PostgreSqlCollection.Name)]
public sealed class Bug035AnalysisSearchSharedContextPostgreSqlTests(FinancialStatementValueSearch131PostgreSqlFixture fixture)
{
    private const string Symbol = "شسپا";

    [SkippableFact]
    public async Task NoRecentAnalysis_WithOneSharedContext_IsAlwaysTheNoRecentAnalysisNoData()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var now = DateTimeOffset.UtcNow;
        await using (var seed = database.CreateContext())
        {
            // Production shape: only an analysis older than the 30-day window exists for the symbol.
            seed.CyclicalWavesV2Analyses.Add(new CyclicalWavesV2AnalysisRow
            {
                Id = 35001, Title = $"{Symbol} - تحلیل قدیمی", Summary = "<p>old</p>", PlainTextSummary = "old",
                PublishedAt = now.AddDays(-400),
                ContentHash = 35001.ToString(CultureInfo.InvariantCulture).PadLeft(64, '0'),
                FirstSyncedAt = now, LastSyncedAt = now, UpdatedAt = now
            });
            await seed.SaveChangesAsync();
        }

        await using var shared = database.CreateContext();
        var analysis = new RecentComprehensiveAnalysisQueryUseCase(
            new CyclicalWavesV2ComprehensiveAnalysisQuery(shared),
            new ComprehensiveAnalysisPresentationOptions(),
            TimeProvider.System);
        var executor = new ComprehensiveAnalysisCapabilityExecutor(
            analysis, new SharedContextLookupService(shared), TimeProvider.System);

        for (var run = 0; run < 10; run++)
        {
            var result = await executor.ExecuteAsync(Frame(), Context(), CancellationToken.None);

            Assert.Equal(CapabilityExecutionStatus.NoData, result.Status);
            Assert.Equal(DialogueOutcomeReasonCodes.ComprehensiveAnalysisNotRecent, result.ReasonCode);
            var payload = Assert.IsType<SemanticComprehensiveAnalysisPayload>(result.Payload);
            Assert.Equal(30, payload.Analysis.RecentWindowDays);
            Assert.NotEmpty(payload.Lookup.Rows);
            Assert.DoesNotContain("live_metrics_failed", result.Warnings!);
        }
    }

    private static ValidatedQueryFrame Frame() => new(
        "comprehensive_analysis", 1,
        [new(QuerySlotType.CompanyOrSymbol, Symbol, QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid)],
        new($"{Symbol} را بررسی کن", $"{Symbol} را بررسی کن", "fa", [], [], [], null, null, null, [], [], 1m, [], 1));

    private static QueryExecutionContext Context() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "test", "fa");

    /// <summary>Reads the same context several times, as the production lookup does (company, quote and metric reads).</summary>
    private sealed class SharedContextLookupService(FinancialIngestionDbContext db) : ISymbolMetricLookupService
    {
        public async Task<SymbolLookupTableResult> LookupAsync(SymbolLookupRequest request, CancellationToken cancellationToken)
        {
            for (var read = 0; read < 5; read++)
                _ = await db.CyclicalWavesV2Analyses.AsNoTracking().CountAsync(cancellationToken);
            var row = new ScannerTableRow(Symbol, Symbol, new Dictionary<string, ScannerTableCell>(), 1d, []);
            return new SymbolLookupTableResult(Guid.NewGuid(), [], [row],
                new(DateTimeOffset.UtcNow, TimeSpan.Zero, 1, 1, false), [], []);
        }
    }
}
