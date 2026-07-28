using FinancialCopilot.Application.FinancialData.Providers;

namespace FinancialCopilot.Infrastructure.Financial.Providers.CodalDb;

/// <summary>
/// Configuration for the read-only Noavaran Amin archive (MS SQL Server / legacy CodalDB snapshot)
/// provider. The connection string is read from configuration/secrets and is never hardcoded. The
/// account is expected to have read-only rights; the provider never issues writes or DDL.
/// Spec 051 renamed the physical source from <c>CodalDb</c> to <c>NoavaranArchiveSql</c>.
/// </summary>
public sealed class CodalDbProviderOptions
{
    public const string SectionName = "NoavaranArchiveSql";

    public string ProviderName { get; init; } = ProviderSources.NoavaranArchiveSqlName;

    public string ConnectionString { get; init; } = string.Empty;

    public int CommandTimeoutSeconds { get; init; } = 30;

    public int MaxReadParallelism { get; init; } = 4;

    /// <summary>Bounded transient-error retry attempts for SQL command execution.</summary>
    public int RetryCount { get; init; } = 2;

    /// <summary>Base backoff (milliseconds) between transient retries; grows linearly per attempt.</summary>
    public int RetryBaseDelayMilliseconds { get; init; } = 200;

    /// <summary>
    /// When both consolidated (IsComposing=1) and parent variants exist for the same period,
    /// prefer the consolidated statement. Default: true. Set to false to prefer parent statements.
    /// </summary>
    public bool PreferConsolidatedStatements { get; init; } = true;
}
