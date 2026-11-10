namespace FinancialCopilot.Application.FinancialData.Ingestion;

public enum MonthlyReportCoverageKind
{
    /// <summary>The company has a verified monthly report route; missing data is a data gap.</summary>
    HasRoute,

    /// <summary>The company's report type has no verified monthly route in the data source.</summary>
    NoRoute,

    /// <summary>The company is outside the eligible universe or is known only to another provider.</summary>
    OutsideScope,

    /// <summary>Coverage is not known: missing metadata, an unknown company, or a failed read.</summary>
    Unknown,

    /// <summary>More than one company matches the symbol.</summary>
    CompanyAmbiguous
}

/// <summary>Whether a company can have persisted monthly data at all. Labels are set only for NoRoute.</summary>
public sealed record MonthlyReportCoverage(
    MonthlyReportCoverageKind Kind,
    string? CategoryFa = null,
    string? CategoryEn = null);

public interface IMonthlyReportCoverageReader
{
    /// <summary>Never throws, except <see cref="OperationCanceledException"/> when the caller cancels.</summary>
    Task<MonthlyReportCoverage> ReadAsync(string symbol, CancellationToken cancellationToken);
}
