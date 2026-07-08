using FinancialCopilot.Domain.Financial.Entities;

namespace FinancialCopilot.Application.FinancialData.Ingestion;

public enum FinancialStatementTableSide
{
    Assets,
    LiabilitiesAndEquity,
    Unclassified
}

public sealed record FinancialStatementTableQuery(
    string UserMessage,
    string? CompanyQuery,
    FinancialStatementType? StatementType,
    int? PeriodMonths = null,
    bool? IsAudited = null,
    bool? IsRepresented = null,
    bool? IsComposing = null);

public sealed record FinancialStatementTableSelection(
    string ExternalCompanyId,
    FinancialStatementType StatementType,
    string ProviderName,
    int? PeriodMonths,
    bool? IsAudited,
    bool? IsRepresented,
    bool IsComposing,
    string? CompanySymbol = null,
    string? CompanyName = null);

public sealed record FinancialStatementTableSource(
    Guid StatementId,
    string ExternalStatementId,
    string ProviderName,
    string ExternalCompanyId,
    string CompanySymbol,
    string? CompanyName,
    FinancialStatementType StatementType,
    string PeriodType,
    int PeriodMonths,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateTimeOffset? AnnouncementDate,
    string? JalaliPeriodEnd,
    string? JalaliFiscalYearEnd,
    string? JalaliAnnouncementDate,
    bool IsAudited,
    bool IsRepresented,
    bool IsComposing,
    string? Unit);

public sealed record FinancialStatementTableLineItem(
    int RowNumber,
    int? SourceItemId,
    string? TitleFa,
    string? TitleEn,
    string? MetricCode,
    decimal? Value,
    string? FormattedValue,
    string? Unit,
    FinancialStatementTableSide Side);

public sealed record BalanceSheetTableRow(
    FinancialStatementTableLineItem? Asset,
    FinancialStatementTableLineItem? LiabilityOrEquity);

public sealed record FinancialStatementTableResult(
    FinancialStatementTableSource Source,
    IReadOnlyList<FinancialStatementTableLineItem> LineItems,
    IReadOnlyList<BalanceSheetTableRow> BalanceSheetRows,
    IReadOnlyList<string> Warnings,
    string? RenderedAnswer,
    DateTimeOffset GeneratedAtUtc);

public interface IFinancialStatementTableRepository
{
    Task<FinancialStatementTableSource?> FindLatestStatementAsync(
        FinancialStatementTableSelection selection,
        CancellationToken ct = default);

    Task<IReadOnlyList<FinancialStatementTableLineItem>> GetStatementLineItemsAsync(
        Guid statementId,
        CancellationToken ct = default);
}

public interface IFinancialStatementTableRenderer
{
    string Render(FinancialStatementTableResult result);
}

public interface IFinancialStatementTableQueryUseCase
{
    Task<FinancialStatementTableResult?> ExecuteAsync(
        FinancialStatementTableQuery query,
        CancellationToken ct = default);
}
