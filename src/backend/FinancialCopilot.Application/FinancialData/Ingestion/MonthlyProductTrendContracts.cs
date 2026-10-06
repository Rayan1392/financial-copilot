using FinancialCopilot.Application.AI.Orchestration;

namespace FinancialCopilot.Application.FinancialData.Ingestion;

public enum MonthlyProductTrendResolutionState { Resolved, NotFound, Ambiguous }

public enum MonthlyProductTrendRateStatus
{
    ValidRate,
    ValidZeroRate,
    MissingQuantity,
    ZeroQuantity,
    MissingValue,
    InvalidNegativeInput,
    Overflow,
    UnavailableRate
}

public sealed record MonthlyProductTrendCandidate(
    string DisplayTitle,
    string? Unit,
    string ProductKey,
    string? ProviderProductCode,
    long? ProviderProductId);

public sealed record MonthlyProductTrendQuery(
    string CompanyText,
    string ProductText,
    JalaliPeriod? FromPeriod = null,
    JalaliPeriod? ToPeriod = null,
    MonthlyProductComparisonFocus Focus = MonthlyProductComparisonFocus.Sales,
    CanonicalQueryEntity? CanonicalCompany = null,
    CanonicalQueryProduct? CanonicalProduct = null);

public sealed record MonthlyProductTrendPoint(
    JalaliPeriod Period,
    string FiscalLabel,
    string ProductKey,
    string ProductTitle,
    string? ProductUnit,
    decimal? ProductionQuantity,
    decimal? SaleQuantity,
    decimal? SalesValueMillionRial,
    decimal? SalesValueBillionToman,
    decimal? CalculatedSaleRateToman,
    MonthlyProductTrendRateStatus RateStatus,
    IReadOnlyCollection<MonthlyProductComparisonEvidence> Evidence,
    bool IsGap = false);

public sealed record MonthlyProductTrendResult(
    string ResultDiscriminator,
    int ResultVersion,
    MonthlyProductTrendResolutionState ResolutionState,
    string CompanyText,
    string? ExternalCompanyId,
    string? CompanyName,
    string? CompanySymbol,
    string? ProductKey,
    string? ProviderProductCode,
    long? ProviderProductId,
    string? ProductTitle,
    string? ProductUnit,
    IReadOnlyList<MonthlyProductTrendPoint> Points,
    IReadOnlyList<MonthlyProductTrendCandidate> Candidates,
    IReadOnlyCollection<MonthlyProductComparisonEvidence> Evidence,
    string? BlockingReason = null,
    string? Message = null)
{
    public const string Discriminator = "monthly_product_trend";
    public const int CurrentVersion = 1;

    public bool IsResolved => ResolutionState == MonthlyProductTrendResolutionState.Resolved;
}

public interface IMonthlyProductTrendQueryUseCase
{
    Task<MonthlyProductTrendResult> ExecuteAsync(MonthlyProductTrendQuery query, CancellationToken ct = default);
}

public static class MonthlyProductTrendCalculator
{
    public static (decimal? Rate, MonthlyProductTrendRateStatus Status) CalculateRate(
        decimal? salesValueMillionRial,
        decimal? saleQuantity)
    {
        if (!salesValueMillionRial.HasValue) return (null, MonthlyProductTrendRateStatus.MissingValue);
        if (!saleQuantity.HasValue) return (null, MonthlyProductTrendRateStatus.MissingQuantity);
        if (salesValueMillionRial < 0 || saleQuantity < 0)
            return (null, MonthlyProductTrendRateStatus.InvalidNegativeInput);
        if (saleQuantity == 0) return (null, MonthlyProductTrendRateStatus.ZeroQuantity);

        try
        {
            var rate = checked(salesValueMillionRial.Value * 100_000m / saleQuantity.Value);
            return rate == 0m
                ? (0m, MonthlyProductTrendRateStatus.ValidZeroRate)
                : (rate, MonthlyProductTrendRateStatus.ValidRate);
        }
        catch (OverflowException)
        {
            return (null, MonthlyProductTrendRateStatus.Overflow);
        }
    }

    public static string ProductKey(string externalCompanyId, ProductSalesObservation row)
    {
        var code = !string.IsNullOrWhiteSpace(row.ProviderProductCode)
            ? $"CODE:{MonthlyProductComparisonNormalizer.Normalize(row.ProviderProductCode)}"
            : row.ProviderProductId is > 0
                ? $"ID:{row.ProviderProductId.Value}"
                : $"TITLE:{MonthlyProductComparisonNormalizer.Normalize(row.Title)}|UNIT:{MonthlyProductComparisonNormalizer.Unit(row.Unit)}";
        return $"{row.ProviderName}:{externalCompanyId}:{code}";
    }

    public static string NormalizeProductText(string? value) => MonthlyProductComparisonNormalizer.Normalize(value);
}
