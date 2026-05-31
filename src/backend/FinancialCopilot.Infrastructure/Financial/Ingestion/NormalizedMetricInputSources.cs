using FinancialCopilot.Application.FinancialData.Metrics;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

public sealed class NormalizedMetricInputReader(
    IEnumerable<INormalizedMetricInputSource> sources) : INormalizedMetricInputReader
{
    private readonly IReadOnlyDictionary<MetricCode, INormalizedMetricInputSource> _sources =
        sources.ToDictionary(source => source.MetricCode);

    public Task<IReadOnlyCollection<MetricInputObservation>> LoadAsync(
        string externalCompanyId,
        MetricCode metricCode,
        CancellationToken cancellationToken) =>
        _sources.TryGetValue(metricCode, out var source)
            ? source.LoadAsync(externalCompanyId, cancellationToken)
            : throw new KeyNotFoundException($"No normalized input source is registered for metric '{metricCode}'.");
}

/// <summary>
/// Generic input source for any metric code backed by <c>NormalizedFinancialStatementLineItems</c>.
/// Reads line items keyed by <paramref name="metricCode"/> from all providers, provider-agnostically.
/// Replaces/subsumes per-metric source classes such as the legacy <c>NetProfitMetricInputSource</c>.
/// </summary>
public sealed class LineItemMetricInputSource(
    FinancialIngestionDbContext dbContext,
    MetricCode metricCode) : INormalizedMetricInputSource
{
    public MetricCode MetricCode { get; } = metricCode;

    public async Task<IReadOnlyCollection<MetricInputObservation>> LoadAsync(
        string externalCompanyId,
        CancellationToken cancellationToken)
    {
        var observations = await (
                from statement in dbContext.FinancialStatements.AsNoTracking()
                join item in dbContext.FinancialStatementLineItems.AsNoTracking()
                    on statement.Id equals item.FinancialStatementId
                where statement.ExternalCompanyId == externalCompanyId && item.MetricCode == MetricCode.Value
                select new { statement, item.Value })
            .ToListAsync(cancellationToken);

        return observations.Select(observation => NormalizedMetricInputFactory.Create(
            MetricCode,
            FiscalPeriod.Closed(
                Enum.Parse<FiscalPeriodType>(observation.statement.PeriodType),
                observation.statement.PeriodStart,
                observation.statement.PeriodEnd),
            observation.Value,
            observation.statement.ProviderName,
            observation.statement.ExternalStatementId,
            observation.statement.LastSynchronizedAt)).ToArray();
    }
}

// Kept for backward compatibility in tests that construct it directly.
public sealed class NetProfitMetricInputSource(
    FinancialIngestionDbContext dbContext) : INormalizedMetricInputSource
{
    public MetricCode MetricCode { get; } = new("NET_PROFIT");

    public async Task<IReadOnlyCollection<MetricInputObservation>> LoadAsync(
        string externalCompanyId,
        CancellationToken cancellationToken) =>
        await new LineItemMetricInputSource(dbContext, MetricCode).LoadAsync(externalCompanyId, cancellationToken);
}

public sealed class MonthlySalesMetricInputSource(
    FinancialIngestionDbContext dbContext) : INormalizedMetricInputSource
{
    public MetricCode MetricCode { get; } = new("MONTHLY_SALES");

    public async Task<IReadOnlyCollection<MetricInputObservation>> LoadAsync(
        string externalCompanyId,
        CancellationToken cancellationToken)
    {
        var reports = await dbContext.MonthlyReports.AsNoTracking()
            .Where(report => report.ExternalCompanyId == externalCompanyId)
            .ToListAsync(cancellationToken);
        var results = new List<MetricInputObservation>(reports.Count);

        foreach (var report in reports)
        {
            var values = await dbContext.MonthlyReportLineItems.AsNoTracking()
                .Where(item => item.MonthlyReportId == report.Id)
                .Select(item => item.SalesAmount)
                .ToListAsync(cancellationToken);
            decimal? value = values.Count > 0 && values.All(item => item is not null)
                ? values.Sum(item => item!.Value)
                : null;
            results.Add(NormalizedMetricInputFactory.Create(
                MetricCode,
                FiscalPeriod.Closed(FiscalPeriodType.Monthly, report.PeriodStart, report.PeriodEnd),
                value,
                report.ProviderName,
                report.ExternalReportId,
                report.LastSynchronizedAt));
        }

        return results;
    }
}

internal static class NormalizedMetricInputFactory
{
    public static MetricInputObservation Create(
        MetricCode code,
        FiscalPeriod period,
        decimal? value,
        string providerName,
        string documentId,
        DateTimeOffset synchronizedAt)
    {
        var observedAt = new DateTimeOffset(
            period.EndDate!.Value.ToDateTime(TimeOnly.MinValue),
            TimeSpan.Zero);

        return new MetricInputObservation(
            code,
            new MetricVersion("v1"),
            new CalculationPolicyVersion("normalized-source-v1"),
            period,
            value,
            [new FinancialSourceEvidence(providerName, observedAt, synchronizedAt, documentId)]);
    }
}
