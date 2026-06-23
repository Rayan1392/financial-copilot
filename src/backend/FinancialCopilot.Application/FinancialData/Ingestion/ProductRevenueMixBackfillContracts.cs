namespace FinancialCopilot.Application.FinancialData.Ingestion;

/// <summary>
/// Manual, DataAdmin-only one-time backfill of persisted company product revenue mix rows from
/// already-normalized Noavaran monthly ProductSales data. Reuses the existing calculator so the
/// historical fill path matches the live ingestion path exactly.
/// </summary>
public interface IProductRevenueMixBackfillService
{
    Task<ProductRevenueMixBackfillResult> RunAsync(
        ProductRevenueMixBackfillRequest request,
        CancellationToken cancellationToken);
}

public sealed record ProductRevenueMixBackfillRequest(string RequestedBy);

public sealed record ProductRevenueMixBackfillResult(
    string Outcome,
    string RequestedBy,
    int CompaniesConsidered,
    int CompanyMonthsDiscovered,
    int CompanyMonthsProcessed,
    int CompanyMonthsSkippedNoSalesLineItems,
    string Duration);
