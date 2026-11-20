using System.Text.Json;
using FinancialCopilot.Application.AI.GroundedAnalysis;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.AI.ReportDiscussion;

namespace FinancialCopilot.Application.AI.SymbolGroundedFallback;

/// <summary>
/// The analysis call of a confirmed 163 offer (change «sol-deep-analysis-web-search», FR-1, FR-5):
/// <see cref="AiWorkloadKind.ExplanationGeneration"/> on the configured analysis model, structured output, the depth's
/// reasoning effort, output budget and timeout, the hosted web search only when the user chose it, no retry. Usage
/// facts are recorded by the execution service under the turn's correlation id.
/// </summary>
public sealed class LlmSymbolGroundedFallbackModel(
    IAiModelExecutionService executionService,
    GroundedAnalysisOptions options,
    GroundedAnalysisProfiles profiles) : ISymbolGroundedFallbackModel
{
    // The analysis output adds the cited external sources to the v2 reply (reportScore, «analytical-answer-and-report-score»).
    internal const string AnalysisSchemaJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["disposition", "answer", "closingOffer", "usedEvidenceIds", "reportScore", "scoreReason", "externalSources"],
          "properties": {
            "disposition": { "type": "string", "enum": ["Answered", "NotAnswerable"] },
            "answer": { "type": "string", "maxLength": 9000 },
            "closingOffer": { "type": "string", "maxLength": 400 },
            "usedEvidenceIds": { "type": "array", "maxItems": 80, "items": { "type": "string", "maxLength": 12 } },
        """ + ReportDiscussion.GroundedAnalysis.ScoreSchemaProperty + """
        ,
        """ + ReportDiscussion.GroundedAnalysis.ScoreReasonSchemaProperty + """
        ,
        """ + ReportDiscussion.GroundedAnalysis.ExternalSourcesSchemaProperty + """

          }
        }
        """;

    public static readonly AiStructuredOutputContract AnalysisContract = new(
        "SymbolGroundedFallbackAnalysis_v1",
        ["disposition", "answer", "closingOffer", "usedEvidenceIds", "reportScore", "scoreReason", "externalSources"],
        AnalysisSchemaJson);

    public async Task<SymbolGroundedFallbackModelReply> AnalyzeAsync(
        SymbolGroundedFallbackPrompt prompt,
        AnalysisChoice choice,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var profile = profiles.For(choice.Depth);
        var selection = GroundedAnalysisModelCall.Selection(profile, choice.WebSearch, tenantId, correlationId);
        var request = GroundedAnalysisModelCall.Request(profile, options.WebSearch, choice.WebSearch, AnalysisContract,
            prompt.SystemPrompt, prompt.UserContent, tenantId, correlationId);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(profile.TimeoutSeconds));
        AiModelResult result;
        try
        {
            result = await executionService.ExecuteAsync(selection, request, timeout.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ReportDiscussionModelUnavailableException("timeout", exception);
        }
        catch (AiModelProviderException exception)
        {
            throw new ReportDiscussionModelUnavailableException($"{exception.Status}:{exception.Code}", exception);
        }

        return Parse(result.StructuredJson ?? result.Text) with
        {
            ConsultedSources = result.ConsultedSources,
            Usage = result.Usage
        };
    }

    internal static SymbolGroundedFallbackModelReply Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ReportDiscussionModelUnavailableException("empty_output");
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new ReportDiscussionModelUnavailableException("invalid_json");
            var disposition = Text(root, "disposition") switch
            {
                "Answered" => SymbolGroundedFallbackDisposition.Answered,
                "NotAnswerable" => SymbolGroundedFallbackDisposition.NotAnswerable,
                _ => throw new ReportDiscussionModelUnavailableException("invalid_disposition")
            };
            var evidence = root.TryGetProperty("usedEvidenceIds", out var evidenceProperty) &&
                           evidenceProperty.ValueKind == JsonValueKind.Array
                ? evidenceProperty.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString()!)
                    .Take(60)
                    .ToArray()
                : [];
            return new SymbolGroundedFallbackModelReply(
                disposition, Text(root, "answer").Trim(), Text(root, "closingOffer").Trim(), evidence,
                ReportDiscussion.GroundedAnalysis.ParseScore(root),
                ReportDiscussion.GroundedAnalysis.ParseExternalSources(root),
                ScoreReason: ReportDiscussion.GroundedAnalysis.ParseScoreReason(root));
        }
        catch (JsonException exception)
        {
            throw new ReportDiscussionModelUnavailableException("invalid_json", exception);
        }
    }

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
}
