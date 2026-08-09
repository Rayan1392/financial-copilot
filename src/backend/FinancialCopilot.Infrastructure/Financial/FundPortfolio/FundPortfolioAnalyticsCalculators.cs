using System.Security.Cryptography;
using System.Text;
using FinancialCopilot.Application.FinancialData.Features;
using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Domain.Financial.DataQuality;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Domain.Financial.Features;
using FinancialCopilot.Domain.Financial.FundPortfolio;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

public static class FundPortfolioAnalyticsFeatureDefinition
{
    public static FeatureDefinition Current => new(
        new FeatureCode(FundPortfolioAnalyticsCalculationPolicy.FeatureCode),
        new FeatureVersion("v1"),
        "Fund portfolio analytics completeness",
        "Versioned completeness score for persisted fund portfolio analytics inputs.",
        new CalculationPolicyVersion(FundPortfolioAnalyticsCalculationPolicy.CalculationVersion),
        1,
        new FeatureOutputSpecification(MetricValueUnit.Ratio, 0m, 1m),
        [
            FeatureDependency.Metric(new MetricCode("FUND_EQUITY_INPUT"), new MetricVersion("v1"), false),
            FeatureDependency.Metric(new MetricCode("FUND_ALLOCATION_INPUT"), new MetricVersion("v1"), false),
            FeatureDependency.Metric(new MetricCode("FUND_NON_EQUITY_INPUT"), new MetricVersion("v1"), false),
            FeatureDependency.Metric(new MetricCode("FUND_INCOME_INPUT"), new MetricVersion("v1"), false),
            FeatureDependency.Metric(new MetricCode("FUND_MARKET_LIQUIDITY_INPUT"), new MetricVersion("v1"), false),
            FeatureDependency.Metric(new MetricCode("FUND_VALUATION_QUALITY_INPUT"), new MetricVersion("v1"), false)
        ],
        new FeatureReproducibilityMetadata(
            "fund-portfolio-analytics-completeness",
            FundPortfolioAnalyticsCalculationPolicy.CalculationVersion,
            FundPortfolioAnalyticsCalculationPolicy.InputSchemaVersion));
}

public sealed class DeterministicFundPortfolioAnalyticsCalculator : IFundPortfolioAnalyticsCalculator
{
    public string CalculationVersion => FundPortfolioAnalyticsCalculationPolicy.CalculationVersion;

    public Task<FundPortfolioAnalyticsResult> CalculateAsync(
        FundPortfolioAnalyticsCalculationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(context.Snapshot.CalculationVersion, CalculationVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Snapshot calculation version '{context.Snapshot.CalculationVersion}' does not match '{CalculationVersion}'.");
        }

        var signals = context.Signals
            .GroupBy(signal => signal.DeduplicationKey, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(signal => signal.ImportanceScore)
                .ThenByDescending(signal => signal.ConfidenceScore)
                .ThenBy(signal => signal.Id)
                .First())
            .OrderBy(signal => signal.SignalType)
            .ThenBy(signal => signal.ExternalCompanyId, StringComparer.Ordinal)
            .ThenBy(signal => signal.IndustryCode, StringComparer.Ordinal)
            .ThenBy(signal => signal.DeduplicationKey, StringComparer.Ordinal)
            .ThenBy(signal => signal.Id)
            .ToArray();

        var confidence = Math.Min(context.Snapshot.ConfidenceScore, context.Snapshot.InputCompleteness.Score);
        return Task.FromResult(new FundPortfolioAnalyticsResult(
            context.Snapshot.WithConfidence(confidence),
            signals));
    }
}

public sealed class FundPortfolioAnalyticsFeatureCalculator : IDerivedFeatureCalculator
{
    public FeatureCode FeatureCode =>
        new(FundPortfolioAnalyticsCalculationPolicy.FeatureCode);

    public Task<FeatureSnapshot> CalculateAsync(
        FeatureCalculationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var inputByCode = context.Inputs
            .Where(input => input.Value is not null)
            .GroupBy(input => input.Dependency.Code, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var completed = new[]
        {
            "FUND_EQUITY_INPUT",
            "FUND_ALLOCATION_INPUT",
            "FUND_NON_EQUITY_INPUT",
            "FUND_INCOME_INPUT",
            "FUND_MARKET_LIQUIDITY_INPUT",
            "FUND_VALUATION_QUALITY_INPUT"
        }.Count(inputByCode.ContainsKey);
        var fingerprint = string.Join("|", inputByCode
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}:{pair.Value.Dependency.RequiredVersion}:{pair.Value.EvidenceFingerprint}"));
        var periodEnd = context.Period.EndDate ??
            throw new ArgumentException("Fund portfolio analytics requires a closed period.", nameof(context));
        var observedAt = new DateTimeOffset(periodEnd.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        var dependencyEvidence = inputByCode.Values
            .OrderBy(input => input.Dependency.Code, StringComparer.Ordinal)
            .Select(input => new FeatureDependencyEvidence(
                input.Dependency.Kind,
                input.Dependency.Code,
                input.Dependency.RequiredVersion,
                null))
            .ToArray();

        return Task.FromResult(new FeatureSnapshot(
            StableSnapshotId(context.ExternalCompanyId, periodEnd, fingerprint),
            context.ExternalCompanyId,
            new DerivedFeature(context.Definition.Code, context.Definition.Version, context.Definition.PolicyVersion),
            context.Period,
            completed / 6m,
            context.Definition.Output.Unit,
            FinancialObservationQuality.Current(observedAt, observedAt),
            [new FinancialSourceEvidence("FundPortfolioNormalizedData", observedAt, observedAt)],
            dependencyEvidence,
            fingerprint.Length == 0 ? "no-inputs" : fingerprint));
    }

    private static Guid StableSnapshotId(string externalCompanyId, DateOnly periodEnd, string fingerprint)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{FundPortfolioAnalyticsCalculationPolicy.FeatureCode}|{externalCompanyId}|{periodEnd:O}|{fingerprint}"));
        return new Guid(bytes[..16]);
    }
}
