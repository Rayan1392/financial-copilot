using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Features;
using FinancialCopilot.Domain.Financial.DataQuality;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Domain.Financial.Features;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Features;

public sealed class PersistedFeatureDefinitionRegistry(
    FinancialIngestionDbContext dbContext) : IFeatureDefinitionRegistry
{
    public async Task<FeatureDefinition> ResolveAsync(
        FeatureCode code,
        FeatureVersion version,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.FeatureDefinitions.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.FeatureCode == code.Value && candidate.FeatureVersion == version.Value,
            cancellationToken);
        return row is null
            ? throw new KeyNotFoundException($"Feature definition '{code.Value}' version '{version.Value}' is not registered.")
            : Map(row);
    }

    public async Task RegisterAsync(FeatureDefinition definition, CancellationToken cancellationToken)
    {
        if (definition.RequiredObservationWindow <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(definition),
                "Required observation window must be positive.");
        }

        var row = await dbContext.FeatureDefinitions.SingleOrDefaultAsync(
            candidate => candidate.FeatureCode == definition.Code.Value &&
                candidate.FeatureVersion == definition.Version.Value,
            cancellationToken);
        if (row is null)
        {
            row = new FeatureDefinitionRow { Id = Guid.NewGuid() };
            dbContext.FeatureDefinitions.Add(row);
        }

        row.FeatureCode = definition.Code.Value;
        row.FeatureVersion = definition.Version.Value;
        row.DisplayName = definition.DisplayName;
        row.Description = definition.Description;
        row.PolicyVersion = definition.PolicyVersion.Value;
        row.RequiredObservationWindow = definition.RequiredObservationWindow;
        row.Unit = definition.Output.Unit.ToString();
        row.MinimumValue = definition.Output.MinimumValue;
        row.MaximumValue = definition.Output.MaximumValue;
        row.StrategyKey = definition.Reproducibility.StrategyKey;
        row.AlgorithmVersion = definition.Reproducibility.AlgorithmVersion;
        row.InputSchemaVersion = definition.Reproducibility.InputSchemaVersion;
        row.DependenciesJson = JsonSerializer.Serialize(definition.Dependencies, JsonOptions);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static FeatureDefinition Map(FeatureDefinitionRow row) =>
        new(
            new FeatureCode(row.FeatureCode),
            new FeatureVersion(row.FeatureVersion),
            row.DisplayName,
            row.Description,
            new CalculationPolicyVersion(row.PolicyVersion),
            row.RequiredObservationWindow,
            new FeatureOutputSpecification(
                Enum.Parse<MetricValueUnit>(row.Unit),
                row.MinimumValue,
                row.MaximumValue),
            JsonSerializer.Deserialize<FeatureDependency[]>(row.DependenciesJson, JsonOptions) ?? [],
            new FeatureReproducibilityMetadata(
                row.StrategyKey,
                row.AlgorithmVersion,
                row.InputSchemaVersion));

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

public sealed class PersistedFeatureSnapshotRepository(
    FinancialIngestionDbContext dbContext) : IFeatureSnapshotRepository, IFeatureQueryService
{
    public async Task StoreAsync(FeatureSnapshot snapshot, CancellationToken cancellationToken)
    {
        var start = snapshot.Period.StartDate ??
            throw new ArgumentException("Persisted feature snapshots require a closed period.", nameof(snapshot));
        var end = snapshot.Period.EndDate ??
            throw new ArgumentException("Persisted feature snapshots require a closed period.", nameof(snapshot));
        var row = await dbContext.FeatureSnapshots.SingleOrDefaultAsync(
            candidate => candidate.ExternalCompanyId == snapshot.ExternalCompanyId &&
                candidate.FeatureCode == snapshot.Feature.Code.Value &&
                candidate.FeatureVersion == snapshot.Feature.Version.Value &&
                candidate.PolicyVersion == snapshot.Feature.PolicyVersion.Value &&
                candidate.PeriodEnd == end &&
                candidate.InputFingerprint == snapshot.InputFingerprint,
            cancellationToken);

        if (row is null)
        {
            row = new FeatureSnapshotRow
            {
                Id = snapshot.Id,
                ExternalCompanyId = snapshot.ExternalCompanyId,
                FeatureCode = snapshot.Feature.Code.Value,
                FeatureVersion = snapshot.Feature.Version.Value,
                PolicyVersion = snapshot.Feature.PolicyVersion.Value,
                PeriodEnd = end,
                InputFingerprint = snapshot.InputFingerprint
            };
            dbContext.FeatureSnapshots.Add(row);
        }

        row.PeriodType = snapshot.Period.Type.ToString();
        row.PeriodStart = start;
        row.Value = snapshot.Value;
        row.Unit = snapshot.Unit.ToString();
        row.ObservedAt = snapshot.Quality.ObservedAt;
        row.LastSynchronizedAt = snapshot.Quality.LastSynchronizedAt;
        row.WarningsJson = JsonSerializer.Serialize(snapshot.Quality.Warnings, JsonOptions);
        row.SourceEvidenceJson = JsonSerializer.Serialize(snapshot.SourceEvidence, JsonOptions);
        row.DependencyEvidenceJson = JsonSerializer.Serialize(snapshot.DependencyEvidence, JsonOptions);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<FeatureSnapshot>> QueryAsync(
        FeatureSnapshotQuery query,
        CancellationToken cancellationToken)
    {
        var codes = query.FeatureCodes.Select(code => code.Value).ToArray();
        var rows = dbContext.FeatureSnapshots.AsNoTracking()
            .Where(row => codes.Contains(row.FeatureCode));
        if (query.ExternalCompanyIds is { Count: > 0 })
        {
            rows = rows.Where(row => query.ExternalCompanyIds.Contains(row.ExternalCompanyId));
        }
        if (query.AsOfDate is { } asOf)
        {
            rows = rows.Where(row => row.PeriodEnd <= asOf);
        }

        return (await rows.OrderByDescending(row => row.PeriodEnd).ToListAsync(cancellationToken))
            .Select(Map)
            .ToArray();
    }

    private static FeatureSnapshot Map(FeatureSnapshotRow row) =>
        new(
            row.Id,
            row.ExternalCompanyId,
            new DerivedFeature(
                new FeatureCode(row.FeatureCode),
                new FeatureVersion(row.FeatureVersion),
                new CalculationPolicyVersion(row.PolicyVersion)),
            FiscalPeriod.Closed(
                Enum.Parse<FiscalPeriodType>(row.PeriodType),
                row.PeriodStart,
                row.PeriodEnd),
            row.Value,
            Enum.Parse<MetricValueUnit>(row.Unit),
            new FinancialObservationQuality(
                row.ObservedAt,
                row.LastSynchronizedAt,
                JsonSerializer.Deserialize<FinancialDataWarning[]>(row.WarningsJson, JsonOptions) ?? []),
            JsonSerializer.Deserialize<FinancialSourceEvidence[]>(row.SourceEvidenceJson, JsonOptions) ?? [],
            JsonSerializer.Deserialize<FeatureDependencyEvidence[]>(row.DependencyEvidenceJson, JsonOptions) ?? [],
            row.InputFingerprint);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

public sealed class PersistedFeatureComputationJobRepository(
    FinancialIngestionDbContext dbContext) : IFeatureComputationJobRepository
{
    public async Task<FeatureComputationJob?> GetByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.FeatureComputationJobs.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.IdempotencyKey == idempotencyKey,
            cancellationToken);
        return row is null ? null : Map(row);
    }

    public async Task StoreAsync(FeatureComputationJob job, CancellationToken cancellationToken)
    {
        var start = job.Period.StartDate ??
            throw new ArgumentException("Feature jobs require a closed period.", nameof(job));
        var end = job.Period.EndDate ??
            throw new ArgumentException("Feature jobs require a closed period.", nameof(job));
        var row = await dbContext.FeatureComputationJobs.SingleOrDefaultAsync(
            candidate => candidate.IdempotencyKey == job.IdempotencyKey,
            cancellationToken);
        if (row is null)
        {
            row = new FeatureComputationJobRow { Id = job.Id };
            dbContext.FeatureComputationJobs.Add(row);
        }

        row.FeatureCode = job.FeatureCode.Value;
        row.FeatureVersion = job.FeatureVersion.Value;
        row.ExternalCompanyId = job.ExternalCompanyId;
        row.PeriodType = job.Period.Type.ToString();
        row.PeriodStart = start;
        row.PeriodEnd = end;
        row.IdempotencyKey = job.IdempotencyKey;
        row.Status = job.Status.ToString();
        row.RequestedAt = job.RequestedAt;
        row.StartedAt = job.StartedAt;
        row.CompletedAt = job.CompletedAt;
        row.ErrorMessage = job.ErrorMessage;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static FeatureComputationJob Map(FeatureComputationJobRow row) =>
        new(
            row.Id,
            new FeatureCode(row.FeatureCode),
            new FeatureVersion(row.FeatureVersion),
            row.ExternalCompanyId,
            FiscalPeriod.Closed(Enum.Parse<FiscalPeriodType>(row.PeriodType), row.PeriodStart, row.PeriodEnd),
            row.IdempotencyKey,
            Enum.Parse<FeatureComputationStatus>(row.Status),
            row.RequestedAt,
            row.StartedAt,
            row.CompletedAt,
            row.ErrorMessage);
}
