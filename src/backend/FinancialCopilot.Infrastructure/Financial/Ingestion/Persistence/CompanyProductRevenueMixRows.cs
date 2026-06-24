namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class CompanyProductRevenueMixRow
{
    public Guid Id { get; set; }

    public string ExternalCompanyId { get; set; } = string.Empty;

    public string? CompanySymbol { get; set; }

    public string? CompanyName { get; set; }

    public int ReportYear { get; set; }

    public byte ReportMonth { get; set; }

    public string? FiscalEndDate { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public decimal? ProductionQuantity { get; set; }

    public decimal? SalesQuantity { get; set; }

    public decimal? SalesRate { get; set; }

    public decimal SalesAmount { get; set; }

    public decimal TotalCompanySalesAmount { get; set; }

    public decimal RevenueSharePercentage { get; set; }

    public int ProductRank { get; set; }

    public bool IsDominantProduct { get; set; }

    public string SourceProviderName { get; set; } = string.Empty;

    public DateTimeOffset CalculatedAtUtc { get; set; }
}
