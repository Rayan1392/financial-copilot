using System.Text.Json;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.AI.ReportDiscussion;

namespace FinancialCopilot.Application.AI.SymbolGroundedFallback;

/// <summary>
/// The single fallback model call (FR-4): <see cref="AiWorkloadKind.ExplanationGeneration"/>, structured output,
/// no tools, explicit timeout, no retry. Usage facts are recorded by the execution service under the turn's correlation id.
/// </summary>
public sealed class LlmSymbolGroundedFallbackModel(
    IAiModelExecutionService executionService,
    SymbolGroundedFallbackOptions options) : ISymbolGroundedFallbackModel
{
    // v2 adds reportScore (change «analytical-answer-and-report-score», D3).
    private const string SchemaJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["disposition", "answer", "closingOffer", "usedEvidenceIds", "reportScore"],
          "properties": {
            "disposition": { "type": "string", "enum": ["Answered", "NotAnswerable"] },
            "answer": { "type": "string", "maxLength": 4000 },
            "closingOffer": { "type": "string", "maxLength": 400 },
            "usedEvidenceIds": { "type": "array", "maxItems": 60, "items": { "type": "string", "maxLength": 12 } },
        """ + GroundedAnalysis.ScoreSchemaProperty + """

          }
        }
        """;

    private static readonly AiStructuredOutputContract Contract = new(
        "SymbolGroundedFallbackReply_v2",
        ["disposition", "answer", "closingOffer", "usedEvidenceIds", "reportScore"],
        SchemaJson);

    public async Task<SymbolGroundedFallbackModelReply> AnswerAsync(
        SymbolGroundedFallbackPrompt prompt,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
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
            StructuredOutput: Contract,
            MaxOutputTokens: options.MaxOutputTokens);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.ModelTimeoutSeconds));
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

        return Parse(result.StructuredJson ?? result.Text);
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
                GroundedAnalysis.ParseScore(root));
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
