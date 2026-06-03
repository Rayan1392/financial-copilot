using System.Globalization;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Domain.Financial.Periods;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

public sealed class NadpcoApiFundamentalIndexNormalizer(
    FinancialIngestionDbContext dbContext) : IFinancialPayloadNormalizer
{
    public string ProviderName => NadpcoApiCompanyNormalizer.NadpcoApiProviderName;

    public ProviderDataset Dataset => ProviderDataset.FundamentalIndexes;

    public async Task<int> NormalizeAsync(ProviderRawPayload payload, CancellationToken cancellationToken)
    {
        IReadOnlyList<NadpcoApiFundamentalIndexRecord> records;
        try
        {
            records = JsonSerializer.Deserialize<IReadOnlyList<NadpcoApiFundamentalIndexRecord>>(payload.Payload, JsonOptions) ??
                throw new JsonException("Payload was null.");
        }
        catch (JsonException exception)
        {
            throw new FinancialProviderException(
                FinancialProviderErrorCode.InvalidResponse,
                "NADPCO fundamental-index payload is invalid.",
                exception);
        }

        var selected = SelectCanonicalIndexes(records);
        var count = 0;

        foreach (var item in selected)
        {
            if (!NadpcoApiFundamentalIndexMap.IndexIdToMetric.TryGetValue(
                    item.Index.CompanyIndexId,
                    out var mapping))
            {
                continue;
            }

            if (!mapping.ScaleVerified || item.Index.CompanyIndexValue is null)
            {
                continue;
            }

            var externalCompanyId = item.Record.ComID.ToString(CultureInfo.InvariantCulture);
            var symbol = await dbContext.Symbols.SingleOrDefaultAsync(
                symbol => symbol.ProviderName == ProviderName &&
                    symbol.ExternalSymbolId == externalCompanyId,
                cancellationToken);

            if (symbol is null)
            {
                continue;
            }

            var period = MapPeriod(item.Record);
            await UpsertDerivedMetricRowAsync(
                symbol.Id,
                mapping,
                item,
                period,
                payload,
                cancellationToken);
            count++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return count;
    }

    private async Task UpsertDerivedMetricRowAsync(
        Guid symbolId,
        NadpcoApiFundamentalIndexMapping mapping,
        NadpcoApiSelectedFundamentalIndex selected,
        NadpcoApiFundamentalIndexPeriod period,
        ProviderRawPayload payload,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.DerivedMetrics.SingleOrDefaultAsync(
            row => row.SymbolId == symbolId &&
                row.MetricCode == mapping.MetricCode &&
                row.MetricVersion == MetricVersion &&
                row.CalculationPolicyVersion == NadpcoApiFundamentalIndexMap.CalculationPolicyVersion &&
                row.PeriodEnd == period.PeriodEnd,
            cancellationToken);

        if (existing is null)
        {
            existing = new DerivedMetricRow
            {
                Id = Guid.NewGuid(),
                SymbolId = symbolId,
                MetricCode = mapping.MetricCode,
                MetricVersion = MetricVersion,
                CalculationPolicyVersion = NadpcoApiFundamentalIndexMap.CalculationPolicyVersion,
                PeriodEnd = period.PeriodEnd
            };
            dbContext.DerivedMetrics.Add(existing);
        }

        existing.PeriodType = period.FiscalPeriodType.ToString();
        existing.PeriodStart = period.PeriodStart;
        existing.Value = selected.Index.CompanyIndexValue;
        existing.Unit = mapping.UnitKey;
        existing.ObservedAt = payload.ReceivedAt;
        existing.LastSynchronizedAt = payload.ReceivedAt;
        existing.WarningsJson = "[]";
        existing.SourceEvidenceJson = BuildSourceEvidence(selected, mapping, period);
        existing.DependencyEvidenceJson = "[]";
    }

    private static IReadOnlyList<NadpcoApiSelectedFundamentalIndex> SelectCanonicalIndexes(
        IReadOnlyList<NadpcoApiFundamentalIndexRecord> records) =>
        records
            .SelectMany(record => record.Indexes.Select(index => new NadpcoApiSelectedFundamentalIndex(record, index)))
            .GroupBy(item => (
                item.Record.ComID,
                item.Record.PeriodType,
                PeriodEnd: item.Record.JalaliPeriodEnd ?? string.Empty,
                item.Index.CompanyIndexId))
            .Select(group => group
                .OrderByDescending(item => item.Record.IsAudited)
                .ThenBy(item => item.Record.IsRepresented)
                .ThenByDescending(item => item.Record.IsComposing)
                .ThenByDescending(item => item.Record.JalaliAnouncementDate ?? string.Empty, StringComparer.Ordinal)
                .ThenByDescending(item => item.Record.ComBSID)
                .First())
            .ToArray();

    private static NadpcoApiFundamentalIndexPeriod MapPeriod(NadpcoApiFundamentalIndexRecord record)
    {
        var fiscalYearEnd = ParseJalaliDate(record.JalaliFiscalYearEnd, "jalaliFiscalYearEnd");
        var periodEnd = ParseJalaliDate(record.JalaliPeriodEnd, "jalaliPeriodEnd");
        var periodStart = fiscalYearEnd.AddYears(-1).AddDays(1);
        var fiscalPeriodType = record.PeriodType switch
        {
            3 => FiscalPeriodType.ThreeMonths,
            6 => FiscalPeriodType.SixMonths,
            9 => FiscalPeriodType.NineMonths,
            12 => FiscalPeriodType.TwelveMonths,
            _ => FiscalPeriodType.TwelveMonths
        };

        return new NadpcoApiFundamentalIndexPeriod(fiscalPeriodType, periodStart, periodEnd);
    }

    private static DateOnly ParseJalaliDate(string? value, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FinancialProviderException(
                FinancialProviderErrorCode.InvalidResponse,
                $"NADPCO fundamental-index '{propertyName}' is required.");
        }

        var parts = value.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var month) ||
            !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var day))
        {
            throw new FinancialProviderException(
                FinancialProviderErrorCode.InvalidResponse,
                $"NADPCO fundamental-index '{propertyName}' value '{value}' is not a valid Jalali date.");
        }

        try
        {
            var date = PersianCalendar.ToDateTime(year, month, day, 0, 0, 0, 0);
            return DateOnly.FromDateTime(date);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new FinancialProviderException(
                FinancialProviderErrorCode.InvalidResponse,
                $"NADPCO fundamental-index '{propertyName}' value '{value}' is not a valid Jalali date.",
                exception);
        }
    }

    private static string BuildSourceEvidence(
        NadpcoApiSelectedFundamentalIndex selected,
        NadpcoApiFundamentalIndexMapping mapping,
        NadpcoApiFundamentalIndexPeriod period) =>
        JsonSerializer.Serialize(new[]
        {
            new
            {
                Source = NadpcoApiCompanyNormalizer.NadpcoApiProviderName,
                FundamentalIndexId = selected.Index.CompanyIndexId,
                selected.Index.CompanyIndexTitle,
                selected.Index.CompanyIndexGroupId,
                selected.Index.CompanyIndexGroupTitle,
                selected.Index.CompanyIndexUnit,
                VendorPrecomputed = true,
                SourcePolicyVersion = NadpcoApiFundamentalIndexMap.CalculationPolicyVersion,
                MappingReviewNote = mapping.ReviewNote,
                selected.Record.ComBSID,
                selected.Record.ComID,
                selected.Record.ComTitle,
                selected.Record.PeriodType,
                selected.Record.JalaliFiscalYearEnd,
                selected.Record.JalaliPeriodEnd,
                selected.Record.JalaliAnouncementDate,
                selected.Record.IsAudited,
                selected.Record.IsRepresented,
                selected.Record.IsComposing,
                GregorianPeriodStart = period.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                GregorianPeriodEnd = period.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            }
        }, JsonOptions);

    private sealed record NadpcoApiSelectedFundamentalIndex(
        NadpcoApiFundamentalIndexRecord Record,
        NadpcoApiFundamentalIndexItem Index);

    private sealed record NadpcoApiFundamentalIndexPeriod(
        FiscalPeriodType FiscalPeriodType,
        DateOnly PeriodStart,
        DateOnly PeriodEnd);

    private const string MetricVersion = "v1";
    private static readonly PersianCalendar PersianCalendar = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
