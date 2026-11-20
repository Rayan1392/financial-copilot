using System.Text.Json;
using FinancialCopilot.Application.AI.GroundedAnalysis;
using FinancialCopilot.Application.AI.ModelProviders;

namespace FinancialCopilot.Application.AI.ReportDiscussion;

/// <summary>What the analysis call returned (change «sol-deep-analysis-web-search»). Declared sources are unverified.</summary>
public sealed record ReportDiscussionAnalysisReply(
    ReportDiscussionDisposition Disposition,
    string Answer,
    IReadOnlyList<string> UsedEvidenceIds,
    int? ReportScore,
    IReadOnlyList<DeclaredExternalSource> DeclaredSources,
    IReadOnlyCollection<string> ConsultedSources,
    AiExecutionUsageFacts? Usage = null,
    // Amendment A2: the model's one-sentence reason for the score (shown only through GroundedAnalysis.ScoreBlock).
    string? ScoreReason = null);

/// <summary>
/// The two calls of a 162 analysis turn (design-proposal §5): a light routing call with the workload's existing model
/// and no tools, then the analysis call with the depth's profile. Both throw
/// <see cref="ReportDiscussionModelUnavailableException"/> on provider failure, timeout or invalid output.
/// </summary>
public interface IReportDiscussionAnalysisModel
{
    Task<ReportDiscussionDisposition> DecideAsync(
        ReportDiscussionPrompt prompt,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken);

    Task<ReportDiscussionAnalysisReply> AnalyzeAsync(
        ReportDiscussionPrompt prompt,
        AnalysisChoice choice,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken);
}

public sealed class LlmReportDiscussionAnalysisModel(
    IAiModelExecutionService executionService,
    GroundedAnalysisOptions options,
    GroundedAnalysisProfiles profiles) : IReportDiscussionAnalysisModel
{
    private const string DispositionSchemaJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["disposition"],
          "properties": {
            "disposition": { "type": "string", "enum": ["Answered", "MissingInReport", "NeedsDifferentData", "OutsideReport"] }
          }
        }
        """;

    internal const string AnalysisSchemaJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["disposition", "answer", "usedEvidenceIds", "reportScore", "scoreReason", "externalSources"],
          "properties": {
            "disposition": { "type": "string", "enum": ["Answered", "MissingInReport"] },
            "answer": { "type": "string", "maxLength": 9000 },
            "usedEvidenceIds": { "type": "array", "maxItems": 80, "items": { "type": "string", "maxLength": 12 } },
        """ + GroundedAnalysis.ScoreSchemaProperty + """
        ,
        """ + GroundedAnalysis.ScoreReasonSchemaProperty + """
        ,
        """ + GroundedAnalysis.ExternalSourcesSchemaProperty + """

          }
        }
        """;

    public static readonly AiStructuredOutputContract DispositionContract = new(
        "ReportDiscussionDisposition_v1", ["disposition"], DispositionSchemaJson);

    public static readonly AiStructuredOutputContract AnalysisContract = new(
        "ReportDiscussionAnalysis_v1",
        ["disposition", "answer", "usedEvidenceIds", "reportScore", "scoreReason", "externalSources"],
        AnalysisSchemaJson);

    public async Task<ReportDiscussionDisposition> DecideAsync(
        ReportDiscussionPrompt prompt,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        // Disposition:Model null = the workload's existing model; no reasoning setting and never a tool (PO-14).
        var selection = new AiModelSelectionRequest(
            tenantId,
            AiWorkloadKind.ExplanationGeneration,
            AiModelCapability.ChatCompletion | AiModelCapability.StructuredOutput,
            correlationId);
        var request = new AiModelRequest(
            correlationId,
            tenantId,
            AiWorkloadKind.ExplanationGeneration,
            [
                new AiConversationMessage(AiMessageRole.System, prompt.SystemPrompt),
                new AiConversationMessage(AiMessageRole.User, prompt.UserContent)
            ],
            StructuredOutput: DispositionContract,
            MaxOutputTokens: options.Disposition.MaxOutputTokens,
            ModelKeyOverride: string.IsNullOrWhiteSpace(options.Disposition.Model) ? null : options.Disposition.Model.Trim());
        var result = await ExecuteAsync(selection, request, options.Disposition.TimeoutSeconds, cancellationToken);
        return ParseDisposition(result.StructuredJson ?? result.Text);
    }

    public async Task<ReportDiscussionAnalysisReply> AnalyzeAsync(
        ReportDiscussionPrompt prompt,
        AnalysisChoice choice,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var profile = profiles.For(choice.Depth);
        var result = await ExecuteAsync(
            GroundedAnalysisModelCall.Selection(profile, choice.WebSearch, tenantId, correlationId),
            GroundedAnalysisModelCall.Request(profile, options.WebSearch, choice.WebSearch, AnalysisContract,
                prompt.SystemPrompt, prompt.UserContent, tenantId, correlationId),
            profile.TimeoutSeconds,
            cancellationToken);
        return ParseAnalysis(result.StructuredJson ?? result.Text, result.ConsultedSources, result.Usage);
    }

    private async Task<AiModelResult> ExecuteAsync(
        AiModelSelectionRequest selection,
        AiModelRequest request,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            return await executionService.ExecuteAsync(selection, request, timeout.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ReportDiscussionModelUnavailableException("timeout", exception);
        }
        catch (AiModelProviderException exception)
        {
            throw new ReportDiscussionModelUnavailableException($"{exception.Status}:{exception.Code}", exception);
        }
    }

    internal static ReportDiscussionDisposition ParseDisposition(string? json)
    {
        using var document = ParseObject(json);
        return Text(document.RootElement, "disposition") switch
        {
            "Answered" => ReportDiscussionDisposition.Answered,
            "MissingInReport" => ReportDiscussionDisposition.MissingInReport,
            "NeedsDifferentData" => ReportDiscussionDisposition.NeedsDifferentData,
            "OutsideReport" => ReportDiscussionDisposition.OutsideReport,
            _ => throw new ReportDiscussionModelUnavailableException("invalid_disposition")
        };
    }

    internal static ReportDiscussionAnalysisReply ParseAnalysis(
        string? json,
        IReadOnlyCollection<string>? consultedSources = null,
        AiExecutionUsageFacts? usage = null)
    {
        using var document = ParseObject(json);
        var root = document.RootElement;
        var disposition = Text(root, "disposition") switch
        {
            "Answered" => ReportDiscussionDisposition.Answered,
            "MissingInReport" => ReportDiscussionDisposition.MissingInReport,
            _ => throw new ReportDiscussionModelUnavailableException("invalid_disposition")
        };
        return new ReportDiscussionAnalysisReply(
            disposition,
            Text(root, "answer").Trim(),
            EvidenceIds(root),
            GroundedAnalysis.ParseScore(root),
            GroundedAnalysis.ParseExternalSources(root),
            consultedSources ?? [],
            usage,
            GroundedAnalysis.ParseScoreReason(root));
    }

    internal static JsonDocument ParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ReportDiscussionModelUnavailableException("empty_output");
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new ReportDiscussionModelUnavailableException("invalid_json", exception);
        }

        if (document.RootElement.ValueKind == JsonValueKind.Object) return document;
        document.Dispose();
        throw new ReportDiscussionModelUnavailableException("invalid_json");
    }

    internal static IReadOnlyList<string> EvidenceIds(JsonElement root) =>
        root.TryGetProperty("usedEvidenceIds", out var property) && property.ValueKind == JsonValueKind.Array
            ? property.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!)
                .Take(80)
                .ToArray()
            : [];

    internal static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
}
