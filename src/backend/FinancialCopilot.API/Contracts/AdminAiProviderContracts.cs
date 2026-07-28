namespace FinancialCopilot.API.Contracts;

public sealed record AdminAiProviderResponse(
    string? ConfiguredProvider,
    string? Provider,
    string? Model,
    string Capabilities,
    bool Available);
