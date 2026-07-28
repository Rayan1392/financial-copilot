using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.Insights;
using FinancialCopilot.Domain.Financial.ProfessionalScanners;

namespace FinancialCopilot.Application.FinancialData.ProfessionalScanners;

public enum ProfessionalFilterCategory { Technical, Flow, Volume, Queue, LargeTrade, Fundamental, Industry, Composite }
public enum ProfessionalFilterExecutionKind { MetricScanner, InsightEvents }
public enum ProfessionalFilterState { Active, Deprecated }
public enum ProfessionalParameterType { Decimal, Integer, Text }
public enum ProfessionalSessionPolicy { LatestCompleteObservation, TodayOrHistorical }
public enum ProfessionalExecutionStatus { Complete, Empty, Partial, Stale, Unavailable, Rejected }
public enum ProfessionalAccessMode { Unlimited, Metered }

public sealed record ProfessionalFilterParameter(
    string Name, string TitleFa, ProfessionalParameterType Type, string Unit,
    decimal? Minimum, decimal? Maximum, decimal? DefaultValue,
    IReadOnlyCollection<string> PersianAliases, bool Required = false);

public sealed record ProfessionalMetricConditionTemplate(
    string MetricCode, ConditionOperator Operator, string ParameterName, string Unit, string PeriodType);

public sealed record ProfessionalFilterDefinition(
    string Code,
    string Version,
    string TitleFa,
    IReadOnlyCollection<string> PersianAliases,
    ProfessionalFilterCategory Category,
    ProfessionalFilterExecutionKind ExecutionKind,
    IReadOnlyCollection<ProfessionalFilterParameter> Parameters,
    IReadOnlyCollection<ProfessionalMetricConditionTemplate> Conditions,
    InsightType? InsightType,
    IReadOnlyCollection<string> RequiredDatasets,
    ProfessionalSessionPolicy SessionPolicy,
    string Ranking,
    string TieBreaker,
    string EntitlementCode,
    ProfessionalFilterState State);

public sealed record UnsupportedProfessionalFilter(string RequestedFilter, string Reason);

public sealed record ProfessionalCatalogQuery(
    ProfessionalFilterCategory? Category = null, string? Search = null, int Page = 1, int PageSize = 20);

public sealed record ProfessionalCatalogPage(
    IReadOnlyCollection<ProfessionalFilterDefinition> Items,
    IReadOnlyCollection<UnsupportedProfessionalFilter> UnsupportedFilters,
    int Page, int PageSize, int TotalCount, int TotalPages);

public sealed record ProfessionalAliasResolution(
    bool Resolved, bool Ambiguous, ProfessionalFilterDefinition? Definition,
    IReadOnlyCollection<ProfessionalFilterDefinition> Candidates, string? Message);

public sealed record ProfessionalScannerScope(string? IndustryCode = null, string? InstrumentClass = null);

public sealed record ProfessionalExecuteCommand(
    CurrentActor Actor,
    string FilterCodeOrAlias,
    string? FilterVersion,
    IReadOnlyDictionary<string, string>? Parameters,
    DateOnly? FromDate,
    DateOnly? ToDate,
    ProfessionalScannerScope? Scope,
    int Page,
    int PageSize,
    string CorrelationId,
    string Source = "Api");

public sealed record ProfessionalMatchedValue(
    string Code, decimal Value, string Unit, string? Period, DateTimeOffset SourceFreshnessUtc);

public sealed record ProfessionalMatchReason(
    string EvidenceCode, string Operator, decimal? ActualValue, decimal? Threshold,
    string Unit, string Text);

public sealed record ProfessionalScannerResultRow(
    string ExternalCompanyId, string Symbol, string? CompanyName, int Rank,
    IReadOnlyCollection<ProfessionalMatchedValue> MatchedValues,
    IReadOnlyCollection<ProfessionalMatchReason> Reasons,
    decimal Score, DateTimeOffset SourceFreshnessUtc, string EvidenceReference);

public sealed record ProfessionalScannerExecutionResult(
    string FilterCode, string FilterVersion, ProfessionalExecutionStatus Status,
    ProfessionalAccessMode AccessMode, IReadOnlyDictionary<string, string> Parameters,
    ProfessionalScannerScope Scope, DateOnly FromDate, DateOnly ToDate,
    IReadOnlyCollection<ProfessionalScannerResultRow> Rows,
    int Page, int PageSize, int TotalCount, int TotalPages,
    string EvidenceHash, DateTimeOffset ExecutedAtUtc, TimeSpan Duration,
    IReadOnlyCollection<string> DatasetMessages, string CorrelationId, string Ranking, string TieBreaker);

public sealed record SavedFilterDto(
    Guid Id, string Name, string FilterCode, string FilterVersion,
    IReadOnlyDictionary<string, string> Parameters, int Version,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

public sealed record SaveProfessionalFilterCommand(
    CurrentActor Actor, string Name, string FilterCodeOrAlias, string? FilterVersion,
    IReadOnlyDictionary<string, string>? Parameters);
public sealed record UpdateProfessionalFilterCommand(
    CurrentActor Actor, Guid Id, int ExpectedVersion, string Name, string FilterCodeOrAlias,
    string? FilterVersion, IReadOnlyDictionary<string, string>? Parameters);
public sealed record DeleteProfessionalFilterCommand(CurrentActor Actor, Guid Id, int ExpectedVersion);
public sealed record RunSavedProfessionalFilterCommand(
    CurrentActor Actor, Guid Id, DateOnly? FromDate, DateOnly? ToDate,
    ProfessionalScannerScope? Scope, int Page, int PageSize, string CorrelationId, string Source = "Api");

public interface IProfessionalFilterCatalog
{
    ProfessionalCatalogPage List(ProfessionalCatalogQuery query);
    ProfessionalFilterDefinition Get(string code, string? version = null);
    ProfessionalAliasResolution ResolveAlias(string text);
    IReadOnlyDictionary<string, string> ValidateParameters(
        ProfessionalFilterDefinition definition, IReadOnlyDictionary<string, string>? supplied);
}

public interface ISavedFilterRepository
{
    Task<IReadOnlyCollection<SavedFilter>> ListAsync(SavedFilterActor actor, int page, int pageSize, CancellationToken cancellationToken);
    Task<int> CountAsync(SavedFilterActor actor, CancellationToken cancellationToken);
    Task<SavedFilter?> FindAsync(SavedFilterActor actor, Guid id, bool includeRemoved, CancellationToken cancellationToken);
    Task SaveAsync(SavedFilter value, CancellationToken cancellationToken);
}

public interface IProfessionalScannerEntitlementPolicy
{
    Task<ProfessionalAccessMode> ValidateExecuteAsync(CurrentActor actor, CancellationToken cancellationToken);
    Task ValidateSaveAsync(CurrentActor actor, int currentSavedCount, CancellationToken cancellationToken);
}

public interface IProfessionalScannerUseCases
{
    ProfessionalCatalogPage ListCatalog(ProfessionalCatalogQuery query);
    ProfessionalFilterDefinition GetFilter(string code, string? version = null);
    ProfessionalAliasResolution ResolveAlias(string text);
    Task<ProfessionalScannerExecutionResult> ExecuteAsync(ProfessionalExecuteCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<SavedFilterDto>> ListSavedAsync(CurrentActor actor, int page, int pageSize, CancellationToken cancellationToken);
    Task<SavedFilterDto> SaveAsync(SaveProfessionalFilterCommand command, CancellationToken cancellationToken);
    Task<SavedFilterDto> UpdateAsync(UpdateProfessionalFilterCommand command, CancellationToken cancellationToken);
    Task DeleteAsync(DeleteProfessionalFilterCommand command, CancellationToken cancellationToken);
    Task<ProfessionalScannerExecutionResult> RunSavedAsync(RunSavedProfessionalFilterCommand command, CancellationToken cancellationToken);
}

public sealed class ProfessionalScannerValidationException(string message) : InvalidOperationException(message);
