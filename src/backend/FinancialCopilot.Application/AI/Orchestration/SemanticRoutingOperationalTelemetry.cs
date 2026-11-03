using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace FinancialCopilot.Application.AI.Orchestration;

public enum SemanticRoutingDisagreementCategory
{
    AGREE,
    SEMANTIC_CORRECTS_LEGACY,
    LEGACY_CORRECTS_SEMANTIC,
    AMBIGUOUS,
    SEMANTIC_UNAVAILABLE,
    UNSUPPORTED,
    ENTITY_RESOLUTION_DIFFERENCE
}

public sealed record SemanticRoutingTelemetryContext(
    string QueryHash,
    string? LegacyCapability,
    decimal? LegacyConfidence,
    string? SemanticIntent,
    string? SemanticCandidate,
    decimal? SemanticConfidence,
    IReadOnlyCollection<string> ResolvedEntities,
    string? ArbitrationPreferredCandidate,
    string? ArbitrationReason,
    IReadOnlyCollection<string> HardVetoes,
    string? ActualExecutedCapability,
    SemanticRoutingMode RolloutMode,
    string ProviderModelVersion,
    int RegistryVersion,
    string ArbitrationPolicyVersion,
    SemanticRoutingDisagreementCategory Category = SemanticRoutingDisagreementCategory.AMBIGUOUS);

/// <summary>
/// One bounded, structured diagnostic record for a Feature 128 V2 routing decision.
/// This is diagnostic data only; it is not consulted by routing or arbitration.
/// </summary>
public sealed record SemanticRoutingDiagnostic(
    string CorrelationId,
    SemanticRoutingMode RolloutMode,
    string? LegacyCapability,
    decimal? LegacyConfidence,
    string? SemanticIntent,
    string? SemanticCapabilityCandidate,
    decimal? SemanticConfidence,
    string? ExtractedCompany,
    string? ExtractedProduct,
    string? ExtractedMetric,
    string? ExtractedPeriod,
    string? ResolvedCompany,
    string CompanyResolutionState,
    string? ResolvedProduct,
    string ProductResolutionState,
    string? ArbitrationWinner,
    string? ArbitrationReason,
    IReadOnlyCollection<string> ArbitrationVetoes,
    string? ActualExecutedCapability,
    string ExecutionSource,
    string? SemanticProvider,
    string? SemanticModel,
    string SemanticStatus = "Completed",
    string? QueryHash = null,
    string? FinalFrameCapability = null,
    string? FinalFrameCompany = null,
    string? FinalFrameCompanyState = null,
    string? FinalFrameProduct = null,
    string? FinalFrameProductState = null,
    string? ExecutorCapability = null,
    string? ExecutorCompany = null,
    string? ExecutorProduct = null,
    Guid? FinalFrameCompanyId = null,
    string? FinalFrameProductKey = null,
    string? FinalFrameProviderProductCode = null,
    long? FinalFrameProviderProductId = null,
    Guid? ExecutorCompanyId = null,
    string? ExecutorProductKey = null,
    string? ExecutorProviderProductCode = null,
    long? ExecutorProviderProductId = null);

public interface ISemanticRoutingDiagnosticSink
{
    void Record(SemanticRoutingDiagnostic diagnostic);
}

public sealed class NullSemanticRoutingDiagnosticSink : ISemanticRoutingDiagnosticSink
{
    public void Record(SemanticRoutingDiagnostic diagnostic) { }
}

public sealed record SemanticRoutingLatencySample(
    string CorrelationId,
    double? LegacyInterpretationMs = null,
    double? SemanticModelMs = null,
    double? EntityResolutionMs = null,
    double? ArbitrationMs = null,
    double? RoutingMs = null,
    double? TotalRequestMs = null,
    bool SemanticTimedOut = false,
    bool ProviderError = false,
    bool InvalidStructuredOutput = false,
    string ProviderModelVersion = "unknown",
    string? ProviderFailureCode = null);

public interface ISemanticRoutingLatencySink
{
    void Record(SemanticRoutingLatencySample sample);
    IReadOnlyCollection<SemanticRoutingLatencySample> Snapshot();
}

public sealed record SemanticRoutingCanarySample(
    string CorrelationId,
    string ActorCohortKeyHash,
    string CohortDecision,
    int? CohortBucket,
    string CapabilityCode,
    SemanticRoutingMode RolloutMode,
    bool SemanticExecutionAllowed,
    string QueryHash,
    string? SemanticIntent,
    string? SemanticCapability,
    string? LegacyCapability,
    string? ArbitrationSelectedCandidate,
    string? ArbitrationReason,
    IReadOnlyCollection<string> ResolvedEntities,
    string? ActualExecutedCapability,
    SemanticRoutingDisagreementCategory DisagreementCategory,
    string CorrectnessClassification,
    string EntityResolutionOutcome,
    string ExecutionStatus,
    bool BillingReservationObserved,
    bool BillingFinalizationObserved,
    decimal? CreditsCharged,
    string? ProviderName,
    string? ModelName,
    int? InputTokens,
    int? OutputTokens,
    int? TotalTokens,
    decimal? EstimatedModelCost,
    bool SemanticUsageSeparable,
    DateTimeOffset RecordedAtUtc,
    string? ProviderFailureCode = null);

public interface ISemanticRoutingCanaryTelemetrySink
{
    void Record(SemanticRoutingCanarySample sample);
    void RecordFailure(string correlationId, string executionStatus = "ProviderFailed");
    IReadOnlyCollection<SemanticRoutingCanarySample> Snapshot();
}

public sealed class BoundedSemanticRoutingCanaryTelemetrySink : ISemanticRoutingCanaryTelemetrySink
{
    private const int MaximumSamples = 10_000;
    private readonly ConcurrentDictionary<string, SemanticRoutingCanarySample> samples = new(StringComparer.Ordinal);

    public void Record(SemanticRoutingCanarySample sample)
    {
        if (string.IsNullOrWhiteSpace(sample.CorrelationId) ||
            string.IsNullOrWhiteSpace(sample.CapabilityCode) ||
            string.IsNullOrWhiteSpace(sample.QueryHash))
            return;

        samples[sample.CorrelationId] = sample;
        while (samples.Count > MaximumSamples)
        {
            var oldest = samples.Values.OrderBy(item => item.RecordedAtUtc).FirstOrDefault();
            if (oldest is null || !samples.TryRemove(oldest.CorrelationId, out _)) break;
        }
    }

    public void RecordFailure(string correlationId, string executionStatus = "ProviderFailed")
    {
        if (string.IsNullOrWhiteSpace(correlationId) ||
            !samples.TryGetValue(correlationId, out var sample))
            return;

        samples[correlationId] = sample with
        {
            DisagreementCategory = SemanticRoutingDisagreementCategory.SEMANTIC_UNAVAILABLE,
            CorrectnessClassification = "PROVIDER_FAILURE",
            ExecutionStatus = executionStatus
        };
    }

    public IReadOnlyCollection<SemanticRoutingCanarySample> Snapshot() => samples.Values.ToArray();
}

public interface ISemanticRoutingComparisonQuery
{
    IReadOnlyCollection<SemanticRoutingComparison> Snapshot();
}

public sealed record SemanticRoutingOperationalSnapshot(
    DateTimeOffset GeneratedAtUtc,
    SemanticRoutingMode DefaultMode,
    int CanaryPercentage,
    IReadOnlyDictionary<string, SemanticRoutingMode> CapabilityModes,
    int LiveCanarySampleCount,
    IReadOnlyDictionary<string, int> SamplesByCapability,
    IReadOnlyDictionary<string, int> CorrectnessCounts,
    IReadOnlyDictionary<string, int> DisagreementCounts,
    int EntityResolutionFailures,
    int ProviderTimeoutCount,
    int ProviderErrorCount,
    int InvalidStructuredOutputCount,
    int SemanticUnavailableCount,
    int SafeLegacyFallbackCount,
    SemanticRoutingLatencyPercentiles SemanticProviderLatency,
    SemanticRoutingLatencyPercentiles RoutingLatency,
    SemanticRoutingLatencyPercentiles TotalRequestLatency,
    IReadOnlyCollection<SemanticRoutingCanarySample> Samples);

public interface ISemanticRoutingOperationalTelemetryQuery
{
    SemanticRoutingOperationalSnapshot GetSnapshot(string? capabilityCode = null, int maximumSamples = 500);
}

public sealed class SemanticRoutingOperationalTelemetryQuery(
    SemanticRoutingOptions options,
    ISemanticRoutingCanaryTelemetrySink canarySink,
    ISemanticRoutingLatencySink latencySink,
    TimeProvider timeProvider) : ISemanticRoutingOperationalTelemetryQuery
{
    public SemanticRoutingOperationalSnapshot GetSnapshot(string? capabilityCode = null, int maximumSamples = 500)
    {
        var samples = canarySink.Snapshot()
            .Where(sample => string.IsNullOrWhiteSpace(capabilityCode) ||
                string.Equals(sample.CapabilityCode, capabilityCode, StringComparison.Ordinal))
            .OrderByDescending(sample => sample.RecordedAtUtc)
            .Take(Math.Clamp(maximumSamples, 1, 2_000))
            .ToArray();
        var correlationIds = samples.Select(sample => sample.CorrelationId).ToHashSet(StringComparer.Ordinal);
        var latencies = latencySink.Snapshot()
            .Where(sample => correlationIds.Contains(sample.CorrelationId))
            .ToArray();

        return new(
            timeProvider.GetUtcNow(),
            options.DefaultMode,
            options.CanaryPercentage,
            (options.Capabilities ?? new Dictionary<string, SemanticRoutingMode>(StringComparer.Ordinal))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            samples.Length,
            samples.GroupBy(sample => sample.CapabilityCode, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            CountBy(samples, sample => sample.CorrectnessClassification),
            CountBy(samples, sample => sample.DisagreementCategory.ToString()),
            samples.Count(sample => !string.Equals(sample.EntityResolutionOutcome, "Resolved", StringComparison.Ordinal)),
            latencies.Count(sample => sample.SemanticTimedOut),
            latencies.Count(sample => sample.ProviderError),
            latencies.Count(sample => sample.InvalidStructuredOutput),
            samples.Count(sample => sample.DisagreementCategory == SemanticRoutingDisagreementCategory.SEMANTIC_UNAVAILABLE),
            samples.Count(sample => string.Equals(sample.CorrectnessClassification, "LEGACY_FALLBACK", StringComparison.Ordinal)),
            SemanticRoutingLatencyStatistics.Calculate(latencies.Select(sample => sample.SemanticModelMs).Where(value => value.HasValue).Select(value => value!.Value)),
            SemanticRoutingLatencyStatistics.Calculate(latencies.Select(sample => sample.RoutingMs).Where(value => value.HasValue).Select(value => value!.Value)),
            SemanticRoutingLatencyStatistics.Calculate(latencies.Select(sample => sample.TotalRequestMs).Where(value => value.HasValue).Select(value => value!.Value)),
            samples);
    }

    private static IReadOnlyDictionary<string, int> CountBy(
        IEnumerable<SemanticRoutingCanarySample> samples,
        Func<SemanticRoutingCanarySample, string> selector) =>
        samples.GroupBy(selector, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
}

public sealed class BoundedSemanticRoutingLatencySink : ISemanticRoutingLatencySink
{
    private const int MaximumSamples = 10_000;
    private readonly ConcurrentDictionary<string, SemanticRoutingLatencySample> samples = new(StringComparer.Ordinal);

    public void Record(SemanticRoutingLatencySample sample)
    {
        if (string.IsNullOrWhiteSpace(sample.CorrelationId))
            return;

        samples.AddOrUpdate(
            sample.CorrelationId,
            sample,
            (_, existing) => Merge(existing, sample));
        while (samples.Count > MaximumSamples)
        {
            var oldest = samples.Keys.FirstOrDefault();
            if (oldest is null || !samples.TryRemove(oldest, out _)) break;
        }
    }

    public IReadOnlyCollection<SemanticRoutingLatencySample> Snapshot() => samples.Values.ToArray();

    private static SemanticRoutingLatencySample Merge(
        SemanticRoutingLatencySample existing,
        SemanticRoutingLatencySample incoming) =>
        existing with
        {
            LegacyInterpretationMs = incoming.LegacyInterpretationMs ?? existing.LegacyInterpretationMs,
            SemanticModelMs = incoming.SemanticModelMs ?? existing.SemanticModelMs,
            EntityResolutionMs = incoming.EntityResolutionMs ?? existing.EntityResolutionMs,
            ArbitrationMs = incoming.ArbitrationMs ?? existing.ArbitrationMs,
            RoutingMs = incoming.RoutingMs ?? existing.RoutingMs,
            TotalRequestMs = incoming.TotalRequestMs ?? existing.TotalRequestMs,
            SemanticTimedOut = existing.SemanticTimedOut || incoming.SemanticTimedOut,
            ProviderError = existing.ProviderError || incoming.ProviderError,
            InvalidStructuredOutput = existing.InvalidStructuredOutput || incoming.InvalidStructuredOutput,
            ProviderModelVersion = incoming.ProviderModelVersion != "unknown"
                ? incoming.ProviderModelVersion
                : existing.ProviderModelVersion,
            ProviderFailureCode = incoming.ProviderFailureCode ?? existing.ProviderFailureCode
        };
}

public sealed record SemanticRoutingLatencyPercentiles(
    int SampleCount,
    double? P50Ms,
    double? P95Ms,
    double? P99Ms);

public static class SemanticRoutingLatencyStatistics
{
    public static SemanticRoutingLatencyPercentiles Calculate(IEnumerable<double> samples)
    {
        var ordered = samples.Where(value => !double.IsNaN(value) && !double.IsInfinity(value))
            .OrderBy(value => value)
            .ToArray();
        return ordered.Length == 0
            ? new(0, null, null, null)
            : new(ordered.Length, Percentile(ordered, 0.50), Percentile(ordered, 0.95), Percentile(ordered, 0.99));
    }

    private static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        var index = (int)Math.Ceiling(values.Count * percentile) - 1;
        return values[Math.Clamp(index, 0, values.Count - 1)];
    }
}

public static class SemanticRoutingTelemetryFactory
{
    public static SemanticRoutingDiagnostic CreateDiagnostic(
        AiQueryRequest request,
        ValidatedQueryFrame frame,
        SemanticRoutingMode rolloutMode,
        string? actualExecutedCapability,
        string executionSource,
        string? semanticProvider = null,
        string? semanticModel = null,
        string? semanticStatus = null,
        string? legacyCapability = null,
        decimal? legacyConfidence = null,
        ValidatedQueryFrame? executorFrame = null)
    {
        var candidates = frame.Interpretation.CapabilityCandidates;
        var semanticCandidate = candidates.FirstOrDefault();
        legacyCapability ??= frame.LegacyCapability ?? semanticCandidate?.CapabilityCode;
        legacyConfidence ??= frame.LegacyConfidence ?? candidates.FirstOrDefault(candidate =>
            string.Equals(candidate.CapabilityCode, legacyCapability, StringComparison.Ordinal))?.Confidence;

        var companyMention = frame.Interpretation.EntityMentions.FirstOrDefault(IsCompanyMention)?.Text;
        var productMention = frame.Interpretation.EntityMentions.FirstOrDefault(IsProductMention)?.Text;
        var companySlot = FindSlot(frame, QuerySlotType.CompanyOrSymbol, QuerySlotType.CompaniesOrSymbols);
        var productSlot = FindSlot(frame, QuerySlotType.Product);
        var arbitrationVetoes = frame.Interpretation.Evidence
            .Where(evidence => string.Equals(evidence.Category, "arbitration-veto", StringComparison.Ordinal))
            .Select(evidence => DiagnosticVetoName(evidence.Value))
            .Take(20)
            .ToArray();
        var arbitrationReason = frame.Interpretation.Evidence.FirstOrDefault(evidence =>
            string.Equals(evidence.Category, "arbitration-reason", StringComparison.Ordinal))?.Value;
        if (arbitrationReason is null &&
            arbitrationVetoes.Contains("product_revenue_mix", StringComparer.Ordinal) &&
            productSlot?.ValidationState == QuerySlotValidationState.Valid)
        {
            arbitrationReason = "ExplicitResolvedProductScope";
        }

        var executorCompanySlot = executorFrame is null ? null : FindSlot(executorFrame, QuerySlotType.CompanyOrSymbol, QuerySlotType.CompaniesOrSymbols);
        var executorProductSlot = executorFrame is null ? null : FindSlot(executorFrame, QuerySlotType.Product);

        return new(
            request.CorrelationId,
            rolloutMode,
            legacyCapability,
            legacyConfidence,
            frame.Interpretation.CapabilityCandidates.FirstOrDefault()?.CapabilityCode ?? frame.CapabilityCode,
            semanticCandidate?.CapabilityCode ?? frame.CapabilityCode,
            semanticCandidate?.Confidence,
            companyMention,
            productMention,
            frame.Interpretation.Metrics.FirstOrDefault()?.MetricCode,
            frame.Interpretation.Period?.Value,
            companySlot?.Value,
            ResolutionState(companySlot, "Company"),
            productSlot?.Value,
            ResolutionState(productSlot, "Product"),
            frame.CapabilityCode,
            arbitrationReason,
            arbitrationVetoes,
            actualExecutedCapability,
            executionSource,
            semanticProvider ?? frame.SemanticProvider,
            semanticModel ?? frame.SemanticModel,
            semanticStatus ?? frame.SemanticStatus,
            QueryHash(request.Message),
            frame.CapabilityCode,
            companySlot?.Value,
            ResolutionState(companySlot, "Company"),
            productSlot?.Value,
            ResolutionState(productSlot, "Product"),
            executorFrame?.CapabilityCode,
            executorCompanySlot?.Value,
            executorProductSlot?.Value,
            companySlot?.CanonicalEntity?.CanonicalId ?? productSlot?.CanonicalProduct?.CompanyId,
            productSlot?.CanonicalProduct?.ProductKey,
            productSlot?.CanonicalProduct?.ProviderProductCode,
            productSlot?.CanonicalProduct?.ProviderProductId,
            executorCompanySlot?.CanonicalEntity?.CanonicalId ?? executorProductSlot?.CanonicalProduct?.CompanyId,
            executorProductSlot?.CanonicalProduct?.ProductKey,
            executorProductSlot?.CanonicalProduct?.ProviderProductCode,
            executorProductSlot?.CanonicalProduct?.ProviderProductId);
    }

    public static SemanticRoutingTelemetryContext FromFrame(
        AiQueryRequest request,
        ValidatedQueryFrame frame,
        string legacyCapability,
        string? actualExecutedCapability,
        SemanticRoutingMode rolloutMode,
        string providerModelVersion = "unknown")
    {
        var candidates = frame.Interpretation.CapabilityCandidates;
        var semanticCandidate = candidates.FirstOrDefault();
        var legacyCandidate = candidates.FirstOrDefault(candidate =>
            string.Equals(candidate.CapabilityCode, legacyCapability, StringComparison.Ordinal));
        var resolvedEntities = frame.Slots
            .Where(slot => slot.ValidationState == QuerySlotValidationState.Valid &&
                slot.Type is QuerySlotType.CompanyOrSymbol or QuerySlotType.CompaniesOrSymbols or QuerySlotType.Product)
            .Select(slot => slot.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Take(20)
            .Cast<string>()
            .ToArray();
        var hardVetoes = frame.Interpretation.Evidence
            .Where(evidence => string.Equals(evidence.Category, "arbitration-veto", StringComparison.Ordinal))
            .Select(evidence => evidence.Value)
            .Take(20)
            .ToArray();
        var arbitrationReason = frame.Interpretation.Evidence
            .FirstOrDefault(evidence => string.Equals(evidence.Category, "arbitration-reason", StringComparison.Ordinal))?.Value;

        return new(
            QueryHash(request.Message),
            legacyCapability,
            legacyCandidate?.Confidence,
            semanticCandidate?.CapabilityCode,
            semanticCandidate?.CapabilityCode,
            semanticCandidate?.Confidence,
            resolvedEntities,
            semanticCandidate?.CapabilityCode,
            arbitrationReason,
            hardVetoes,
            actualExecutedCapability,
            rolloutMode,
            providerModelVersion,
            frame.RegistryVersion,
            SemanticArbitrationPolicy.Version);
    }

    public static string QueryHash(string query) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(query ?? string.Empty)))[..16];

    public static string ActorCohortKeyHash(string actorKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actorKey ?? string.Empty)))[..16];

    private static ResolvedQuerySlot? FindSlot(ValidatedQueryFrame frame, params QuerySlotType[] types) =>
        frame.Slots.FirstOrDefault(slot => types.Contains(slot.Type));

    private static string ResolutionState(ResolvedQuerySlot? slot, string entityType) =>
        slot is null
            ? "NotApplicable"
            : slot.ValidationState switch
            {
                QuerySlotValidationState.Valid => "Resolved",
                QuerySlotValidationState.Ambiguous => "Ambiguous",
                QuerySlotValidationState.Invalid when slot.Detail is "entity_not_found" or "product_not_found" => "NotFound",
                QuerySlotValidationState.Invalid => "Invalid",
                QuerySlotValidationState.Missing => "Missing",
                QuerySlotValidationState.Unsupported => "Unsupported",
                _ => entityType
            };

    private static bool IsProductMention(EntityMention mention) =>
        string.Equals(mention.EntityType, "product", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(mention.Scope, "product", StringComparison.OrdinalIgnoreCase);

    private static bool IsCompanyMention(EntityMention mention) => !IsProductMention(mention) &&
        (string.IsNullOrWhiteSpace(mention.EntityType) ||
         string.Equals(mention.EntityType, "company", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(mention.EntityType, "symbol", StringComparison.OrdinalIgnoreCase));

    private static string DiagnosticVetoName(string value) =>
        string.Equals(value, SemanticArbitrationReasonCodes.ProductScopeVeto, StringComparison.Ordinal)
            ? "product_revenue_mix"
            : value;
}
