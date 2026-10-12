namespace FinancialCopilot.Application.FinancialData.Ingestion;

public enum ManualNoavaranFinancialStatementType
{
    BalanceSheet,
    IncomeStatement,
    CashFlow,
    FinancialRatios
}

public sealed record ManualNoavaranFinancialStatementBackfillRequest(
    string ExternalCompanyId,
    int FiscalYearEnding,
    int PeriodMonths,
    ManualNoavaranFinancialStatementType StatementType);

public sealed record ManualNoavaranFinancialStatementBackfillResult(
    Guid? RunId,
    string Outcome,
    int ProcessedRecords,
    string? Message = null);

public interface IManualNoavaranFinancialStatementBackfillService
{
    Task<ManualNoavaranFinancialStatementBackfillResult> ExecuteAsync(
        ManualNoavaranFinancialStatementBackfillRequest request,
        CancellationToken cancellationToken);
}
