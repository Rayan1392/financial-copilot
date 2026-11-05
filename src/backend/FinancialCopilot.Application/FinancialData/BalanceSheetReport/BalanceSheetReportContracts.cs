namespace FinancialCopilot.Application.FinancialData.BalanceSheetReport;

// Feature 154 typed contract (design §3, contractVersion 154.3). Every discriminator is a string so the
// payload is stable across serializers (R-33). Amounts are decimal? in million IRR; a null value is
// "missing" and stays distinct from a reported zero through serialization and replay.

public static class BalanceSheetReportContract
{
    public const string Version = "154.3";
    public const string Title = "ترازنامه (صورت وضعیت مالی)";
    public const string ProviderName = "NoavaranCurrentApi";
    public const string Unit = "million_irr";
    public const int MaxChartDates = 5;
    public const int MaxSnapshots = 9;
    /// <summary>At most 9 statements' line items per request (AC-X04), counting equally ranked peer versions.</summary>
    public const int MaxLineItemStatements = 9;
}

public static class BalanceSheetOutcomes
{
    public const string Available = "available";
    public const string CompanyUnresolved = "company_unresolved";
    public const string CompanyAmbiguous = "company_ambiguous";
    public const string ProviderUnavailable = "provider_unavailable";
    public const string ClassificationUnavailable = "company_classification_unavailable";
    public const string NoStandaloneBalanceSheet = "no_standalone_balance_sheet";
    public const string NoSnapshotAtDate = "no_snapshot_at_date";
    public const string UnsupportedDateWithVariant = "unsupported_date_with_variant";
    public const string ConflictingReportRevision = "conflicting_report_revision";
    public const string IneligibleStorageScale = "ineligible_storage_scale";
    /// <summary>More equally ranked versions than the bounded read allows (fail closed, never a silent partial).</summary>
    public const string TooManyReportVersions = "too_many_report_versions";
}

public static class BalanceSheetViews
{
    public const string Default = "default";
    public const string SingleDate = "single_date";
    public const string Comparison = "comparison";
}

public static class BalanceSheetBaselineKinds
{
    public const string FiscalYearEnd = "fiscal_year_end";
    public const string PreviousFiscalQuarterEnd = "previous_fiscal_quarter_end";
    public const string PreviousReport = "previous_report";
    public const string SameStagePreviousYear = "same_stage_previous_year";
    public const string Explicit = "explicit";

    public static readonly IReadOnlyList<string> Standard =
        [FiscalYearEnd, PreviousFiscalQuarterEnd, PreviousReport, SameStagePreviousYear];

    public static string Label(string kind) => kind switch
    {
        FiscalYearEnd => "پایان سال مالی قبل",
        PreviousFiscalQuarterEnd => "پایان فصل قبل",
        PreviousReport => "گزارش قبلی",
        SameStagePreviousYear => "مدت مشابه سال مالی قبل",
        Explicit => "تاریخ انتخابی",
        _ => kind
    };
}

public static class BalanceSheetTemplates
{
    public const string Banking = "BankingBalanceSheet";
    public const string General = "GeneralBalanceSheet";
}

public static class BalanceSheetCellStates
{
    public const string Available = "available";
    public const string Missing = "missing";
    public const string Ambiguous = "ambiguous";
}

public static class BalanceSheetChangeStates
{
    public const string Available = "available";
    public const string PercentUnavailable = "percent_unavailable";
    public const string Unavailable = "unavailable";
}

public static class BalanceSheetCheckStates
{
    public const string Reconciled = "reconciled";
    public const string Discrepancy = "discrepancy";
    public const string Unavailable = "unavailable";
}

/// <summary>Plain request model across the use-case boundary (no HTTP or orchestration types).</summary>
public sealed record BalanceSheetReportRequest(
    string CompanyQuery,
    string View = BalanceSheetViews.Default,
    string? BaselineKind = null,
    IReadOnlyList<DateOnly>? Dates = null,
    string? UnsupportedVariantFilter = null,
    string ProviderName = BalanceSheetReportContract.ProviderName);

public sealed record BalanceSheetReportResult(
    string ContractVersion,
    string Outcome,
    string? OutcomeReason,
    string? OutcomeMessage,
    DateOnly? RequestedDate,
    string? RequestedJalaliDate,
    BalanceSheetNearestDates? NearestAvailableDates,
    string? UnsupportedFilter,
    bool ConsolidatedAlternativeAvailable,
    string? TemplateId,
    string Title,
    BalanceSheetCompany Company,
    Guid? ClassificationGroupId,
    IReadOnlyDictionary<string, BalanceSheetSnapshot> Snapshots,
    string? AnchorStatementId,
    string RequestedView,
    BalanceSheetComparison? Comparison,
    BalanceSheetChart Chart,
    DateTimeOffset GeneratedAtUtc);

public sealed record BalanceSheetNearestDates(
    DateOnly? Earlier,
    string? EarlierJalali,
    DateOnly? Later,
    string? LaterJalali);

public sealed record BalanceSheetCompany(
    string Symbol,
    string? Name,
    string ProviderName,
    string? ExternalCompanyId);

public sealed record BalanceSheetSnapshot(
    BalanceSheetStatementInfo Statement,
    IReadOnlyList<BalanceSheetSection> Sections,
    BalanceSheetFinalTotals FinalTotals,
    BalanceSheetRatio Ratio,
    BalanceSheetReconciliation Reconciliation,
    IReadOnlyList<BalanceSheetRow> AnalyticalItems);

public sealed record BalanceSheetStatementInfo(
    Guid StatementId,
    string ExternalStatementId,
    DateOnly PeriodEnd,
    string JalaliPeriodEnd,
    string PeriodType,
    int PeriodMonths,
    bool IsAudited,
    bool IsRepresented,
    bool IsComposing,
    DateOnly? PublishedAt,
    string? JalaliPublishedAt,
    string? JalaliFiscalYearEnd,
    string Unit,
    string AssumedScale);

public sealed record BalanceSheetSection(
    string SectionId,
    string Label,
    string Caption,
    IReadOnlyList<BalanceSheetGroup> Groups,
    BalanceSheetRow? Total);

public sealed record BalanceSheetGroup(
    string GroupId,
    string? Label,
    IReadOnlyList<BalanceSheetRow> Rows,
    BalanceSheetRow? Subtotal);

public sealed record BalanceSheetRow(
    string RowId,
    string Label,
    string Kind,
    string Unit,
    BalanceSheetCell Cell);

public sealed record BalanceSheetCell(
    decimal? Value,
    string State,
    string? Reason,
    int? SelectedSourceItemId,
    Guid? LineItemId,
    Guid StatementId,
    BalanceSheetAliasDiscrepancy? AliasDiscrepancy,
    IReadOnlyList<BalanceSheetCellCandidate> Candidates);

public sealed record BalanceSheetCellCandidate(
    int SourceItemId,
    string Role,
    Guid? LineItemId,
    decimal? Value,
    bool Verified,
    string? SourceTitle);

public sealed record BalanceSheetAliasDiscrepancy(
    int OtherSourceItemId,
    decimal? OtherValue,
    decimal? Difference);

public sealed record BalanceSheetFinalTotals(
    BalanceSheetRow TotalAssets,
    BalanceSheetRow TotalLiabilitiesAndEquity);

public sealed record BalanceSheetRatio(
    decimal? Value,
    string State,
    string? Reason,
    string Unit = "percent",
    int DisplayDecimals = 2,
    string Rounding = "half_away_from_zero");

public sealed record BalanceSheetReconciliation(
    BalanceSheetCheck R1,
    BalanceSheetCheck R2);

public sealed record BalanceSheetCheck(
    string State,
    decimal? Left,
    decimal? Right,
    decimal? Difference);

public sealed record BalanceSheetComparison(
    string SelectedBaselineKind,
    IReadOnlyList<BalanceSheetBaselineCandidate> Candidates);

public sealed record BalanceSheetBaselineCandidate(
    string Kind,
    string Label,
    string State,
    string? Reason,
    DateOnly? BaselineDate,
    string? JalaliBaselineDate,
    string? StatementId,
    string Source,
    bool AnchorLinkVerified,
    IReadOnlyDictionary<string, BalanceSheetChange> RowChanges,
    BalanceSheetRatioChange? RatioChange,
    IReadOnlyList<string> Alternatives);

public sealed record BalanceSheetChange(
    decimal? Amount,
    decimal? Percent,
    string State,
    string? Reason);

public sealed record BalanceSheetRatioChange(
    decimal? Anchor,
    decimal? Baseline,
    decimal? PpDifference,
    string State,
    string? Reason);

public sealed record BalanceSheetChart(
    int MaxDates,
    IReadOnlyList<BalanceSheetChartPoint> Points);

/// <summary>Chart values are read from <see cref="BalanceSheetReportResult.Snapshots"/> by statement id.</summary>
public sealed record BalanceSheetChartPoint(
    DateOnly PeriodEnd,
    string JalaliPeriodEnd,
    string? StatementId,
    string R2State,
    decimal? R2Difference,
    bool StackRepresentsAssets,
    bool NegativeEquity,
    string? GapReason);

// Port model (Application owns it; Infrastructure implements it).

public sealed record BalanceSheetStatementHeader(
    Guid StatementId,
    string ExternalStatementId,
    DateOnly PeriodEnd,
    string PeriodType,
    bool IsAudited,
    bool IsRepresented,
    bool IsComposing,
    DateOnly? PublishedAt,
    string? JalaliPublishedAt,
    string? JalaliFiscalYearEnd,
    string? AssumedScale,
    bool? SourceIsComposing);

public sealed record BalanceSheetFiscalEvidence(
    DateOnly PeriodEnd,
    string? JalaliFiscalYearEnd);

public sealed record BalanceSheetHeaderSet(
    IReadOnlyList<BalanceSheetStatementHeader> BalanceSheets,
    IReadOnlyList<BalanceSheetFiscalEvidence> FiscalEvidence);

public sealed record BalanceSheetItemObservation(
    int SourceItemId,
    Guid LineItemId,
    decimal? Value,
    string? SourceTitle);

/// <summary>
/// Bounded read-only access to persisted Noavaran balance sheets (design §5). Every method is one database
/// round trip except <see cref="ReadHeadersAsync"/>, which issues two (balance-sheet headers and
/// fiscal-evidence headers). It never calls a provider.
/// </summary>
public interface IBalanceSheetStatementReader
{
    Task<BalanceSheetHeaderSet> ReadHeadersAsync(string providerName, string externalCompanyId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BalanceSheetStatementHeader>> ReadHeadersAtDatesAsync(string providerName,
        string externalCompanyId, IReadOnlyCollection<DateOnly> periodEnds, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, IReadOnlyList<BalanceSheetItemObservation>>> ReadLineItemsAsync(string providerName,
        IReadOnlyCollection<Guid> statementIds, IReadOnlyCollection<int> sourceItemIds,
        CancellationToken cancellationToken = default);

    Task<bool> ConsolidatedExistsAsync(string providerName, string externalCompanyId,
        CancellationToken cancellationToken = default);
}

public interface IBalanceSheetReportQueryUseCase
{
    Task<BalanceSheetReportResult> ExecuteAsync(BalanceSheetReportRequest request,
        CancellationToken cancellationToken = default);
}
