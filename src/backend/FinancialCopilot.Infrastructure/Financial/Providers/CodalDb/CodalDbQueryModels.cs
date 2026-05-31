namespace FinancialCopilot.Infrastructure.Financial.Providers.CodalDb;

// Public projection records for CodalDB query result rows. They are serialized verbatim into the
// JSON ProviderRawPayload, so their property names/order form the payload contract consumed by the
// dataset normalizers (022/023/024). Kept inside the provider layer; never referenced by core code.

/// <summary>One curated income/balance line item under a statement (catalog id + English title + amount).</summary>
public sealed record CodalStatementLineItem(int ItemId, string? ItemTitleEn, decimal Amount);

/// <summary>
/// A CodalDB <c>Statements</c> header with its income and balance line items. Carries the
/// audit/consolidation/representment flags so the statement-selection policy (023) can pick the
/// canonical variant; both Gregorian and Jalali dates are retained.
/// </summary>
public sealed record CodalStatementRow(
    long Id,
    long StmtId,
    int CompanyId,
    byte PeriodType,
    DateTimeOffset FiscalYearEnd,
    string? FiscalYearEndJalali,
    DateTimeOffset PeriodEnd,
    string? PeriodEndJalali,
    DateTimeOffset AnnouncementDate,
    bool? IsAudited,
    bool? IsRepresented,
    bool? IsComposing,
    DateTimeOffset? ModifiedDateTime,
    IReadOnlyList<CodalStatementLineItem> IncomeItems,
    IReadOnlyList<CodalStatementLineItem> BalanceItems);

/// <summary>One per-product row under a monthly activity report.</summary>
public sealed record CodalMonthlyActivityAmount(
    int ProductId,
    string? ProductTitle,
    long ProductProduceAmount,
    long ProductSaleAmount,
    decimal ProductSaleRate,
    long ProductSaleValue,
    string? ProductUnit);

/// <summary>A CodalDB <c>MonthlyActivity</c> header (Jalali period) with its per-product amounts.</summary>
public sealed record CodalMonthlyActivityRow(
    long Id,
    int CompanyId,
    byte Month,
    int Year,
    string? FiscalYearEnd,
    DateTimeOffset? ModifiedDateTime,
    IReadOnlyList<CodalMonthlyActivityAmount> Products);

/// <summary>
/// One row from <c>FinancialRatios</c> (vendor-precomputed ratio value, period, qualifier flags).
/// </summary>
public sealed record CodalRatioRow(
    long Id,
    int CompanyId,
    DateTimeOffset FiscalYearEnd,
    string? JalaliFiscalYearEnd,
    DateTimeOffset PeriodEnd,
    string? JalaliPeriodEnd,
    int PeriodType,
    bool? IsAudited,
    bool? IsRepresented,
    bool? IsComposing,
    int ItemId,
    double ItemValue,
    DateTimeOffset? ModifiedDateTime);

/// <summary>Result of a lightweight CodalDB health probe.</summary>
public sealed record CodalDbHealthProbe(bool Reachable, long? CompanyCount, string? Detail);
