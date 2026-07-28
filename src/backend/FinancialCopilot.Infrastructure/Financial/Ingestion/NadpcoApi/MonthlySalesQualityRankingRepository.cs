using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

internal sealed class MonthlySalesQualityRankingRepository(FinancialIngestionDbContext dbContext)
    : IMonthlySalesQualityRankingRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<(int ReportYear, byte ReportMonth)?> GetLatestAvailablePeriodAsync(CancellationToken ct = default)
    {
        var period = await dbContext.MonthlySalesQualityRankingSnapshots
            .AsNoTracking()
            .Where(r => r.IsEligible)
            .OrderByDescending(r => r.ReportYear)
            .ThenByDescending(r => r.ReportMonth)
            .Select(r => new { r.ReportYear, r.ReportMonth })
            .FirstOrDefaultAsync(ct);

        return period is null ? null : (period.ReportYear, period.ReportMonth);
    }

    public async Task<MonthlySalesQualityRankingResponse> GetRankingAsync(
        MonthlySalesQualityRankingQuery query,
        CancellationToken ct = default)
    {
        var period = query.ReportYear.HasValue && query.ReportMonth.HasValue
            ? (query.ReportYear.Value, query.ReportMonth.Value)
            : await GetLatestAvailablePeriodAsync(ct) ?? (0, (byte)0);

        if (period.Item1 == 0)
        {
            return new MonthlySalesQualityRankingResponse(
                0,
                0,
                query.Scope,
                query.Direction,
                0,
                DateTimeOffset.UtcNow,
                []);
        }

        var limit = Math.Clamp(query.Limit <= 0 ? 10 : query.Limit, 1, 50);
        var symbols = query.Symbols?
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(NormalizeSymbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rowsQuery = dbContext.MonthlySalesQualityRankingSnapshots
            .AsNoTracking()
            .Where(r => r.ReportYear == period.Item1 && r.ReportMonth == period.Item2);

        if (query.OnlyEligibleRows)
            rowsQuery = rowsQuery.Where(r => r.IsEligible);

        if (query.IndustryId.HasValue)
            rowsQuery = rowsQuery.Where(r => r.IndustryId == query.IndustryId.Value);

        if (query.IndustryGroupId.HasValue)
            rowsQuery = rowsQuery.Where(r => r.IndustryGroupId == query.IndustryGroupId.Value);

        if (!string.IsNullOrWhiteSpace(query.IndustryTitle))
        {
            var industryTitle = query.IndustryTitle.Trim();
            rowsQuery = rowsQuery.Where(r => r.IndustryTitle != null && EF.Functions.ILike(r.IndustryTitle, $"%{industryTitle}%"));
        }

        if (!string.IsNullOrWhiteSpace(query.IndustryGroupTitle))
        {
            var industryGroupTitle = query.IndustryGroupTitle.Trim();
            rowsQuery = rowsQuery.Where(r => r.IndustryGroupTitle != null && EF.Functions.ILike(r.IndustryGroupTitle, $"%{industryGroupTitle}%"));
        }

        if (query.MinimumSalesAmount.HasValue)
            rowsQuery = rowsQuery.Where(r => r.MonthlySalesAmount >= query.MinimumSalesAmount.Value);

        if (symbols is { Count: > 0 })
            rowsQuery = rowsQuery.Where(r => symbols.Contains(r.CompanySymbol));

        var totalEligible = await rowsQuery.CountAsync(ct);

        rowsQuery = query.Direction == MonthlySalesQualityDirection.Bottom
            ? rowsQuery.OrderBy(r => r.QualityScore).ThenByDescending(r => r.ConfidenceScore).ThenBy(r => r.CompanySymbol)
            : rowsQuery.OrderByDescending(r => r.QualityScore).ThenByDescending(r => r.ConfidenceScore).ThenBy(r => r.CompanySymbol);

        var rows = await rowsQuery.Take(limit).ToListAsync(ct);

        return new MonthlySalesQualityRankingResponse(
            period.Item1,
            period.Item2,
            query.Scope,
            query.Direction,
            totalEligible,
            rows.Count > 0 ? rows.Max(r => r.CalculatedAtUtc) : DateTimeOffset.UtcNow,
            rows.Select((row, index) => MapToItem(row, query, index + 1)).ToList());
    }

    public async Task DeletePeriodSnapshotsAsync(int reportYear, byte reportMonth, CancellationToken ct = default)
    {
        var rows = await dbContext.MonthlySalesQualityRankingSnapshots
            .Where(r => r.ReportYear == reportYear && r.ReportMonth == reportMonth)
            .ToListAsync(ct);

        if (rows.Count == 0) return;

        dbContext.MonthlySalesQualityRankingSnapshots.RemoveRange(rows);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task UpsertSnapshotsAsync(
        IReadOnlyList<MonthlySalesQualityRankingSnapshotUpsertRow> rows,
        CancellationToken ct = default)
    {
        if (rows.Count == 0) return;

        var first = rows[0];
        await DeletePeriodSnapshotsAsync(first.ReportYear, first.ReportMonth, ct);

        foreach (var row in rows)
            dbContext.MonthlySalesQualityRankingSnapshots.Add(MapToRow(row));

        await dbContext.SaveChangesAsync(ct);
    }

    private static MonthlySalesQualityRankingItem MapToItem(
        MonthlySalesQualityRankingSnapshotRow row,
        MonthlySalesQualityRankingQuery query,
        int fallbackRank)
    {
        var rank = query.Scope == MonthlySalesQualityScope.Industry && row.RankIndustry.HasValue
            ? row.RankIndustry.Value
            : row.RankMarket > 0 ? row.RankMarket : fallbackRank;

        return new MonthlySalesQualityRankingItem(
            rank,
            row.CompanySymbol,
            row.CompanyName,
            row.IndustryTitle,
            row.QualityScore,
            row.QualityLabel,
            row.ConfidenceScore,
            row.MonthlySalesAmount,
            row.Avg12MonthSalesAmount,
            row.SalesVsAvg12MPercent,
            row.SalesMonthOverMonthPercent,
            row.SalesYearOverYearPercent,
            query.IncludeDimensionScores ? Deserialize<MonthlySalesQualityDimensionScores>(row.DimensionScoresJson) : null,
            query.IncludeExplanation ? Deserialize<IReadOnlyList<string>>(row.PositiveDriversJson) ?? [] : [],
            query.IncludeExplanation ? Deserialize<IReadOnlyList<string>>(row.NegativeDriversJson) ?? [] : [],
            Deserialize<MonthlySalesQualityDataCoverage>(row.DataCoverageJson)
                ?? new MonthlySalesQualityDataCoverage(0, false, false, 0),
            row.SourceProviderName,
            row.CalculatedAtUtc);
    }

    private static T? Deserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static MonthlySalesQualityRankingSnapshotRow MapToRow(MonthlySalesQualityRankingSnapshotUpsertRow row) => new()
    {
        Id = row.Id,
        ExternalCompanyId = row.ExternalCompanyId,
        CompanySymbol = row.CompanySymbol,
        CompanyName = row.CompanyName,
        IndustryId = row.IndustryId,
        IndustryTitle = row.IndustryTitle,
        IndustryGroupId = row.IndustryGroupId,
        IndustryGroupTitle = row.IndustryGroupTitle,
        ReportYear = row.ReportYear,
        ReportMonth = row.ReportMonth,
        MonthlySalesAmount = row.MonthlySalesAmount,
        Avg12MonthSalesAmount = row.Avg12MonthSalesAmount,
        SalesVsAvg12MPercent = row.SalesVsAvg12MPercent,
        SalesMonthOverMonthPercent = row.SalesMonthOverMonthPercent,
        SalesYearOverYearPercent = row.SalesYearOverYearPercent,
        QualityScore = row.QualityScore,
        QualityLabel = row.QualityLabel,
        ConfidenceScore = row.ConfidenceScore,
        RankMarket = row.RankMarket,
        RankIndustry = row.RankIndustry,
        DimensionScoresJson = row.DimensionScoresJson,
        PositiveDriversJson = row.PositiveDriversJson,
        NegativeDriversJson = row.NegativeDriversJson,
        DataCoverageJson = row.DataCoverageJson,
        SourceProviderName = row.SourceProviderName,
        CalculatedAtUtc = row.CalculatedAtUtc,
        IsEligible = row.IsEligible
    };

    private static string NormalizeSymbol(string symbol) =>
        symbol.Trim().Replace('ك', 'ک').Replace('ي', 'ی').Replace('‌', ' ');
}
