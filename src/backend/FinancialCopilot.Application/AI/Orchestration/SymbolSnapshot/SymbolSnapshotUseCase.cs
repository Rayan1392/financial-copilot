using System.Diagnostics;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.ValueObjects;

namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>
/// Composes the card from the same use cases that serve the full capabilities, so a card number equals the number
/// the user sees after pressing its button. Each section is isolated: a failure in one never fails the turn.
/// </summary>
public sealed class SymbolSnapshotUseCase(
    IEligibleSymbolResolver eligibleSymbols,
    IMarketQuoteResolver quotes,
    IMonthlyActivityTrendQueryUseCase monthlyTrend,
    IQuarterlyFinancialReportsQueryUseCase quarterlyReports,
    SymbolSnapshotOptions options,
    IChatCapabilityAvailability? availability = null,
    ISymbolSnapshotSectionObserver? observer = null) : ISymbolSnapshotUseCase
{
    private const string NoPriceFa = "قیمتی برای این نماد ثبت نشده است.";
    private const string NoMonthlyFa = "گزارش ماهانه‌ای برای این نماد ثبت نشده است.";
    private const string NoQuarterlyFa = "گزارش فصلی‌ای برای این نماد ثبت نشده است.";
    private const string UnavailableFa = "این بخش فعلاً در دسترس نیست.";
    private const string UnavailableForProviderFa = "این بخش در منبع دادهٔ فعال در دسترس نیست.";

    private const int SalesItem = 15;
    private const int GrossProfitItem = 139;
    private const int NetProfitItem = 143;
    private const int EpsItem = 160;
    private const int NetInterestIncomeItem = 306;
    private const int NetFeeIncomeItem = 307;
    private const string BankingTemplate = "BankingIncomeStatement";

    // The same fiscal quarter of the previous year is the fifth column when no quarter is missing.
    private const int QuartersForYearOverYear = 5;

    private static readonly int[] GeneralRowItems = [SalesItem, GrossProfitItem, NetProfitItem, EpsItem];
    private static readonly int[] BankingRowItems = [NetInterestIncomeItem, NetFeeIncomeItem, NetProfitItem, EpsItem];
    private static readonly string[] QuarterOrdinals = ["اول", "دوم", "سوم", "چهارم"];
    private static readonly string[] MonthNames =
        ["فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"];

    public async Task<SymbolSnapshotResult?> ExecuteAsync(string symbol, string correlationId, CancellationToken cancellationToken)
    {
        var company = await eligibleSymbols.ResolveAsync(SymbolSnapshotIntentRules.CompactKey(symbol), cancellationToken);
        if (company is null)
            return null;

        using var turn = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        turn.CancelAfter(options.TurnBudgetMilliseconds);

        var price = await RunSectionAsync(
            "price", correlationId, turn.Token, cancellationToken,
            token => ReadPriceAsync(company.Symbol, token),
            result => result.Status,
            status => new SymbolSnapshotPrice(status, UnavailableFa));
        var monthly = IsAvailable(CapabilityExecutionRoutes.MonthlyActivityTrend)
            ? await RunSectionAsync(
                "monthly", correlationId, turn.Token, cancellationToken,
                token => ReadMonthlyAsync(company.Symbol, token),
                result => result.Status,
                status => new SymbolSnapshotMonthlySales(status, UnavailableFa))
            : new SymbolSnapshotMonthlySales(SymbolSnapshotSectionStatus.UnavailableForProvider, UnavailableForProviderFa);
        var quarterly = await RunSectionAsync(
            "quarterly", correlationId, turn.Token, cancellationToken,
            token => ReadQuarterlyAsync(company.Symbol, token),
            result => result.Status,
            status => new SymbolSnapshotQuarterlyReport(status, UnavailableFa));

        var hasData = price.Status == SymbolSnapshotSectionStatus.Available ||
                      monthly.Status == SymbolSnapshotSectionStatus.Available ||
                      quarterly.Status == SymbolSnapshotSectionStatus.Available;
        var intro = hasData
            ? $"خلاصهٔ آخرین داده‌های «{company.Symbol}». برای جزئیات یکی از گزینه‌های زیر را انتخاب کنید."
            : $"برای «{company.Symbol}» فعلاً داده‌ای برای نمایش وجود ندارد.";
        return new SymbolSnapshotResult(
            company.Symbol, company.CompanyName, company.MarketName, company.GroupName, intro, price, monthly, quarterly);
    }

    private bool IsAvailable(string route) => availability?.Evaluate(route).IsAvailable ?? true;

    private async Task<TSection> RunSectionAsync<TSection>(
        string section,
        string correlationId,
        CancellationToken turnToken,
        CancellationToken callerToken,
        Func<CancellationToken, Task<TSection>> read,
        Func<TSection, SymbolSnapshotSectionStatus> statusOf,
        Func<SymbolSnapshotSectionStatus, TSection> unavailable)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            using var sectionTimeout = CancellationTokenSource.CreateLinkedTokenSource(turnToken);
            sectionTimeout.CancelAfter(options.SectionTimeoutMilliseconds);
            var result = await read(sectionTimeout.Token);
            Report(section, correlationId, statusOf(result), started, null);
            return result;
        }
        catch (Exception exception) when (!callerToken.IsCancellationRequested)
        {
            Report(section, correlationId, SymbolSnapshotSectionStatus.Unavailable, started, exception);
            return unavailable(SymbolSnapshotSectionStatus.Unavailable);
        }
    }

    private void Report(string section, string correlationId, SymbolSnapshotSectionStatus status, long started, Exception? failure)
    {
        var elapsed = Stopwatch.GetElapsedTime(started);
        Activity.Current?.SetTag($"symbol_snapshot.{section}.status", status.ToString());
        Activity.Current?.SetTag($"symbol_snapshot.{section}.elapsed_ms", elapsed.TotalMilliseconds);
        observer?.Record(correlationId, section, status, elapsed, failure);
    }

    private async Task<SymbolSnapshotPrice> ReadPriceAsync(string symbol, CancellationToken cancellationToken)
    {
        var result = await quotes.ResolveAsync([new SymbolCode(symbol)], cancellationToken);
        var quote = result.Observations.FirstOrDefault();
        if (quote is null)
            return new SymbolSnapshotPrice(SymbolSnapshotSectionStatus.NoData, NoPriceFa);

        var tradingDate = quote.TradingDate != default ? quote.TradingDate : DateOnly.FromDateTime(quote.AsOf.UtcDateTime);
        return new SymbolSnapshotPrice(
            SymbolSnapshotSectionStatus.Available,
            null,
            quote.LatestPrice,
            quote.PriceChangePercentage,
            FiscalCalendarPolicy.ToJalali(tradingDate));
    }

    private async Task<SymbolSnapshotMonthlySales> ReadMonthlyAsync(string symbol, CancellationToken cancellationToken)
    {
        var trend = await monthlyTrend.ExecuteAsync(
            new MonthlyActivityTrendQuery(symbol, symbol, IncludeChartPayload: false),
            cancellationToken);
        if (trend?.LatestMonthlySalesAmount is not { } amount)
            return new SymbolSnapshotMonthlySales(SymbolSnapshotSectionStatus.NoData, NoMonthlyFa);

        return new SymbolSnapshotMonthlySales(
            SymbolSnapshotSectionStatus.Available,
            null,
            $"{MonthNames[trend.LatestReportMonth - 1]} {trend.LatestReportYear}",
            trend.UnitLabelFa,
            amount,
            trend.SalesAmountMomGrowthPercent,
            trend.SalesAmountYoYGrowthPercent);
    }

    private async Task<SymbolSnapshotQuarterlyReport> ReadQuarterlyAsync(string symbol, CancellationToken cancellationToken)
    {
        var report = await quarterlyReports.ExecuteAsync(
            new QuarterlyFinancialReportTableQuery(string.Empty, symbol, QuartersForYearOverYear),
            cancellationToken);
        var latest = report.PeriodsNewestFirst.FirstOrDefault();
        if (latest is null ||
            report.Outcome is not (QuarterlyFinancialReportOutcome.SuccessfulTable or QuarterlyFinancialReportOutcome.PartialTable))
            return new SymbolSnapshotQuarterlyReport(SymbolSnapshotSectionStatus.NoData, NoQuarterlyFa);

        var isBanking = report.TemplateId == BankingTemplate;
        var sameQuarterLastYear = report.PeriodsNewestFirst.FirstOrDefault(period =>
            period.FiscalYear == latest.FiscalYear - 1 && period.Quarter == latest.Quarter);
        var rows = (isBanking ? BankingRowItems : GeneralRowItems)
            .Select(item => report.Rows.FirstOrDefault(row => row.SourceItemId == item))
            .Where(row => row is not null)
            .Select(row => ToSummaryRow(row!, latest, sameQuarterLastYear, isBanking))
            .ToArray();
        if (rows.All(row => row.Value is null))
            return new SymbolSnapshotQuarterlyReport(SymbolSnapshotSectionStatus.NoData, NoQuarterlyFa);

        var latestReport = report.SelectedReports?.FirstOrDefault(selected =>
            selected.PeriodEnd == latest.End && selected.PeriodMonths == latest.Quarter * 3);
        return new SymbolSnapshotQuarterlyReport(
            SymbolSnapshotSectionStatus.Available,
            null,
            $"فصل {QuarterOrdinals[latest.Quarter - 1]} {latest.FiscalYear}",
            latestReport?.IsAudited,
            rows);
    }

    private static SymbolSnapshotQuarterlyRow ToSummaryRow(
        QuarterlyFinancialRow row,
        QuarterlyFiscalPeriod latest,
        QuarterlyFiscalPeriod? sameQuarterLastYear,
        bool isBanking)
    {
        var current = row.Cells.GetValueOrDefault(latest.Label);
        var value = current is { State: QuarterlyCellState.Available } ? current.Value : null;
        var baseCell = sameQuarterLastYear is null ? null : row.Cells.GetValueOrDefault(sameQuarterLastYear.Label);
        var yearOverYear = current is not null && baseCell is not null
            ? QuarterlyFinancialReportsNormalizer.Compare(row.RowId, latest, sameQuarterLastYear!, current, baseCell, isBanking)
            : null;
        return new SymbolSnapshotQuarterlyRow(
            row.LabelFa,
            row.Unit == "rial_per_share" ? "ریال" : "میلیون ریال",
            value,
            yearOverYear is { State: QuarterlyComparisonState.Available } ? yearOverYear.RelativeChangePercent : null);
    }
}
