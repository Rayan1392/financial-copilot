using FinancialCopilot.Application.AI.ModelProviders;

namespace FinancialCopilot.Infrastructure.AI.ModelProviders;

public sealed class AiModelProviderOptions
{
    public const string SectionName = "AiModelProviders";

    public List<AiModelProviderRegistration> Providers { get; init; } = [];
}

public sealed class AiModelProviderRegistration
{
    public string ProviderKey { get; init; } = string.Empty;

    public string ModelKey { get; init; } = string.Empty;

    public AiProviderHostingMode HostingMode { get; init; }

    public string Adapter { get; init; } = "Fake";

    public string? Endpoint { get; init; }

    public string? CredentialSecretReference { get; init; }

    public bool Enabled { get; init; }

    public int Priority { get; init; } = 100;

    public AiModelCapability Capabilities { get; init; }

    public List<Guid> AllowedTenantIds { get; init; } = [];

    public string? DataResidency { get; init; }

    public bool AllowSensitivePrompts { get; init; }

    public bool LogPromptContent { get; init; }

    public int TimeoutSeconds { get; init; } = 30;
}

public sealed class AbravranAiProviderRegistration
{
    public string ProviderKey { get; init; } = "Abravran";

    public AiProviderHostingMode HostingMode => AiProviderHostingMode.ContractPending;

    public bool Enabled => false;

    public string IntegrationStatus => "Official API and authentication contract required before implementation.";
}
