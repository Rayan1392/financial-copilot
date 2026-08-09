namespace FinancialCopilot.Domain.Financial.Metrics;

public interface IFinancialMetricCalculator
{
    MetricCode MetricCode { get; }

    Task<MetricCalculationResult> CalculateAsync(
        MetricCalculationContext context,
        CancellationToken cancellationToken);
}

public interface IFinancialMetricRegistry
{
    FinancialMetricDefinition ResolveDefinition(MetricCode metricCode, DateOnly asOf);

    IFinancialMetricCalculator ResolveCalculator(MetricCode metricCode);

    IReadOnlyCollection<FinancialMetricDefinition> GetSupportedMetrics(DateOnly asOf);
}

public interface IMetricAliasResolver
{
    MetricResolutionResult ResolveAlias(
        string userExpression,
        string language,
        MetricResolutionContext context,
        DateOnly asOf);
}

public interface IMetricCalculationPolicyProvider
{
    MetricCalculationPolicy GetPolicy(
        MetricCode metricCode,
        CalculationPolicyVersion version);

    IReadOnlyCollection<MetricCalculationPolicy> GetPolicies(MetricCode metricCode);
}

public interface IMetricDependencyResolver
{
    IReadOnlyCollection<MetricDependency> ResolveDependencies(
        MetricCode metricCode,
        DateOnly asOf);
}

public sealed class FinancialMetricRegistry : IFinancialMetricRegistry, IMetricDependencyResolver
{
    private readonly IReadOnlyCollection<FinancialMetricDefinition> _definitions;
    private readonly IReadOnlyDictionary<MetricCode, IFinancialMetricCalculator> _calculators;

    public FinancialMetricRegistry(
        IEnumerable<FinancialMetricDefinition> definitions,
        IEnumerable<IFinancialMetricCalculator> calculators)
    {
        _definitions = definitions.ToArray();
        _calculators = calculators.ToDictionary(calculator => calculator.MetricCode);
    }

    public FinancialMetricDefinition ResolveDefinition(MetricCode metricCode, DateOnly asOf) =>
        _definitions
            .Where(definition =>
                definition.Code == metricCode &&
                definition.EffectiveFrom <= asOf &&
                (definition.EffectiveTo is null || definition.EffectiveTo >= asOf))
            .OrderByDescending(definition => definition.EffectiveFrom)
            .FirstOrDefault() ??
        throw new KeyNotFoundException($"No active definition is registered for metric '{metricCode}'.");

    public IFinancialMetricCalculator ResolveCalculator(MetricCode metricCode) =>
        _calculators.TryGetValue(metricCode, out var calculator)
            ? calculator
            : throw new KeyNotFoundException($"No calculator is registered for metric '{metricCode}'.");

    public IReadOnlyCollection<FinancialMetricDefinition> GetSupportedMetrics(DateOnly asOf) =>
        _definitions
            .Where(definition =>
                definition.EffectiveFrom <= asOf &&
                (definition.EffectiveTo is null || definition.EffectiveTo >= asOf))
            .GroupBy(definition => definition.Code)
            .Select(group => group.OrderByDescending(definition => definition.EffectiveFrom).First())
            .OrderBy(definition => definition.Code.Value, StringComparer.Ordinal)
            .ToArray();

    public IReadOnlyCollection<MetricDependency> ResolveDependencies(MetricCode metricCode, DateOnly asOf) =>
        ResolveDefinition(metricCode, asOf).Dependencies;
}

public sealed class MetricAliasResolver(IFinancialMetricRegistry registry) : IMetricAliasResolver
{
    public MetricResolutionResult ResolveAlias(
        string userExpression,
        string language,
        MetricResolutionContext context,
        DateOnly asOf)
    {
        if (string.IsNullOrWhiteSpace(userExpression) || string.IsNullOrWhiteSpace(language))
        {
            throw new ArgumentException("Expression and language are required for metric resolution.");
        }

        var expression = MetricAliasTextNormalizer.Normalize(userExpression);
        var candidates = registry.GetSupportedMetrics(asOf)
            .Where(definition => definition.Aliases.Any(alias =>
                string.Equals(MetricAliasTextNormalizer.Normalize(alias.Expression), expression, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(alias.Language, language, StringComparison.OrdinalIgnoreCase) &&
                (context.Comparison is null || alias.ComparisonQualifier == context.Comparison)))
            .Where(definition =>
                context.PeriodType is null || definition.SupportedPeriodTypes.Contains(context.PeriodType.Value))
            .ToArray();

        return candidates.Length switch
        {
            0 => new MetricResolutionResult(expression, language, MetricResolutionStatus.NotFound, []),
            1 => new MetricResolutionResult(expression, language, MetricResolutionStatus.Resolved, candidates),
            _ => new MetricResolutionResult(
                expression,
                language,
                MetricResolutionStatus.Ambiguous,
                candidates,
                "Select a comparison basis to resolve the requested metric.")
        };
    }
}

/// <summary>
/// Canonicalizes metric aliases before registry matching. This is deliberately
/// limited to text normalization; it does not infer intent or parse formulas.
/// </summary>
public static class MetricAliasTextNormalizer
{
    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim()
            .Replace('\u0643', '\u06A9')
            .Replace('\u064A', '\u06CC')
            .Replace('\u0649', '\u06CC')
            .Replace('\u0626', '\u06CC')
            .Replace('\u0640', '\0')
            .Replace('\u200C', ' ')
            .Replace('\u200D', ' ')
            .Replace('\uFEFF', ' ')
            .Replace('\u066A', '%')
            .Replace('\u066B', '.')
            .Replace(',', '.');

        normalized = normalized.Replace("\0", string.Empty, StringComparison.Ordinal);

        for (var i = 0; i <= 9; i++)
        {
            normalized = normalized
                .Replace((char)('\u06F0' + i), (char)('0' + i))
                .Replace((char)('\u0660' + i), (char)('0' + i));
        }

        while (normalized.Contains("  ", StringComparison.Ordinal))
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);

        return normalized.Trim().ToLowerInvariant();
    }
}

public sealed class MetricCalculationPolicyProvider(
    IEnumerable<MetricCalculationPolicy> policies) : IMetricCalculationPolicyProvider
{
    private readonly IReadOnlyDictionary<(MetricCode Code, CalculationPolicyVersion Version), MetricCalculationPolicy>
        _policies = policies.ToDictionary(policy => (policy.MetricCode, policy.Version));

    public MetricCalculationPolicy GetPolicy(
        MetricCode metricCode,
        CalculationPolicyVersion version) =>
        _policies.TryGetValue((metricCode, version), out var policy)
            ? policy
            : throw new KeyNotFoundException(
                $"Policy '{version}' is not registered for metric '{metricCode}'.");

    public IReadOnlyCollection<MetricCalculationPolicy> GetPolicies(MetricCode metricCode) =>
        _policies.Values
            .Where(policy => policy.MetricCode == metricCode)
            .OrderByDescending(policy => policy.EffectiveFrom)
            .ToArray();
}
