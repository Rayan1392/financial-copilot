using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.CodalAlerts;
using FinancialCopilot.Application.FinancialData.Insights;
using FinancialCopilot.Application.Notifications;
using FinancialCopilot.Domain.Financial.CodalAlerts;
using FinancialCopilot.Domain.Financial.Insights;
using FinancialCopilot.Domain.Financial.Services;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Insights;

internal abstract class InsightDetectorBase(
    FinancialIngestionDbContext dbContext,
    IInsightScoringService scoring,
    IInsightDeduplicationPolicy deduplication) : IInsightDetector
{
    protected FinancialIngestionDbContext DbContext { get; } = dbContext;

    protected IInsightScoringService Scoring { get; } = scoring;

    protected IInsightDeduplicationPolicy Deduplication { get; } = deduplication;

    public abstract string DetectorName { get; }

    public abstract Task<IReadOnlyCollection<InsightEvent>> DetectAsync(
        InsightDetectionContext context,
        CancellationToken cancellationToken = default);

    protected InsightEvent CreateInsight(
        string externalCompanyId,
        string symbol,
        string? industryCode,
        InsightType type,
        InsightScore score,
        string title,
        string summary,
        string reason,
        IReadOnlyCollection<InsightEvidenceItem> evidence,
        string sourceProviderName,
        InsightSourceEntityType sourceEntityType,
        string? sourceEntityId,
        string? sourcePeriod,
        DateTimeOffset detectedAtUtc,
        DateTimeOffset? expiresAtUtc,
        IReadOnlyCollection<InsightAction>? actions = null)
    {
        var key = Deduplication.BuildKey(
            type,
            externalCompanyId,
            sourceProviderName,
            sourceEntityType,
            sourceEntityId,
            sourcePeriod);

        return new InsightEvent(
            Guid.NewGuid(),
            externalCompanyId,
            symbol,
            industryCode,
            type,
            score.Severity,
            score.ImportanceScore,
            score.ConfidenceScore,
            title,
            summary,
            reason,
            evidence,
            sourceProviderName,
            sourceEntityType,
            sourceEntityId,
            sourcePeriod,
            detectedAtUtc,
            expiresAtUtc,
            key,
            actions);
    }

    protected static string DisplaySymbol(NormalizedCompanyRow? company, string externalCompanyId) =>
        FirstNonEmpty(company?.Ticker, company?.CompanySymbol, company?.TseSymbol, company?.CompanySymbolEnglish, externalCompanyId);

    protected static string? IndustryCode(NormalizedCompanyRow? company) =>
        company?.IndustryId?.ToString();

    protected static string Percent(decimal value) =>
        $"{Math.Round(value, 2).ToString(CultureInfo.InvariantCulture)}%";

    protected static string Amount(decimal value) =>
        Math.Round(value, 2).ToString(CultureInfo.InvariantCulture);

    protected static string Period(DateOnly periodEnd) =>
        periodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    protected static decimal ClampMagnitude(decimal value) =>
        Math.Clamp(Math.Abs(value), 0m, 100m);

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}

internal sealed class MonthlyReportPublishedDetector(
    FinancialIngestionDbContext dbContext,
    IInsightScoringService scoring,
    IInsightDeduplicationPolicy deduplication)
    : InsightDetectorBase(dbContext, scoring, deduplication)
{
    public override string DetectorName => nameof(MonthlyReportPublishedDetector);

    public override async Task<IReadOnlyCollection<InsightEvent>> DetectAsync(
        InsightDetectionContext context,
        CancellationToken cancellationToken = default)
    {
        var rows = await (
            from report in DbContext.MonthlyReports.AsNoTracking()
            where report.LastSynchronizedAt >= context.SinceUtc
                  && (report.OutputType == null || report.OutputType == 0)
            join company in DbContext.Companies.AsNoTracking()
                on report.ExternalCompanyId equals company.ExternalCompanyId into companyJoin
            from company in companyJoin.DefaultIfEmpty()
            select new { report, company })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => new { r.report.ProviderName, r.report.ExternalCompanyId, r.report.PeriodEnd })
            .Select(g => g.OrderByDescending(r => r.report.LastSynchronizedAt).First())
            .Select(row =>
            {
                var sourcePeriod = Period(row.report.PeriodEnd);
                var score = Scoring.Score(new InsightScoringInput(45m, 92m, 95m, 90m, 35m));
                var symbol = DisplaySymbol(row.company, row.report.ExternalCompanyId);
                return CreateInsight(
                    row.report.ExternalCompanyId,
                    symbol,
                    IndustryCode(row.company),
                    InsightType.MonthlyReportPublished,
                    score,
                    $"{symbol} monthly report was published",
                    $"A new monthly production/sales report is available for {symbol}.",
                    "A newly synchronized monthly report can change sales, production, and trend views.",
                    [
                        new InsightEvidenceItem("Report period", sourcePeriod, row.report.ProviderName, sourcePeriod, row.report.LastSynchronizedAt),
                        new InsightEvidenceItem("Report type", row.report.ReportType ?? "MonthlyActivity", row.report.ProviderName, sourcePeriod, row.report.LastSynchronizedAt)
                    ],
                    row.report.ProviderName,
                    InsightSourceEntityType.MonthlyReport,
                    row.report.ExternalReportId,
                    sourcePeriod,
                    context.DetectedAtUtc,
                    context.DetectedAtUtc.AddDays(30));
            })
            .ToList();
    }
}

internal sealed class MonthlySalesAnomalyDetector(
    FinancialIngestionDbContext dbContext,
    IInsightScoringService scoring,
    IInsightDeduplicationPolicy deduplication)
    : InsightDetectorBase(dbContext, scoring, deduplication)
{
    private const decimal AnomalyThresholdPercent = 30m;

    public override string DetectorName => nameof(MonthlySalesAnomalyDetector);

    public override async Task<IReadOnlyCollection<InsightEvent>> DetectAsync(
        InsightDetectionContext context,
        CancellationToken cancellationToken = default)
    {
        var rows = await DbContext.CompanyMonthlyActivityTrendSnapshots
            .AsNoTracking()
            .Where(row => row.CalculatedAtUtc >= context.SinceUtc
                          && row.MonthlySalesAmount > 0m
                          && row.Average12MonthSalesAmount.HasValue
                          && row.Average12MonthSalesAmount.Value > 0m)
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new
            {
                row,
                Average12MonthSalesAmount = row.Average12MonthSalesAmount!.Value,
                Change = PercentChange(row.MonthlySalesAmount, row.Average12MonthSalesAmount!.Value)
            })
            .Where(x => Math.Abs(x.Change) >= AnomalyThresholdPercent)
            .Select(x =>
            {
                var row = x.row;
                var direction = x.Change > 0 ? "above" : "below";
                var period = $"{row.ReportYear}/{row.ReportMonth:00}";
                var score = Scoring.Score(new InsightScoringInput(
                    ClampMagnitude(x.Change),
                    row.DataCompletenessScore,
                    row.IsAverage12MonthComplete ? 95m : 70m,
                    88m,
                    Math.Abs(x.Change) >= 50m ? 80m : 55m));
                return CreateInsight(
                    row.ExternalCompanyId,
                    row.CompanySymbol ?? row.ExternalCompanyId,
                    row.IndustryId?.ToString(CultureInfo.InvariantCulture),
                    InsightType.MonthlySalesAnomaly,
                    score,
                    $"{row.CompanySymbol ?? row.ExternalCompanyId} monthly sales were materially {direction} baseline",
                    $"Latest monthly sales were {Percent(x.Change)} {direction} the 12-month average.",
                    "The latest monthly sales amount crossed the configured anomaly threshold versus the trailing 12-month baseline.",
                    [
                        new InsightEvidenceItem("Latest monthly sales", Amount(row.MonthlySalesAmount), row.SourceProviderName, period, row.CalculatedAtUtc),
                        new InsightEvidenceItem("12-month average", Amount(x.Average12MonthSalesAmount), row.SourceProviderName, period, row.CalculatedAtUtc),
                        new InsightEvidenceItem("Sales versus average", Percent(x.Change), row.SourceProviderName, period, row.CalculatedAtUtc)
                    ],
                    row.SourceProviderName,
                    InsightSourceEntityType.MonthlyActivityTrendSnapshot,
                    row.SourceReportId ?? row.Id.ToString(),
                    period,
                    context.DetectedAtUtc,
                    context.DetectedAtUtc.AddDays(30));
            })
            .ToList();
    }

    private static decimal PercentChange(decimal current, decimal baseline) =>
        (current - baseline) / baseline * 100m;
}

internal sealed class MonthlyQualityRankingChangeDetector(
    FinancialIngestionDbContext dbContext,
    IInsightScoringService scoring,
    IInsightDeduplicationPolicy deduplication)
    : InsightDetectorBase(dbContext, scoring, deduplication)
{
    private const decimal ScoreChangeThreshold = 15m;

    public override string DetectorName => nameof(MonthlyQualityRankingChangeDetector);

    public override async Task<IReadOnlyCollection<InsightEvent>> DetectAsync(
        InsightDetectionContext context,
        CancellationToken cancellationToken = default)
    {
        var currentRows = await DbContext.MonthlySalesQualityRankingSnapshots
            .AsNoTracking()
            .Where(row => row.CalculatedAtUtc >= context.SinceUtc && row.IsEligible)
            .ToListAsync(cancellationToken);

        if (currentRows.Count == 0) return [];

        var externalIds = currentRows.Select(row => row.ExternalCompanyId).Distinct().ToArray();
        var allRows = await DbContext.MonthlySalesQualityRankingSnapshots
            .AsNoTracking()
            .Where(row => externalIds.Contains(row.ExternalCompanyId))
            .ToListAsync(cancellationToken);

        var events = new List<InsightEvent>();
        foreach (var current in currentRows)
        {
            var previous = allRows
                .Where(row => row.ExternalCompanyId == current.ExternalCompanyId
                              && IsBefore(row.ReportYear, row.ReportMonth, current.ReportYear, current.ReportMonth))
                .OrderByDescending(row => row.ReportYear)
                .ThenByDescending(row => row.ReportMonth)
                .FirstOrDefault();

            if (previous is null) continue;

            var delta = current.QualityScore - previous.QualityScore;
            if (Math.Abs(delta) < ScoreChangeThreshold) continue;

            var direction = delta > 0 ? "improved" : "deteriorated";
            var period = $"{current.ReportYear}/{current.ReportMonth:00}";
            var score = Scoring.Score(new InsightScoringInput(
                Math.Clamp(Math.Abs(delta) * 2m, 0m, 100m),
                current.ConfidenceScore,
                90m,
                85m,
                Math.Abs(delta) >= 25m ? 80m : 55m));

            events.Add(CreateInsight(
                current.ExternalCompanyId,
                current.CompanySymbol,
                current.IndustryId?.ToString(),
                InsightType.MonthlyQualityRankingChange,
                score,
                $"{current.CompanySymbol} monthly report quality {direction}",
                $"Monthly sales quality score {direction} by {Amount(Math.Abs(delta))} points versus the prior period.",
                "Feature 080 ranking moved materially compared with the previous available period.",
                [
                    new InsightEvidenceItem("Current quality score", Amount(current.QualityScore), current.SourceProviderName, period, current.CalculatedAtUtc),
                    new InsightEvidenceItem("Previous quality score", Amount(previous.QualityScore), previous.SourceProviderName, $"{previous.ReportYear}/{previous.ReportMonth:00}", previous.CalculatedAtUtc),
                    new InsightEvidenceItem("Quality label", current.QualityLabel, current.SourceProviderName, period, current.CalculatedAtUtc)
                ],
                current.SourceProviderName,
                InsightSourceEntityType.MonthlySalesQualityRankingSnapshot,
                current.Id.ToString(),
                period,
                context.DetectedAtUtc,
                context.DetectedAtUtc.AddDays(30)));
        }

        return events;
    }

    private static bool IsBefore(int year, byte month, int otherYear, byte otherMonth) =>
        year < otherYear || (year == otherYear && month < otherMonth);
}

internal sealed class PriceMovementDetector(
    FinancialIngestionDbContext dbContext,
    IInsightScoringService scoring,
    IInsightDeduplicationPolicy deduplication)
    : InsightDetectorBase(dbContext, scoring, deduplication)
{
    private const decimal PriceMoveThresholdPercent = 5m;

    public override string DetectorName => nameof(PriceMovementDetector);

    public override async Task<IReadOnlyCollection<InsightEvent>> DetectAsync(
        InsightDetectionContext context,
        CancellationToken cancellationToken = default)
    {
        var rows = await (
            from quote in DbContext.LatestMarketQuotes.AsNoTracking()
            where quote.AsOf >= context.SinceUtc
                  && Math.Abs(quote.PriceChangePercentage) >= PriceMoveThresholdPercent
            join instrument in DbContext.TradingInstruments.AsNoTracking()
                on quote.TradingInstrumentId equals instrument.Id
            join company in DbContext.Companies.AsNoTracking()
                on instrument.NormalizedCompanyId equals company.Id
            select new { quote, instrument, company })
            .ToListAsync(cancellationToken);

        return rows.Select(row =>
        {
            var direction = row.quote.PriceChangePercentage > 0 ? "up" : "down";
            var period = row.quote.TradingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var symbol = DisplaySymbol(row.company, row.company.ExternalCompanyId);
            var score = Scoring.Score(new InsightScoringInput(
                ClampMagnitude(row.quote.PriceChangePercentage * 10m),
                90m,
                85m,
                95m,
                Math.Abs(row.quote.PriceChangePercentage) >= 8m ? 80m : 55m));

            return CreateInsight(
                row.company.ExternalCompanyId,
                symbol,
                IndustryCode(row.company),
                InsightType.PriceMovement,
                score,
                $"{symbol} had a large daily price move",
                $"Latest market quote moved {Percent(row.quote.PriceChangePercentage)} {direction}.",
                "The latest quote crossed the configured daily price movement threshold.",
                [
                    new InsightEvidenceItem("Latest price", Amount(row.quote.LatestPrice), row.quote.ProviderName, period, row.quote.AsOf),
                    new InsightEvidenceItem("Daily change", Percent(row.quote.PriceChangePercentage), row.quote.ProviderName, period, row.quote.AsOf)
                ],
                row.quote.ProviderName,
                InsightSourceEntityType.MarketQuote,
                row.quote.Id.ToString(),
                period,
                context.DetectedAtUtc,
                context.DetectedAtUtc.AddDays(2));
        }).ToList();
    }
}

internal sealed class ComprehensiveAnalysisPublishedDetector(
    FinancialIngestionDbContext dbContext,
    IInsightScoringService scoring,
    IInsightDeduplicationPolicy deduplication)
    : InsightDetectorBase(dbContext, scoring, deduplication)
{
    public override string DetectorName => nameof(ComprehensiveAnalysisPublishedDetector);

    public override async Task<IReadOnlyCollection<InsightEvent>> DetectAsync(
        InsightDetectionContext context,
        CancellationToken cancellationToken = default)
    {
        var rows = await (
            from analysis in DbContext.ComprehensiveAnalyses.AsNoTracking()
            where analysis.SyncedAt >= context.SinceUtc || analysis.CreatedAt >= context.SinceUtc
            join tag in DbContext.ComprehensiveAnalysisTags.AsNoTracking().Where(t => t.TagTypeId == 1)
                on analysis.Id equals tag.AnalysisId
            select new { analysis, tag })
            .Take(500)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0) return [];

        var companies = await DbContext.Companies.AsNoTracking().ToListAsync(cancellationToken);
        var events = new List<InsightEvent>();

        foreach (var row in rows)
        {
            var company = ResolveCompany(companies, row.tag.TagName);
            if (company is null) continue;

            var symbol = DisplaySymbol(company, company.ExternalCompanyId);
            var period = row.analysis.CreatedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var score = Scoring.Score(new InsightScoringInput(55m, 85m, 90m, 85m, 60m));
            events.Add(CreateInsight(
                company.ExternalCompanyId,
                symbol,
                IndustryCode(company),
                InsightType.ComprehensiveAnalysisPublished,
                score,
                $"New comprehensive analysis was published for {symbol}",
                row.analysis.Title,
                "A new stored analysis post is available for review through the comprehensive-analysis source.",
                [
                    new InsightEvidenceItem("Analysis title", row.analysis.Title, "CyclicalWaves", period, row.analysis.SyncedAt),
                    new InsightEvidenceItem("Author", row.analysis.AuthorName, "CyclicalWaves", period, row.analysis.SyncedAt)
                ],
                "CyclicalWaves",
                InsightSourceEntityType.ComprehensiveAnalysis,
                row.analysis.Id.ToString(CultureInfo.InvariantCulture),
                period,
                context.DetectedAtUtc,
                context.DetectedAtUtc.AddDays(30)));
        }

        return events;
    }

    private static NormalizedCompanyRow? ResolveCompany(IEnumerable<NormalizedCompanyRow> companies, string symbol)
    {
        var normalized = PersianSymbolNormalizer.Normalize(symbol);
        return companies.FirstOrDefault(company =>
            Matches(company.Ticker, normalized) ||
            Matches(company.CompanySymbol, normalized) ||
            Matches(company.TseSymbol, normalized) ||
            Matches(company.CompanySymbolPinglish, normalized) ||
            Matches(company.CompanySymbolEnglish, normalized));
    }

    private static bool Matches(string? candidate, string normalized) =>
        !string.IsNullOrWhiteSpace(candidate)
        && string.Equals(PersianSymbolNormalizer.Normalize(candidate), normalized, StringComparison.OrdinalIgnoreCase);
}

internal sealed class FinancialStatementPublishedDetector(
    FinancialIngestionDbContext dbContext,
    IInsightScoringService scoring,
    IInsightDeduplicationPolicy deduplication)
    : InsightDetectorBase(dbContext, scoring, deduplication)
{
    public override string DetectorName => nameof(FinancialStatementPublishedDetector);

    public override async Task<IReadOnlyCollection<InsightEvent>> DetectAsync(
        InsightDetectionContext context,
        CancellationToken cancellationToken = default)
    {
        var rows = await (
            from statement in DbContext.FinancialStatements.AsNoTracking()
            where statement.LastSynchronizedAt >= context.SinceUtc
            join company in DbContext.Companies.AsNoTracking()
                on statement.ExternalCompanyId equals company.ExternalCompanyId into companyJoin
            from company in companyJoin.DefaultIfEmpty()
            select new { statement, company })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => new
            {
                r.statement.ProviderName,
                r.statement.ExternalCompanyId,
                r.statement.ExternalStatementId,
                r.statement.StatementType
            })
            .Select(g => g.OrderByDescending(r => r.statement.LastSynchronizedAt).First())
            .Select(row =>
            {
                var period = Period(row.statement.PeriodEnd);
                var symbol = DisplaySymbol(row.company, row.statement.ExternalCompanyId);
                var score = Scoring.Score(new InsightScoringInput(50m, 90m, 90m, 85m, 50m));
                return CreateInsight(
                    row.statement.ExternalCompanyId,
                    symbol,
                    IndustryCode(row.company),
                    InsightType.FinancialStatementPublished,
                    score,
                    $"{symbol} financial statement was published",
                    $"{row.statement.StatementType} for period ending {period} is available.",
                    "A newly synchronized financial statement can change deterministic statement analysis and metric calculations.",
                    [
                        new InsightEvidenceItem("Statement type", row.statement.StatementType, row.statement.ProviderName, period, row.statement.LastSynchronizedAt),
                        new InsightEvidenceItem("Period type", row.statement.PeriodType, row.statement.ProviderName, period, row.statement.LastSynchronizedAt)
                    ],
                    row.statement.ProviderName,
                    InsightSourceEntityType.FinancialStatement,
                    row.statement.ExternalStatementId,
                    period,
                    context.DetectedAtUtc,
                    context.DetectedAtUtc.AddDays(30));
            })
            .ToList();
    }
}

internal sealed class SubscribedCodalAnnouncementDetector(
    FinancialIngestionDbContext dbContext,
    IInsightScoringService scoring,
    IInsightDeduplicationPolicy deduplication,
    ICodalAlertSubscriptionRepository subscriptions,
    INotificationIntentPublisher notificationPublisher)
    : InsightDetectorBase(dbContext, scoring, deduplication)
{
    public override string DetectorName => nameof(SubscribedCodalAnnouncementDetector);

    public override async Task<IReadOnlyCollection<InsightEvent>> DetectAsync(
        InsightDetectionContext context,
        CancellationToken cancellationToken = default)
    {
        var statementRows = await (
            from statement in DbContext.FinancialStatements.AsNoTracking()
            where statement.LastSynchronizedAt >= context.SinceUtc
            join company in DbContext.Companies.AsNoTracking()
                on statement.ExternalCompanyId equals company.ExternalCompanyId into companyJoin
            from company in companyJoin.DefaultIfEmpty()
            select new { statement, company })
            .ToArrayAsync(cancellationToken);

        var monthlyRows = await (
            from report in DbContext.MonthlyReports.AsNoTracking()
            where report.LastSynchronizedAt >= context.SinceUtc
                  && (report.OutputType == null || report.OutputType == 0)
            join company in DbContext.Companies.AsNoTracking()
                on report.ExternalCompanyId equals company.ExternalCompanyId into companyJoin
            from company in companyJoin.DefaultIfEmpty()
            select new { report, company })
            .ToArrayAsync(cancellationToken);

        var companyIds = statementRows.Select(row => row.statement.ExternalCompanyId)
            .Concat(monthlyRows.Select(row => row.report.ExternalCompanyId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var activeSubscriptions = await subscriptions.GetActiveForCompaniesAsync(companyIds, cancellationToken);
        if (activeSubscriptions.Count == 0) return [];

        var subscriptionLookup = activeSubscriptions
            .GroupBy(item => item.ExternalCompanyId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var events = new List<InsightEvent>();

        foreach (var row in statementRows)
        {
            var score = ScoreStatement(row.statement);
            var matching = MatchingSubscriptions(
                subscriptionLookup,
                row.statement.ExternalCompanyId,
                CodalAnnouncementType.FinancialStatement,
                score.Severity);
            if (matching.Count == 0)
            {
                continue;
            }

            var period = Period(row.statement.PeriodEnd);
            var symbol = DisplaySymbol(row.company, row.statement.ExternalCompanyId);
            var insight = CreateInsight(
                row.statement.ExternalCompanyId,
                symbol,
                IndustryCode(row.company),
                InsightType.CodalAnnouncementMatched,
                score,
                $"Codal announcement matched for {symbol}",
                $"{row.statement.StatementType} announcement for period ending {period} matched an active Codal alert subscription.",
                "A subscribed Codal financial-statement announcement was synchronized from the authoritative ingestion boundary.",
                [
                    new InsightEvidenceItem("Announcement type", CodalAnnouncementType.FinancialStatement.ToString(), row.statement.ProviderName, period, row.statement.LastSynchronizedAt),
                    new InsightEvidenceItem("Statement type", row.statement.StatementType, row.statement.ProviderName, period, row.statement.LastSynchronizedAt),
                    new InsightEvidenceItem("Period type", row.statement.PeriodType, row.statement.ProviderName, period, row.statement.LastSynchronizedAt),
                    new InsightEvidenceItem("Source checksum", row.statement.SourcePayloadChecksum, row.statement.ProviderName, period, row.statement.LastSynchronizedAt)
                ],
                row.statement.ProviderName,
                InsightSourceEntityType.FinancialStatement,
                row.statement.ExternalStatementId,
                period,
                context.DetectedAtUtc,
                context.DetectedAtUtc.AddDays(30));
            events.Add(insight);
            await PublishIntentsAsync(
                matching,
                insight,
                CodalAnnouncementType.FinancialStatement,
                row.statement.ProviderName,
                row.statement.ExternalStatementId,
                period,
                context,
                cancellationToken);
        }

        foreach (var row in monthlyRows)
        {
            var score = Scoring.Score(new InsightScoringInput(55m, 92m, 90m, 95m, 45m));
            var matching = MatchingSubscriptions(
                subscriptionLookup,
                row.report.ExternalCompanyId,
                CodalAnnouncementType.MonthlyActivity,
                score.Severity);
            if (matching.Count == 0)
            {
                continue;
            }

            var period = Period(row.report.PeriodEnd);
            var symbol = DisplaySymbol(row.company, row.report.ExternalCompanyId);
            var insight = CreateInsight(
                row.report.ExternalCompanyId,
                symbol,
                IndustryCode(row.company),
                InsightType.CodalAnnouncementMatched,
                score,
                $"Codal monthly announcement matched for {symbol}",
                $"Monthly activity announcement for period ending {period} matched an active Codal alert subscription.",
                "A subscribed Codal monthly activity announcement was synchronized from the authoritative ingestion boundary.",
                [
                    new InsightEvidenceItem("Announcement type", CodalAnnouncementType.MonthlyActivity.ToString(), row.report.ProviderName, period, row.report.LastSynchronizedAt),
                    new InsightEvidenceItem("Report type", row.report.ReportType ?? "MonthlyActivity", row.report.ProviderName, period, row.report.LastSynchronizedAt),
                    new InsightEvidenceItem("Source checksum", row.report.SourcePayloadChecksum, row.report.ProviderName, period, row.report.LastSynchronizedAt)
                ],
                row.report.ProviderName,
                InsightSourceEntityType.MonthlyReport,
                row.report.ExternalReportId,
                period,
                context.DetectedAtUtc,
                context.DetectedAtUtc.AddDays(30));
            events.Add(insight);
            await PublishIntentsAsync(
                matching,
                insight,
                CodalAnnouncementType.MonthlyActivity,
                row.report.ProviderName,
                row.report.ExternalReportId,
                period,
                context,
                cancellationToken);
        }

        return events
            .GroupBy(item => item.DeduplicationKey, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(item => item.ImportanceScore).First())
            .ToArray();
    }

    private InsightScore ScoreStatement(NormalizedFinancialStatementRow statement)
    {
        var magnitude = statement.PeriodType.Equals("TwelveMonths", StringComparison.OrdinalIgnoreCase) ? 75m : 60m;
        var rarity = statement.IsAudited ? 70m : 50m;
        return Scoring.Score(new InsightScoringInput(magnitude, 92m, 90m, 95m, rarity));
    }

    private async Task PublishIntentsAsync(
        IReadOnlyCollection<CodalAlertSubscription> subscriptions,
        InsightEvent insight,
        CodalAnnouncementType type,
        string providerName,
        string sourceEntityId,
        string sourcePeriod,
        InsightDetectionContext context,
        CancellationToken cancellationToken)
    {
        foreach (var subscription in subscriptions)
        {
            var dedup = $"codal-alert:v1:{subscription.Id}:{type}:{providerName}:{sourceEntityId}:{sourcePeriod}:raw";
            var payload = JsonSerializer.Serialize(new
            {
                insightEventId = insight.Id,
                insightDeduplicationKey = insight.DeduplicationKey,
                subscriptionId = subscription.Id,
                externalCompanyId = insight.ExternalCompanyId,
                symbol = insight.Symbol,
                announcementType = type.ToString(),
                title = insight.Title,
                summary = insight.Summary,
                evidence = insight.Evidence,
                sourceProvider = providerName,
                sourceEntityId,
                sourcePeriod,
                sourceFreshnessUtc = insight.DetectedAtUtc,
                correlationId = insight.Id.ToString()
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

            var notification = await notificationPublisher.EnqueueAsync(
                new NotificationIntentRequest(
                    new NotificationActor(subscription.Actor.TenantId, subscription.Actor.ActorId, subscription.Actor.ActorType),
                    NotificationChannel.Telegram,
                    "CodalAnnouncementMatched",
                    $"{insight.ExternalCompanyId}:{sourceEntityId}:{sourcePeriod}",
                    dedup,
                    insight.Severity,
                    payload,
                    context.DetectedAtUtc,
                    insight.ExpiresAtUtc,
                    insight.Id.ToString(),
                    SourceEventId: insight.Id,
                    EvidenceReference: insight.DeduplicationKey,
                    Category: "Codal",
                    CooldownKey: $"Codal:{insight.ExternalCompanyId}:{type}"),
                cancellationToken);

            if (subscription.AiSummaryEnabled)
            {
                await EnsurePendingSummaryAsync(subscription, insight, notification.Id, cancellationToken);
            }
        }
    }

    private async Task EnsurePendingSummaryAsync(
        CodalAlertSubscription subscription,
        InsightEvent insight,
        Guid notificationIntentId,
        CancellationToken cancellationToken)
    {
        var exists = await DbContext.CodalAlertSummaries.AnyAsync(row =>
            row.TenantId == subscription.Actor.TenantId &&
            row.ActorId == subscription.Actor.ActorId &&
            row.ActorType == subscription.Actor.ActorType &&
            row.InsightEventId == insight.Id,
            cancellationToken);
        if (exists) return;

        DbContext.CodalAlertSummaries.Add(new CodalAlertSummaryRow
        {
            Id = Guid.NewGuid(),
            TenantId = subscription.Actor.TenantId,
            ActorId = subscription.Actor.ActorId,
            ActorType = subscription.Actor.ActorType,
            InsightEventId = insight.Id,
            NotificationIntentId = notificationIntentId,
            Status = "Pending",
            EvidenceHash = HashEvidence(insight),
            PromptPolicyVersion = "codal-alert-summary-v1",
            CreatedAtUtc = insight.DetectedAtUtc,
            UpdatedAtUtc = insight.DetectedAtUtc
        });
        await DbContext.SaveChangesAsync(cancellationToken);
    }

    private static string HashEvidence(InsightEvent insight)
    {
        var input = JsonSerializer.Serialize(new { insight.SourceProviderName, insight.SourceEntityId, insight.SourcePeriod, insight.Evidence });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }

    private static IReadOnlyCollection<CodalAlertSubscription> MatchingSubscriptions(
        IReadOnlyDictionary<string, CodalAlertSubscription[]> subscriptions,
        string externalCompanyId,
        CodalAnnouncementType type,
        InsightSeverity severity) =>
        subscriptions.TryGetValue(externalCompanyId, out var rows)
            ? rows.Where(subscription => subscription.RawAlertEnabled && subscription.Matches(type, severity)).ToArray()
            : [];
}

internal sealed class DataFreshnessDetector(
    FinancialIngestionDbContext dbContext,
    IInsightScoringService scoring,
    IInsightDeduplicationPolicy deduplication)
    : InsightDetectorBase(dbContext, scoring, deduplication)
{
    public override string DetectorName => nameof(DataFreshnessDetector);

    public override async Task<IReadOnlyCollection<InsightEvent>> DetectAsync(
        InsightDetectionContext context,
        CancellationToken cancellationToken = default)
    {
        var events = new List<InsightEvent>();
        var staleBefore = context.DetectedAtUtc.AddDays(-2);

        var nadpcoStates = await DbContext.NadpcoApiSyncStates.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var state in nadpcoStates.Where(s => s.LastSuccessfulSyncAt is null || s.LastSuccessfulSyncAt < staleBefore))
        {
            events.Add(CreateFreshnessInsight(
                state.Dataset,
                "NoavaranCurrentApi",
                state.LastSuccessfulSyncAt,
                context));
        }

        var marketStates = await DbContext.StockMarketSyncStates.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var state in marketStates.Where(s => s.LastRunCompletedAt is null || s.LastRunCompletedAt < staleBefore))
        {
            events.Add(CreateFreshnessInsight(
                state.Dataset,
                state.PhysicalSource ?? "Tsetmc",
                state.LastRunCompletedAt,
                context));
        }

        return events;
    }

    private InsightEvent CreateFreshnessInsight(
        string dataset,
        string sourceProvider,
        DateTimeOffset? lastSuccessfulAt,
        InsightDetectionContext context)
    {
        var staleHours = lastSuccessfulAt is null
            ? 100m
            : Math.Clamp((decimal)(context.DetectedAtUtc - lastSuccessfulAt.Value).TotalHours, 0m, 100m);
        var score = Scoring.Score(new InsightScoringInput(staleHours, 80m, 80m, 95m, 50m));
        return CreateInsight(
            "MARKET",
            "MARKET",
            null,
            InsightType.DataFreshnessWarning,
            score,
            $"{dataset} data may be stale",
            lastSuccessfulAt is null
                ? $"No successful sync timestamp is recorded for {dataset}."
                : $"Latest successful sync for {dataset} was at {lastSuccessfulAt:O}.",
            "The source freshness monitor crossed the configured staleness threshold.",
            [
                new InsightEvidenceItem("Dataset", dataset, sourceProvider, null, lastSuccessfulAt),
                new InsightEvidenceItem("Last successful sync", lastSuccessfulAt?.ToString("O", CultureInfo.InvariantCulture) ?? "missing", sourceProvider, null, lastSuccessfulAt)
            ],
            sourceProvider,
            InsightSourceEntityType.SyncState,
            dataset,
            dataset,
            context.DetectedAtUtc,
            context.DetectedAtUtc.AddDays(1),
            [InsightAction.AskAiAboutThis]);
    }
}
