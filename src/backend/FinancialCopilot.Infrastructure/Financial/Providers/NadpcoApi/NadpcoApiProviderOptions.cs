namespace FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;

public sealed class NadpcoApiProviderOptions
{
    public const string SectionName = "NadpcoApi";

    public string ProviderName { get; init; } = "NadpcoApi";

    public string BaseAddress { get; init; } = "https://data3.nadpco.com/";

    public string UserName { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public int TimeoutSeconds { get; init; } = 30;

    public int RetryCount { get; init; } = 2;

    public int CircuitBreakSeconds { get; init; } = 60;

    public int CircuitFailureThreshold { get; init; } = 5;

    public int BatchSize { get; init; } = 100;

    public int MaxReadParallelism { get; init; } = 4;

    /// <summary>
    /// Fallback token lifetime until the vendor confirms a definitive expiry field. The provider
    /// still honors explicit expiry fields when present in the token response.
    /// </summary>
    public int DefaultTokenLifetimeMinutes { get; init; } = 20;

    public int? StatementFromYear { get; init; } = 1400;

    public int? StatementToYear { get; init; }

    public int? StatementPeriodTypeId { get; init; }

    public bool? StatementIsAudited { get; init; }

    public bool? StatementIsRepresented { get; init; }

    public bool? StatementIsComposing { get; init; }

    public int? FundamentalIndexFromYear { get; init; } = 1400;

    public int? FundamentalIndexToYear { get; init; }

    public int? FundamentalIndexPeriodTypeId { get; init; }

    public bool? FundamentalIndexIsAudited { get; init; }

    public bool? FundamentalIndexIsRepresented { get; init; }

    public bool? FundamentalIndexIsComposing { get; init; }

    public string? MonthlyActivityFromDate { get; init; } = "1400/01/01";

    public string? MonthlyActivityToDate { get; init; }

    public int? MonthlyActivityOutputType { get; init; }
}
