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
        var hasExactValueSearch = candidates.Any(candidate => candidate.CapabilityCode == "financial_statement_value_search");
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
        var hasProductScope = interpretation.EntityMentions.Any(entity =>
            string.Equals(entity.EntityType, "product", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entity.Scope, "product", StringComparison.OrdinalIgnoreCase));
        var hasProductSalesValue = candidates.Any(candidate => candidate.CapabilityCode == "product_sales_value");
        var hasProductSalesTrend = candidates.Any(candidate => candidate.CapabilityCode == "product_sales_trend");
        var hasDisclosure = candidates.Any(candidate => candidate.CapabilityCode == "disclosure_listing");
        var hasRanking = candidates.Any(candidate => candidate.CapabilityCode == "monthly_sales_quality_ranking");
        var hasRelativeValuation = text.Contains("relative valuation", StringComparison.OrdinalIgnoreCase) ||
                                   text.Contains("ارزش گذاری نسبی", StringComparison.Ordinal) ||
                                   text.Contains("ارزش‌گذاری نسبی", StringComparison.Ordinal) ||
                                   text.Contains("مقایسه", StringComparison.Ordinal) &&
                                   (text.Contains("صنعت", StringComparison.Ordinal) || text.Contains("industry", StringComparison.OrdinalIgnoreCase));
        var relativeCandidate = candidates.FirstOrDefault(candidate => candidate.CapabilityCode is
            "symbol_vs_industry_relative_valuation" or "industry_relative_valuation_ranking" or
            "industry_relative_valuation_summary" or "symbol_pair_within_industry");

        var preferred = hasThreshold && candidates.Any(candidate => candidate.CapabilityCode == "stock_screening") ? "stock_screening"
            : hasGauge && hasPs ? "ps_gauge_visualization"
            : hasStatement && hasAnalysis ? "financial_statement_period_analysis"
            : hasStatement ? "financial_statement_table"
            : hasProductScope && hasProductSalesTrend ? "product_sales_trend"
            : hasProductScope && hasProductSalesValue ? "product_sales_value"
            : hasProduct ? "product_revenue_mix"
            : hasDisclosure ? "disclosure_listing"
            : hasRelativeValuation && relativeCandidate is not null ? relativeCandidate.CapabilityCode
            : hasRanking ? "monthly_sales_quality_ranking"
            : hasTrend && hasMetric && hasEntity ? "monthly_activity_trend"
            : hasAnalysis && hasEntity && !IsExplicitPointMetric(text) ? "comprehensive_analysis"
            : hasExactValueSearch ? "financial_statement_value_search"
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
    IReadOnlyCollection<string> Evidence,
    string? Intent = null,
    IReadOnlyCollection<SemanticEntityProposal>? Entities = null,
    IReadOnlyCollection<string>? MetricHints = null,
    string? Period = null,
    string? Comparison = null);

public sealed record SemanticEntityProposal(
    string Text,
    string? EntityType = null,
    string? Scope = null,
    decimal Confidence = 0m);

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
    IConversationalCapabilityRegistry registry,
    SemanticRoutingOptions? routingOptions = null) : IQueryInterpretationProposalProvider
{
    private const string SchemaJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["capabilityCodes", "missingSlots", "presentation", "confidence", "evidence", "intent", "entities", "metricHints"],
          "properties": {
            "capabilityCodes": { "type": "array", "maxItems": 10, "items": { "type": "string", "maxLength": 128 } },
            "missingSlots": { "type": "array", "maxItems": 20, "items": { "type": "string", "maxLength": 128 } },
            "presentation": { "type": ["string", "null"], "enum": ["Table", "Chart", "Gauge", "Summary", "List", null] },
            "confidence": { "type": "number", "minimum": 0, "maximum": 1 },
            "evidence": { "type": "array", "maxItems": 20, "items": { "type": "string", "maxLength": 256 } },
            "intent": { "type": ["string", "null"], "maxLength": 128 },
            "entities": {
              "type": "array",
              "maxItems": 20,
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["text", "entityType", "scope", "confidence"],
                "properties": {
                  "text": { "type": "string", "maxLength": 200 },
                  "entityType": { "type": ["string", "null"], "maxLength": 64 },
                  "scope": { "type": ["string", "null"], "maxLength": 64 },
                  "confidence": { "type": "number", "minimum": 0, "maximum": 1 }
                }
              }
            },
            "metricHints": { "type": "array", "maxItems": 20, "items": { "type": "string", "maxLength": 128 } }
          }
        }
        """;

    private static readonly AiStructuredOutputContract Contract = new(
        "QueryInterpretationProposal_v2",
        ["capabilityCodes", "missingSlots", "presentation", "confidence", "evidence", "intent", "entities", "metricHints"],
        SchemaJson);

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
                    $"Return exactly one JSON object and no prose, markdown, explanation, or reasoning. The top-level fields must be exactly capabilityCodes, missingSlots, presentation, confidence, evidence, intent, entities, and metricHints. Use null for presentation or intent when absent. presentation must be one of Table, Chart, Gauge, Summary, or List. entities must contain objects with text, entityType, scope, and confidence; return every company/symbol and product mention as a separate entity span. Classify product mentions with entityType=product and preserve the product base text after removing only a grammatical possessive suffix. For product-specific sales or trend requests, choose the product-specific capability and do not silently replace it with a company-wide metric. Allowed capability codes: {string.Join(", ", registry.GetEnabled().Select(item => item.Code).OrderBy(item => item, StringComparer.Ordinal))}. Capability codes must come from this governed catalog; never return routes, SQL, formulas, canonical IDs, metric definitions, tool names, or executable arguments. Entities are mentions only. Metric hints are non-authoritative aliases and must never be treated as canonical MetricCode values."),
                new AiConversationMessage(AiMessageRole.User, originalText)
            ],
            StructuredOutput: Contract,
            MaxOutputTokens: Math.Max(32, routingOptions?.SemanticInterpretationMaxOutputTokens ?? 384));

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
                ReadStrings(root, "evidence").Take(20).ToArray(),
                root.TryGetProperty("intent", out var intentProperty) && intentProperty.ValueKind == JsonValueKind.String
                    ? intentProperty.GetString()
                    : null,
                ReadEntities(root).Take(20).ToArray(),
                ReadStrings(root, "metricHints").Take(20).ToArray(),
                root.TryGetProperty("period", out var periodProperty) && periodProperty.ValueKind == JsonValueKind.String
                    ? periodProperty.GetString()
                    : null,
                root.TryGetProperty("comparison", out var comparisonProperty) && comparisonProperty.ValueKind == JsonValueKind.String
                    ? comparisonProperty.GetString()
                    : null);
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

    private static IReadOnlyCollection<SemanticEntityProposal> ReadEntities(JsonElement root) =>
        root.TryGetProperty("entities", out var property) && property.ValueKind == JsonValueKind.Array
            ? property.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object &&
                    item.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                .Select(item => new SemanticEntityProposal(
                    item.GetProperty("text").GetString()!,
                    item.TryGetProperty("entityType", out var entityType) && entityType.ValueKind == JsonValueKind.String ? entityType.GetString() : null,
                    item.TryGetProperty("scope", out var scope) && scope.ValueKind == JsonValueKind.String ? scope.GetString() : null,
                    item.TryGetProperty("confidence", out var confidence) && confidence.TryGetDecimal(out var parsed) ? parsed : 0m))
                .Where(item => !string.IsNullOrWhiteSpace(item.Text) && item.Confidence is >= 0 and <= 1)
                .ToArray()
            : [];
}

public sealed record HybridInterpretationResult(
    QueryInterpretation Interpretation,
    DialogueOutcomeResult? FailureOutcome,
    bool ModelProposalUsed,
    string SemanticStatus = "Completed",
    string? LegacyCapability = null,
    decimal? LegacyConfidence = null);

public sealed class HybridCapabilityInterpreter(
    ICapabilityInterpreter deterministicInterpreter,
    IConversationalCapabilityRegistry registry,
    QueryInterpretationValidator validator,
    IQueryInterpretationProposalProvider proposalProvider,
    ISemanticRoutingLatencySink? latencySink = null,
    SemanticRoutingOptions? routingOptions = null)
{
    public async Task<HybridInterpretationResult> InterpretAsync(
        string message,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var totalStart = Stopwatch.GetTimestamp();
        double? deterministicMs = null;
        double? semanticModelMs = null;
        double? arbitrationMs = null;
        var timedOut = false;
        var providerError = false;
        var invalidStructuredOutput = false;
        string? providerFailureCode = null;
        var deterministicStart = Stopwatch.GetTimestamp();
        var deterministicTask = Task.Run(() => deterministicInterpreter.Interpret(message), CancellationToken.None);
        var modelStart = Stopwatch.GetTimestamp();
        var proposalTask = ProposeWithTimeoutAsync(message, tenantId, correlationId, cancellationToken);
        QueryInterpretation deterministic;

        try
        {
            var proposal = await proposalTask;
            deterministic = await deterministicTask;
            deterministicMs = Stopwatch.GetElapsedTime(deterministicStart).TotalMilliseconds;
            semanticModelMs = Stopwatch.GetElapsedTime(modelStart).TotalMilliseconds;
            if (proposal is null)
                return new HybridInterpretationResult(
                    deterministic, null, false, "Unavailable",
                    deterministic.CapabilityCandidates.FirstOrDefault()?.CapabilityCode,
                    deterministic.CapabilityCandidates.FirstOrDefault()?.Confidence);

            var hasExplicitProductScope = deterministic.EntityMentions.Any(entity =>
                string.Equals(entity.EntityType, "product", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entity.Scope, "product", StringComparison.OrdinalIgnoreCase)) ||
                deterministic.CapabilityCandidates.Any(candidate => candidate.CapabilityCode == "product_revenue_mix");
            var proposedCandidates = proposal.CapabilityCodes
                .Where(code => hasExplicitProductScope || code is not ("product_sales_value" or "product_sales_trend" or "product_revenue_mix"))
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
                return new HybridInterpretationResult(
                    deterministic, null, false, "InvalidStructuredOutput",
                    deterministic.CapabilityCandidates.FirstOrDefault()?.CapabilityCode,
                    deterministic.CapabilityCandidates.FirstOrDefault()?.Confidence);

            var modelEvidence = proposal.Evidence
                .Select(value => new InterpretationEvidence("model-proposed", value, QueryValueProvenance.ModelProposed))
                .ToArray();
            var deterministicProductSurfaces = deterministic.EntityMentions
                .Where(entity => string.Equals(entity.EntityType, "product", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(entity.Scope, "product", StringComparison.OrdinalIgnoreCase))
                .SelectMany(entity => entity.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Select(ProductSemanticIntentRules.NormalizeProductSurface)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var modelEntities = (proposal.Entities ?? [])
                .Select(entity => new EntityMention(
                    entity.Text,
                    message.IndexOf(entity.Text, StringComparison.Ordinal),
                    entity.Text.Length,
                    QueryValueProvenance.ModelProposed,
                    entity.EntityType,
                    entity.Scope))
                .Where(entity => entity.Start >= 0 &&
                    (hasExplicitProductScope ||
                     !string.Equals(entity.EntityType, "product", StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(entity.Scope, "product", StringComparison.OrdinalIgnoreCase)) &&
                    !deterministicProductSurfaces.Contains(ProductSemanticIntentRules.NormalizeProductSurface(entity.Text)))
                .Take(20)
                .ToArray();
            var semantic = deterministic with
            {
                CapabilityCandidates = proposedCandidates,
                EntityMentions = deterministic.EntityMentions.Concat(modelEntities).Take(20).ToArray(),
                MissingSlots = proposal.MissingSlots.ToArray(),
                Presentation = proposal.Presentation is null ? deterministic.Presentation :
                    new PresentationPreference(Enum.Parse<PresentationKind>(proposal.Presentation, true), QueryValueProvenance.ModelProposed),
                Confidence = Math.Max(deterministic.Confidence, proposal.Confidence),
                ConfidenceBand = InterpretationConfidencePolicy.Band(proposal.Confidence),
                Evidence = deterministic.Evidence.Concat(modelEvidence).Take(40).ToArray()
            };
            var arbitrationStart = Stopwatch.GetTimestamp();
            var arbitration = SemanticArbitrator.Arbitrate(deterministic, semantic, registry);
            arbitrationMs = Stopwatch.GetElapsedTime(arbitrationStart).TotalMilliseconds;
            var merged = semantic with
            {
                CapabilityCandidates = arbitration.Candidates,
                Evidence = semantic.Evidence.Concat(arbitration.Vetoes.Select(value =>
                    new InterpretationEvidence("arbitration-veto", value, QueryValueProvenance.PolicyDefaulted))
                    .Concat(arbitration.ModelConfidenceUsed
                        ? []
                        : [new InterpretationEvidence(
                            "arbitration-reason",
                            SemanticArbitrationReasonCodes.DeterministicCandidatePreferred,
                            QueryValueProvenance.PolicyDefaulted)])
                    .Take(40).ToArray()).ToArray(),
                Confidence = arbitration.ModelConfidenceUsed ? semantic.Confidence : deterministic.Confidence,
                ConfidenceBand = InterpretationConfidencePolicy.Band(arbitration.ModelConfidenceUsed ? semantic.Confidence : deterministic.Confidence)
            };
            validator.Validate(merged);
            return new HybridInterpretationResult(
                merged, null, true, "Completed",
                deterministic.CapabilityCandidates.FirstOrDefault()?.CapabilityCode,
                deterministic.CapabilityCandidates.FirstOrDefault()?.Confidence);
        }
        catch (AiModelProviderException exception)
        {
            deterministic = await deterministicTask;
            deterministicMs = Stopwatch.GetElapsedTime(deterministicStart).TotalMilliseconds;
            semanticModelMs = Stopwatch.GetElapsedTime(modelStart).TotalMilliseconds;
            timedOut = exception.Status == AiExecutionStatus.TimedOut;
            providerError = !timedOut;
            invalidStructuredOutput = exception.Status == AiExecutionStatus.InvalidStructuredOutput;
            providerFailureCode = exception.Code;
            return new HybridInterpretationResult(
                deterministic,
                AiDialogueOutcomePolicy.FromException(message, exception),
                false,
                SemanticStatus(exception.Status),
                deterministic.CapabilityCandidates.FirstOrDefault()?.CapabilityCode,
                deterministic.CapabilityCandidates.FirstOrDefault()?.Confidence);
        }
        catch (OperationCanceledException exception)
        {
            deterministic = await deterministicTask;
            deterministicMs = Stopwatch.GetElapsedTime(deterministicStart).TotalMilliseconds;
            semanticModelMs = Stopwatch.GetElapsedTime(modelStart).TotalMilliseconds;
            timedOut = true;
            return new HybridInterpretationResult(
                deterministic,
                AiDialogueOutcomePolicy.FromException(message, exception),
                false,
                "Timeout",
                deterministic.CapabilityCandidates.FirstOrDefault()?.CapabilityCode,
                deterministic.CapabilityCandidates.FirstOrDefault()?.Confidence);
        }
        catch (TimeoutException exception)
        {
            deterministic = await deterministicTask;
            deterministicMs = Stopwatch.GetElapsedTime(deterministicStart).TotalMilliseconds;
            semanticModelMs = Stopwatch.GetElapsedTime(modelStart).TotalMilliseconds;
            timedOut = true;
            return new HybridInterpretationResult(
                deterministic,
                AiDialogueOutcomePolicy.FromException(message, exception),
                false,
                "Timeout",
                deterministic.CapabilityCandidates.FirstOrDefault()?.CapabilityCode,
                deterministic.CapabilityCandidates.FirstOrDefault()?.Confidence);
        }
        finally
        {
            latencySink?.Record(new SemanticRoutingLatencySample(
                correlationId,
                LegacyInterpretationMs: deterministicMs,
                SemanticModelMs: semanticModelMs,
                ArbitrationMs: arbitrationMs,
                RoutingMs: Stopwatch.GetElapsedTime(totalStart).TotalMilliseconds,
                SemanticTimedOut: timedOut,
                ProviderError: providerError,
                InvalidStructuredOutput: invalidStructuredOutput,
                ProviderFailureCode: providerFailureCode));
        }
    }

    private async Task<QueryInterpretationProposal?> ProposeWithTimeoutAsync(
        string message,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var timeoutMs = Math.Max(1, routingOptions?.SemanticInterpretationTimeoutMilliseconds ?? 5000);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);
        try
        {
            return await proposalProvider.ProposeAsync(message, tenantId, correlationId, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiModelProviderException(
                AiExecutionStatus.TimedOut,
                "semantic_interpretation_timeout",
                $"Semantic interpretation exceeded the configured timeout of {timeoutMs} ms.");
        }
    }

    private static string SemanticStatus(AiExecutionStatus status) => status switch
    {
        AiExecutionStatus.InvalidStructuredOutput => "InvalidStructuredOutput",
        AiExecutionStatus.TimedOut => "Timeout",
        _ => "ProviderError"
    };
}

public sealed record SemanticArbitrationResult(
    IReadOnlyList<CapabilityCandidate> Candidates,
    IReadOnlyCollection<string> Vetoes,
    bool ModelConfidenceUsed,
    string PolicyVersion = SemanticArbitrationPolicy.Version);

public static class SemanticArbitrationPolicy
{
    public const string Version = "feature-128-arbitration-v1";
}

public static class SemanticArbitrationReasonCodes
{
    public const string ProductScopeVeto = "resolved_product_scope_vetoed_company_wide_product_revenue_mix";
    public const string ProductCapabilityWithoutExplicitProductScope = "product_capability_without_explicit_product_scope";
    public const string DeterministicCandidatePreferred = "deterministic_candidate_preferred_over_model_confidence";
}

public static class SemanticArbitrator
{
    public static SemanticArbitrationResult Arbitrate(
        QueryInterpretation deterministic,
        QueryInterpretation semantic,
        IConversationalCapabilityRegistry registry)
    {
        var deterministicProductScope = deterministic.EntityMentions.Any(entity =>
            string.Equals(entity.EntityType, "product", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entity.Scope, "product", StringComparison.OrdinalIgnoreCase) ||
            deterministic.CapabilityCandidates.Any(candidate => candidate.CapabilityCode == "product_revenue_mix"));
        var semanticProductMention = semantic.EntityMentions.Any(entity =>
            string.Equals(entity.EntityType, "product", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entity.Scope, "product", StringComparison.OrdinalIgnoreCase));
        var vetoes = new List<string>();
        var semanticCandidates = semantic.CapabilityCandidates
            .Where(candidate => registry.Find(candidate.CapabilityCode) is { Enabled: true })
            .Where(candidate =>
            {
                if (!deterministicProductScope && candidate.CapabilityCode is "product_sales_value" or "product_sales_trend" or "product_revenue_mix")
                {
                    vetoes.Add(candidate.CapabilityCode == "product_revenue_mix" && semanticProductMention
                        ? SemanticArbitrationReasonCodes.ProductScopeVeto
                        : SemanticArbitrationReasonCodes.ProductCapabilityWithoutExplicitProductScope);
                    return false;
                }
                if (deterministicProductScope && candidate.CapabilityCode == "product_revenue_mix" &&
                    deterministic.EntityMentions.Any(entity => string.Equals(entity.EntityType, "product", StringComparison.OrdinalIgnoreCase)))
                {
                    vetoes.Add(SemanticArbitrationReasonCodes.ProductScopeVeto);
                    return false;
                }
                return true;
            })
            .ToArray();

        var deterministicCodes = deterministic.CapabilityCandidates
            .Select(candidate => candidate.CapabilityCode)
            .ToHashSet(StringComparer.Ordinal);
        var deterministicTop = deterministic.CapabilityCandidates.FirstOrDefault();
        var semanticIndustryComparison = semanticCandidates.FirstOrDefault(candidate =>
            candidate.CapabilityCode is "symbol_vs_industry_relative_valuation" or
                "industry_relative_valuation_ranking" or
                "industry_relative_valuation_summary" or
                "symbol_pair_within_industry");
        var semanticComparisonWins = semanticIndustryComparison is not null &&
            (deterministicTop is null || semanticIndustryComparison.Confidence > deterministicTop.Confidence);
        var candidates = semanticCandidates
            .Concat(deterministic.CapabilityCandidates.Where(candidate =>
                !semanticCandidates.Any(other => other.CapabilityCode == candidate.CapabilityCode) &&
                !(deterministicProductScope && candidate.CapabilityCode == "product_revenue_mix" &&
                  deterministic.EntityMentions.Any(entity => string.Equals(entity.EntityType, "product", StringComparison.OrdinalIgnoreCase)))))
            .OrderByDescending(candidate => semanticComparisonWins
                ? candidate.CapabilityCode == semanticIndustryComparison!.CapabilityCode
                : deterministicCodes.Contains(candidate.CapabilityCode))
            .ThenByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.CapabilityCode, StringComparer.Ordinal)
            .ToArray();

        var modelConfidenceUsed = candidates.Length > 0 && !deterministicCodes.Contains(candidates[0].CapabilityCode);
        if (!modelConfidenceUsed && semanticCandidates.Any(candidate => !deterministicCodes.Contains(candidate.CapabilityCode)))
            vetoes.Add(SemanticArbitrationReasonCodes.DeterministicCandidatePreferred);
        return new(candidates, vetoes, modelConfidenceUsed);
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
