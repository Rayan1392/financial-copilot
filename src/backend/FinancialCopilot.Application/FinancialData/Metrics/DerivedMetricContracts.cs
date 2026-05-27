using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;

namespace FinancialCopilot.Application.FinancialData.Metrics;

public sealed record CalculateDerivedMetricCommand(
    Guid SymbolId,
    MetricCode MetricCode,
    CalculationPolicyVersion CalculationPolicyVersion,
    FiscalPeriod EffectivePeriod,
    IReadOnlyCollection<MetricInputObservation> Inputs);

public interface IDerivedMetricResultStore
{
    Task StoreAsync(DerivedMetric metric, CancellationToken cancellationToken);
}

public interface INormalizedMetricInputSource
{
    MetricCode MetricCode { get; }

    Task<IReadOnlyCollection<MetricInputObservation>> LoadAsync(
        string externalCompanyId,
        CancellationToken cancellationToken);
}

public interface INormalizedMetricInputReader
{
    Task<IReadOnlyCollection<MetricInputObservation>> LoadAsync(
        string externalCompanyId,
        MetricCode metricCode,
        CancellationToken cancellationToken);
}

public interface IDerivedMetricCalculationService
{
    Task<DerivedMetric> CalculateAsync(
        CalculateDerivedMetricCommand command,
        CancellationToken cancellationToken);
}

public interface IDerivedMetricRecalculationCommand
{
    Task<IReadOnlyCollection<DerivedMetric>> ExecuteAsync(
        IReadOnlyCollection<CalculateDerivedMetricCommand> commands,
        CancellationToken cancellationToken);
}

public sealed class DerivedMetricCalculationService(
    IFinancialMetricRegistry metricRegistry,
    IMetricCalculationPolicyProvider policyProvider,
    IDerivedMetricResultStore resultStore) : IDerivedMetricCalculationService
{
    public async Task<DerivedMetric> CalculateAsync(
        CalculateDerivedMetricCommand command,
        CancellationToken cancellationToken)
    {
        var asOf = command.EffectivePeriod.EndDate ??
            throw new ArgumentException("Derived metric calculation requires a closed period.", nameof(command));
        var definition = metricRegistry.ResolveDefinition(command.MetricCode, asOf);
        var policy = policyProvider.GetPolicy(command.MetricCode, command.CalculationPolicyVersion);
        var result = await metricRegistry.ResolveCalculator(command.MetricCode)
            .CalculateAsync(
                new MetricCalculationContext(
                    command.SymbolId,
                    definition,
                    policy,
                    command.EffectivePeriod,
                    command.Inputs),
                cancellationToken);
        var metric = new DerivedMetric(
            Guid.NewGuid(),
            command.SymbolId,
            result.MetricCode,
            result.DefinitionVersion,
            result.CalculationPolicyVersion,
            result.EffectivePeriod,
            result.Value,
            policy.Unit,
            result.Quality,
            result.SourceEvidence,
            result.Dependencies.Select(dependency => new DerivedMetricDependencyEvidence(
                dependency.Code,
                dependency.DefinitionVersion,
                dependency.CalculationPolicyVersion)).ToArray());

        await resultStore.StoreAsync(metric, cancellationToken);
        return metric;
    }
}

public sealed class DerivedMetricRecalculationCommand(
    IDerivedMetricCalculationService calculationService) : IDerivedMetricRecalculationCommand
{
    public async Task<IReadOnlyCollection<DerivedMetric>> ExecuteAsync(
        IReadOnlyCollection<CalculateDerivedMetricCommand> commands,
        CancellationToken cancellationToken)
    {
        var results = new List<DerivedMetric>(commands.Count);

        foreach (var command in commands)
        {
            results.Add(await calculationService.CalculateAsync(command, cancellationToken));
        }

        return results;
    }
}
