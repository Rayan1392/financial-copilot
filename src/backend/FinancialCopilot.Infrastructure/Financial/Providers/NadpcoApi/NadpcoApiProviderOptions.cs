using FinancialCopilot.Application.FinancialData.Providers;

namespace FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;

/// <summary>
/// Configuration for the Noavaran Amin current HTTP API source. Spec 051 renamed the physical source
/// from <c>NadpcoApi</c> to <c>NoavaranCurrentApi</c> (it is a source mode of the Noavaran Amin vendor,
/// not a standalone vendor).
/// </summary>
public sealed class NadpcoApiProviderOptions
{
    public const string SectionName = "NoavaranCurrentApi";

    public string ProviderName { get; init; } = ProviderSources.NoavaranCurrentApiName;

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
    public int DefaultTokenLifetimeMinutes { get; init; } = 1380;

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

    /// <summary>
    /// Earliest Jalali date for monthly product/service activity requests. Access to the Noavaran
    /// current API monthly-activity endpoints is granted only from Shamsi <b>1404</b> onward; calling
    /// 1403 and earlier returns HTTP 500 (no permission), so the default start is fixed at 1404/01/01.
    /// Monthly data before 1404 must come from the archive source. See spec 042 / order 54 (spec 053).
    /// </summary>
    public string? MonthlyActivityFromDate { get; init; } = "1404/01/01";

    public string? MonthlyActivityToDate { get; init; }

    public int? MonthlyActivityOutputType { get; init; }

    public int OrchestrationOverlapDays { get; init; } = 7;
}
