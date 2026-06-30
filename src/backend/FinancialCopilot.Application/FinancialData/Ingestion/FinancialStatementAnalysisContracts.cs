using FinancialCopilot.Domain.Financial.Entities;

namespace FinancialCopilot.Application.FinancialData.Ingestion;

public enum FinancialStatementVariantPreference
{
    DefaultNonConsolidated,
    ConsolidatedOnly,
    NonConsolidatedOnly
}

public sealed record FinancialStatementAnalysisQuery(
    string UserMessage,
    string? SymbolOrCompanyName,
    int? PeriodMonths = null,
    FinancialStatementType? StatementTypeFocus = null,
    FinancialStatementVariantPreference VariantPreference = FinancialStatementVariantPreference.DefaultNonConsolidated,
    bool? IsAuditedPreference = null,
    IReadOnlyList<string>? MetricFocusCodes = null,
    bool IncludeBalanceSheetSummary = false,
    bool IncludeReturnMetrics = false,
    bool IncludeSourceDetails = true);

public sealed record FinancialStatementMetricComparison(
    string MetricCode,
    string LabelFa,
    decimal? CurrentValue,
    string? CurrentFormattedValue,
    decimal? PreviousValue,
    string? PreviousFormattedValue,
    decimal? ChangePercent,
    string? ChangeDirectionFa,
    string? Indicator,
    bool IsUnavailable = false,
    string? Warning = null);

public sealed record FinancialStatementAnalysisSection(
    string TitleFa,
    IReadOnlyList<string> SummaryBullets,
    IReadOnlyList<FinancialStatementMetricComparison> Metrics);

public sealed record FinancialStatementSourceReference(
    string StatementType,
    Guid StatementId,
    string ExternalStatementId,
    string ProviderName,
    string PeriodType,
    int PeriodMonths,
    string? JalaliPeriodEnd,
    string? JalaliFiscalYearEnd,
    string? JalaliAnnouncementDate,
    bool IsAudited,
    bool IsRepresented,
    bool IsComposing);

public sealed record FinancialStatementAnalysisResponse(
    string CompanySymbol,
    string? CompanyName,
    int SelectedPeriodMonths,
    string SelectedPeriodType,
    string? JalaliPeriodEnd,
    string? JalaliFiscalYearEnd,
    string SelectedVariant,
    bool? SelectedAuditedStatus,
    IReadOnlyList<string> SummaryBullets,
    IReadOnlyList<FinancialStatementAnalysisSection> Sections,
    IReadOnlyList<FinancialStatementSourceReference> SourceReferences,
    IReadOnlyList<string> Warnings,
    double ConfidenceScore,
    string? RenderedAnswer,
    DateTimeOffset GeneratedAtUtc);

public interface IFinancialStatementAnalysisUseCase
{
    Task<FinancialStatementAnalysisResponse?> ExecuteAsync(
        FinancialStatementAnalysisQuery query,
        CancellationToken ct = default);
}

public sealed record FinancialStatementAnalysisStatementSnapshot(
    Guid StatementId,
    string ExternalStatementId,
    string ProviderName,
    string ExternalCompanyId,
    string? CompanySymbol,
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
    IReadOnlyDictionary<string, decimal?> LineItems);

public interface IFinancialStatementAnalysisRepository
{
    Task<IReadOnlyList<FinancialStatementAnalysisStatementSnapshot>> ListCompanyStatementsAsync(
        string externalCompanyId,
        CancellationToken ct = default);
}

public sealed record FinancialStatementSelectionRequest(
    int? PeriodMonths,
    FinancialStatementType? StatementTypeFocus,
    FinancialStatementVariantPreference VariantPreference,
    bool? IsAuditedPreference);

public sealed record FinancialStatementSelectionResult(
    FinancialStatementAnalysisStatementSnapshot? IncomeStatement,
    FinancialStatementAnalysisStatementSnapshot? PriorIncomeStatement,
    FinancialStatementAnalysisStatementSnapshot? BalanceSheet,
    FinancialStatementAnalysisStatementSnapshot? PriorBalanceSheet,
    IReadOnlyList<string> Warnings);

public interface IFinancialStatementSelectionService
{
    FinancialStatementSelectionResult Select(
        IReadOnlyList<FinancialStatementAnalysisStatementSnapshot> statements,
        FinancialStatementSelectionRequest request);
}

public interface IFinancialStatementAnalysisRenderer
{
    string Render(FinancialStatementAnalysisResponse response);
}
