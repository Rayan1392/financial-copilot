using System.Text.Json;

namespace FinancialCopilot.Application.AI.ModelProviders;

public sealed class CapabilityBasedAiModelProviderResolver(
    IEnumerable<IAiModelClient> clients) : IAiModelProviderResolver, IAiProviderCapabilityRegistry
{
    private readonly IReadOnlyCollection<IAiModelClient> _clients = clients.ToArray();

    public IReadOnlyCollection<IAiModelClient> ResolveCandidates(AiModelSelectionRequest request) =>
        _clients
            .Where(client => IsAllowed(client.Descriptor, request))
            .OrderBy(client => client.Descriptor.Priority)
            .ToArray();

    public IReadOnlyCollection<AiModelProviderDescriptor> GetAvailableProviders(Guid tenantId) =>
        _clients
            .Select(client => client.Descriptor)
            .Where(descriptor =>
                descriptor.Enabled &&
                (descriptor.AllowedTenantIds is null || descriptor.AllowedTenantIds.Contains(tenantId)))
            .OrderBy(descriptor => descriptor.Priority)
            .ToArray();

    private static bool IsAllowed(
        AiModelProviderDescriptor descriptor,
        AiModelSelectionRequest request) =>
        descriptor.Enabled &&
        (descriptor.Capabilities & request.RequiredCapabilities) == request.RequiredCapabilities &&
        (request.AllowLocalRuntime || descriptor.HostingMode != AiProviderHostingMode.Local) &&
        (descriptor.AllowedTenantIds is null || descriptor.AllowedTenantIds.Contains(request.TenantId)) &&
        (request.RequiredDataResidency is null ||
            string.Equals(descriptor.DataResidency, request.RequiredDataResidency, StringComparison.OrdinalIgnoreCase));
}

public sealed class JsonStructuredOutputValidator : IAiStructuredOutputValidator
{
    public void Validate(AiStructuredOutputContract contract, string? structuredJson)
    {
        if (string.IsNullOrWhiteSpace(structuredJson))
        {
            throw Invalid(contract.SchemaName, "Structured output was not provided.");
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(structuredJson);
        }
        catch (JsonException exception)
        {
            throw new AiModelProviderException(
                AiExecutionStatus.InvalidStructuredOutput,
                "invalid_json",
                $"Structured output for '{contract.SchemaName}' is invalid JSON.",
                exception);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw Invalid(contract.SchemaName, "Structured output must be a JSON object.");
            }

            foreach (var property in contract.RequiredRootProperties)
            {
                if (!document.RootElement.TryGetProperty(property, out _))
                {
                    throw Invalid(contract.SchemaName, $"Required property '{property}' is missing.");
                }
            }
        }
    }

    private static AiModelProviderException Invalid(string schemaName, string detail) =>
        new(
            AiExecutionStatus.InvalidStructuredOutput,
            "schema_validation_failed",
            $"Structured output for '{schemaName}' failed validation. {detail}");
}

public sealed class AiModelExecutionService(
    IAiModelProviderResolver resolver,
    IAiStructuredOutputValidator structuredOutputValidator,
    IAiExecutionTelemetrySink telemetrySink,
    TimeProvider timeProvider) : IAiModelExecutionService
{
    public async Task<AiModelResult> ExecuteAsync(
        AiModelSelectionRequest selection,
        AiModelRequest request,
        CancellationToken cancellationToken)
    {
        if (selection.TenantId != request.TenantId ||
            selection.Workload != request.Workload ||
            !string.Equals(selection.CorrelationId, request.CorrelationId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "AI model selection and execution request must carry the same tenant, workload, and correlation identifiers.");
        }

        var required = selection.RequiredCapabilities | AiWorkloadCapabilities.RequiredFor(selection.Workload);
        var normalizedSelection = selection with { RequiredCapabilities = required };
        var candidates = resolver.ResolveCandidates(normalizedSelection);

        if (candidates.Count == 0)
        {
            throw new AiModelProviderException(
                AiExecutionStatus.CapabilityUnavailable,
                "compatible_provider_not_configured",
                "No configured AI model provider satisfies the requested capabilities and routing policy.");
        }

        AiModelProviderException? lastFailure = null;
        var attempt = 0;

        foreach (var client in candidates)
        {
            attempt++;
            var startedAt = timeProvider.GetUtcNow();

            try
            {
                var result = await client.CompleteAsync(request, cancellationToken);

                if (request.StructuredOutput is not null)
                {
                    structuredOutputValidator.Validate(request.StructuredOutput, result.StructuredJson);
                }

                var facts = result.Usage with
                {
                    CorrelationId = selection.CorrelationId,
                    ProviderKey = client.Descriptor.ProviderKey,
                    ModelKey = client.Descriptor.ModelKey,
                    AttemptNumber = attempt,
                    Duration = timeProvider.GetUtcNow() - startedAt,
                    Status = AiExecutionStatus.Completed
                };
                await telemetrySink.RecordAttemptAsync(facts, cancellationToken);
                return result with { Usage = facts };
            }
            catch (AiModelProviderException exception)
            {
                lastFailure = exception;
                await telemetrySink.RecordAttemptAsync(
                    new AiExecutionUsageFacts(
                        selection.CorrelationId,
                        client.Descriptor.ProviderKey,
                        client.Descriptor.ModelKey,
                        exception.Status,
                        timeProvider.GetUtcNow() - startedAt,
                        attempt,
                        FailureCode: exception.Code),
                    cancellationToken);
            }
        }

        throw lastFailure!;
    }
}
