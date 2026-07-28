using System.Globalization;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

/// <summary>
/// All-index coverage normalizer (spec 050). Persists <b>every</b> vendor fundamental index for a
/// company/period into the non-scannable <see cref="NadpcoFundamentalIndexObservationRow"/> staging
/// table — it does NOT write <c>DerivedMetrics</c> and does NOT use the curated 041 allowlist to
/// filter (it only flags which indexes are governed candidates). The scanner never reads this table;
/// the curated <see cref="NadpcoApiFundamentalIndexNormalizer"/> remains the only path that promotes
/// reviewed indexes into governed metrics. Idempotent upsert on the canonical observation key.
/// </summary>
public sealed class NadpcoApiFundamentalIndexCoverageNormalizer(
    FinancialIngestionDbContext dbContext) : IFinancialPayloadNormalizer
{
    public string ProviderName => NadpcoApiCompanyNormalizer.NadpcoApiProviderName;

    public ProviderDataset Dataset => ProviderDataset.FundamentalIndexCoverage;

    public async Task<NormalizationOutcome> NormalizeAsync(ProviderRawPayload payload, CancellationToken cancellationToken)
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
                "NADPCO fundamental-index coverage payload is invalid.",
                exception);
        }

        var selected = SelectCanonicalIndexes(records);
        var count = 0;

        foreach (var item in selected)
        {
            // Period dates are required for the canonical key; skip rows the vendor returns without
            // a parseable period rather than failing the whole company batch.
            if (!TryMapPeriod(item.Record, out var periodStart, out var periodEnd))
            {
                continue;
            }

            await UpsertObservationAsync(item, periodStart, periodEnd, payload, cancellationToken);
            count++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new NormalizationOutcome(count);
    }

    private async Task UpsertObservationAsync(
        NadpcoSelectedCoverageIndex selected,
        DateOnly periodStart,
        DateOnly periodEnd,
        ProviderRawPayload payload,
        CancellationToken cancellationToken)
    {
        var externalCompanyId = selected.Record.ComID.ToString(CultureInfo.InvariantCulture);
        var existing = await dbContext.NadpcoFundamentalIndexObservations.SingleOrDefaultAsync(
            row => row.ProviderName == ProviderName &&
                row.ExternalCompanyId == externalCompanyId &&
                row.CompanyIndexId == selected.Index.CompanyIndexId &&
                row.PeriodType == selected.Record.PeriodType &&
                row.PeriodEnd == periodEnd,
            cancellationToken);

        if (existing is null)
        {
            existing = new NadpcoFundamentalIndexObservationRow
            {
                Id = Guid.NewGuid(),
                ProviderName = ProviderName,
                ExternalCompanyId = externalCompanyId,
                CompanyIndexId = selected.Index.CompanyIndexId,
                PeriodType = selected.Record.PeriodType,
                PeriodEnd = periodEnd
            };
            dbContext.NadpcoFundamentalIndexObservations.Add(existing);
        }

        existing.CompanyTitle = selected.Record.ComTitle;
        existing.ExternalStatementId = selected.Record.ComBSID;
        existing.CompanyIndexTitle = selected.Index.CompanyIndexTitle;
        existing.CompanyIndexGroupId = selected.Index.CompanyIndexGroupId;
        existing.CompanyIndexGroupTitle = selected.Index.CompanyIndexGroupTitle;
        existing.CompanyIndexValue = selected.Index.CompanyIndexValue;
        existing.CompanyIndexUnit = selected.Index.CompanyIndexUnit;
        existing.PeriodStart = periodStart;
        existing.JalaliFiscalYearEnd = selected.Record.JalaliFiscalYearEnd;
        existing.JalaliPeriodEnd = selected.Record.JalaliPeriodEnd;
        existing.JalaliAnnouncementDate = selected.Record.JalaliAnouncementDate;
        existing.IsAudited = selected.Record.IsAudited;
        existing.IsRepresented = selected.Record.IsRepresented;
        existing.IsComposing = selected.Record.IsComposing;
        // Flag (do not filter): which indexes the curated 041 allowlist would govern.
        existing.IsGovernedCandidate =
            NadpcoApiFundamentalIndexMap.IndexIdToMetric.ContainsKey(selected.Index.CompanyIndexId);
        existing.SourcePayloadChecksum = payload.Checksum;
        existing.LastSynchronizedAt = payload.ReceivedAt;
    }

    // Deterministic variant selection mirrors the curated normalizer: prefer audited, not-represented,
    // composing, later announcement, higher statement id — per (company, period type, period end, index).
    private static IReadOnlyList<NadpcoSelectedCoverageIndex> SelectCanonicalIndexes(
        IReadOnlyList<NadpcoApiFundamentalIndexRecord> records) =>
        records
            .SelectMany(record => record.Indexes.Select(index => new NadpcoSelectedCoverageIndex(record, index)))
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

    private static bool TryMapPeriod(
        NadpcoApiFundamentalIndexRecord record,
        out DateOnly periodStart,
        out DateOnly periodEnd)
    {
        periodStart = default;
        periodEnd = default;
        if (!TryParseJalaliDate(record.JalaliFiscalYearEnd, out var fiscalYearEnd) ||
            !TryParseJalaliDate(record.JalaliPeriodEnd, out periodEnd))
        {
            return false;
        }

        periodStart = fiscalYearEnd.AddYears(-1).AddDays(1);
        return true;
    }

    private static bool TryParseJalaliDate(string? value, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var month) ||
            !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var day))
        {
            return false;
        }

        try
        {
            date = DateOnly.FromDateTime(PersianCalendar.ToDateTime(year, month, day, 0, 0, 0, 0));
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private sealed record NadpcoSelectedCoverageIndex(
        NadpcoApiFundamentalIndexRecord Record,
        NadpcoApiFundamentalIndexItem Index);

    private static readonly PersianCalendar PersianCalendar = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
