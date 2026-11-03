namespace FinancialCopilot.Application.AI.ModelProviders;

[Flags]
public enum AiModelCapability
{
    None = 0,
    ChatCompletion = 1,
    StructuredOutput = 2,
    ToolCalling = 4,
    Streaming = 8,
    Embeddings = 16,
    UsageReporting = 32,
    HealthCheck = 64
}

public enum AiProviderHostingMode
{
    Fake,
    Hosted,
    Local,
    ContractPending
}

public enum AiWorkloadKind
{
    ScannerParsing,
    ExplanationGeneration,
    SuggestedQuestions,
    Summarization,
    Embeddings,
    ResearchTool,
    SchemaNavigation
}

public enum AiMessageRole
{
    System,
    User,
    Assistant,
    Tool
}

public enum AiExecutionStatus
{
    Completed,
    Failed,
    TimedOut,
    CapabilityUnavailable,
    InvalidStructuredOutput,
    RuntimeUnavailable
}

public sealed record AiConversationMessage(AiMessageRole Role, string Content, string? ToolCallId = null);

public sealed record AiToolDefinition(string Name, string Description, string ParametersJsonSchema);

public sealed record AiStructuredOutputContract(
    string SchemaName,
    IReadOnlyCollection<string> RequiredRootProperties,
    string? JsonSchema = null);

public sealed record AiModelRequest(
    string CorrelationId,
    Guid TenantId,
    AiWorkloadKind Workload,
    IReadOnlyCollection<AiConversationMessage> Messages,
    AiStructuredOutputContract? StructuredOutput = null,
    IReadOnlyCollection<AiToolDefinition>? Tools = null,
    bool Stream = false,
    string? PreviousResponseId = null,
    int? MaxOutputTokens = null);

public sealed record AiToolCall(string Id, string Name, string ArgumentsJson, string? ItemId = null);

public sealed record AiExecutionUsageFacts(
    string CorrelationId,
    string ProviderKey,
    string ModelKey,
    AiExecutionStatus Status,
    TimeSpan Duration,
    int AttemptNumber,
    int? InputTokens = null,
    int? OutputTokens = null,
    bool CacheHit = false,
    bool UsedTools = false,
    bool EmbeddingOperation = false,
    decimal? ProviderReportedCost = null,
    string? ProviderReportedCurrency = null,
    string? FailureCode = null,
    // Provider-reported prompt-cache hits; a subset of InputTokens.
    int? CachedInputTokens = null,
    // Cache writes are separate billing categories, also subsets of InputTokens.
    // Null means the provider did not report the field; explicit zero remains distinguishable.
    int? CacheWriteInputTokens = null,
    int? CacheWrite30mInputTokens = null,
    int? CacheWrite12hInputTokens = null,
    AiWorkloadKind? Workload = null);

public sealed record AiExecutionUsageSummary(
    string ProviderKey,
    string ModelKey,
    int? InputTokens,
    int? OutputTokens,
    decimal? ProviderReportedCost = null,
    string? ProviderReportedCurrency = null,
    int? CachedInputTokens = null,
    int? CacheWriteInputTokens = null,
    int? CacheWrite30mInputTokens = null,
    int? CacheWrite12hInputTokens = null)
{
    public int? TotalTokens => InputTokens is null && OutputTokens is null
        ? null
        : (InputTokens ?? 0) + (OutputTokens ?? 0);

    // Provider and model keys are listed once each in first-seen order, which keeps the
    // persisted ledger values bounded however many calls a turn makes.
    public static AiExecutionUsageSummary? From(IReadOnlyCollection<AiExecutionUsageFacts> calls)
    {
        if (calls.Count == 0)
        {
            return null;
        }

        return new AiExecutionUsageSummary(
            string.Join(",", calls.Select(call => call.ProviderKey).Distinct(StringComparer.OrdinalIgnoreCase)),
            string.Join(",", calls.Select(call => call.ModelKey).Distinct(StringComparer.OrdinalIgnoreCase)),
            Sum(calls.Select(call => call.InputTokens)),
            Sum(calls.Select(call => call.OutputTokens)),
            HasCompatibleReportedCurrency(calls) ? Sum(calls.Select(call => call.ProviderReportedCost)) : null,
            CompatibleReportedCurrency(calls),
            SumIfAllReported(calls.Select(call => call.CachedInputTokens)),
            SumIfAllReported(calls.Select(call => call.CacheWriteInputTokens)),
            SumIfAllReported(calls.Select(call => call.CacheWrite30mInputTokens)),
            SumIfAllReported(calls.Select(call => call.CacheWrite12hInputTokens)));
    }

    private static int? Sum(IEnumerable<int?> values) =>
        values.Aggregate((int?)null, (total, value) => total is null && value is null ? null : (total ?? 0) + (value ?? 0));

    private static int? SumIfAllReported(IEnumerable<int?> values)
    {
        var materialized = values.ToArray();
        return materialized.All(value => value.HasValue)
            ? materialized.Sum(value => value!.Value)
            : null;
    }

    private static decimal? Sum(IEnumerable<decimal?> values) =>
        values.Aggregate((decimal?)null, (total, value) => total is null && value is null ? null : (total ?? 0m) + (value ?? 0m));

    private static bool HasCompatibleReportedCurrency(IReadOnlyCollection<AiExecutionUsageFacts> calls) =>
        calls.Where(call => call.ProviderReportedCost.HasValue)
            .Select(call => call.ProviderReportedCurrency)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() <= 1;

    private static string? CompatibleReportedCurrency(IReadOnlyCollection<AiExecutionUsageFacts> calls) =>
        HasCompatibleReportedCurrency(calls)
            ? calls.Select(call => call.ProviderReportedCurrency).FirstOrDefault(currency => currency is not null)
            : null;
}

public sealed record AiModelResult(
    string? Text,
    string? StructuredJson,
    IReadOnlyCollection<AiToolCall> ToolCalls,
    AiExecutionUsageFacts Usage,
    string? ResponseId = null);

public sealed record AiStreamingChunk(
    string? TextDelta,
    AiToolCall? ToolCall,
    bool IsComplete,
    AiExecutionUsageFacts? Usage = null);

public sealed record AiEmbeddingRequest(
    string CorrelationId,
    Guid TenantId,
    IReadOnlyCollection<string> Inputs);

public sealed record AiEmbeddingResult(
    IReadOnlyCollection<IReadOnlyList<float>> Vectors,
    AiExecutionUsageFacts Usage);

public sealed record AiProviderHealthResult(
    string ProviderKey,
    string ModelKey,
    bool Available,
    DateTimeOffset CheckedAt,
    string? Detail = null);

public sealed record AiModelProviderDescriptor(
    string ProviderKey,
    string ModelKey,
    AiProviderHostingMode HostingMode,
    AiModelCapability Capabilities,
    bool Enabled,
    int Priority,
    IReadOnlySet<Guid>? AllowedTenantIds = null,
    string? DataResidency = null,
    bool AllowSensitivePrompts = false);

public sealed record AiModelSelectionRequest(
    Guid TenantId,
    AiWorkloadKind Workload,
    AiModelCapability RequiredCapabilities,
    string CorrelationId,
    bool AllowLocalRuntime = true,
    string? RequiredDataResidency = null);

public static class AiWorkloadCapabilities
{
    public static AiModelCapability RequiredFor(AiWorkloadKind workload) =>
        workload switch
        {
            AiWorkloadKind.ScannerParsing =>
                AiModelCapability.ChatCompletion | AiModelCapability.StructuredOutput,
            AiWorkloadKind.SchemaNavigation =>
                AiModelCapability.ChatCompletion | AiModelCapability.StructuredOutput,
            AiWorkloadKind.ExplanationGeneration or AiWorkloadKind.SuggestedQuestions or AiWorkloadKind.Summarization =>
                AiModelCapability.ChatCompletion,
            AiWorkloadKind.Embeddings => AiModelCapability.Embeddings,
            AiWorkloadKind.ResearchTool =>
                AiModelCapability.ChatCompletion | AiModelCapability.ToolCalling,
            _ => throw new ArgumentOutOfRangeException(nameof(workload))
        };
}

public sealed class AiModelProviderException(
    AiExecutionStatus status,
    string code,
    string message,
    Exception? innerException = null,
    int? httpStatusCode = null,
    string? providerErrorCode = null,
    string? providerRequestId = null,
    string? requestHost = null,
    IReadOnlyDictionary<string, string>? providerQuotaMetadata = null,
    string? providerResponseId = null,
    int? providerReportedInputTokens = null,
    int? providerReportedOutputTokens = null,
    string? providerCompletionReason = null,
    TimeSpan? providerLatency = null,
    int? evaluationModelCalls = null,
    int? accumulatedProviderInputTokens = null,
    int? accumulatedProviderOutputTokens = null,
    TimeSpan? evaluationLatency = null,
    int? evaluationRetryAttempts = null) : Exception(message, innerException)
{
    public AiExecutionStatus Status { get; } = status;

    public string Code { get; } = code;

    public int? HttpStatusCode { get; } = httpStatusCode;

    public string? ProviderErrorCode { get; } = providerErrorCode;

    public string? ProviderRequestId { get; } = providerRequestId;

    public string? RequestHost { get; } = requestHost;

    public IReadOnlyDictionary<string, string>? ProviderQuotaMetadata { get; } = providerQuotaMetadata;

    public string? ProviderResponseId { get; } = providerResponseId;

    public int? ProviderReportedInputTokens { get; } = providerReportedInputTokens;

    public int? ProviderReportedOutputTokens { get; } = providerReportedOutputTokens;

    public string? ProviderCompletionReason { get; } = providerCompletionReason;

    public TimeSpan? ProviderLatency { get; } = providerLatency;

    public int? EvaluationModelCalls { get; } = evaluationModelCalls;

    public int? AccumulatedProviderInputTokens { get; } = accumulatedProviderInputTokens;

    public int? AccumulatedProviderOutputTokens { get; } = accumulatedProviderOutputTokens;

    public TimeSpan? EvaluationLatency { get; } = evaluationLatency;

    public int? EvaluationRetryAttempts { get; } = evaluationRetryAttempts;
}

public interface IAiModelClient
{
    AiModelProviderDescriptor Descriptor { get; }

    Task<AiModelResult> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken);

    IAsyncEnumerable<AiStreamingChunk> StreamAsync(AiModelRequest request, CancellationToken cancellationToken);

    Task<AiEmbeddingResult> CreateEmbeddingsAsync(
        AiEmbeddingRequest request,
        CancellationToken cancellationToken);

    Task<AiProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken);
}

public interface IAiModelProviderResolver
{
    IReadOnlyCollection<IAiModelClient> ResolveCandidates(AiModelSelectionRequest request);
}

public interface IAiModelProviderRoutingPolicy
{
    string? DefaultProviderKey { get; }
}

public interface IAiProviderCapabilityRegistry
{
    IReadOnlyCollection<AiModelProviderDescriptor> GetAvailableProviders(Guid tenantId);
}

public sealed record ActiveAiModelProviderDescriptor(
    string? ConfiguredProviderKey,
    string? ProviderKey,
    string? ModelKey,
    AiModelCapability Capabilities,
    bool Available);

public interface IAiModelProviderDiagnostics
{
    ActiveAiModelProviderDescriptor GetActiveProvider(Guid tenantId);
}

public interface IAiExecutionUsageAccumulator
{
    void Record(AiExecutionUsageFacts facts);

    AiExecutionUsageSummary? GetSummary(string correlationId);

    IReadOnlyList<AiExecutionUsageFacts> GetUnbilled(string correlationId) => [];

    // Returns the completed model calls of the correlation id that no earlier claim took, so a
    // turn settled on more than one reservation never bills a call twice. A repeated claim key
    // returns its original calls, which keeps a replayed finalization deterministic.
    IReadOnlyList<AiExecutionUsageFacts> ClaimUnbilled(string correlationId, string claimKey);
}

public interface IAiExecutionTelemetrySink
{
    Task RecordAttemptAsync(AiExecutionUsageFacts facts, CancellationToken cancellationToken);
}

public interface IAiStructuredOutputValidator
{
    void Validate(AiStructuredOutputContract contract, string? structuredJson);
}

public interface IAiModelExecutionService
{
    Task<AiModelResult> ExecuteAsync(
        AiModelSelectionRequest selection,
        AiModelRequest request,
        CancellationToken cancellationToken);
}
