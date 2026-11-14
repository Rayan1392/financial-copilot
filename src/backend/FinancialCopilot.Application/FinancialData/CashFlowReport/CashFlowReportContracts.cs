namespace FinancialCopilot.Application.FinancialData.CashFlowReport;

public static class CashFlowReportContract
{
    public const string Version = "161.1";
    public const string ProviderName = "NoavaranCurrentApi";
    public const int HeaderWindow = 48;
    public const int MaxCashFlowStatements = 12;
    public const int MaxIncomeStatements = 2;
}

public sealed record CashFlowReportRequest(
    string CompanyQuery, string ProviderName = CashFlowReportContract.ProviderName, string View = "default",
    string Focus = "report", string? MetricId = null, string? AnalysisTopic = null,
    int? RequestedPeriodMonths = null, int? RequestedFiscalYear = null, int? PeriodCount = null,
    DateOnly? RequestedDate = null, bool PreviousReport = false);

public sealed record CashFlowReportCompany(string? Symbol, string? Name, string ProviderName, string? ExternalCompanyId);
public sealed record CashFlowReportStatement(Guid StatementId, string ExternalStatementId, DateOnly PeriodEnd,
    string JalaliPeriodEnd, string PeriodType, int PeriodMonths, bool IsAudited, bool IsRepresented,
    DateOnly? PublishedAt, string? JalaliFiscalYearEnd, string PresentationBasis, string Unit);
public sealed record CashFlowReportColumn(string ColumnId, string Kind, string Label, int? FiscalYear, int? Quarter,
    int PeriodMonths, DateOnly? PeriodEnd, string? JalaliPeriodEnd, string Derivation, IReadOnlyList<Guid> StatementIds,
    string State, string? Reason);
public sealed record CashFlowReportRow(string RowId, string Label, string Kind, IReadOnlyList<int> SourceItemIds,
    string? Note, IReadOnlyDictionary<string, CashFlowCell> Cells);
public sealed record CashFlowReportSection(string SectionId, string Label, IReadOnlyList<CashFlowReportRow> Rows);
public sealed record CashFlowReportChange(decimal? Amount, decimal? Percent, string State, string? Reason);
public sealed record CashFlowReportChanges(string BaseColumnId, string CompareColumnId, IReadOnlyDictionary<string, CashFlowReportChange> Rows);
public sealed record CashFlowReportKpi(string MetricId, string Label, string ColumnId, decimal? Value, string Unit,
    string State, string? Reason, string Formula, IReadOnlyList<CashFlowMetricComponent> Components);
public sealed record CashFlowReportCheck(string CheckId, string ColumnId, string State, decimal? Expected, decimal? Actual, decimal? Difference);
public sealed record CashFlowReportVerification(string Status, IReadOnlyList<CashFlowReportCheck> Checks);
public sealed record CashFlowReportWarning(string Code, string Severity, string? ColumnId, string Message);

public sealed record CashFlowReportResult(
    string ContractVersion, DateTimeOffset GeneratedAtUtc, string Outcome, string? OutcomeReason, string? Message,
    IReadOnlyList<DateOnly> NearestPeriods, bool ConsolidatedAlternativeAvailable, string? TemplateId,
    CashFlowReportCompany Company, CashFlowReportRequest Request, IReadOnlyDictionary<Guid, CashFlowReportStatement> Statements,
    Guid? AnchorStatementId, IReadOnlyList<CashFlowReportColumn> Columns, IReadOnlyList<CashFlowReportSection> Sections,
    CashFlowReportChanges? Changes, IReadOnlyList<CashFlowReportKpi> Kpis, CashFlowWaterfall? Waterfall,
    CashFlowReportVerification Verification, IReadOnlyList<CashFlowReportWarning> Warnings, IReadOnlyList<string> Notices,
    CashFlowAnalysis? Analysis);

public sealed record CashFlowHeaderSet(IReadOnlyList<CashFlowStatementHeader> Statements, IReadOnlyList<CashFlowFiscalEvidence> FiscalEvidence);
public sealed record CashFlowIncomeStatementObservation(CashFlowStatementHeader Header, IReadOnlyList<CashFlowItemObservation> Items);

/// <summary>Bounded persisted data reads, scoped to provider and company; no provider or catalogue writes.</summary>
public interface ICashFlowStatementReader
{
    Task<CashFlowHeaderSet> ReadHeadersAsync(string providerName, string externalCompanyId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CashFlowStatementHeader>> ReadHeadersAtDatesAsync(string providerName, string externalCompanyId,
        IReadOnlyCollection<DateOnly> dates, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<CashFlowItemObservation>>> ReadLineItemsAsync(string providerName,
        IReadOnlyCollection<Guid> statementIds, IReadOnlyCollection<int> sourceItemIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CashFlowIncomeStatementObservation>> ReadIncomeStatementsAsync(string providerName, string externalCompanyId,
        IReadOnlyCollection<CashFlowStatementHeader> periods, CancellationToken cancellationToken = default);
    Task<bool> ConsolidatedExistsAsync(string providerName, string externalCompanyId, CancellationToken cancellationToken = default);
}

public interface ICashFlowReportQueryUseCase
{
    Task<CashFlowReportResult> ExecuteAsync(CashFlowReportRequest request, CancellationToken cancellationToken = default);
}
