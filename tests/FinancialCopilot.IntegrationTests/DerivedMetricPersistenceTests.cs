using System.Security.Cryptography;
using System.Text;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Metrics;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.IntegrationTests;

public sealed class DerivedMetricPersistenceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-26T09:00:00Z");

    [Fact]
    public async Task IngestedMonthlySales_CanBeReadCalculatedAndPersistedWithVersionEvidence()
    {
        await using var providerDb = CreateProviderDbContext();
        await using var ingestionDb = CreateIngestionDbContext();
        var store = new ProviderRawPayloadStore(providerDb);
        var mock = new MockFinancialDataProvider(store, new FixedTimeProvider(Now));
        var monthlyProvider = new MonthlySequenceProvider(store);
        var processor = new FinancialDataSyncProcessor(
            ingestionDb,
            store,
            mock,
            mock,
            monthlyProvider,
            [new MonthlyReportPayloadNormalizer(ingestionDb, MonthlySequenceProvider.ProviderName)],
            new StoredDerivedMetricRecalculationPublisher(ingestionDb),
            new FixedTimeProvider(Now),
            NullLogger<FinancialDataSyncProcessor>.Instance);

        await processor.ProcessAsync(Request("company-live|2026-03", "march"), CancellationToken.None);
        await processor.ProcessAsync(Request("company-live|2026-04", "april"), CancellationToken.None);

        var reader = new NormalizedMetricInputReader([new MonthlySalesMetricInputSource(ingestionDb)]);
        var inputs = await reader.LoadAsync("company-live", new MetricCode("MONTHLY_SALES"), CancellationToken.None);
        var cache = new TrackingScannerCache();
        var service = new DerivedMetricCalculationService(
            new FinancialMetricRegistry(
                PhaseOneFinancialSemanticCatalog.Definitions,
                [new PercentageGrowthMetricCalculator(
                    new MetricCode("MONTHLY_SALES_GROWTH_MOM"),
                    new MetricCode("MONTHLY_SALES"))]),
            new MetricCalculationPolicyProvider(PhaseOneFinancialSemanticCatalog.Policies),
            new PersistedDerivedMetricResultStore(ingestionDb, cache, new FixedTimeProvider(Now)));

        var metrics = await new DerivedMetricRecalculationCommand(service).ExecuteAsync(
            [new CalculateDerivedMetricCommand(
                Guid.NewGuid(),
                new MetricCode("MONTHLY_SALES_GROWTH_MOM"),
                new CalculationPolicyVersion("mom-monthly-sales-v1"),
                Month(2026, 4),
                inputs)],
            CancellationToken.None);
        var metric = metrics.Single();
        var row = await ingestionDb.DerivedMetrics.SingleAsync();

        Assert.Equal(100m, metric.Value);
        Assert.Equal("MONTHLY_SALES_GROWTH_MOM", row.MetricCode);
        Assert.Equal("v1", row.MetricVersion);
        Assert.Equal("mom-monthly-sales-v1", row.CalculationPolicyVersion);
        Assert.Contains("MONTHLY_SALES", row.DependencyEvidenceJson);
        Assert.Contains(MonthlySequenceProvider.ProviderName, row.SourceEvidenceJson);
        Assert.Equal(2, await ingestionDb.MetricRecalculationRequests.CountAsync());
        Assert.Contains(cache.Invalidations, item => item.Reason == "DerivedMetric.MONTHLY_SALES_GROWTH_MOM");
    }

    [Fact]
    public async Task PersistedValuationMetric_RetainsQuoteObservationMetadata()
    {
        await using var ingestionDb = CreateIngestionDbContext();
        var registry = new FinancialMetricRegistry(
            PhaseOneFinancialSemanticCatalog.Definitions,
            [new ValuationRatioMetricCalculator(
                new MetricCode("PE_TTM"),
                new MetricCode("LATEST_PRICE"),
                new MetricCode("TTM_EPS"))]);
        var service = new DerivedMetricCalculationService(
            registry,
            new MetricCalculationPolicyProvider(PhaseOneFinancialSemanticCatalog.Policies),
            new PersistedDerivedMetricResultStore(ingestionDb));
        var quoteAt = DateTimeOffset.Parse("2026-05-26T08:00:00Z");
        var ttm = FiscalPeriod.Closed(
            FiscalPeriodType.TrailingTwelveMonths,
            new DateOnly(2025, 4, 1),
            new DateOnly(2026, 3, 31));

        await service.CalculateAsync(
            new CalculateDerivedMetricCommand(
                Guid.NewGuid(),
                new MetricCode("PE_TTM"),
                new CalculationPolicyVersion("ttm-valuation-v1"),
                ttm,
                [
                    Input("LATEST_PRICE", ttm, 25m, new FinancialSourceEvidence("QuoteProvider", quoteAt, Now)),
                    Input("TTM_EPS", ttm, 5m, new FinancialSourceEvidence("MetricsEngine", Now, Now))
                ]),
            CancellationToken.None);
        var row = await ingestionDb.DerivedMetrics.SingleAsync();

        Assert.Equal(5m, row.Value);
        Assert.Contains("QuoteProvider", row.SourceEvidenceJson);
        Assert.Contains("2026-05-26T08:00:00", row.SourceEvidenceJson);
    }

    private static DataSyncRequest Request(string externalReference, string key) =>
        new(Guid.NewGuid(), ProviderDataset.MonthlyProductionSales, externalReference, Now, key);

    private static FiscalPeriod Month(int year, int month) =>
        FiscalPeriod.Closed(
            FiscalPeriodType.Monthly,
            new DateOnly(year, month, 1),
            new DateOnly(year, month, 1).AddMonths(1).AddDays(-1));

    private static MetricInputObservation Input(
        string code,
        FiscalPeriod period,
        decimal value,
        FinancialSourceEvidence source) =>
        new(
            new MetricCode(code),
            new MetricVersion("v1"),
            new CalculationPolicyVersion("source-v1"),
            period,
            value,
            [source]);

    private static FinancialProviderDbContext CreateProviderDbContext() =>
        new(new DbContextOptionsBuilder<FinancialProviderDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static FinancialIngestionDbContext CreateIngestionDbContext() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class MonthlySequenceProvider(
        IProviderRawPayloadStore payloadStore) : IMonthlyProductionSalesProvider
    {
        public const string ProviderName = "MonthlySequenceProvider";

        public async Task<ProviderRawPayload> FetchMonthlyReportsAsync(
            string externalCompanyId,
            CancellationToken cancellationToken)
        {
            var isMarch = externalCompanyId.EndsWith("2026-03", StringComparison.Ordinal);
            var reportMonth = isMarch ? "2026-03" : "2026-04";
            var salesAmount = isMarch ? 100 : 200;
            var payloadText = $$"""{"reportId":"company-live-{{reportMonth}}","companyId":"company-live","periodStart":"{{reportMonth}}-01","periodEnd":"{{reportMonth}}-{{(isMarch ? "31" : "30")}}","productCode":"PRODUCT_A","productionQuantity":10,"salesQuantity":8,"salesAmount":{{salesAmount}}}""";
            var payload = new ProviderRawPayload(
                Guid.NewGuid(),
                ProviderName,
                ProviderDataset.MonthlyProductionSales,
                $"/reports/{reportMonth}",
                externalCompanyId,
                payloadText,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payloadText))),
                Now);
            await payloadStore.StoreAsync(payload, cancellationToken);
            return payload;
        }
    }
}
