using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Domain.Financial.RelativeValuation;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

public static class IndustryRelativeValuationSourceMapper
{
    public static RelativeValuationSourceFact Map(CyclicalWavesMetricSnapshot snapshot)
    {
        var metric = snapshot.MetricType switch
        {
            CyclicalWavesMetricType.PS => RelativeValuationMetric.Ps,
            CyclicalWavesMetricType.PE => RelativeValuationMetric.Pe,
            CyclicalWavesMetricType.Equilibrium => RelativeValuationMetric.Equilibrium,
            _ => throw new ArgumentOutOfRangeException(nameof(snapshot), snapshot.MetricType, null)
        };

        var (currentValue, referenceValue) = ReadValues(snapshot.MetricType, snapshot.RawResponseJson);
        return new RelativeValuationSourceFact(
            snapshot.CompanyId,
            metric,
            currentValue,
            referenceValue,
            IsAvailable: true,
            IsFresh: true,
            IdentityValid: true,
            SourceObservationTimestamp: snapshot.AcquisitionDateUtc,
            PersistedAtUtc: snapshot.CompletedAtUtc,
            SourceObservationId: snapshot.SnapshotId.ToString("D"),
            SourceFactId: snapshot.SnapshotId,
            SourceVersion: snapshot.ResponseHash,
            SourceWatermark: $"{snapshot.AcquisitionCheckId:D}|{snapshot.ResponseHash}");
    }

    private static (decimal? CurrentValue, decimal? ReferenceValue) ReadValues(
        CyclicalWavesMetricType metricType,
        string rawResponseJson)
    {
        try
        {
            using var document = JsonDocument.Parse(rawResponseJson);
            var root = document.RootElement;
            var current = ReadDecimal(root, "close");
            var reference = ReadDecimal(
                root,
                metricType == CyclicalWavesMetricType.Equilibrium ? "balance" : "avg");
            return (current, reference);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static decimal? ReadDecimal(JsonElement root, string propertyName) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDecimal(out var result)
            ? result
            : null;
}
