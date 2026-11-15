using System.Text.Json;
using FinancialCopilot.Application.AI.ModelProviders;

namespace FinancialCopilot.Application.AI.ReportDiscussion;

/// <summary>
/// The single discussion model call (FR-6, D9): <see cref="AiWorkloadKind.ExplanationGeneration"/>, structured output,
/// no tools, explicit timeout, no retry. Usage facts are recorded by the execution service under the turn's correlation id.
/// </summary>
public sealed class LlmReportDiscussionModel(
    IAiModelExecutionService executionService,
    ReportDiscussionOptions options) : IReportDiscussionModel
{
    private const string SchemaJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["disposition", "answer", "usedEvidenceIds"],
          "properties": {
            "disposition": { "type": "string", "enum": ["Answered", "MissingInReport", "NeedsDifferentData", "OutsideReport"] },
            "answer": { "type": "string", "maxLength": 4000 },
            "usedEvidenceIds": { "type": "array", "maxItems": 60, "items": { "type": "string", "maxLength": 12 } }
          }
        }
        """;

    private static readonly AiStructuredOutputContract Contract = new(
        "ReportDiscussionReply_v1",
        ["disposition", "answer", "usedEvidenceIds"],
        SchemaJson);

    public async Task<ReportDiscussionModelReply> AnswerAsync(
        ReportDiscussionPrompt prompt,
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
            throw new ReportDiscussionModelUnavailableException(exception.Status.ToString(), exception);
        }

        return Parse(result.StructuredJson ?? result.Text);
    }

    internal static ReportDiscussionModelReply Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ReportDiscussionModelUnavailableException("empty_output");
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var dispositionText = root.TryGetProperty("disposition", out var dispositionProperty) &&
                                  dispositionProperty.ValueKind == JsonValueKind.String
                ? dispositionProperty.GetString()
                : null;
            if (dispositionText is not ("Answered" or "MissingInReport" or "NeedsDifferentData" or "OutsideReport") ||
                !Enum.TryParse<ReportDiscussionDisposition>(dispositionText, out var disposition))
                throw new ReportDiscussionModelUnavailableException("invalid_disposition");
            var answer = root.TryGetProperty("answer", out var answerProperty) && answerProperty.ValueKind == JsonValueKind.String
                ? answerProperty.GetString() ?? string.Empty
                : string.Empty;
            var evidence = root.TryGetProperty("usedEvidenceIds", out var evidenceProperty) &&
                           evidenceProperty.ValueKind == JsonValueKind.Array
                ? evidenceProperty.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString()!)
                    .Take(60)
                    .ToArray()
                : [];
            return new ReportDiscussionModelReply(disposition, answer.Trim(), evidence);
        }
        catch (JsonException exception)
        {
            throw new ReportDiscussionModelUnavailableException("invalid_json", exception);
        }
    }
}
