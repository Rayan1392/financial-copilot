namespace FinancialCopilot.Infrastructure.Financial.Providers.CodalDb;

/// <summary>
/// Configuration for the read-only CodalDB (MS SQL Server) provider. The connection string is read
/// from configuration/secrets and is never hardcoded. The account is expected to have read-only
/// rights; the provider never issues writes or DDL.
/// </summary>
public sealed class CodalDbProviderOptions
{
    public const string SectionName = "CodalDb";

    public string ProviderName { get; init; } = "CodalDb";

    public string ConnectionString { get; init; } = string.Empty;

    public int CommandTimeoutSeconds { get; init; } = 30;

    public int MaxReadParallelism { get; init; } = 4;

    /// <summary>Bounded transient-error retry attempts for SQL command execution.</summary>
    public int RetryCount { get; init; } = 2;

    /// <summary>Base backoff (milliseconds) between transient retries; grows linearly per attempt.</summary>
    public int RetryBaseDelayMilliseconds { get; init; } = 200;
}
