using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.AI.ModelProviders;

namespace FinancialCopilot.Application.AI.Orchestration;

public enum InterpretationConfidenceBand
{
    Low,
    Medium,
    High
}

public static class InterpretationConfidencePolicy
{
    public const decimal LowThreshold = 0.60m;
    public const decimal HighThreshold = 0.85m;

    public static InterpretationConfidenceBand Band(decimal confidence) =>
        confidence >= HighThreshold ? InterpretationConfidenceBand.High
        : confidence >= LowThreshold ? InterpretationConfidenceBand.Medium
        : InterpretationConfidenceBand.Low;

    public static bool IsAmbiguous(decimal confidence, decimal? runnerUp = null) =>
        confidence < LowThreshold || runnerUp is not null && confidence - runnerUp.Value < 0.10m;
}

public static class CapabilityRoutingPrecedence
{
    public static IReadOnlyList<CapabilityCandidate> Order(
        QueryInterpretation interpretation,
        IReadOnlyList<CapabilityCandidate> candidates)
    {
        var text = interpretation.NormalizedText;
        var hasThreshold = text.Contains("below", StringComparison.OrdinalIgnoreCase) ||
                           text.Contains("above", StringComparison.OrdinalIgnoreCase) ||
                           text.Contains("زیر", StringComparison.Ordinal) ||
                           text.Contains("بالای", StringComparison.Ordinal);
        var hasEntity = interpretation.EntityMentions.Count > 0;
        var hasMetric = interpretation.Metrics.Count > 0;
        var hasTrend = text.Contains("trend", StringComparison.OrdinalIgnoreCase) ||
                       text.Contains("chart", StringComparison.OrdinalIgnoreCase) ||
                       text.Contains("روند", StringComparison.Ordinal) ||
                       text.Contains("چارت", StringComparison.Ordinal) ||
                       text.Contains("نمودار", StringComparison.Ordinal);
        var hasGauge = text.Contains("gauge", StringComparison.OrdinalIgnoreCase) ||
                       text.Contains("گیج", StringComparison.Ordinal);
        var hasPs = text.Contains("p/s", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("p s", StringComparison.OrdinalIgnoreCase);
        var hasAnalysis = text.Contains("analysis", StringComparison.OrdinalIgnoreCase) ||
                          text.Contains("analyze", StringComparison.OrdinalIgnoreCase) ||
                          text.Contains("تحلیل", StringComparison.Ordinal) ||
                          text.Contains("بررسی", StringComparison.Ordinal);
        var hasStatement = candidates.Any(candidate => candidate.CapabilityCode is "financial_statement_table" or "financial_statement_period_analysis");
        var hasProduct = candidates.Any(candidate => candidate.CapabilityCode == "product_revenue_mix");
        var hasDisclosure = candidates.Any(candidate => candidate.CapabilityCode == "disclosure_listing");
        var hasRanking = candidates.Any(candidate => candidate.CapabilityCode == "monthly_sales_quality_ranking");

        var preferred = hasThreshold && candidates.Any(candidate => candidate.CapabilityCode == "stock_screening") ? "stock_screening"
            : hasGauge && hasPs ? "ps_gauge_visualization"
            : hasStatement && hasAnalysis ? "financial_statement_period_analysis"
            : hasStatement ? "financial_statement_table"
            : hasProduct ? "product_revenue_mix"
            : hasDisclosure ? "disclosure_listing"
            : hasRanking ? "monthly_sales_quality_ranking"
            : hasTrend && hasMetric && hasEntity ? "monthly_activity_trend"
            : hasAnalysis && hasEntity && !IsExplicitPointMetric(text) ? "comprehensive_analysis"
            : hasMetric && hasEntity ? "symbol_metric_lookup"
            : null;

        if (preferred is null)
            return candidates;

        return candidates
            .OrderByDescending(candidate => candidate.CapabilityCode == preferred)
            .ThenByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.CapabilityCode, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsExplicitPointMetric(string text) =>
        text.Contains("p/e", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("p/s", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("eps", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("چقدر", StringComparison.Ordinal) ||
        text.Contains("مقدار", StringComparison.Ordinal);
}

public sealed record QueryInterpretationProposal(
    IReadOnlyCollection<string> CapabilityCodes,
    IReadOnlyCollection<string> MissingSlots,
    string? Presentation,
    decimal Confidence,
    IReadOnlyCollection<string> Evidence);

public interface IQueryInterpretationProposalProvider
{
    Task<QueryInterpretationProposal?> ProposeAsync(
        string originalText,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken);
}

public sealed class NoOpQueryInterpretationProposalProvider : IQueryInterpretationProposalProvider
{
    public Task<QueryInterpretationProposal?> ProposeAsync(
        string originalText,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken) => Task.FromResult<QueryInterpretationProposal?>(null);
}

public sealed class LlmQueryInterpretationProposalProvider(
    IAiModelExecutionService executionService,
    IConversationalCapabilityRegistry registry) : IQueryInterpretationProposalProvider
{
    private static readonly AiStructuredOutputContract Contract = new(
        "QueryInterpretationProposal",
        ["capabilityCodes", "missingSlots", "presentation", "confidence", "evidence"]);

    public async Task<QueryInterpretationProposal?> ProposeAsync(
        string originalText,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var selection = new AiModelSelectionRequest(
            tenantId,
            AiWorkloadKind.ScannerParsing,
            AiModelCapability.ChatCompletion | AiModelCapability.StructuredOutput,
            correlationId);
        var request = new AiModelRequest(
            correlationId,
            tenantId,
            AiWorkloadKind.ScannerParsing,
            [
                new AiConversationMessage(
                    AiMessageRole.System,
                    "Return only a JSON query interpretation proposal. Capability codes must come from the supplied governed catalog; never return routes, SQL, formulas, or metric definitions."),
                new AiConversationMessage(AiMessageRole.User, originalText)
            ],
            StructuredOutput: Contract);

        var result = await executionService.ExecuteAsync(selection, request, cancellationToken);
        return Parse(result.StructuredJson);
    }

    private QueryInterpretationProposal? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new AiModelProviderException(AiExecutionStatus.InvalidStructuredOutput, "empty_query_frame", "Empty query interpretation proposal.");

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var codes = ReadStrings(root, "capabilityCodes");
            if (codes.Any(code => registry.Find(code) is null))
                throw new InvalidOperationException("The model proposed an unregistered capability.");

            var confidence = root.TryGetProperty("confidence", out var confidenceProperty) &&
                             confidenceProperty.TryGetDecimal(out var parsedConfidence)
                ? parsedConfidence
                : 0m;
            if (confidence is < 0 or > 1)
                throw new InvalidOperationException("The model proposed an invalid confidence value.");

            var presentation = root.TryGetProperty("presentation", out var presentationProperty) &&
                               presentationProperty.ValueKind == JsonValueKind.String
                ? presentationProperty.GetString()
                : null;
            if (presentation is not null && !Enum.TryParse<PresentationKind>(presentation, true, out _))
                throw new InvalidOperationException("The model proposed an invalid presentation.");

            return new QueryInterpretationProposal(
                codes.Take(10).ToArray(),
                ReadStrings(root, "missingSlots").Take(20).ToArray(),
                presentation,
                confidence,
                ReadStrings(root, "evidence").Take(20).ToArray());
        }
        catch (AiModelProviderException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw new AiModelProviderException(
                AiExecutionStatus.InvalidStructuredOutput,
                "invalid_query_frame",
                "The query interpretation proposal failed schema validation.",
                exception);
        }
    }

    private static IReadOnlyCollection<string> ReadStrings(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Array
            ? property.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToArray()
            : [];
}

public sealed record HybridInterpretationResult(
    QueryInterpretation Interpretation,
    DialogueOutcomeResult? FailureOutcome,
    bool ModelProposalUsed);

public sealed class HybridCapabilityInterpreter(
    ICapabilityInterpreter deterministicInterpreter,
    IConversationalCapabilityRegistry registry,
    QueryInterpretationValidator validator,
    IQueryInterpretationProposalProvider proposalProvider)
{
    public async Task<HybridInterpretationResult> InterpretAsync(
        string message,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var deterministic = deterministicInterpreter.Interpret(message);
        if (deterministic.CapabilityCandidates.Count > 0 && deterministic.Confidence >= InterpretationConfidencePolicy.HighThreshold)
            return new HybridInterpretationResult(deterministic, null, false);

        try
        {
            var proposal = await proposalProvider.ProposeAsync(message, tenantId, correlationId, cancellationToken);
            if (proposal is null)
                return new HybridInterpretationResult(deterministic, null, false);

            var proposedCandidates = proposal.CapabilityCodes
                .Select(code => registry.Find(code))
                .Where(definition => definition?.Enabled == true)
                .Cast<CapabilityDefinition>()
                .Select(definition => new CapabilityCandidate(
                    definition.Code,
                    registry.Version,
                    proposal.Confidence,
                    proposal.Evidence.Select(value => new InterpretationEvidence(
                        "model-proposed",
                        value,
                        QueryValueProvenance.ModelProposed)).ToArray()))
                .ToArray();
            if (proposedCandidates.Length == 0)
                return new HybridInterpretationResult(deterministic, null, false);

            var merged = deterministic with
            {
                CapabilityCandidates = CapabilityRoutingPrecedence.Order(deterministic, proposedCandidates),
                MissingSlots = proposal.MissingSlots.ToArray(),
                Presentation = proposal.Presentation is null ? deterministic.Presentation :
                    new PresentationPreference(Enum.Parse<PresentationKind>(proposal.Presentation, true), QueryValueProvenance.ModelProposed),
                Confidence = proposal.Confidence,
                ConfidenceBand = InterpretationConfidencePolicy.Band(proposal.Confidence),
                Evidence = deterministic.Evidence.Concat(proposal.Evidence.Select(value =>
                    new InterpretationEvidence("model-proposed", value, QueryValueProvenance.ModelProposed))).Take(40).ToArray()
            };
            validator.Validate(merged);
            return new HybridInterpretationResult(merged, null, true);
        }
        catch (Exception exception) when (exception is AiModelProviderException or OperationCanceledException or TimeoutException)
        {
            return new HybridInterpretationResult(
                deterministic,
                AiDialogueOutcomePolicy.FromException(message, exception),
                false);
        }
    }
}

public sealed record CapabilityPromptProjection(
    string Code,
    int Version,
    string OutputType,
    IReadOnlyCollection<string> RequiredSlots,
    IReadOnlyCollection<string> OptionalSlots,
    IReadOnlyCollection<string> Aliases,
    IReadOnlyCollection<string> Examples);

public sealed record CapabilityMetadataProjection(
    string Code,
    int Version,
    string OutputType,
    IReadOnlyCollection<string> Aliases,
    IReadOnlyCollection<string> Examples,
    bool IncludeInGuidance);

public sealed class CapabilityRegistryProjection(IConversationalCapabilityRegistry registry)
{
    public IReadOnlyCollection<CapabilityPromptProjection> BuildPromptProjection(int maxItems = 20) =>
        registry.GetEnabled().Take(maxItems).Select(definition => new CapabilityPromptProjection(
            definition.Code,
            definition.Version,
            definition.OutputType,
            definition.RequiredSlots.Select(slot => slot.Name).ToArray(),
            definition.OptionalSlots.Select(slot => slot.Name).ToArray(),
            definition.Aliases.Select(alias => alias.Value).Take(6).ToArray(),
            definition.Examples.Select(example => example.Text).Take(4).ToArray())).ToArray();

    public IReadOnlyCollection<CapabilityMetadataProjection> BuildMetadataProjection(int maxItems = 20) =>
        registry.GetEnabled().Take(maxItems).Select(definition => new CapabilityMetadataProjection(
            definition.Code,
            definition.Version,
            definition.OutputType,
            definition.Aliases.Select(alias => alias.Value).Take(6).ToArray(),
            definition.Examples.Select(example => example.Text).Take(4).ToArray(),
            definition.SuggestionPolicy.IncludeInGuidance)).ToArray();

    public string BuildBoundedPrompt(int maxCharacters = 6000)
    {
        var builder = new StringBuilder("Enabled capabilities:\n");
        foreach (var item in BuildPromptProjection())
        {
            var line = $"- {item.Code}: output={item.OutputType}; required={string.Join(',', item.RequiredSlots)}; examples={string.Join(" | ", item.Examples)}\n";
            if (builder.Length + line.Length > maxCharacters)
                break;
            builder.Append(line);
        }
        return builder.ToString();
    }
}

public sealed record QueryInterpretationTelemetry(
    int RegistryVersion,
    int CandidateCount,
    string? WinningCapability,
    decimal WinningConfidence,
    InterpretationConfidenceBand ConfidenceBand,
    IReadOnlyCollection<string> EvidenceCategories,
    TimeSpan Duration,
    DialogueOutcome? Outcome = null,
    bool ValidationFailed = false);

public interface IQueryInterpretationTelemetrySink
{
    void Record(QueryInterpretationTelemetry telemetry);
}

public sealed class ActivityQueryInterpretationTelemetrySink : IQueryInterpretationTelemetrySink
{
    public void Record(QueryInterpretationTelemetry telemetry)
    {
        Activity.Current?.SetTag("query.registry_version", telemetry.RegistryVersion);
        Activity.Current?.SetTag("query.candidate_count", telemetry.CandidateCount);
        Activity.Current?.SetTag("query.winning_capability", telemetry.WinningCapability);
        Activity.Current?.SetTag("query.winning_confidence_band", telemetry.ConfidenceBand.ToString());
        Activity.Current?.SetTag("query.evidence_categories", string.Join(',', telemetry.EvidenceCategories.Take(10)));
        Activity.Current?.SetTag("query.interpretation_duration_ms", telemetry.Duration.TotalMilliseconds);
        Activity.Current?.SetTag("query.validation_failed", telemetry.ValidationFailed);
        if (telemetry.Outcome is not null)
            Activity.Current?.SetTag("workflow.outcome", telemetry.Outcome.ToString());
    }
}
