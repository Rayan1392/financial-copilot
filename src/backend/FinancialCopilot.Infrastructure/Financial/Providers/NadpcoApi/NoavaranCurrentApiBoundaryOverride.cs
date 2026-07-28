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

    /// <summary>Override Jalali from-date for monthly-activity requests (spec 057); null = configured value.</summary>
    string? MonthlyActivityFromDate { get; }

    /// <summary>Override Jalali to-date for monthly-activity requests (spec 057); null = configured value.</summary>
    string? MonthlyActivityToDate { get; }

    void Set(int? fromShamsiYear);

    /// <summary>
    /// Bounds the current run's monthly-activity request to an explicit Jalali window (one Shamsi
    /// month for backfill/steady-state runs). The client still clamps to the permitted 1404 floor.
    /// </summary>
    void SetMonthlyActivityWindow(string? fromDate, string? toDate);
}

public sealed class NoavaranCurrentApiBoundaryOverride : INoavaranCurrentApiBoundaryOverride
{
    public int? FromShamsiYear { get; private set; }

    public string? MonthlyActivityFromDate { get; private set; }

    public string? MonthlyActivityToDate { get; private set; }

    public void Set(int? fromShamsiYear) => FromShamsiYear = fromShamsiYear;

    public void SetMonthlyActivityWindow(string? fromDate, string? toDate)
    {
        MonthlyActivityFromDate = fromDate;
        MonthlyActivityToDate = toDate;
    }
}
