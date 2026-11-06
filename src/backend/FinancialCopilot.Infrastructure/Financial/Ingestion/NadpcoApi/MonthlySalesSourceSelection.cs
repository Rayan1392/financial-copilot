using System.Linq.Expressions;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

/// <summary>
/// The single rule for which persisted Noavaran reports are a company's single-month sales source.
/// ProductSales single-month reports carry OutputType 0, or null when they come from legacy payloads.
/// ServiceSales has no output type. ProductSales wins for a company-month when it has line items;
/// ServiceSales is the source only when it does not. The trend snapshot and the product revenue mix
/// both read through this rule, so a month that has a sales trend also has a sales composition.
/// </summary>
internal static class MonthlySalesSourceSelection
{
    public const string ProductSales = "ProductSales";
    public const string ServiceSales = "ServiceSales";

    /// <summary>EF-translatable filter for every single-month sales source candidate.</summary>
    public static readonly Expression<Func<NormalizedMonthlyReportRow, bool>> IsSingleMonthCandidate = report =>
        (report.ReportType == ProductSales && (report.OutputType == 0 || report.OutputType == null)) ||
        (report.ReportType == ServiceSales && report.OutputType == null);

    public static bool IsSingleMonthProductSales(NormalizedMonthlyReportRow report) =>
        report.ReportType == ProductSales && (report.OutputType == 0 || report.OutputType == null);

    public static bool IsSingleMonthServiceSales(NormalizedMonthlyReportRow report) =>
        report.ReportType == ServiceSales && report.OutputType == null;

    /// <summary>
    /// Chooses the source reports for one company-month from its candidates.
    /// </summary>
    /// <param name="candidates">Accepted single-month candidates of one company and one month.</param>
    /// <param name="hasLineItems">Whether a report id has at least one persisted line item.</param>
    public static MonthlySalesSource Choose(
        IReadOnlyCollection<NormalizedMonthlyReportRow> candidates,
        Func<Guid, bool> hasLineItems)
    {
        var productReports = candidates.Where(IsSingleMonthProductSales).ToArray();
        if (productReports.Any(report => hasLineItems(report.Id)))
            return new MonthlySalesSource(UsesProductSales: true, productReports);

        return new MonthlySalesSource(
            UsesProductSales: false,
            candidates.Where(IsSingleMonthServiceSales).ToArray());
    }
}

internal sealed record MonthlySalesSource(
    bool UsesProductSales,
    IReadOnlyList<NormalizedMonthlyReportRow> Reports);
