using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Providers.CodalDb;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class CodalDbDataProviderClientTests
{
    private static CodalDbDataProviderClient CreateClient(FakeQueryExecutor executor) =>
        new(executor, Options.Create(new CodalDbProviderOptions()), TimeProvider.System);

    [Fact]
    public async Task FetchSymbolsAsync_ReturnsSymbolsPayloadWithCodalDbMetadata()
    {
        var client = CreateClient(new FakeQueryExecutor());

        var payload = await client.FetchSymbolsAsync(default);

        Assert.Equal("CodalDb", payload.ProviderName);
        Assert.Equal(ProviderDataset.Symbols, payload.Dataset);
        Assert.Equal("codaldb://companies", payload.Endpoint);
        Assert.Equal("all", payload.ExternalReference);
        Assert.False(string.IsNullOrWhiteSpace(payload.Checksum));
    }

    [Fact]
    public async Task FetchFinancialStatementsAsync_SetsDatasetEndpointAndExternalReference()
    {
        var client = CreateClient(new FakeQueryExecutor());

        var payload = await client.FetchFinancialStatementsAsync("1001", default);

        Assert.Equal(ProviderDataset.FinancialStatements, payload.Dataset);
        Assert.Equal("codaldb://statements/1001", payload.Endpoint);
        Assert.Equal("1001", payload.ExternalReference);
        Assert.Equal("CodalDb", payload.ProviderName);
    }

    [Fact]
    public async Task FetchMonthlyReportsAsync_SetsDatasetEndpointAndExternalReference()
    {
        var client = CreateClient(new FakeQueryExecutor());

        var payload = await client.FetchMonthlyReportsAsync("1001", default);

        Assert.Equal(ProviderDataset.MonthlyProductionSales, payload.Dataset);
        Assert.Equal("codaldb://monthly-activity/1001", payload.Endpoint);
        Assert.Equal("1001", payload.ExternalReference);
    }

    [Fact]
    public async Task FetchFinancialStatementsAsync_InvalidCompanyId_ThrowsFinancialProviderException()
    {
        var client = CreateClient(new FakeQueryExecutor());

        var exception = await Assert.ThrowsAsync<FinancialProviderException>(
            () => client.FetchFinancialStatementsAsync("not-a-number", default));
        Assert.Equal(FinancialProviderErrorCode.InvalidResponse, exception.Code);
    }

    [Fact]
    public async Task CheckAsync_HealthyWhenCompaniesPresent()
    {
        var client = CreateClient(new FakeQueryExecutor { Probe = new CodalDbHealthProbe(true, 2362, null) });

        var result = await client.CheckAsync(default);

        Assert.Equal(ProviderHealthStatus.Healthy, result.Status);
        Assert.Equal("CodalDb", result.ProviderName);
    }

    [Fact]
    public async Task CheckAsync_UnavailableWhenProbeWrapsSqlFailure()
    {
        var client = CreateClient(new FakeQueryExecutor
        {
            ProbeError = new FinancialProviderException(
                FinancialProviderErrorCode.RemoteUnavailable,
                "CodalDb health probe failed.")
        });

        var result = await client.CheckAsync(default);

        Assert.Equal(ProviderHealthStatus.Unavailable, result.Status);
    }

    private sealed class FakeQueryExecutor : ICodalDbQueryExecutor
    {
        public IReadOnlyList<CodalDbCompanyRecord> Companies { get; init; } = [];
        public IReadOnlyList<CodalStatementRow> Statements { get; init; } = [];
        public IReadOnlyList<CodalMonthlyActivityRow> Monthly { get; init; } = [];
        public IReadOnlyList<CodalRatioRow> Ratios { get; init; } = [];
        public CodalDbHealthProbe Probe { get; init; } = new(true, 1, null);
        public Exception? ProbeError { get; init; }

        public Task<IReadOnlyList<CodalDbCompanyRecord>> QueryCompaniesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Companies);

        public Task<IReadOnlyList<CodalStatementRow>> QueryStatementsAsync(int companyId, CancellationToken cancellationToken) =>
            Task.FromResult(Statements);

        public Task<IReadOnlyList<CodalMonthlyActivityRow>> QueryMonthlyActivityAsync(int companyId, CancellationToken cancellationToken) =>
            Task.FromResult(Monthly);

        public Task<IReadOnlyList<CodalRatioRow>> QueryFinancialRatiosAsync(
            int companyId,
            IReadOnlyCollection<int> mappedItemIds,
            CancellationToken cancellationToken) =>
            Task.FromResult(Ratios);

        public Task<CodalDbHealthProbe> ProbeAsync(CancellationToken cancellationToken) =>
            ProbeError is not null
                ? Task.FromException<CodalDbHealthProbe>(ProbeError)
                : Task.FromResult(Probe);
    }
}
