using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Application.Scanner.CyclicalWavesV2;
using FinancialCopilot.Domain.Financial.CyclicalWavesV2;
using FinancialCopilot.Infrastructure.Financial.Scanner;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWavesV2;

/// <summary>
/// CyclicalWaves-mode symbol metric lookup (design §10.3). Values come only from the v2 screener and, for
/// EPS, from the latest stored v2 income statement. Unmapped or null values are reported missing, never
/// substituted from another period or provider.
/// </summary>
public sealed class CyclicalWavesV2SymbolMetricLookupService(
    CyclicalWavesV2ReadModel readModel,
    TimeProvider timeProvider) : ISymbolMetricLookupService
{
    private const int EpsLineKey = 85;

    public async Task<SymbolLookupTableResult> LookupAsync(SymbolLookupRequest request, CancellationToken cancellationToken)
    {
        var started = timeProvider.GetUtcNow();
        var columns = new List<ScannerTableColumn>
        {
            new("SYMBOL", "نماد", ScannerColumnType.Symbol),
            new("COMPANY_NAME", "شرکت", ScannerColumnType.CompanyName)
        };
        var warnings = new List<string>();
        var columnBindings = new List<(string Identifier, SymbolLookupRequestPair Pair, ScreenerMetricBinding? Binding)>();

        foreach (var pair in request.Pairs.DistinctBy(pair => (pair.MetricCode.Value, pair.PeriodSelector)))
        {
            var identifier = pair.PeriodSelector is null ? pair.MetricCode.Value : $"{pair.MetricCode.Value}__{pair.PeriodSelector}";
            var binding = IsEps(pair.MetricCode.Value) ? null : ScreenerMetricMapping.ForLookup(pair.MetricCode, pair.PeriodSelector);
            columns.Add(new ScannerTableColumn(identifier, DisplayName(pair, binding), ScannerColumnType.Metric, pair.MetricCode.Value));
            columnBindings.Add((identifier, pair, binding));
            if (binding is { IsAvailable: false })
            {
                warnings.Add($"شاخص «{pair.DisplayLabel ?? pair.MetricCode.Value}» در منبع دادهٔ فعال (CyclicalWaves) منتشر نمی‌شود.");
            }
        }

        var rows = new List<ScannerTableRow>();
        var unresolved = new List<string>();
        foreach (var symbol in request.Pairs.Select(pair => pair.SymbolName.Trim()).Distinct(StringComparer.Ordinal))
        {
            var ticker = await readModel.FindTickerAsync(symbol, cancellationToken);
            if (ticker is null)
            {
                unresolved.Add(symbol);
                continue;
            }

            var snapshot = (await readModel.LoadSnapshotsAsync([ticker.Id], cancellationToken)).FirstOrDefault();
            var cells = new Dictionary<string, ScannerTableCell>(StringComparer.OrdinalIgnoreCase)
            {
                ["SYMBOL"] = new(null, ticker.Ticker, CellFreshnessStatus.Persisted, null),
                ["COMPANY_NAME"] = new(null, ticker.Ticker, CellFreshnessStatus.Persisted, null)
            };

            foreach (var (identifier, pair, binding) in columnBindings)
            {
                var cell = binding is null
                    ? await BuildEpsCellAsync(ticker.Id, pair.MetricCode.Value, cancellationToken)
                    : BuildCell(binding, snapshot);
                cells[identifier] = cell;
                if (cell.FreshnessStatus == CellFreshnessStatus.Missing && binding?.IsAvailable != false)
                {
                    warnings.Add($"مقدار «{pair.DisplayLabel ?? pair.MetricCode.Value}» برای نماد {ticker.Ticker} در منبع CyclicalWaves موجود نیست.");
                }
            }

            rows.Add(new ScannerTableRow(ticker.Ticker, ticker.Ticker, cells, 1.0, [], CyclicalWavesV2ReadModel.SourceLabel,
                CyclicalWavesV2CanonicalEntityResolver.ExternalCompanyId(ticker.Id)));
        }

        var finished = timeProvider.GetUtcNow();
        return new SymbolLookupTableResult(
            Guid.NewGuid(),
            columns,
            rows,
            new ScannerExecutionFacts(
                finished,
                finished - started,
                TotalSymbolsEvaluated: rows.Count + unresolved.Count,
                MatchingSymbolCount: rows.Count(row => row.Cells.Values.Any(cell => cell.Value is not null)),
                FromCache: false,
                PageSize: Math.Max(1, rows.Count)),
            warnings.Distinct(StringComparer.Ordinal).ToList(),
            unresolved,
            request.Pairs.Select(pair => pair.MetricCode.Value).Distinct(StringComparer.Ordinal).ToList());
    }

    internal static ScannerTableCell BuildCell(ScreenerMetricBinding binding, CyclicalWavesV2TickerSnapshot? snapshot)
    {
        if (snapshot is null || !binding.IsAvailable)
        {
            return Missing;
        }

        var value = binding.Evaluate(snapshot.Values);
        var definition = ScreenerMetricCatalog.Get(binding.FieldKey!);
        var periodDate = CyclicalWavesV2Presentation.PeriodDate(definition, snapshot);
        if (definition.ZeroMeansNotComputable && value == 0m)
        {
            return new ScannerTableCell(null, CyclicalWavesV2Presentation.NotComputable, CellFreshnessStatus.Persisted,
                snapshot.LastSyncedAt, null, null, CyclicalWavesV2ReadModel.SourceLabel);
        }

        return value is null
            ? Missing
            : new ScannerTableCell(
                value,
                CyclicalWavesV2Presentation.Format(binding.ComparisonUnit, value.Value),
                CellFreshnessStatus.Persisted,
                snapshot.LastSyncedAt,
                periodDate,
                periodDate is null ? null : CyclicalWavesV2Presentation.PersianDate(periodDate.Value),
                CyclicalWavesV2ReadModel.SourceLabel);
    }

    private async Task<ScannerTableCell> BuildEpsCellAsync(Guid tickerId, string metricCode, CancellationToken cancellationToken)
    {
        var line = await readModel.GetLatestStatementLineAsync(tickerId, EpsLineKey, cancellationToken);
        var value = metricCode == "EPS_TTM" ? line?.TrailingYear : line?.Quarter;
        return line is null || value is null
            ? Missing
            : new ScannerTableCell(
                value,
                FinancialNumberFormatter.Ratio(value.Value),
                CellFreshnessStatus.Persisted,
                null,
                line.Value.PeriodEnd,
                CyclicalWavesV2Presentation.PersianDate(line.Value.PeriodEnd),
                CyclicalWavesV2ReadModel.SourceLabel);
    }

    private static string DisplayName(SymbolLookupRequestPair pair, ScreenerMetricBinding? binding)
    {
        if (binding is null)
        {
            return pair.MetricCode.Value == "EPS_TTM" ? "سود هر سهم دوازده‌ماهه (ریال)" : "سود هر سهم فصل (ریال)";
        }

        if (!binding.IsAvailable)
        {
            return pair.DisplayLabel ?? pair.MetricCode.Value;
        }

        var name = binding.Kind == ScreenerOperandKind.Growth
            ? pair.DisplayLabel ?? pair.MetricCode.Value
            : ScreenerMetricCatalog.Get(binding.FieldKey!).NameFa;
        var unit = CyclicalWavesV2Presentation.UnitSuffix(binding.ComparisonUnit);
        return unit.Length == 0 ? name : $"{name} ({unit})";
    }

    private static bool IsEps(string metricCode) => metricCode is "EPS" or "EPS_TTM";

    private static ScannerTableCell Missing { get; } = new(null, null, CellFreshnessStatus.Missing, null);
}
