namespace FinancialCopilot.Application.FinancialData.Providers;

/// <summary>
/// Maps user query intent to the NADPCO outputTypeId stored in <c>MonthlyReports.OutputType</c>.
/// Values mirror the vendor's outputTypeId parameter (spec 059).
/// </summary>
public enum MonthlyActivityQueryIntent
{
    SingleMonth = 0,
    YearToDate = 1,
    Adjustment = 2,
    YearToDatePreviousAdjusted = 3,
    YearToDatePrevious = 4
}

/// <summary>
/// Determines which NADPCO outputTypeId (0–4) the user intends when querying a monthly metric.
/// The resolved intent is passed as a filter to <c>MonthlyReportAggregateInputSource</c>
/// so only one set of rows per company-month period is read.
/// </summary>
public interface IMonthlyActivityOutputTypeResolver
{
    /// <param name="userQueryHint">
    /// Optional free-text clue extracted from the user's query (e.g. "از ابتدای سال", "ماه جاری").
    /// May be null when the query carries no explicit time-scope modifier.
    /// </param>
    /// <param name="hasExplicitMonth">
    /// True when the user named a specific month (e.g. "فروش اردیبهشت کگل").
    /// </param>
    MonthlyActivityQueryIntent Resolve(string? userQueryHint, bool hasExplicitMonth);
}
