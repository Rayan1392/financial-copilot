using System.Globalization;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.CodalDb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CodalDb;

/// <summary>
/// Normalizes the CodalDB <c>FinancialRatios</c> payload (a JSON array of <see cref="CodalRatioRow"/>
/// for one company) into <c>DerivedMetricRow</c>s with
/// <c>CalculationPolicyVersion = "codal-ratio-source-v1"</c>.
/// <para>
/// Each mapped ratio value is persisted as a <em>vendor-precomputed</em> observation so the scanner
/// can query it through the existing <c>DerivedMetrics</c> read path with no engine changes. These
/// rows never overwrite engine-calculated metrics because they use a distinct
/// <c>CalculationPolicyVersion</c>.
/// </para>
/// <para>
/// Canonical variant selection per <c>(PeriodEnd.Date, PeriodType, ItemId)</c> group follows the
/// same priority as <see cref="CodalDbStatementSelectionPolicy"/>: audited → latest representment
/// → consolidated/parent by configuration → lowest row Id tie-break.
/// </para>
/// <para>
/// Idempotent on the <c>DerivedMetricRow</c> unique key
/// <c>(SymbolId, MetricCode, MetricVersion, CalculationPolicyVersion, PeriodEnd)</c>.
/// If the company's symbol has not yet been synced (spec 022), the row is skipped with a warning.
/// </para>
/// </summary>
public sealed class CodalDbRatioNormalizer(
    FinancialIngestionDbContext dbContext,
    IOptions<CodalDbProviderOptions> options) : IFinancialPayloadNormalizer
{
    private readonly bool _preferConsolidated = options.Value.PreferConsolidatedStatements;

    public string ProviderName => CodalDbSymbolNormalizer.CodalDbProviderName;

    public ProviderDataset Dataset => ProviderDataset.FinancialRatios;

    public async Task<int> NormalizeAsync(ProviderRawPayload payload, CancellationToken cancellationToken)
    {
        var rows = JsonSerializer.Deserialize<IReadOnlyList<CodalRatioRow>>(payload.Payload, JsonOptions)
            ?? throw new FinancialProviderException(
                FinancialProviderErrorCode.InvalidResponse,
                "CodalDb financial-ratios payload is null or invalid.");

        var externalCompanyId = payload.ExternalReference;

        // Resolve the symbol once; skip entire payload if symbol not yet synced.
        var symbol = await dbContext.Symbols.SingleOrDefaultAsync(
            s => s.ProviderName == ProviderName && s.ExternalSymbolId == externalCompanyId,
            cancellationToken);

        if (symbol is null)
        {
            return 0;
        }

        var selected = SelectCanonicalVariants(rows, _preferConsolidated);
        var count = 0;

        foreach (var row in selected)
        {
            if (!CodalDbRatioItemMap.RatioIdToMetric.TryGetValue(row.ItemId, out var mapping))
            {
                continue; // unmapped ratio — not in Phase 1 catalog
            }

            var (metricCode, unitKey) = mapping;

            var period = CodalDbFiscalPeriodMapper.Map(
                row.FiscalYearEnd, row.PeriodEnd, (byte)Math.Min(row.PeriodType, 255),
                row.JalaliPeriodEnd, row.JalaliFiscalYearEnd);

            await UpsertDerivedMetricRowAsync(
                symbol.Id,
                metricCode,
                unitKey,
                (decimal)row.ItemValue,
                period,
                row,
                payload,
                cancellationToken);

            count++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return count;
    }

    private async Task UpsertDerivedMetricRowAsync(
        Guid symbolId,
        string metricCode,
        string unitKey,
        decimal value,
        CodalDbMappedPeriod period,
        CodalRatioRow sourceRow,
        ProviderRawPayload payload,
        CancellationToken cancellationToken)
    {
        var periodEnd = period.PeriodEnd;

        var existing = await dbContext.DerivedMetrics.SingleOrDefaultAsync(
            row => row.SymbolId == symbolId
                && row.MetricCode == metricCode
                && row.MetricVersion == MetricVersion
                && row.CalculationPolicyVersion == CodalDbRatioItemMap.CalculationPolicyVersion
                && row.PeriodEnd == periodEnd,
            cancellationToken);

        if (existing is null)
        {
            existing = new DerivedMetricRow
            {
                Id = Guid.NewGuid(),
                SymbolId = symbolId,
                MetricCode = metricCode,
                MetricVersion = MetricVersion,
                CalculationPolicyVersion = CodalDbRatioItemMap.CalculationPolicyVersion,
                PeriodEnd = periodEnd
            };
            dbContext.DerivedMetrics.Add(existing);
        }

        existing.PeriodType = period.FiscalPeriodType.ToString();
        existing.PeriodStart = period.PeriodStart;
        existing.Value = value;
        existing.Unit = unitKey;
        existing.ObservedAt = payload.ReceivedAt;
        existing.LastSynchronizedAt = payload.ReceivedAt;
        existing.WarningsJson = "[]";
        existing.SourceEvidenceJson = BuildSourceEvidence(sourceRow);
        existing.DependencyEvidenceJson = "[]";
    }

    /// <summary>
    /// Selects one canonical variant per <c>(PeriodEnd.Date, PeriodType, ItemId)</c> group using
    /// the same priority as <see cref="CodalDbStatementSelectionPolicy"/>:
    /// audited → representment → consolidated/parent → lowest Id.
    /// </summary>
    private static IReadOnlyList<CodalRatioRow> SelectCanonicalVariants(
        IReadOnlyList<CodalRatioRow> rows,
        bool preferConsolidated) =>
        rows
            .GroupBy(row => (row.PeriodEnd.Date, row.PeriodType, row.ItemId))
            .Select(group => group
                .OrderByDescending(row => row.IsAudited == true ? 1 : 0)
                .ThenByDescending(row => row.IsRepresented == true ? 1 : 0)
                .ThenByDescending(row =>
                    preferConsolidated
                        ? (row.IsComposing == true ? 1 : 0)
                        : (row.IsComposing == true ? 0 : 1))
                .ThenBy(row => row.Id)
                .First())
            .ToList();

    private static string BuildSourceEvidence(CodalRatioRow row) =>
        JsonSerializer.Serialize(new[]
        {
            new
            {
                Source = CodalDbSymbolNormalizer.CodalDbProviderName,
                RatioItemId = row.ItemId,
                VendorPrecomputed = true,
                IsAudited = row.IsAudited,
                IsRepresented = row.IsRepresented,
                IsComposing = row.IsComposing,
                PeriodEndJalali = row.JalaliPeriodEnd,
                FiscalYearEndJalali = row.JalaliFiscalYearEnd
            }
        }, JsonOptions);

    private const string MetricVersion = "v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
