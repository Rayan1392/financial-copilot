namespace FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWaves;

public sealed class CyclicalWavesProviderOptions
{
    public const string SectionName = "CyclicalWaves";

    public string ProviderName { get; init; } = "CyclicalWaves";

    public string BaseAddress { get; init; } = "https://back1.cyclicalwaves.com/api/";

    public string UserName { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public int TimeoutSeconds { get; init; } = 30;

    public int RetryCount { get; init; } = 2;

    public int TokenExpirationSafetyMarginSeconds { get; init; } = 60;

    public int CircuitBreakSeconds { get; init; } = 60;

    public int CircuitFailureThreshold { get; init; } = 5;

    /// <summary>Hard provider-response bound for the P/S visualization endpoints.</summary>
    public int PsMaxResponseBytes { get; init; } = 5 * 1024 * 1024;

    public int PsMaxHistoryPointsPerCompany { get; init; } = 10_000;
}
