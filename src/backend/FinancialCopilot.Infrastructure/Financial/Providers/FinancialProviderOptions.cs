namespace FinancialCopilot.Infrastructure.Financial.Providers;

public sealed class FinancialProviderOptions
{
    public const string SectionName = "FinancialDataProvider";

    public string ProviderName { get; init; } = "ConfiguredFinancialProvider";

    public string BaseAddress { get; init; } = "https://provider.invalid/";

    public string ApiKey { get; init; } = string.Empty;

    public int TimeoutSeconds { get; init; } = 10;

    public int RetryCount { get; init; } = 2;

    public int CircuitBreakSeconds { get; init; } = 30;

    public int CircuitFailureThreshold { get; init; } = 3;
}
