using System.Text.RegularExpressions;
using FinancialCopilot.Domain.Financial.DataQuality;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;

namespace FinancialCopilot.Domain.Financial.Features;

public sealed record FeatureCode
{
    private static readonly Regex CanonicalPattern = new(
        "^[A-Z][A-Z0-9]*(?:_[A-Z0-9]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public FeatureCode(string value)
    {
        var canonical = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(canonical) || !CanonicalPattern.IsMatch(canonical))
        {
            throw new ArgumentException("Feature code must be a canonical uppercase identifier.", nameof(value));
        }

        Value = canonical;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record FeatureVersion
{
    public FeatureVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Feature version is required.", nameof(value));
        }

        Value = value.Trim();
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public enum FeatureDependencyKind
{
    Metric,
    Feature
}

public sealed record FeatureDependency(
    FeatureDependencyKind Kind,
    string Code,
    string RequiredVersion,
    bool Required = true)
{
    public static FeatureDependency Metric(MetricCode code, MetricVersion version, bool required = true) =>
        new(FeatureDependencyKind.Metric, code.Value, version.Value, required);

    public static FeatureDependency Feature(FeatureCode code, FeatureVersion version, bool required = true) =>
        new(FeatureDependencyKind.Feature, code.Value, version.Value, required);
}

public sealed record FeatureOutputSpecification(
    MetricValueUnit Unit,
    decimal? MinimumValue = null,
    decimal? MaximumValue = null);

public sealed record FeatureReproducibilityMetadata(
    string StrategyKey,
    string AlgorithmVersion,
    string InputSchemaVersion);

public sealed record FeatureDefinition(
    FeatureCode Code,
    FeatureVersion Version,
    string DisplayName,
    string Description,
    CalculationPolicyVersion PolicyVersion,
    int RequiredObservationWindow,
    FeatureOutputSpecification Output,
    IReadOnlyCollection<FeatureDependency> Dependencies,
    FeatureReproducibilityMetadata Reproducibility);

public sealed record FeatureDependencyEvidence(
    FeatureDependencyKind Kind,
    string Code,
    string Version,
    CalculationPolicyVersion? PolicyVersion);

public sealed class DerivedFeature
{
    public DerivedFeature(
        FeatureCode code,
        FeatureVersion version,
        CalculationPolicyVersion policyVersion)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        Version = version ?? throw new ArgumentNullException(nameof(version));
        PolicyVersion = policyVersion ?? throw new ArgumentNullException(nameof(policyVersion));
    }

    public FeatureCode Code { get; }

    public FeatureVersion Version { get; }

    public CalculationPolicyVersion PolicyVersion { get; }
}

public sealed class FeatureSnapshot
{
    public FeatureSnapshot(
        Guid id,
        Guid symbolId,
        DerivedFeature feature,
        FiscalPeriod period,
        decimal? value,
        MetricValueUnit unit,
        FinancialObservationQuality quality,
        IReadOnlyCollection<FinancialSourceEvidence> sourceEvidence,
        IReadOnlyCollection<FeatureDependencyEvidence> dependencyEvidence,
        string inputFingerprint)
    {
        if (id == Guid.Empty || symbolId == Guid.Empty)
        {
            throw new ArgumentException("Feature snapshot and symbol identifiers are required.");
        }

        if (string.IsNullOrWhiteSpace(inputFingerprint))
        {
            throw new ArgumentException("An input fingerprint is required for reproducible feature evidence.");
        }

        Id = id;
        SymbolId = symbolId;
        Feature = feature ?? throw new ArgumentNullException(nameof(feature));
        Period = period;
        Value = value;
        Unit = unit;
        Quality = quality ?? throw new ArgumentNullException(nameof(quality));
        SourceEvidence = sourceEvidence ?? throw new ArgumentNullException(nameof(sourceEvidence));
        DependencyEvidence = dependencyEvidence ?? throw new ArgumentNullException(nameof(dependencyEvidence));
        InputFingerprint = inputFingerprint.Trim();
    }

    public Guid Id { get; }

    public Guid SymbolId { get; }

    public DerivedFeature Feature { get; }

    public FiscalPeriod Period { get; }

    public decimal? Value { get; }

    public MetricValueUnit Unit { get; }

    public FinancialObservationQuality Quality { get; }

    public IReadOnlyCollection<FinancialSourceEvidence> SourceEvidence { get; }

    public IReadOnlyCollection<FeatureDependencyEvidence> DependencyEvidence { get; }

    public string InputFingerprint { get; }
}

public enum FeatureComputationStatus
{
    Requested,
    Running,
    Completed,
    Failed
}

public sealed record FeatureComputationJob(
    Guid Id,
    FeatureCode FeatureCode,
    FeatureVersion FeatureVersion,
    Guid? SymbolId,
    FiscalPeriod Period,
    string IdempotencyKey,
    FeatureComputationStatus Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? CompletedAt = null,
    string? ErrorMessage = null);

public static class FutureFeatureCodes
{
    public static readonly IReadOnlyCollection<FeatureCode> SupportedCandidates =
    [
        new("MOMENTUM_SCORE"),
        new("EARNINGS_QUALITY_SCORE"),
        new("RELATIVE_STRENGTH"),
        new("VOLATILITY_SCORE"),
        new("LIQUIDITY_SCORE"),
        new("GROWTH_CONSISTENCY"),
        new("SMART_MONEY_SIGNAL")
    ];
}
