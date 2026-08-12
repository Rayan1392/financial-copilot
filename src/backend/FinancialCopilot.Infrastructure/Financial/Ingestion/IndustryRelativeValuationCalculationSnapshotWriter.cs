using System.Text.Json;
using System.Collections.Concurrent;
using FinancialCopilot.Domain.Financial.RelativeValuation;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

public sealed record IndustryRelativeValuationSnapshotWriteResult(
    Guid CalculationId,
    int CalculationVersion,
    string Status,
    bool NoOp);

/// <summary>Atomically writes one calculation version and its complete source evidence.</summary>
public sealed class IndustryRelativeValuationCalculationSnapshotWriter(
    FinancialIngestionDbContext db, IndustryWatchEvaluationService? watchEvaluationService = null, ILogger<IndustryRelativeValuationCalculationSnapshotWriter>? logger = null)
{
    private const string RankVersion = "rank-v1";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> NonRelationalLocks = new();

    public async Task<IndustryRelativeValuationSnapshotWriteResult> WriteAsync(
        DateOnly calculationDate,
        IndustryRelativeValuationCalculationInput input,
        DateTimeOffset calculatedAtUtc,
        CancellationToken cancellationToken)
    {
        var lockKey = $"industry-relative-valuation:{input.IndustryId:D}:{calculationDate:yyyy-MM-dd}";
        SemaphoreSlim? testLock = null;
        if (!db.Database.IsRelational())
        {
            testLock = NonRelationalLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
            await testLock.WaitAsync(cancellationToken);
        }

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            // PostgreSQL transaction-scoped advisory locking serializes allocation for the
            // calculation identity. This is deliberately acquired before any version read.
            if (db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);

            var existing = await db.IndustryRelativeValuationCalculations
                .SingleOrDefaultAsync(row => row.CalculationDate == calculationDate &&
                                             row.IndustryId == input.IndustryId &&
                                             row.SourceBarrierHash == input.SourceBarrier.SourceBarrierHash,
                    cancellationToken);
            if (existing is not null)
            {
                logger?.LogInformation("Feature 125 calculation retry is idempotent for industry {IndustryId}, date {CalculationDate}, calculation {CalculationId}, version {Version}.", input.IndustryId, calculationDate, existing.Id, existing.CalculationVersion);
                return new(existing.Id, existing.CalculationVersion, existing.Status, true);
            }

            var benchmarkReady = input.SourceBarrier.IsComplete && input.Result.Benchmarks.All(x => x.IsAvailable);
            var status = benchmarkReady ? "Published" : "Inconclusive";
            var latest = await db.IndustryRelativeValuationCalculations
                .SingleOrDefaultAsync(row => row.CalculationDate == calculationDate &&
                                             row.IndustryId == input.IndustryId &&
                                             row.IsLatestEvaluation,
                    cancellationToken);
            var nextVersion = await db.IndustryRelativeValuationCalculations
                .Where(row => row.CalculationDate == calculationDate && row.IndustryId == input.IndustryId)
                .Select(row => (int?)row.CalculationVersion)
                .MaxAsync(cancellationToken) ?? 0;

            if (latest is not null) latest.IsLatestEvaluation = false;
            var calculation = new IndustryRelativeValuationCalculationRow
            {
                Id = Guid.NewGuid(),
                CalculationDate = calculationDate,
                IndustryId = input.IndustryId,
                IndustryExternalId = input.IndustryExternalId,
                IndustryTitleSnapshot = input.IndustryTitle,
                CalculationVersion = nextVersion + 1,
                Status = status,
                AlgorithmVersion = IndustryRelativeValuationEngine.AlgorithmVersion,
                MembershipHash = MembershipHash(input.Members),
                SourceBarrierHash = input.SourceBarrier.SourceBarrierHash,
                SourceBarrierEvidenceJson = JsonSerializer.Serialize(input.SourceBarrier.Selections.Select(selection => new
                {
                    selection.CompanyId,
                    selection.Metric,
                    selection.SourceFactId,
                    selection.SourceVersion,
                    selection.SourceObservationId,
                    selection.SourceObservationTimestamp,
                    selection.PersistedAtUtc,
                    selection.SourceWatermark
                })),
                CalculatedAtUtc = calculatedAtUtc,
                PublishedAtUtc = status == "Published" ? calculatedAtUtc : null,
                IsLatestEvaluation = true,
                IsSelectedCurrent = status == "Published"
            };

            // Only a successful Published commit may replace the published pointer.
            if (calculation.IsSelectedCurrent)
            {
                var published = await db.IndustryRelativeValuationCalculations
                    .SingleOrDefaultAsync(row => row.CalculationDate == calculationDate &&
                                                 row.IndustryId == input.IndustryId && row.IsSelectedCurrent,
                        cancellationToken);
                if (published is not null) published.IsSelectedCurrent = false;
            }

            db.IndustryRelativeValuationCalculations.Add(calculation);

        foreach (var benchmark in input.Result.Benchmarks)
            db.IndustryRelativeValuationMetrics.Add(new()
            {
                Id = Guid.NewGuid(), CalculationId = calculation.Id, MetricKind = benchmark.Metric.ToString(),
                ValidCount = benchmark.CandidateCount, OutlierCount = benchmark.OutlierCount,
                CleanCount = benchmark.CleanCount, Quartile1 = benchmark.Q1, Quartile3 = benchmark.Q3,
                LowerBound = benchmark.LowerBound, UpperBound = benchmark.UpperBound,
                CleanAverage = benchmark.CleanAverage, Readiness = benchmark.IsAvailable ? "Ready" : "Inconclusive",
                Reason = benchmark.Reason
            });

        var selections = input.SourceBarrier.Selections.ToDictionary(x => (x.CompanyId, x.Metric));
        foreach (var company in input.Result.Companies)
        {
            var row = new CompanyIndustryRelativeValuationRow
            {
                Id = Guid.NewGuid(), CalculationId = calculation.Id, CompanyId = company.CompanyId,
                PositiveMetricCount = company.PositiveMetricCount, ValidMetricCount = company.ValidMetricCount,
                GlobalRank = company.GlobalRank, RankVersion = RankVersion
            };
            foreach (var metric in company.Metrics)
            {
                selections.TryGetValue((company.CompanyId, metric.Metric), out var selection);
                var source = selection?.Fact;
                ApplySourceEvidence(row, metric.Metric, selection);
                ApplyMetric(row, metric, source);
            }
            db.CompanyIndustryRelativeValuations.Add(row);
        }

            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            if (status == "Published")
                await (watchEvaluationService ?? new IndustryWatchEvaluationService(db, new()))
                    .EvaluateAsync(input.IndustryId, calculation.Id, "Daily", calculatedAtUtc, cancellationToken);
            logger?.LogInformation("Feature 125 calculation completed for industry {IndustryId}, date {CalculationDate}, calculation {CalculationId}, version {Version}, status {Status}, members {Members}, barrier complete {BarrierComplete}.", input.IndustryId, calculationDate, calculation.Id, calculation.CalculationVersion, status, input.Result.Companies.Count, input.SourceBarrier.IsComplete);
            return new(calculation.Id, calculation.CalculationVersion, status, false);
        }
        finally
        {
            testLock?.Release();
        }
    }

    private static void ApplySourceEvidence(
        CompanyIndustryRelativeValuationRow row,
        RelativeValuationMetric metric,
        RelativeValuationSourceSelection? selection)
    {
        var id = selection?.SourceObservationId ?? string.Empty;
        var factId = selection?.SourceFactId;
        var version = selection?.SourceVersion ?? string.Empty;
        var observationTimestamp = selection?.SourceObservationTimestamp;
        var persistedAt = selection?.PersistedAtUtc;
        var watermark = selection?.SourceWatermark ?? string.Empty;
        switch (metric)
        {
            case RelativeValuationMetric.Pe:
                row.PeSourceObservationId = id; row.PeSourceFactId = factId; row.PeSourceVersion = version;
                row.PeSourceObservationTimestamp = observationTimestamp; row.PePersistedAtUtc = persistedAt; row.PeSourceWatermark = watermark; break;
            case RelativeValuationMetric.Ps:
                row.PsSourceObservationId = id; row.PsSourceFactId = factId; row.PsSourceVersion = version;
                row.PsSourceObservationTimestamp = observationTimestamp; row.PsPersistedAtUtc = persistedAt; row.PsSourceWatermark = watermark; break;
            case RelativeValuationMetric.Equilibrium:
                row.EquilibriumSourceObservationId = id; row.EquilibriumSourceFactId = factId; row.EquilibriumSourceVersion = version;
                row.EquilibriumSourceObservationTimestamp = observationTimestamp; row.EquilibriumPersistedAtUtc = persistedAt; row.EquilibriumSourceWatermark = watermark; break;
        }
    }

    private static void ApplyMetric(
        CompanyIndustryRelativeValuationRow row,
        CompanyRelativeMetric metric,
        RelativeValuationSourceFact? source)
    {
        switch (metric.Metric)
        {
            case RelativeValuationMetric.Pe:
                row.CurrentPE = source?.CurrentValue; row.HistoricalAveragePE = source?.ReferenceValue;
                row.PEPercent = metric.Percent; row.PEIsValid = metric.Quality == RelativeValuationQuality.Valid;
                row.PEIsOutlier = metric.IsOutlier; row.PEClassification = metric.Classification.ToString(); row.PEReason = metric.ExclusionReason ?? metric.Quality.ToString(); break;
            case RelativeValuationMetric.Ps:
                row.CurrentPS = source?.CurrentValue; row.HistoricalAveragePS = source?.ReferenceValue;
                row.PSPercent = metric.Percent; row.PSIsValid = metric.Quality == RelativeValuationQuality.Valid;
                row.PSIsOutlier = metric.IsOutlier; row.PSClassification = metric.Classification.ToString(); row.PSReason = metric.ExclusionReason ?? metric.Quality.ToString(); break;
            case RelativeValuationMetric.Equilibrium:
                row.CurrentMarketPrice = source?.CurrentValue; row.EquilibriumPrice = source?.ReferenceValue;
                row.EquilibriumPercent = metric.Percent; row.EquilibriumIsValid = metric.Quality == RelativeValuationQuality.Valid;
                row.EquilibriumIsOutlier = metric.IsOutlier; row.EquilibriumClassification = metric.Classification.ToString(); row.EquilibriumReason = metric.ExclusionReason ?? metric.Quality.ToString(); break;
        }
    }

    private static string MembershipHash(IReadOnlyList<CanonicalIndustryMember> members)
    {
        var canonical = string.Join("\n", members.OrderBy(x => x.IndustryId).ThenBy(x => x.CompanyId)
            .Select(x => $"{x.IndustryId:D}|{x.IndustryExternalId}|{x.CompanyId:D}"));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
