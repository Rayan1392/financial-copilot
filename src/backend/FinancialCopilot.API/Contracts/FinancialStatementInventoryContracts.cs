namespace FinancialCopilot.API.Contracts;

public sealed record FinancialStatementInventoryResponse(
    string Ticker,
    IReadOnlyList<FinancialStatementInventoryItem> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record FinancialStatementInventoryItem(
    Guid? ReportId,
    string? VersionId,
    string StatementType,
    int? FiscalYearEndJalali,
    string? PeriodEndJalali,
    int? PeriodMonths,
    bool? IsAudited,
    bool? IsRestated,
    bool? IsConsolidated);
