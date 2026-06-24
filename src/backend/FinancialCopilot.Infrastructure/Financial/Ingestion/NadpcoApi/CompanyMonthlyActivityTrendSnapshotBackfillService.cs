using System.Globalization;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

/// <summary>
/// Spec 076 Task 7 — rebuilds CompanyMonthlyActivityTrendSnapshots from already-persisted
/// Noavaran monthly activity data. Reuses <see cref="ICompanyMonthlyActivityTrendSnapshotCalculator"/>
/// so the backfill path is identical to the live ingestion path.
///
/// Date range and forceRebuild are read from <see cref="TrendSnapshotBackfillOptions"/>.
/// Eligible company IDs are enumerated via <see cref="NoavaranCompanyScope"/> (the
/// NoavaranEligibleCompanies view), so the caller passes no payload.
/// </summary>
public sealed class CompanyMonthlyActivityTrendSnapshotBackfillService(
    FinancialIngestionDbContext dbContext,
    ICompanyMonthlyActivityTrendSnapshotCalculator calculator,
    IOptions<TrendSnapshotBackfillOptions> backfillOptions,
    TimeProvider timeProvider,
    ILogger<CompanyMonthlyActivityTrendSnapshotBackfillService> logger)
    : ICompanyMonthlyActivityTrendSnapshotBackfillService
{
    private const string ProviderName = NadpcoApiCompanyNormalizer.NadpcoApiProviderName;
    private static readonly PersianCalendar PersianCalendar = new();
    private static readonly CompanyEvidence EmptyEvidence = new(null, null, null);

    public async Task<CompanyMonthlyActivityTrendSnapshotBackfillResult> RunAsync(
        CompanyMonthlyActivityTrendSnapshotBackfillRequest request,
        CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetUtcNow();
        var opts = backfillOptions.Value;

        // Discover all OutputType=0 ProductSales reports within the configured date range.
        var (fromDate, _) = JalaliDateResolver.ResolveMonth(opts.FromYear, (byte)opts.FromMonth);
        var (_, toDate) = JalaliDateResolver.ResolveMonth(opts.ToYear, (byte)opts.ToMonth);

        // Enumerate all eligible company IDs from the NoavaranEligibleCompanies view.
        var eligibleCompanyIds = await NoavaranCompanyScope
            .EligibleCompanies(dbContext, ProviderName)
            .Select(c => c.ExternalCompanyId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var reportsQuery = dbContext.MonthlyReports
            .AsNoTracking()
            .Where(r => r.ProviderName == ProviderName
                     && r.ReportType == "ProductSales"
                     && (r.OutputType == null || r.OutputType == 0)
                     && r.PeriodStart >= fromDate
                     && r.PeriodStart <= toDate
                     && eligibleCompanyIds.Contains(r.ExternalCompanyId));

        var reports = await reportsQuery
            .Select(r => new CandidateReport(r.Id, r.ExternalCompanyId, r.PeriodStart, r.WarningsJson))
            .ToListAsync(cancellationToken);

        if (reports.Count == 0)
        {
            logger.LogInformation(
                "Trend snapshot backfill found no candidates. Range={FromYear}/{FromMonth}–{ToYear}/{ToMonth}, " +
                "EligibleCompanies={EligibleCount}, RequestedBy={RequestedBy}.",
                opts.FromYear, opts.FromMonth, opts.ToYear, opts.ToMonth,
                eligibleCompanyIds.Count, request.RequestedBy);

            return Build("NoCandidates", request, 0, 0, 0, 0, 0, startedAt, opts);
        }

        // Load company metadata for symbol/name enrichment.
        var companyLookup = await dbContext.Companies
            .AsNoTracking()
            .Where(c => c.ProviderName == ProviderName)
            .Select(c => new { c.ExternalCompanyId, c.Name, c.CompanySymbol })
            .ToDictionaryAsync(
                c => c.ExternalCompanyId,
                c => new CompanyEvidence(c.CompanySymbol, c.Name, null),
                StringComparer.Ordinal,
                cancellationToken);

        // Group into (company, period) candidates and resolve metadata.
        var candidates = reports
            .GroupBy(r => new { r.ExternalCompanyId, r.PeriodStart })
            .Select(g =>
            {
                var firstWithMeta = g.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.WarningsJson)) ?? g.First();
                var evidence = ParseEvidence(firstWithMeta.WarningsJson);
                        companyLookup.TryGetValue(g.Key.ExternalCompanyId, out var companyRow);
                var resolved = new CompanyEvidence(
                    companyRow?.Symbol ?? evidence.Symbol,
                    companyRow?.Name ?? evidence.Name,
                    evidence.FiscalEndDate);
                return new Candidate(g.Key.ExternalCompanyId, g.Key.PeriodStart, resolved);
            })
            .OrderBy(c => c.ExternalCompanyId, StringComparer.Ordinal)
            .ThenBy(c => c.PeriodStart)
            .ToArray();

        var companiesConsidered = candidates
            .Select(c => c.ExternalCompanyId)
            .Distinct(StringComparer.Ordinal)
            .Count();

        // When not forcing rebuild, find already-existing snapshots and skip them.
        HashSet<(string CompanyId, DateOnly PeriodStart)> existingSnapshots = [];
        if (!opts.ForceRebuild)
        {
            var existingRows = await dbContext.CompanyMonthlyActivityTrendSnapshots
                .AsNoTracking()
                .Where(s => s.SourceProviderName == ProviderName
                         && eligibleCompanyIds.Contains(s.ExternalCompanyId))
                .Select(s => new { s.ExternalCompanyId, s.ReportYear, s.ReportMonth })
                .ToListAsync(cancellationToken);

            foreach (var row in existingRows)
            {
                var (existing, _) = JalaliDateResolver.ResolveMonth(row.ReportYear, (byte)row.ReportMonth);
                existingSnapshots.Add((row.ExternalCompanyId, existing));
            }
        }

        var processed = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var candidate in candidates)
        {
            if (!opts.ForceRebuild && existingSnapshots.Contains((candidate.ExternalCompanyId, candidate.PeriodStart)))
            {
                skipped++;
                continue;
            }

            var month = ToShamsiMonth(candidate.PeriodStart);

            try
            {
                await calculator.RecalculateAsync(
                    candidate.ExternalCompanyId,
                    month.Year,
                    (byte)month.Month,
                    candidate.Evidence.Symbol,
                    candidate.Evidence.Name,
                    candidate.Evidence.FiscalEndDate,
                    cancellationToken);

                processed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                logger.LogWarning(ex,
                    "Trend snapshot backfill failed for company {CompanyId} period {Year}/{Month}.",
                    candidate.ExternalCompanyId, month.Year, month.Month);
            }
        }

        logger.LogInformation(
            "Trend snapshot backfill completed. Range={FromYear}/{FromMonth}–{ToYear}/{ToMonth}, " +
            "EligibleCompanies={EligibleCount}, Companies={Companies}, Discovered={Discovered}, " +
            "Processed={Processed}, Skipped={Skipped}, Failed={Failed}, RequestedBy={RequestedBy}.",
            opts.FromYear, opts.FromMonth, opts.ToYear, opts.ToMonth,
            eligibleCompanyIds.Count, companiesConsidered, candidates.Length,
            processed, skipped, failed, request.RequestedBy);

        return Build("Completed", request, companiesConsidered, candidates.Length, processed, skipped, failed, startedAt, opts);
    }

    private CompanyMonthlyActivityTrendSnapshotBackfillResult Build(
        string outcome,
        CompanyMonthlyActivityTrendSnapshotBackfillRequest request,
        int companiesConsidered,
        int discovered,
        int processed,
        int skipped,
        int failed,
        DateTimeOffset startedAt,
        TrendSnapshotBackfillOptions opts)
    {
        var duration = (timeProvider.GetUtcNow() - startedAt).ToString("g", CultureInfo.InvariantCulture);
        return new CompanyMonthlyActivityTrendSnapshotBackfillResult(
            outcome,
            request.RequestedBy,
            companiesConsidered,
            discovered,
            processed,
            skipped,
            failed,
            duration);
    }

    private static ShamsiMonth ToShamsiMonth(DateOnly date)
    {
        var dt = date.ToDateTime(TimeOnly.MinValue);
        return new ShamsiMonth(PersianCalendar.GetYear(dt), PersianCalendar.GetMonth(dt));
    }

    private static CompanyEvidence ParseEvidence(string? warningsJson)
    {
        if (string.IsNullOrWhiteSpace(warningsJson)) return EmptyEvidence;

        try
        {
            using var doc = JsonDocument.Parse(warningsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                return EmptyEvidence;

            var first = doc.RootElement[0];
            return new CompanyEvidence(
                TryGetString(first, "BourseSymbol"),
                TryGetString(first, "CompanyTitle"),
                TryGetString(first, "JalaliFiscalYearEnd"));
        }
        catch (JsonException)
        {
            return EmptyEvidence;
        }
    }

    private static string? TryGetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private sealed record CandidateReport(
        Guid Id,
        string ExternalCompanyId,
        DateOnly PeriodStart,
        string WarningsJson);

    private sealed record Candidate(
        string ExternalCompanyId,
        DateOnly PeriodStart,
        CompanyEvidence Evidence);

    private sealed record CompanyEvidence(
        string? Symbol,
        string? Name,
        string? FiscalEndDate);
}
