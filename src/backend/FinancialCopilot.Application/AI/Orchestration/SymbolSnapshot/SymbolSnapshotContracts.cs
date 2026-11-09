using System.Text.Json.Serialization;

namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>Feature 156: the default card shown when a message is only an eligible ticker.</summary>
public sealed class SymbolSnapshotOptions
{
    public const string SectionName = "SymbolSnapshot";

    public bool Enabled { get; set; }
    public int SectionTimeoutMilliseconds { get; set; } = 2000;
    public int TurnBudgetMilliseconds { get; set; } = 4000;
}

public static class SymbolSnapshotCapability
{
    public const string Code = "symbol_snapshot";
}

public sealed record EligibleSymbol(string Symbol, string CompanyName, string? MarketName, string? GroupName);

public interface IEligibleSymbolResolver
{
    /// <summary>
    /// Finds the eligible company with this compact key. Returns null when no company or more than one company
    /// has the key, and when the lookup fails, so that a bare-ticker turn falls back to the normal flow.
    /// </summary>
    Task<EligibleSymbol?> ResolveAsync(string compactKey, CancellationToken cancellationToken);
}

public interface ISymbolSnapshotUseCase
{
    Task<SymbolSnapshotResult?> ExecuteAsync(string symbol, string correlationId, CancellationToken cancellationToken);
}

public interface ISymbolSnapshotSectionObserver
{
    void Record(string correlationId, string section, SymbolSnapshotSectionStatus status, TimeSpan elapsed, Exception? failure);
}

/// <summary>Stored in persisted conversation JSON, so it is written by name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SymbolSnapshotSectionStatus>))]
public enum SymbolSnapshotSectionStatus
{
    Available,
    NoData,
    Unavailable,
    UnavailableForProvider
}

public sealed record SymbolSnapshotPrice(
    SymbolSnapshotSectionStatus Status,
    string? NoticeFa,
    decimal? LatestPrice = null,
    decimal? DailyChangePercent = null,
    string? TradingDateJalali = null);

public sealed record SymbolSnapshotMonthlySales(
    SymbolSnapshotSectionStatus Status,
    string? NoticeFa,
    string? PeriodLabelFa = null,
    string? UnitLabelFa = null,
    decimal? SalesAmount = null,
    decimal? MonthOverMonthPercent = null,
    decimal? YearOverYearPercent = null);

public sealed record SymbolSnapshotQuarterlyRow(
    string LabelFa,
    string UnitLabelFa,
    decimal? Value,
    decimal? YearOverYearPercent);

public sealed record SymbolSnapshotQuarterlyReport(
    SymbolSnapshotSectionStatus Status,
    string? NoticeFa,
    string? PeriodLabelFa = null,
    bool? IsAudited = null,
    IReadOnlyList<SymbolSnapshotQuarterlyRow>? Rows = null);

public sealed record SymbolSnapshotResult(
    string Symbol,
    string CompanyName,
    string? MarketName,
    string? GroupName,
    string IntroFa,
    SymbolSnapshotPrice Price,
    SymbolSnapshotMonthlySales MonthlySales,
    SymbolSnapshotQuarterlyReport QuarterlyReport)
{
    public bool HasData() =>
        Price.Status == SymbolSnapshotSectionStatus.Available ||
        MonthlySales.Status == SymbolSnapshotSectionStatus.Available ||
        QuarterlyReport.Status == SymbolSnapshotSectionStatus.Available;

    /// <summary>A section the active data source cannot provide does not make the answer partial.</summary>
    public bool IsComplete() =>
        IsComplete(Price.Status) && IsComplete(MonthlySales.Status) && IsComplete(QuarterlyReport.Status);

    private static bool IsComplete(SymbolSnapshotSectionStatus status) =>
        status is SymbolSnapshotSectionStatus.Available or SymbolSnapshotSectionStatus.UnavailableForProvider;
}
