namespace FinancialCopilot.Application.FinancialData.Ingestion;

public enum QuarterlyCellState { Available, MissingObservation, IncompatibleObservation, ConflictingObservation, UnsupportedFiscalPeriod, MissingPredecessor, CalculationUnavailable }
public enum QuarterlyComparisonState { Available, MissingCurrent, MissingPrevious, NonPositiveBase, SignChanged, Unavailable }
public enum QuarterlyRowPolicy { CumulativeFlow, CumulativeEps, ReportedPointInTime, DerivedGrossMargin, ComparableEps, ComparableShareBasis }
public enum QuarterlyFinancialReportOutcome
{
    SuccessfulTable,
    PartialTable,
    CompanyNotFound,
    CompanyAmbiguous,
    CompanyFoundNoPersistedReports,
    SourceObservationsUnavailable,
    CompanyUnavailableFromProvider,
    InvalidQuery
}

public sealed record QuarterlyFinancialReportTableQuery(
    string ExternalCompanyId,
    string CompanySymbol,
    int QuarterCount = 5,
    DateOnly? EndingPeriod = null,
    bool? IsComposing = null,
    bool? IsAudited = null,
    bool? IsRepresented = null,
    string ProviderName = "NoavaranCurrentApi");

public sealed record QuarterlyReportObservation(
    Guid ReportId,
    string ExternalReportId,
    string ProviderName,
    string ExternalCompanyId,
    string StatementType,
    int PeriodMonths,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    bool IsAudited,
    bool IsRepresented,
    bool IsComposing,
    string SourcePayloadChecksum,
    DateTimeOffset LastSynchronizedAt,
    string? AnnouncementDate,
    string? FiscalYearEvidence,
    IReadOnlyList<QuarterlySourceObservation> Items,
    string? AssumedScale = null,
    string? SourceAmountUnit = null,
    string? CurrencyCode = null,
    string? AccountingBasis = null,
    string? ReportRevisionLineage = null,
    string? PeriodType = null);

public sealed record QuarterlySourceObservation(
    Guid LineItemId,
    Guid CatalogId,
    int SourceItemId,
    string? CatalogTitleFa,
    string? Unit,
    decimal? Value);

public sealed record QuarterlyFiscalPeriod(int FiscalYear, int Quarter, DateOnly Start, DateOnly End)
{
    public string Label => $"{FiscalYear} Q{Quarter}";
}

public sealed record QuarterlyFinancialCell(
    decimal? Value,
    QuarterlyCellState State,
    string? Reason,
    string Unit,
    bool RenderZeroAsDash,
    IReadOnlyList<Guid> SourceReportIds,
    IReadOnlyList<Guid> SourceLineItemIds,
    string NormalizationPolicy,
    IReadOnlyList<decimal?> OperandValues,
    string? CurrencyCode = null,
    int? ValueScale = null,
    QuarterlyComparableEvidence? ComparableEvidence = null);

// Raw statement values remain separate from the derived comparison displayed in Value.
public sealed record QuarterlyComparableEvidence(
    decimal? RawCurrentValue,
    decimal? RawPreviousValue,
    decimal? RawCodalCurrentValue,
    decimal? RawCodalPreviousValue,
    Guid? RawCurrentReportId,
    Guid? RawPreviousReportId,
    Guid? RawCurrentLineItemId,
    Guid? RawPreviousLineItemId,
    Guid? RawCodalCurrentLineItemId,
    Guid? RawCodalPreviousLineItemId,
    decimal? StandaloneAttributableProfitMillionRials,
    decimal? ShareBasisThousand,
    string? ShareBasisType,
    int? ReferenceFiscalYear,
    int? ReferenceQuarter,
    Guid? ReferenceReportId,
    string? ReferenceExternalReportId,
    string? Method,
    string? Rounding,
    string? Reconciliation,
    decimal? RawCurrentShareCountThousand = null,
    decimal? RawReferenceShareCountThousand = null,
    decimal? RawCurrentAttributableProfitMillionRials = null,
    decimal? RawPreviousAttributableProfitMillionRials = null,
    decimal? RawReferenceAttributableProfitMillionRials = null,
    decimal? RawReferenceEpsYtd = null,
    decimal? RawReferenceCodalEpsYtd = null,
    int? AttributableProfitSourceItemId = null,
    int? PreviousAttributableProfitSourceItemId = null);

public sealed record QuarterlyQoQComparison(
    string RowId,
    QuarterlyFiscalPeriod TargetPeriod,
    QuarterlyFiscalPeriod BasePeriod,
    decimal? AbsoluteChange,
    decimal? RelativeChangePercent,
    decimal? PercentagePointChange,
    QuarterlyComparisonState State);

public sealed record QuarterlyFinancialRow(
    string RowId,
    int Order,
    string LabelFa,
    int? SourceItemId,
    Guid? SourceCatalogId,
    QuarterlyRowPolicy Policy,
    string Unit,
    IReadOnlyDictionary<string, QuarterlyFinancialCell> Cells,
    string? SourceCatalogTitleFa = null);

public sealed record QuarterlySelectedReportReference(
    Guid ReportId,
    string ExternalReportId,
    string ProviderName,
    string StatementType,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    int PeriodMonths,
    bool IsAudited,
    bool IsRepresented,
    bool IsComposing,
    string SourcePayloadChecksum,
    DateTimeOffset LastSynchronizedAt,
    string? AssumedScale,
    string? SourceAmountUnit,
    string? CurrencyCode = null,
    string? AccountingBasis = null,
    string? ReportRevisionLineage = null,
    string? FiscalYearEvidence = null,
    string? PeriodType = null);

public sealed record QuarterlyFinancialReportTableResult(
    string CompanySymbol,
    string? CompanyName,
    string ExternalCompanyId,
    string ProviderName,
    string StatementType,
    bool? IsComposing,
    int RequestedQuarterCount,
    IReadOnlyList<QuarterlyFiscalPeriod> PeriodsNewestFirst,
    IReadOnlyList<QuarterlyFinancialRow> Rows,
    IReadOnlyList<QuarterlyQoQComparison> Comparisons,
    DateTimeOffset? FreshestSourceAt,
    string Status,
    string? Reason,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<QuarterlySelectedReportReference>? SelectedReports = null,
    CompanyResolutionStatus CompanyResolutionStatus = CompanyResolutionStatus.Resolved,
    QuarterlyFinancialReportOutcome Outcome = QuarterlyFinancialReportOutcome.SuccessfulTable);

public sealed record QuarterlyFinancialReportSelection(
    string ExternalCompanyId,
    int QuarterCount,
    DateOnly? EndingPeriod,
    bool? IsComposing = null,
    bool? IsAudited = null,
    bool? IsRepresented = null);

public interface IQuarterlyFinancialReportsRepository
{
    Task<IReadOnlyList<QuarterlyReportObservation>> GetIncomeStatementReportsAsync(
        QuarterlyFinancialReportSelection selection,
        CancellationToken cancellationToken = default);
}

public interface IQuarterlyFinancialReportsQueryUseCase
{
    Task<QuarterlyFinancialReportTableResult> ExecuteAsync(
        QuarterlyFinancialReportTableQuery query,
        CancellationToken cancellationToken = default);
}

public sealed class QuarterlyFinancialReportsQueryUseCase(
    IQuarterlyFinancialReportsRepository repository,
    ICompanyResolutionOutcomeService companyResolution,
    TimeProvider timeProvider) : IQuarterlyFinancialReportsQueryUseCase
{
    public async Task<QuarterlyFinancialReportTableResult> ExecuteAsync(
        QuarterlyFinancialReportTableQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.QuarterCount is < 1 or > 12)
        {
            return QuarterlyFinancialReportsNormalizer.Normalize(query.CompanySymbol, null,
                query.ExternalCompanyId, [], query.QuarterCount, query.EndingPeriod, timeProvider.GetUtcNow());
        }

        if (query.IsComposing == true)
        {
            var unavailable = QuarterlyFinancialReportsNormalizer.Normalize(query.CompanySymbol, null,
                query.ExternalCompanyId, [], query.QuarterCount, query.EndingPeriod, timeProvider.GetUtcNow());
            return unavailable with { Status = "Unavailable", Reason = "separate_statement_scope_required",
                Outcome = QuarterlyFinancialReportOutcome.InvalidQuery };
        }

        var resolution = await companyResolution.ResolveAsync(query.CompanySymbol, query.ProviderName, cancellationToken);
        if (resolution.Status != CompanyResolutionStatus.Resolved || resolution.Company is null)
        {
            var unavailable = QuarterlyFinancialReportsNormalizer.Normalize(query.CompanySymbol, null,
                query.ExternalCompanyId, [], query.QuarterCount, query.EndingPeriod, timeProvider.GetUtcNow());
            return unavailable with
            {
                Status = "Unavailable",
                Reason = resolution.Reason ?? resolution.Status.ToString(),
                CompanyResolutionStatus = resolution.Status,
                Outcome = resolution.Status switch
                {
                    CompanyResolutionStatus.NotFound => QuarterlyFinancialReportOutcome.CompanyNotFound,
                    CompanyResolutionStatus.Ambiguous => QuarterlyFinancialReportOutcome.CompanyAmbiguous,
                    CompanyResolutionStatus.UnsupportedProviderCompany => QuarterlyFinancialReportOutcome.CompanyUnavailableFromProvider,
                    _ => QuarterlyFinancialReportOutcome.InvalidQuery
                }
            };
        }

        if (!string.IsNullOrWhiteSpace(query.ExternalCompanyId) &&
            !string.Equals(query.ExternalCompanyId, resolution.Company.ExternalCompanyId, StringComparison.Ordinal))
        {
            var unavailable = QuarterlyFinancialReportsNormalizer.Normalize(query.CompanySymbol, null,
                query.ExternalCompanyId, [], query.QuarterCount, query.EndingPeriod, timeProvider.GetUtcNow());
            return unavailable with
            {
                Status = "Unavailable",
                Reason = "company_identifier_mismatch",
                CompanyResolutionStatus = CompanyResolutionStatus.UnsupportedProviderCompany,
                Outcome = QuarterlyFinancialReportOutcome.CompanyUnavailableFromProvider
            };
        }

        var reports = await repository.GetIncomeStatementReportsAsync(
            new QuarterlyFinancialReportSelection(resolution.Company.ExternalCompanyId, query.QuarterCount,
                query.EndingPeriod, false, query.IsAudited, query.IsRepresented), cancellationToken);
        return QuarterlyFinancialReportsNormalizer.Normalize(
            resolution.Company.CompanySymbol ?? resolution.Company.TseSymbol ?? resolution.Company.Ticker ?? query.CompanySymbol,
            resolution.Company.Name, resolution.Company.ExternalCompanyId, reports, query.QuarterCount, query.EndingPeriod,
            timeProvider.GetUtcNow());
    }
}
