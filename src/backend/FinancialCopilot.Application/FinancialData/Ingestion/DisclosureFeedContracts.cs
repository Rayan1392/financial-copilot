using System.Text.Json.Serialization;

namespace FinancialCopilot.Application.FinancialData.Ingestion;

/// <summary>Provider-neutral disclosure families exposed by the company disclosure feed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CompanyDisclosureType>))]
public enum CompanyDisclosureType
{
    MonthlyProductionSales,
    IncomeStatement,
    BalanceSheet,
    CashFlowStatement
}

[JsonConverter(typeof(JsonStringEnumConverter<DisclosureConsolidationScope>))]
public enum DisclosureConsolidationScope
{
    NonConsolidated,
    Consolidated,
    Both
}

[JsonConverter(typeof(JsonStringEnumConverter<DisclosureCoverageStatus>))]
public enum DisclosureCoverageStatus
{
    Complete,
    UnmappedCompany
}

public sealed record CompanyDisclosureFeedQuery(
    IReadOnlyCollection<CompanyDisclosureType>? Types = null,
    string? SymbolOrCompany = null,
    IReadOnlyCollection<string>? ProviderNames = null,
    DateOnly? PublishedFrom = null,
    DateOnly? PublishedTo = null,
    DateTimeOffset? ReceivedFrom = null,
    DateTimeOffset? ReceivedTo = null,
    DisclosureConsolidationScope ConsolidationScope = DisclosureConsolidationScope.NonConsolidated,
    int Page = 1,
    int PageSize = 20);

public sealed record CompanyDisclosureFeedItem(
    string DisclosureId,
    [property: JsonIgnore]
    string LogicalDisclosureId,
    CompanyDisclosureType Type,
    string ProviderName,
    [property: JsonIgnore] string ExternalCompanyId,
    Guid? CompanyId,
    string? Symbol,
    string? CompanyName,
    string Title,
    DateOnly? PublishedAt,
    DateOnly? ReportingPeriodEnd,
    DateTimeOffset ReceivedAt,
    [property: JsonIgnore] string SourceRecordId,
    int RevisionNumber,
    bool IsRevised,
    DisclosureCoverageStatus CoverageStatus,
    string FreshnessReasonCode,
    bool IsAudited = false,
    bool IsRepresented = false,
    bool IsComposing = false);

public sealed record CompanyDisclosureFeedPage(
    IReadOnlyList<CompanyDisclosureFeedItem> Items,
    int Page,
    int PageSize,
    int TotalCount,
    DateTimeOffset AsOf,
    DisclosureCoverageStatus CoverageStatus);

public interface ICompanyDisclosureFeedRepository
{
    Task<CompanyDisclosureFeedPage> QueryAsync(
        CompanyDisclosureFeedQuery query,
        CancellationToken cancellationToken = default);
}
