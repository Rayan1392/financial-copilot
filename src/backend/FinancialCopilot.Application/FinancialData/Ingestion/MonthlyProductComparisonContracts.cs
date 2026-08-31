namespace FinancialCopilot.Application.FinancialData.Ingestion;

public enum MonthlyProductComparisonFocus { All, Sales, Production, Quantity, Rate }
public enum MonthlyProductComparisonState { Available, Partial, Empty, Unavailable, Error }
public enum MonthlyProductComparisonBlockingReason { CompanyNotFound, CurrentPeriodNotFound, ComparisonPeriodNotFound, EqualPeriods, InvalidPeriod, NoMonthlyProductData }
public enum MonthlyProductComparisonWarning { ProductMatchAmbiguous, UnitChanged, MissingRate, InvalidQuantity, InvalidSalesAmount, PossibleDuplicateRows, PartialDecomposition, ZeroCompanyRevenueChange }
public enum ProductLifecycle { Continuing, New, Discontinued }
public enum ProductIdentityState { Code, TitleAndUnit, Ambiguous, Unmatched }
public enum ProductDriver { QuantityDriven, PriceDriven, Mixed, Unclassified }
public enum ProductionSalesSignal { ProductionAboveSales, SalesAboveProduction, NoMaterialDifference, Unavailable }

public readonly record struct JalaliPeriod
{
    public JalaliPeriod(int year, int month)
    {
        if (year < 1 || month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month));
        Year = year; Month = month;
    }
    public int Year { get; }
    public int Month { get; }
    public override string ToString() => $"{Year:D4}/{Month:D2}";
    public static bool TryCreate(int year, int month, out JalaliPeriod period)
    { try { period = new(year, month); return true; } catch (ArgumentOutOfRangeException) { period = default; return false; } }
    public static bool operator <(JalaliPeriod a, JalaliPeriod b) => a.Year < b.Year || a.Year == b.Year && a.Month < b.Month;
    public static bool operator >(JalaliPeriod a, JalaliPeriod b) => b < a;
}

public sealed record MonthlyProductComparisonQuery(
    string CompanyText,
    JalaliPeriod? CurrentPeriod = null,
    JalaliPeriod? ComparisonPeriod = null,
    string? ProductText = null,
    MonthlyProductComparisonFocus Focus = MonthlyProductComparisonFocus.All);

public sealed record ProductSalesObservation(
    Guid RowId, Guid ReportId, string ExternalCompanyId, JalaliPeriod Period,
    string ProviderName, string ExternalReportId, DateOnly PeriodStart, DateOnly PeriodEnd,
    string? ProductCode, string? Title, string? Unit, decimal? ProductionQuantity,
    decimal? SalesQuantity, decimal? SalesRate, decimal? SalesAmount, int SourceRank = 0);

public sealed record MonthlyProductComparisonEvidence(Guid ReportId, Guid RowId, string ProviderName, string ExternalReportId, JalaliPeriod Period);
public sealed record MonthlyProductComparisonPeriod(JalaliPeriod Period, IReadOnlyList<ProductSalesObservation> Observations, IReadOnlyList<MonthlyProductComparisonEvidence> Evidence);

public interface IMonthlyProductComparisonReadRepository
{
    Task<IReadOnlyList<JalaliPeriod>> GetAvailablePeriodsAsync(string externalCompanyId, CancellationToken ct = default);
    Task<MonthlyProductComparisonPeriod?> GetPeriodAsync(string externalCompanyId, JalaliPeriod period, CancellationToken ct = default);
}
public interface IMonthlyProductComparisonUseCase
{
    Task<MonthlyProductComparisonResponse> ExecuteAsync(MonthlyProductComparisonQuery query, CancellationToken ct = default);
}

public sealed record ProductPeriodValues(decimal? SalesAmount, decimal? ProductionQuantity, decimal? SalesQuantity, decimal? SalesRate, string? Unit = null);
public sealed record ProductComparisonItem(
    string DisplayTitle, string? RawUnit, string NormalizedKey, ProductIdentityState Identity,
    ProductLifecycle Lifecycle, ProductPeriodValues? Current, ProductPeriodValues? Comparison,
    decimal? SalesChange, decimal? ContributionPercent, decimal? QuantityEffect, decimal? PriceEffect,
    decimal? Residual, ProductDriver Driver, ProductionSalesSignal ProductionSalesSignal,
    decimal? ProductionSalesDifference, IReadOnlyCollection<MonthlyProductComparisonWarning> Warnings,
    IReadOnlyCollection<MonthlyProductComparisonEvidence> Evidence);
public sealed record CompanySalesTotals(decimal Current, decimal Comparison, decimal Change, decimal? ChangePercent);
public sealed record MonthlyProductComparisonResponse(
    MonthlyProductComparisonState State, string CompanyText, string? ExternalCompanyId,
    JalaliPeriod? CurrentPeriod, JalaliPeriod? ComparisonPeriod, CompanySalesTotals? Totals,
    ProductDriver? PrimaryDriver, ProductComparisonItem? LargestPositive, ProductComparisonItem? LargestNegative,
    IReadOnlyList<ProductComparisonItem> Products, IReadOnlyCollection<MonthlyProductComparisonWarning> Warnings,
    IReadOnlyCollection<MonthlyProductComparisonEvidence> Evidence,
    MonthlyProductComparisonBlockingReason? BlockingReason = null, string? ClarificationMessage = null,
    string? Narrative = null);
