using System.Globalization;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.CodalDb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CodalDb;

/// <summary>
/// Normalizes the CodalDB <c>FinancialRatios</c> payload into <c>DerivedMetricRow</c>s keyed by
/// <c>ExternalCompanyId</c> (NADPCO coID). Idempotent on
/// <c>(ExternalCompanyId, MetricCode, MetricVersion, CalculationPolicyVersion, PeriodEnd)</c>.
/// </summary>
public sealed class CodalDbRatioNormalizer(
    FinancialIngestionDbContext dbContext,
    IOptions<CodalDbProviderOptions> options) : IFinancialPayloadNormalizer
{
    private readonly bool _preferConsolidated = options.Value.PreferConsolidatedStatements;

    public string ProviderName => CodalDbSymbolNormalizer.CodalDbProviderName;

    public ProviderDataset Dataset => ProviderDataset.FinancialRatios;

    public async Task<NormalizationOutcome> NormalizeAsync(ProviderRawPayload payload, CancellationToken cancellationToken)
    {
        var rows = JsonSerializer.Deserialize<IReadOnlyList<CodalRatioRow>>(payload.Payload, JsonOptions)
            ?? throw new FinancialProviderException(
                FinancialProviderErrorCode.InvalidResponse,
                "CodalDb financial-ratios payload is null or invalid.");

        var externalCompanyId = payload.ExternalReference;

        // Verify the company exists in the catalog; skip payload if not yet synced.
        var companyExists = await dbContext.Companies.AsNoTracking()
            .AnyAsync(c => c.ExternalCompanyId == externalCompanyId, cancellationToken);

        if (!companyExists)
        {
            return new NormalizationOutcome(0);
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
                externalCompanyId,
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
        return new NormalizationOutcome(count, externalCompanyId);
    }

    private async Task UpsertDerivedMetricRowAsync(
        string externalCompanyId,
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
            row => row.ExternalCompanyId == externalCompanyId
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
                ExternalCompanyId = externalCompanyId,
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
