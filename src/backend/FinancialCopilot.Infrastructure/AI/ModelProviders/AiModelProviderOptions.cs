using FinancialCopilot.Application.AI.ModelProviders;

namespace FinancialCopilot.Infrastructure.AI.ModelProviders;

public sealed class AiModelProviderOptions
{
    public const string SectionName = "AiModelProviders";

    public List<AiModelProviderRegistration> Providers { get; init; } = [];
}

public sealed class AiProviderOptions
{
    public const string SectionName = "AiProvider";

    public string? DefaultProvider { get; init; }

    public OpenAiProviderOptions OpenAI { get; init; } = new();

    public DeepSeekProviderOptions DeepSeek { get; init; } = new();

    public AbravranProviderOptions Abravran { get; init; } = new();
}

public sealed class OpenAiProviderOptions
{
    public string ApiKey { get; init; } = string.Empty;

    public string Model { get; init; } = "gpt-5";
}

public sealed class DeepSeekProviderOptions
{
    public string ApiKey { get; init; } = string.Empty;

    public string Model { get; init; } = "deepseek-chat";

    public string BaseUrl { get; init; } = "https://api.deepseek.com";

    public bool ThinkingEnabled { get; init; }

    public string? ReasoningEffort { get; init; }
}

public sealed class AbravranProviderOptions
{
    public int MaxTokens { get; init; } = 3000;

    public double Temperature { get; init; } = 0.7;
}

public sealed class ConfiguredAiProviderRoutingPolicy(
    Microsoft.Extensions.Options.IOptions<AiProviderOptions> options) : IAiModelProviderRoutingPolicy
{
    public string? DefaultProviderKey => options.Value.DefaultProvider;
}

public sealed class AiModelProviderRegistration
{
    public string ProviderKey { get; init; } = string.Empty;

    public string ModelKey { get; init; } = string.Empty;

    public AiProviderHostingMode HostingMode { get; init; }

    public string Adapter { get; init; } = "Fake";

    public string? Endpoint { get; init; }

    public string? CredentialSecretReference { get; init; }

    // Development-only fallback. Production credentials must use CredentialSecretReference.
    public string? ApiKey { get; init; }

    public bool Enabled { get; init; }

    public int Priority { get; init; } = 100;

    public AiModelCapability Capabilities { get; init; }

    public List<Guid> AllowedTenantIds { get; init; } = [];

    public string? DataResidency { get; init; }

    public bool AllowSensitivePrompts { get; init; }

    public bool LogPromptContent { get; init; }

    public int TimeoutSeconds { get; init; } = 30;
}
