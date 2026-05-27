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
    ResearchTool
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
    IReadOnlyCollection<string> RequiredRootProperties);

public sealed record AiModelRequest(
    string CorrelationId,
    Guid TenantId,
    AiWorkloadKind Workload,
    IReadOnlyCollection<AiConversationMessage> Messages,
    AiStructuredOutputContract? StructuredOutput = null,
    IReadOnlyCollection<AiToolDefinition>? Tools = null,
    bool Stream = false);

public sealed record AiToolCall(string Id, string Name, string ArgumentsJson);

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
    string? FailureCode = null);

public sealed record AiModelResult(
    string? Text,
    string? StructuredJson,
    IReadOnlyCollection<AiToolCall> ToolCalls,
    AiExecutionUsageFacts Usage);

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
    Exception? innerException = null) : Exception(message, innerException)
{
    public AiExecutionStatus Status { get; } = status;

    public string Code { get; } = code;
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

public interface IAiProviderCapabilityRegistry
{
    IReadOnlyCollection<AiModelProviderDescriptor> GetAvailableProviders(Guid tenantId);
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
