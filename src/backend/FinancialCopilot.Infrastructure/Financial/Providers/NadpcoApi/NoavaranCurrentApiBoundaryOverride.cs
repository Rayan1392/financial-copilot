namespace FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;

/// <summary>
/// Per-run override of the Noavaran current-API Shamsi start boundary (spec 053 AC #3). A DataAdmin
/// backfill can lower the configured 1403 boundary for a single run without mutating persisted
/// configuration; the scheduled worker keeps using the configured value. The provider client consults
/// this holder when building <c>fromYear</c>/<c>fromDate</c> query parameters and falls back to the
/// configured options when no override is set. Scoped to the ingestion request scope.
/// </summary>
public interface INoavaranCurrentApiBoundaryOverride
{
    /// <summary>Override Shamsi start year for statement/fundamental-index requests; null = use configured value.</summary>
    int? FromShamsiYear { get; }

    void Set(int? fromShamsiYear);
}

public sealed class NoavaranCurrentApiBoundaryOverride : INoavaranCurrentApiBoundaryOverride
{
    public int? FromShamsiYear { get; private set; }

    public void Set(int? fromShamsiYear) => FromShamsiYear = fromShamsiYear;
}
