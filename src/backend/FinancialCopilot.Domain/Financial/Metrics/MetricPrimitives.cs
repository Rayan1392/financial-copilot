using System.Text.RegularExpressions;
using FinancialCopilot.Domain.Financial.Periods;

namespace FinancialCopilot.Domain.Financial.Metrics;

public sealed record MetricCode
{
    private static readonly Regex CanonicalPattern = new(
        "^[A-Z][A-Z0-9]*(?:_[A-Z0-9]+)*$",
        RegexOptions.CultureInvariant);

    public MetricCode(string value)
    {
        var canonical = value?.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(canonical) || !CanonicalPattern.IsMatch(canonical))
        {
            throw new ArgumentException(
                "Metric code must contain canonical uppercase underscore-separated tokens.",
                nameof(value));
        }

        Value = canonical;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record MetricVersion
{
    public MetricVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Metric version is required.", nameof(value));
        }

        Value = value.Trim();
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record CalculationPolicyVersion
{
    public CalculationPolicyVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Calculation policy version is required.", nameof(value));
        }

        Value = value.Trim();
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public enum MetricValueUnit
{
    Amount,
    Count,
    Percentage,
    Ratio
}

public enum MissingDataPolicy
{
    RejectCalculation,
    ReturnMissingValue,
    UseDocumentedFallback
}

public sealed record MetricDataRequirement(
    MetricCode MetricCode,
    FiscalPeriodType PeriodType,
    bool Required);

public sealed record MetricCalculationPolicy(
    MetricCode MetricCode,
    CalculationPolicyVersion Version,
    MetricValueUnit Unit,
    GrowthComparison? Comparison,
    MissingDataPolicy MissingDataPolicy,
    IReadOnlyCollection<MetricDataRequirement> Requirements);

public sealed record MetricIdentity(
    MetricCode Code,
    MetricVersion Version,
    MetricValueUnit Unit,
    IReadOnlyCollection<FiscalPeriodType> SupportedPeriodTypes);

public interface IMetricIdentityRegistry
{
    MetricIdentity Resolve(MetricCode code);

    IReadOnlyCollection<MetricIdentity> GetRegisteredMetrics();
}

public sealed class MetricIdentityRegistry : IMetricIdentityRegistry
{
    private readonly IReadOnlyDictionary<MetricCode, MetricIdentity> _metrics;

    public MetricIdentityRegistry(IEnumerable<MetricIdentity> metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        _metrics = metrics.ToDictionary(metric => metric.Code);
    }

    public MetricIdentity Resolve(MetricCode code) =>
        _metrics.TryGetValue(code, out var metric)
            ? metric
            : throw new KeyNotFoundException($"Metric code '{code}' is not registered.");

    public IReadOnlyCollection<MetricIdentity> GetRegisteredMetrics() => _metrics.Values.ToArray();
}
