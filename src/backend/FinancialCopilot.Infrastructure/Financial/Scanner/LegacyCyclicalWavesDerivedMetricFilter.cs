using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

namespace FinancialCopilot.Infrastructure.Financial.Scanner;

public enum DerivedMetricProvenance
{
    /// <summary>The recorded metadata names no legacy CyclicalWaves source.</summary>
    NotLegacy,

    /// <summary>Every provider named in the evidence is the legacy CyclicalWaves provider.</summary>
    ExclusivelyLegacy,

    /// <summary>The evidence names the legacy CyclicalWaves provider and at least one other provider.</summary>
    MixedWithLegacy,

    /// <summary>
    /// No provider is recorded, but the row's calculation policy reads only a vendor line item that the legacy
    /// CyclicalWaves normalizers alone produce.
    /// </summary>
    LegacyCalculationPolicy,

    /// <summary>The evidence cannot be parsed, so provenance cannot be established.</summary>
    Undetermined
}

/// <summary>
/// Feature 148 decisions C1 and C1-M (permanent, not configurable): legacy CyclicalWaves-derived <c>DerivedMetrics</c>
/// rows are never valid financial evidence. A row is excluded when its evidence names the legacy provider at all
/// (exclusively or mixed with another provider), when its calculation policy is fed only by legacy CyclicalWaves
/// line items, or when its evidence cannot be parsed. Exclusion happens at the read boundary, before the
/// latest-row selection, so an older valid row can still be chosen. Provenance comes only from the structured
/// <c>FinancialSourceEvidence.SourceProvider</c> values and the registered <c>CalculationPolicyVersion</c>; values,
/// dates and tickers are never used. No row is modified or deleted.
/// </summary>
public static class LegacyCyclicalWavesDerivedMetricFilter
{
    /// <summary>
    /// Calculation policies (<c>PhaseOneFinancialSemanticCatalog</c>) whose only input is a vendor line item written
    /// exclusively by the legacy CyclicalWaves normalizers (<c>AVG_12M</c>, <c>AVG_4Q_REVENUE</c>, <c>PE_RATIO</c>,
    /// <c>PS_RATIO</c>). The C1-M census found 7 887 <c>cw-avg-12m-monthly-sales-passthrough-v1</c> rows that record no
    /// provider at all; this set classifies them.
    /// </summary>
    public static IReadOnlySet<string> LegacyOnlyCalculationPolicies { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cw-avg-12m-monthly-sales-passthrough-v1",
            "cw-avg-4q-revenue-passthrough-v1",
            "vendor-pe-ratio-passthrough-v1",
            "vendor-ps-ratio-passthrough-v1"
        };

    public static List<DerivedMetricRow> Apply(IEnumerable<DerivedMetricRow> rows) =>
        rows.Where(row => !IsExcluded(row)).ToList();

    public static bool IsExcluded(DerivedMetricRow row) =>
        IsExcluded(row.SourceEvidenceJson, row.DependencyEvidenceJson, row.CalculationPolicyVersion);

    public static bool IsExcluded(string? sourceEvidenceJson, string? dependencyEvidenceJson, string? calculationPolicyVersion) =>
        Classify(sourceEvidenceJson, dependencyEvidenceJson, calculationPolicyVersion) != DerivedMetricProvenance.NotLegacy;

    public static DerivedMetricProvenance Classify(
        string? sourceEvidenceJson,
        string? dependencyEvidenceJson,
        string? calculationPolicyVersion = null)
    {
        var providers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!TryCollectProviders(sourceEvidenceJson, providers) || !TryCollectProviders(dependencyEvidenceJson, providers))
        {
            return DerivedMetricProvenance.Undetermined;
        }

        if (providers.Contains(ProviderSources.CyclicalWavesName))
        {
            return providers.Count == 1 ? DerivedMetricProvenance.ExclusivelyLegacy : DerivedMetricProvenance.MixedWithLegacy;
        }

        return providers.Count == 0 &&
               calculationPolicyVersion is not null &&
               LegacyOnlyCalculationPolicies.Contains(calculationPolicyVersion)
            ? DerivedMetricProvenance.LegacyCalculationPolicy
            : DerivedMetricProvenance.NotLegacy;
    }

    private static bool TryCollectProviders(string? evidenceJson, HashSet<string> providers)
    {
        if (string.IsNullOrWhiteSpace(evidenceJson))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(evidenceJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var property in item.EnumerateObject())
                {
                    if (string.Equals(property.Name, "sourceProvider", StringComparison.OrdinalIgnoreCase) &&
                        property.Value.ValueKind == JsonValueKind.String &&
                        property.Value.GetString() is { Length: > 0 } provider)
                    {
                        providers.Add(provider);
                    }
                }
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
