using FinancialCopilot.Domain.Financial.Features;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;

namespace FinancialCopilot.Application.FinancialData.Features;

public sealed record FeatureInputObservation(
    FeatureDependency Dependency,
    decimal? Value,
    FiscalPeriod Period,
    string EvidenceFingerprint);

public sealed record FeatureCalculationContext(
    Guid SymbolId,
    FeatureDefinition Definition,
    FiscalPeriod Period,
    IReadOnlyCollection<FeatureInputObservation> Inputs);

public sealed record FeatureSnapshotQuery(
    IReadOnlyCollection<FeatureCode> FeatureCodes,
    IReadOnlyCollection<Guid>? SymbolIds = null,
    DateOnly? AsOfDate = null);

public interface IFeatureDefinitionRegistry
{
    Task<FeatureDefinition> ResolveAsync(
        FeatureCode code,
        FeatureVersion version,
        CancellationToken cancellationToken);

    Task RegisterAsync(FeatureDefinition definition, CancellationToken cancellationToken);
}

public interface IDerivedFeatureCalculator
{
    FeatureCode FeatureCode { get; }

    Task<FeatureSnapshot> CalculateAsync(
        FeatureCalculationContext context,
        CancellationToken cancellationToken);
}

public interface IFeatureSnapshotRepository
{
    Task StoreAsync(FeatureSnapshot snapshot, CancellationToken cancellationToken);
}

public interface IFeatureQueryService
{
    Task<IReadOnlyCollection<FeatureSnapshot>> QueryAsync(
        FeatureSnapshotQuery query,
        CancellationToken cancellationToken);
}

public sealed record CalculateDerivedFeatureCommand(
    Guid SymbolId,
    FeatureCode FeatureCode,
    FeatureVersion FeatureVersion,
    FiscalPeriod Period,
    IReadOnlyCollection<FeatureInputObservation> Inputs);

public interface IDerivedFeatureCalculationService
{
    Task<FeatureSnapshot> CalculateAsync(
        CalculateDerivedFeatureCommand command,
        CancellationToken cancellationToken);
}

public sealed class DerivedFeatureCalculationService(
    IFeatureDefinitionRegistry definitionRegistry,
    IEnumerable<IDerivedFeatureCalculator> calculators,
    IFeatureSnapshotRepository snapshotRepository) : IDerivedFeatureCalculationService
{
    private readonly IReadOnlyDictionary<FeatureCode, IDerivedFeatureCalculator> _calculators =
        calculators.ToDictionary(calculator => calculator.FeatureCode);

    public async Task<FeatureSnapshot> CalculateAsync(
        CalculateDerivedFeatureCommand command,
        CancellationToken cancellationToken)
    {
        var definition = await definitionRegistry.ResolveAsync(
            command.FeatureCode,
            command.FeatureVersion,
            cancellationToken);

        if (!_calculators.TryGetValue(command.FeatureCode, out var calculator))
        {
            throw new KeyNotFoundException(
                $"Feature calculator '{command.FeatureCode.Value}' is not registered.");
        }

        ValidateInputs(definition, command.Inputs);
        var snapshot = await calculator.CalculateAsync(
            new FeatureCalculationContext(command.SymbolId, definition, command.Period, command.Inputs),
            cancellationToken);
        ValidateSnapshot(command, definition, snapshot);
        await snapshotRepository.StoreAsync(snapshot, cancellationToken);
        return snapshot;
    }

    private static void ValidateInputs(
        FeatureDefinition definition,
        IReadOnlyCollection<FeatureInputObservation> inputs)
    {
        foreach (var required in definition.Dependencies.Where(dependency => dependency.Required))
        {
            if (!inputs.Any(input =>
                    input.Dependency.Kind == required.Kind &&
                    input.Dependency.Code == required.Code &&
                    input.Dependency.RequiredVersion == required.RequiredVersion))
            {
                throw new InvalidOperationException(
                    $"Required feature input '{required.Code}' version '{required.RequiredVersion}' is missing.");
            }
        }
    }

    private static void ValidateSnapshot(
        CalculateDerivedFeatureCommand command,
        FeatureDefinition definition,
        FeatureSnapshot snapshot)
    {
        if (snapshot.SymbolId != command.SymbolId ||
            snapshot.Feature.Code != definition.Code ||
            snapshot.Feature.Version != definition.Version ||
            snapshot.Feature.PolicyVersion != definition.PolicyVersion ||
            snapshot.Period != command.Period)
        {
            throw new InvalidOperationException(
                "A feature calculator returned evidence outside its requested definition, symbol, policy, or period.");
        }
    }
}

public sealed record FeatureComputationPeriod(
    FiscalPeriodType Type,
    DateOnly StartDate,
    DateOnly EndDate)
{
    public FiscalPeriod ToFiscalPeriod() => FiscalPeriod.Closed(Type, StartDate, EndDate);

    public static FeatureComputationPeriod From(FiscalPeriod period) =>
        new(
            period.Type,
            period.StartDate ?? throw new ArgumentException("Feature computation requires a closed period.", nameof(period)),
            period.EndDate ?? throw new ArgumentException("Feature computation requires a closed period.", nameof(period)));
}

public sealed record FeatureRecalculationRequested(
    Guid JobId,
    FeatureCode FeatureCode,
    FeatureVersion FeatureVersion,
    Guid? SymbolId,
    FeatureComputationPeriod Period,
    string IdempotencyKey,
    DateTimeOffset RequestedAt);

public sealed record FeatureRecalculationCompleted(
    Guid JobId,
    string IdempotencyKey,
    Guid SnapshotId,
    DateTimeOffset CompletedAt);

public sealed record FeatureRecalculationFailed(
    Guid JobId,
    string IdempotencyKey,
    string ErrorMessage,
    DateTimeOffset FailedAt);

public interface IFeatureRecalculationPublisher
{
    Task PublishRequestedAsync(FeatureRecalculationRequested request, CancellationToken cancellationToken);

    Task PublishCompletedAsync(FeatureRecalculationCompleted notification, CancellationToken cancellationToken);

    Task PublishFailedAsync(FeatureRecalculationFailed notification, CancellationToken cancellationToken);
}

public interface IFeatureRecalculationScheduler
{
    Task<FeatureComputationJob> ScheduleAsync(
        FeatureRecalculationRequested request,
        CancellationToken cancellationToken);
}

public interface IFeatureRecalculationConsumer
{
    Task ConsumeAsync(
        Func<FeatureRecalculationRequested, CancellationToken, Task> handler,
        CancellationToken cancellationToken);
}

public interface IFeatureComputationJobRepository
{
    Task<FeatureComputationJob?> GetByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task StoreAsync(FeatureComputationJob job, CancellationToken cancellationToken);
}

public interface IFeatureComputationProcessor
{
    Task ProcessAsync(FeatureRecalculationRequested request, CancellationToken cancellationToken);
}

public interface IFeatureInputReader
{
    Task<IReadOnlyCollection<FeatureInputObservation>> LoadAsync(
        FeatureDefinition definition,
        Guid symbolId,
        FiscalPeriod period,
        CancellationToken cancellationToken);
}
