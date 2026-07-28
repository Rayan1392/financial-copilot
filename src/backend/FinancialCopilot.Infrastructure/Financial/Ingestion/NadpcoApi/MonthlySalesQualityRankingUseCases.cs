using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

internal sealed class MonthlySalesQualityRankingQueryUseCase(
    IMonthlySalesQualityRankingRepository repository)
    : IMonthlySalesQualityRankingQueryUseCase
{
    public Task<MonthlySalesQualityRankingResponse> ExecuteAsync(
        MonthlySalesQualityRankingQuery query,
        CancellationToken ct = default) =>
        repository.GetRankingAsync(query with
        {
            Limit = Math.Clamp(query.Limit <= 0 ? 10 : query.Limit, 1, 50)
        }, ct);
}

internal sealed class RecalculateMonthlySalesQualityRankingUseCase(
    FinancialIngestionDbContext dbContext,
    IMonthlySalesQualityScoreCalculator calculator,
    IMonthlySalesQualityRankingRepository repository,
    ILogger<RecalculateMonthlySalesQualityRankingUseCase> logger,
    TimeProvider timeProvider)
    : IRecalculateMonthlySalesQualityRankingUseCase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<RecalculateMonthlySalesQualityRankingResult> ExecuteAsync(
        RecalculateMonthlySalesQualityRankingRequest request,
        CancellationToken ct = default)
    {
        var started = timeProvider.GetUtcNow();
        var period = request.ReportYear.HasValue && request.ReportMonth.HasValue
            ? (request.ReportYear.Value, request.ReportMonth.Value)
            : await ResolveLatestTrendPeriodAsync(ct)
                ?? throw new InvalidOperationException("No monthly activity trend snapshots are available for ranking recalculation.");

        logger.LogInformation(
            "Recalculating monthly sales quality ranking for {ReportYear}/{ReportMonth:00}.",
            period.Item1,
            period.Item2);

        var currentRows = await LoadCurrentRowsAsync(period.Item1, period.Item2, ct);
        var eligibleRows = currentRows
            .Where(r => r.MonthlySalesAmount > 0m && HasAnyBaseline(r))
            .ToList();
        var skipped = currentRows.Count - eligibleRows.Count;

        var externalIds = eligibleRows.Select(r => r.ExternalCompanyId).Distinct().ToList();
        var history = await LoadHistoryAsync(externalIds, period.Item1, period.Item2, ct);
        var productMix = await LoadProductMixAsync(externalIds, period.Item1, period.Item2, ct);

        var salesVsAvgByCompany = eligibleRows.ToDictionary(
            r => r.ExternalCompanyId,
            r => PercentChange(r.MonthlySalesAmount, r.Average12MonthSalesAmount));
        var industryPeerCounts = eligibleRows
            .Where(r => r.IndustryId.HasValue && salesVsAvgByCompany[r.ExternalCompanyId].HasValue)
            .GroupBy(r => r.IndustryId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());
        var industryPercentiles = CalculateIndustryPercentiles(eligibleRows, salesVsAvgByCompany);

        var calculatedAt = timeProvider.GetUtcNow();
        var snapshots = new List<MonthlySalesQualityRankingSnapshotUpsertRow>(eligibleRows.Count);

        foreach (var row in eligibleRows)
        {
            history.TryGetValue(row.ExternalCompanyId, out var companyHistory);
            companyHistory ??= [];
            var previous = companyHistory
                .Where(h => IsBefore(h.ReportYear, h.ReportMonth, period.Item1, period.Item2))
                .OrderByDescending(h => h.ReportYear)
                .ThenByDescending(h => h.ReportMonth)
                .FirstOrDefault();
            productMix.TryGetValue(row.ExternalCompanyId, out var mixRows);
            mixRows ??= [];

            var input = new MonthlySalesQualityScoreInput(
                MonthlySalesAmount: row.MonthlySalesAmount,
                Avg12MonthSalesAmount: row.Average12MonthSalesAmount,
                PreviousMonthSalesAmount: previous?.MonthlySalesAmount,
                SameMonthPreviousYearSalesAmount: row.SameMonthPreviousYearSalesAmount,
                MonthlySalesQuantity: row.MonthlySalesQuantity,
                PreviousMonthSalesQuantity: previous?.MonthlySalesQuantity,
                MonthlyProductionQuantity: row.MonthlyProductionQuantity,
                PreviousMonthProductionQuantity: previous?.MonthlyProductionQuantity,
                MonthlyAverageSalesRate: row.MonthlyAverageSalesRate,
                PreviousMonthAverageSalesRate: previous?.MonthlyAverageSalesRate,
                ProductMixRows: mixRows,
                LastThreeMonthlySalesAmounts: companyHistory
                    .Where(h => IsBeforeOrSame(h.ReportYear, h.ReportMonth, period.Item1, period.Item2))
                    .OrderBy(h => h.ReportYear)
                    .ThenBy(h => h.ReportMonth)
                    .TakeLast(3)
                    .Select(h => h.MonthlySalesAmount)
                    .ToList(),
                IndustryPercentile: industryPercentiles.GetValueOrDefault(row.ExternalCompanyId),
                IndustryPeerCount: row.IndustryId.HasValue
                    ? industryPeerCounts.GetValueOrDefault(row.IndustryId.Value)
                    : 0,
                HistoryMonths: companyHistory.Count,
                HasProductLineItems: row.MonthlySalesQuantity.HasValue || row.MonthlyProductionQuantity.HasValue || row.MonthlyAverageSalesRate.HasValue);

            var score = calculator.Calculate(input);
            snapshots.Add(new MonthlySalesQualityRankingSnapshotUpsertRow(
                Id: Guid.NewGuid(),
                ExternalCompanyId: row.ExternalCompanyId,
                CompanySymbol: row.CompanySymbol ?? row.ExternalCompanyId,
                CompanyName: row.CompanyName,
                IndustryId: row.IndustryId,
                IndustryTitle: row.IndustryTitle,
                IndustryGroupId: row.IndustryGroupId,
                IndustryGroupTitle: row.IndustryGroupTitle,
                ReportYear: period.Item1,
                ReportMonth: period.Item2,
                MonthlySalesAmount: row.MonthlySalesAmount,
                Avg12MonthSalesAmount: row.Average12MonthSalesAmount,
                SalesVsAvg12MPercent: score.SalesVsAvg12MPercent,
                SalesMonthOverMonthPercent: score.SalesMonthOverMonthPercent,
                SalesYearOverYearPercent: score.SalesYearOverYearPercent,
                QualityScore: score.QualityScore,
                QualityLabel: score.QualityLabel,
                ConfidenceScore: score.ConfidenceScore,
                RankMarket: 0,
                RankIndustry: null,
                DimensionScoresJson: JsonSerializer.Serialize(score.DimensionScores, JsonOptions),
                PositiveDriversJson: JsonSerializer.Serialize(score.PositiveDrivers, JsonOptions),
                NegativeDriversJson: JsonSerializer.Serialize(score.NegativeDrivers, JsonOptions),
                DataCoverageJson: JsonSerializer.Serialize(score.DataCoverage, JsonOptions),
                SourceProviderName: row.SourceProviderName,
                CalculatedAtUtc: calculatedAt,
                IsEligible: true));
        }

        snapshots = AssignRanks(snapshots);
        await repository.UpsertSnapshotsAsync(snapshots, ct);

        var elapsed = timeProvider.GetUtcNow() - started;
        logger.LogInformation(
            "Monthly sales quality ranking recalculated for {ReportYear}/{ReportMonth:00}: eligible={Eligible}, skipped={Skipped}, elapsedMs={ElapsedMs}.",
            period.Item1,
            period.Item2,
            snapshots.Count,
            skipped,
            elapsed.TotalMilliseconds);

        return new RecalculateMonthlySalesQualityRankingResult(
            period.Item1,
            period.Item2,
            snapshots.Count,
            skipped,
            calculatedAt);
    }

    private async Task<(int, byte)?> ResolveLatestTrendPeriodAsync(CancellationToken ct)
    {
        var period = await dbContext.CompanyMonthlyActivityTrendSnapshots
            .AsNoTracking()
            .OrderByDescending(r => r.ReportYear)
            .ThenByDescending(r => r.ReportMonth)
            .Select(r => new { r.ReportYear, r.ReportMonth })
            .FirstOrDefaultAsync(ct);

        return period is null ? null : (period.ReportYear, period.ReportMonth);
    }

    private async Task<List<RankingSourceRow>> LoadCurrentRowsAsync(int reportYear, byte reportMonth, CancellationToken ct)
    {
        var query =
            from trend in dbContext.CompanyMonthlyActivityTrendSnapshots.AsNoTracking()
            where trend.ReportYear == reportYear && trend.ReportMonth == reportMonth
            join company in dbContext.Companies.AsNoTracking()
                on trend.ExternalCompanyId equals company.ExternalCompanyId into companyJoin
            from company in companyJoin.DefaultIfEmpty()
            join industry in dbContext.Industries.AsNoTracking()
                on company.IndustryId equals industry.Id into industryJoin
            from industry in industryJoin.DefaultIfEmpty()
            join groupRow in dbContext.IndustryGroups.AsNoTracking()
                on company.GroupId equals groupRow.Id into groupJoin
            from groupRow in groupJoin.DefaultIfEmpty()
            select new RankingSourceRow(
                trend.ExternalCompanyId,
                trend.CompanySymbol ?? company.CompanySymbol ?? company.Ticker,
                trend.CompanyName ?? company.Name,
                company.IndustryId,
                industry.Name ?? trend.IndustryTitle,
                company.GroupId,
                groupRow.Name,
                trend.ReportYear,
                trend.ReportMonth,
                trend.MonthlySalesAmount,
                trend.MonthlyProductionQuantity,
                trend.MonthlySalesQuantity,
                trend.MonthlyAverageSalesRate,
                trend.SameMonthPreviousYearSalesAmount,
                trend.Average12MonthSalesAmount,
                trend.SalesAmountMomGrowthPercent,
                trend.SalesAmountYoYGrowthPercent,
                trend.SourceProviderName);

        return await query.ToListAsync(ct);
    }

    private async Task<Dictionary<string, List<RankingHistoryRow>>> LoadHistoryAsync(
        IReadOnlyCollection<string> externalCompanyIds,
        int reportYear,
        byte reportMonth,
        CancellationToken ct)
    {
        var minYear = reportYear - 1;
        var rows = await dbContext.CompanyMonthlyActivityTrendSnapshots
            .AsNoTracking()
            .Where(r => externalCompanyIds.Contains(r.ExternalCompanyId)
                        && (r.ReportYear > minYear || (r.ReportYear == minYear && r.ReportMonth >= reportMonth))
                        && (r.ReportYear < reportYear || (r.ReportYear == reportYear && r.ReportMonth <= reportMonth)))
            .Select(r => new RankingHistoryRow(
                r.ExternalCompanyId,
                r.ReportYear,
                r.ReportMonth,
                r.MonthlySalesAmount,
                r.MonthlySalesQuantity,
                r.MonthlyProductionQuantity,
                r.MonthlyAverageSalesRate))
            .ToListAsync(ct);

        return rows.GroupBy(r => r.ExternalCompanyId).ToDictionary(g => g.Key, g => g.ToList());
    }

    private async Task<Dictionary<string, IReadOnlyList<MonthlySalesQualityProductMixInput>>> LoadProductMixAsync(
        IReadOnlyCollection<string> externalCompanyIds,
        int reportYear,
        byte reportMonth,
        CancellationToken ct)
    {
        var rows = await dbContext.CompanyProductRevenueMix
            .AsNoTracking()
            .Where(r => externalCompanyIds.Contains(r.ExternalCompanyId)
                        && r.ReportYear == reportYear
                        && r.ReportMonth == reportMonth)
            .OrderBy(r => r.ProductRank)
            .Select(r => new
            {
                r.ExternalCompanyId,
                Item = new MonthlySalesQualityProductMixInput(
                    r.ProductName,
                    r.SalesAmount,
                    r.RevenueSharePercentage,
                    r.ProductRank,
                    r.IsDominantProduct,
                    r.ProductionQuantity,
                    r.SalesQuantity,
                    r.SalesRate)
            })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.ExternalCompanyId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<MonthlySalesQualityProductMixInput>)g.Select(r => r.Item).ToList());
    }

    private static bool HasAnyBaseline(RankingSourceRow row) =>
        row.Average12MonthSalesAmount > 0m ||
        row.SameMonthPreviousYearSalesAmount > 0m ||
        row.SalesAmountMomGrowthPercent.HasValue ||
        row.SalesAmountYoYGrowthPercent.HasValue;

    private static Dictionary<string, decimal?> CalculateIndustryPercentiles(
        IReadOnlyList<RankingSourceRow> rows,
        IReadOnlyDictionary<string, decimal?> salesVsAvgByCompany)
    {
        var result = rows.ToDictionary(r => r.ExternalCompanyId, _ => (decimal?)null);
        foreach (var industryGroup in rows.Where(r => r.IndustryId.HasValue).GroupBy(r => r.IndustryId!.Value))
        {
            var peers = industryGroup
                .Select(r => new { r.ExternalCompanyId, SalesVsAvg = salesVsAvgByCompany[r.ExternalCompanyId] })
                .Where(r => r.SalesVsAvg.HasValue)
                .OrderBy(r => r.SalesVsAvg!.Value)
                .ToList();

            if (peers.Count < 5) continue;

            for (var i = 0; i < peers.Count; i++)
            {
                var percentile = peers.Count == 1 ? 1m : (decimal)i / (peers.Count - 1);
                result[peers[i].ExternalCompanyId] = percentile;
            }
        }

        return result;
    }

    private static List<MonthlySalesQualityRankingSnapshotUpsertRow> AssignRanks(
        List<MonthlySalesQualityRankingSnapshotUpsertRow> rows)
    {
        var marketRanked = rows
            .OrderByDescending(r => r.QualityScore)
            .ThenByDescending(r => r.ConfidenceScore)
            .ThenBy(r => r.CompanySymbol)
            .Select((row, index) => row with { RankMarket = index + 1 })
            .ToList();

        var byId = marketRanked.ToDictionary(r => r.ExternalCompanyId);
        foreach (var industry in marketRanked.Where(r => r.IndustryId.HasValue).GroupBy(r => r.IndustryId!.Value))
        {
            var ranked = industry
                .OrderByDescending(r => r.QualityScore)
                .ThenByDescending(r => r.ConfidenceScore)
                .ThenBy(r => r.CompanySymbol)
                .Select((row, index) => row with { RankIndustry = index + 1 });

            foreach (var row in ranked)
                byId[row.ExternalCompanyId] = row;
        }

        return byId.Values
            .OrderBy(r => r.RankMarket)
            .ToList();
    }

    private static decimal? PercentChange(decimal current, decimal? baseline)
    {
        if (!baseline.HasValue || baseline.Value <= 0m) return null;
        return (current - baseline.Value) / baseline.Value * 100m;
    }

    private static bool IsBefore(int year, byte month, int otherYear, byte otherMonth) =>
        year < otherYear || (year == otherYear && month < otherMonth);

    private static bool IsBeforeOrSame(int year, byte month, int otherYear, byte otherMonth) =>
        year < otherYear || (year == otherYear && month <= otherMonth);

    private sealed record RankingSourceRow(
        string ExternalCompanyId,
        string? CompanySymbol,
        string? CompanyName,
        Guid? IndustryId,
        string? IndustryTitle,
        Guid? IndustryGroupId,
        string? IndustryGroupTitle,
        int ReportYear,
        byte ReportMonth,
        decimal MonthlySalesAmount,
        decimal? MonthlyProductionQuantity,
        decimal? MonthlySalesQuantity,
        decimal? MonthlyAverageSalesRate,
        decimal? SameMonthPreviousYearSalesAmount,
        decimal? Average12MonthSalesAmount,
        decimal? SalesAmountMomGrowthPercent,
        decimal? SalesAmountYoYGrowthPercent,
        string SourceProviderName);

    private sealed record RankingHistoryRow(
        string ExternalCompanyId,
        int ReportYear,
        byte ReportMonth,
        decimal MonthlySalesAmount,
        decimal? MonthlySalesQuantity,
        decimal? MonthlyProductionQuantity,
        decimal? MonthlyAverageSalesRate);
}
