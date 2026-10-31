using System.Globalization;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Domain.Financial.CyclicalWavesV2;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWavesV2;

/// <summary>
/// CyclicalWaves-mode quarterly income statement table built only from <c>cyclicalwaves</c> statements.
/// Rows follow the provider's own line keys and Persian names in printed order, so bank and insurer lines
/// appear by their own keys. Money is shown in million IRR (converted here), EPS in IRR, nulls as unavailable.
/// </summary>
public sealed class CyclicalWavesV2QuarterlyIncomeStatementUseCase(
    FinancialIngestionDbContext db,
    CyclicalWavesV2ReadModel readModel,
    TimeProvider timeProvider) : IQuarterlyFinancialReportsQueryUseCase
{
    public const string ProviderName = "CyclicalWavesV2";
    private static readonly IReadOnlySet<int> PerShareLineKeys = new HashSet<int> { 85, 6220 };
    private const int RegisteredCapitalLineKey = 86;

    public async Task<QuarterlyFinancialReportTableResult> ExecuteAsync(
        QuarterlyFinancialReportTableQuery query,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var ticker = await readModel.FindTickerAsync(query.CompanySymbol, cancellationToken);
        if (ticker is null)
        {
            return Empty(query, now, "Unavailable", "نماد در فهرست نمادهای CyclicalWaves پیدا نشد.", QuarterlyFinancialReportOutcome.CompanyNotFound,
                CompanyResolutionStatus.NotFound);
        }

        var count = Math.Clamp(query.QuarterCount, 1, 12);
        var statementsQuery = db.CyclicalWavesV2IncomeStatements.AsNoTracking().Where(row => row.TickerId == ticker.Id);
        if (query.EndingPeriod is { } ending)
        {
            statementsQuery = statementsQuery.Where(row => row.PeriodEndingDate <= ending);
        }

        var statements = await statementsQuery
            .OrderByDescending(row => row.PeriodEndingDate)
            .ThenByDescending(row => row.FiscalMonth)
            .Take(count)
            .ToListAsync(cancellationToken);
        if (statements.Count == 0)
        {
            return Empty(query with { CompanySymbol = ticker.Ticker }, now, "Unavailable",
                "صورت سود و زیانی برای این نماد در منبع CyclicalWaves ذخیره نشده است.",
                QuarterlyFinancialReportOutcome.CompanyFoundNoPersistedReports, CompanyResolutionStatus.Resolved);
        }

        var ids = statements.Select(row => row.Id).ToArray();
        var values = await db.CyclicalWavesV2IncomeStatementValues.AsNoTracking()
            .Where(row => ids.Contains(row.StatementId))
            .ToListAsync(cancellationToken);
        var lineKeys = values.Select(row => row.LineDefinitionId).Distinct().ToArray();
        var definitions = await db.CyclicalWavesV2IncomeStatementLineDefinitions.AsNoTracking()
            .Where(row => lineKeys.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, cancellationToken);

        var periods = statements.Select(ToPeriod).ToList();
        var valuesByStatement = values.ToLookup(row => row.StatementId);
        // Printed order of the newest statement first, then any line only older statements carry.
        var orderedKeys = statements
            .SelectMany((statement, index) => valuesByStatement[statement.Id]
                .OrderBy(row => row.SortOrder)
                .Select(row => (row.LineDefinitionId, Rank: index * 10_000 + row.SortOrder)))
            .GroupBy(item => item.LineDefinitionId)
            .OrderBy(group => group.Min(item => item.Rank))
            .Select(group => group.Key)
            .ToList();

        var rows = orderedKeys.Select((key, order) =>
        {
            var isPerShare = PerShareLineKeys.Contains(key);
            var unit = isPerShare ? "ریال" : CyclicalWavesV2Presentation.MillionRials;
            var cells = new Dictionary<string, QuarterlyFinancialCell>(StringComparer.Ordinal);
            for (var index = 0; index < statements.Count; index++)
            {
                var raw = valuesByStatement[statements[index].Id].FirstOrDefault(row => row.LineDefinitionId == key)?.Quarter;
                var shown = raw is null ? (decimal?)null : isPerShare ? raw : raw / 1_000_000m;
                cells[periods[index].Label] = new QuarterlyFinancialCell(
                    shown,
                    shown is null ? QuarterlyCellState.MissingObservation : QuarterlyCellState.Available,
                    shown is null ? "مقدار فصل در منبع CyclicalWaves گزارش نشده است." : null,
                    unit,
                    false,
                    [],
                    [],
                    "CyclicalWavesV2Quarter",
                    raw is null ? [] : [raw],
                    "IRR",
                    isPerShare ? 0 : 6);
            }

            return new QuarterlyFinancialRow(
                $"cw2_{key}",
                order,
                definitions.TryGetValue(key, out var definition) && definition.NameFa.Length > 0 ? definition.NameFa : key.ToString(CultureInfo.InvariantCulture),
                key,
                null,
                key == RegisteredCapitalLineKey ? QuarterlyRowPolicy.ReportedPointInTime
                    : isPerShare ? QuarterlyRowPolicy.CumulativeEps : QuarterlyRowPolicy.CumulativeFlow,
                unit,
                cells);
        }).ToList();

        var comparisons = BuildComparisons(rows, periods);
        var missing = rows.Any(row => row.Cells.Values.Any(cell => cell.State != QuarterlyCellState.Available));
        return new QuarterlyFinancialReportTableResult(
            ticker.Ticker,
            ticker.Ticker,
            CyclicalWavesV2CanonicalEntityResolver.ExternalCompanyId(ticker.Id),
            ProviderName,
            "IncomeStatement",
            null,
            count,
            periods,
            rows,
            comparisons,
            statements.Max(row => row.UpdatedAt),
            missing ? "Partial" : "Available",
            missing ? "برخی مقادیر فصلی در منبع CyclicalWaves گزارش نشده‌اند." : null,
            now,
            statements.Select((statement, index) => new QuarterlySelectedReportReference(
                statement.Id,
                statement.Id.ToString("N"),
                ProviderName,
                "IncomeStatement",
                periods[index].Start,
                statement.PeriodEndingDate,
                statement.FiscalMonth,
                statement.Audited ?? false,
                false,
                false,
                string.Empty,
                statement.UpdatedAt,
                null,
                "IRR",
                "IRR",
                PeriodType: statement.FiscalMonth switch
                {
                    3 => "ThreeMonths",
                    6 => "SixMonths",
                    9 => "NineMonths",
                    12 => "TwelveMonths",
                    _ => null
                })).ToList(),
            CompanyResolutionStatus.Resolved,
            missing ? QuarterlyFinancialReportOutcome.PartialTable : QuarterlyFinancialReportOutcome.SuccessfulTable,
            "CyclicalWavesV2IncomeStatement");
    }

    /// <summary>
    /// The fiscal year is the Jalali year in which the statement's fiscal year ends (period end plus the
    /// remaining fiscal months), so a quarter of a Mehr-ending year is labeled with that year.
    /// </summary>
    internal static QuarterlyFiscalPeriod ToPeriod(CyclicalWavesV2IncomeStatementRow statement)
    {
        var calendar = new PersianCalendar();
        var end = statement.PeriodEndingDate.ToDateTime(TimeOnly.MinValue);
        var fiscalYearEnd = calendar.AddMonths(end, Math.Max(0, 12 - statement.FiscalMonth));
        var quarter = Math.Clamp((statement.FiscalMonth + 2) / 3, 1, 4);
        var start = DateOnly.FromDateTime(calendar.AddMonths(end, -3).AddDays(1));
        return new QuarterlyFiscalPeriod(calendar.GetYear(fiscalYearEnd), quarter, start, statement.PeriodEndingDate);
    }

    private static List<QuarterlyQoQComparison> BuildComparisons(
        IReadOnlyList<QuarterlyFinancialRow> rows,
        IReadOnlyList<QuarterlyFiscalPeriod> periods)
    {
        if (periods.Count < 2)
        {
            return [];
        }

        var target = periods[0];
        var basePeriod = periods[1];
        return rows.Select(row =>
        {
            var current = row.Cells[target.Label].Value;
            var previous = row.Cells[basePeriod.Label].Value;
            var state = current is null ? QuarterlyComparisonState.MissingCurrent
                : previous is null ? QuarterlyComparisonState.MissingPrevious
                : previous <= 0m ? QuarterlyComparisonState.NonPositiveBase
                : QuarterlyComparisonState.Available;
            return new QuarterlyQoQComparison(
                row.RowId,
                target,
                basePeriod,
                current - previous,
                state == QuarterlyComparisonState.Available ? ScreenerGrowth.Percent(current, previous) : null,
                null,
                state);
        }).ToList();
    }

    private static QuarterlyFinancialReportTableResult Empty(
        QuarterlyFinancialReportTableQuery query,
        DateTimeOffset now,
        string status,
        string reason,
        QuarterlyFinancialReportOutcome outcome,
        CompanyResolutionStatus resolution) =>
        new(query.CompanySymbol, null, query.ExternalCompanyId, ProviderName, "IncomeStatement", null, query.QuarterCount,
            [], [], [], null, status, reason, now, [], resolution, outcome, "CyclicalWavesV2IncomeStatement");
}

/// <summary>
/// CyclicalWaves-mode statement tables: only the quarterly income statement exists in v2. Every other
/// statement (balance sheet, cash flow, non-quarterly tables) gets an explicit unavailable answer instead of
/// reaching the NoavaranAmin repositories.
/// </summary>
public sealed class CyclicalWavesV2FinancialStatementTableQueryUseCase(
    IQuarterlyFinancialReportsQueryUseCase quarterlyReportsUseCase,
    TimeProvider timeProvider) : IFinancialStatementTableQueryUseCase
{
    public async Task<FinancialStatementTableResult?> ExecuteAsync(
        FinancialStatementTableQuery query,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query.CompanyQuery) || query.StatementType is null)
            return null;

        if (query.StatementType != FinancialStatementType.IncomeStatement)
        {
            return new FinancialStatementTableResult(null, [], [], [ChatCapabilityAvailability.UnavailableForProviderMessageFa],
                ChatCapabilityAvailability.UnavailableForProviderMessageFa, timeProvider.GetUtcNow());
        }

        var quarterly = await quarterlyReportsUseCase.ExecuteAsync(
            new QuarterlyFinancialReportTableQuery(string.Empty, query.CompanyQuery, query.QuarterCount, query.EndingPeriod),
            ct);
        return new FinancialStatementTableResult(null, [], [], [], FinancialStatementTableQueryUseCase.RenderQuarterly(quarterly),
            timeProvider.GetUtcNow(), quarterly);
    }
}
