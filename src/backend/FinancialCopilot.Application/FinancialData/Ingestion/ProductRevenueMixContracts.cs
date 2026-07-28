namespace FinancialCopilot.Application.FinancialData.Ingestion;

// ---------------------------------------------------------------------------
// Query input
// ---------------------------------------------------------------------------

public sealed record ProductRevenueMixQuery(
    string CompanySymbol,
    int? Year = null,
    byte? Month = null,
    int TopN = 3);

// ---------------------------------------------------------------------------
// Response DTOs (Application boundary — no ORM types)
// ---------------------------------------------------------------------------

public sealed record ProductRevenueMixProductItem(
    string ProductName,
    decimal SalesAmount,
    decimal RevenueSharePercentage,
    int Rank,
    bool IsDominantProduct,
    decimal? ProductionQuantity,
    decimal? SalesQuantity,
    decimal? SalesRate);

public sealed record ProductRevenueMixResponse(
    string CompanySymbol,
    string? CompanyName,
    int ReportYear,
    byte ReportMonth,
    decimal TotalSalesAmount,
    string SourceProviderName,
    IReadOnlyList<ProductRevenueMixProductItem> Products);

// ---------------------------------------------------------------------------
// Repository interface (Application owns it; Infrastructure implements)
// ---------------------------------------------------------------------------

public interface ICompanyProductRevenueMixRepository
{
    /// <summary>Latest persisted revenue mix for a company (most recent year/month).</summary>
    Task<ProductRevenueMixResponse?> GetLatestAsync(
        string externalCompanyId,
        CancellationToken ct = default);

    /// <summary>Revenue mix for a specific Jalali year and month.</summary>
    Task<ProductRevenueMixResponse?> GetByPeriodAsync(
        string externalCompanyId,
        int year,
        byte month,
        CancellationToken ct = default);

    /// <summary>Upserts (replace) all product rows for one company/month atomically.</summary>
    Task UpsertAsync(
        IReadOnlyList<ProductRevenueMixUpsertRow> rows,
        CancellationToken ct = default);
}

public sealed record ProductRevenueMixUpsertRow(
    Guid Id,
    string ExternalCompanyId,
    string? CompanySymbol,
    string? CompanyName,
    int ReportYear,
    byte ReportMonth,
    string? FiscalEndDate,
    string ProductName,
    decimal? ProductionQuantity,
    decimal? SalesQuantity,
    decimal? SalesRate,
    decimal SalesAmount,
    decimal TotalCompanySalesAmount,
    decimal RevenueSharePercentage,
    int ProductRank,
    bool IsDominantProduct,
    string SourceProviderName,
    DateTimeOffset CalculatedAtUtc);

// ---------------------------------------------------------------------------
// Use-case interface
// ---------------------------------------------------------------------------

public interface IProductRevenueMixQueryUseCase
{
    Task<ProductRevenueMixResponse?> ExecuteAsync(
        ProductRevenueMixQuery query,
        CancellationToken ct = default);
}

// ---------------------------------------------------------------------------
// Calculator interface (thin boundary so the normalizer stays decoupled)
// ---------------------------------------------------------------------------

public interface ICompanyProductRevenueMixCalculator
{
    /// <summary>
    /// Calculates and persists the product revenue mix for one company/month
    /// using the Noavaran Amin OutputType=0 (single-month) line items already
    /// stored in <c>MonthlyReportLineItems</c>.
    /// </summary>
    Task RecalculateAsync(
        string externalCompanyId,
        int jalaliYear,
        byte jalaliMonth,
        string? bourseSymbol,
        string? companyTitle,
        string? fiscalEndDate,
        CancellationToken ct = default);
}
