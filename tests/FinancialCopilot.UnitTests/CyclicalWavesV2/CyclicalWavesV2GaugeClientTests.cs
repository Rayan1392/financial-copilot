using System.Net;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWavesV2;

namespace FinancialCopilot.UnitTests.CyclicalWavesV2;

public sealed class CyclicalWavesV2GaugeClientTests
{
    [Fact]
    public async Task Catalogs_parse_the_documented_shapes()
    {
        var gaugeClient = Client(HttpStatusCode.OK, CyclicalWavesV2Fixtures.Read("gauge-catalog.json"));
        var equilibriumClient = Client(HttpStatusCode.OK, CyclicalWavesV2Fixtures.Read("equilibrium-catalog.json"));

        var gauge = Assert.Single(await gaugeClient.GetGaugeCatalogAsync(CancellationToken.None));
        var equilibrium = Assert.Single(await equilibriumClient.GetEquilibriumCatalogAsync(CancellationToken.None));

        Assert.Equal(("آباد", "IRO1ABAD0001", true, true), (gauge.Ticker, gauge.Enticker, gauge.HasPe, gauge.HasPs));
        Assert.Equal(("فولاد", "IRO1FOLD0001"), (equilibrium.Ticker, equilibrium.Enticker));
    }

    [Fact]
    public async Task Gauge_details_preserve_all_counts_decimal_values_nulls_and_status()
    {
        var client = Client(HttpStatusCode.OK, CyclicalWavesV2Fixtures.Read("gauge-details.json"));
        var result = await client.GetGaugeDetailsAsync("IRO1FOLD0001", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("IRO1FOLD0001", result.Enticker);
        Assert.Equal((414, 848, 1490, 851, 534, 506),
            (result.Pe!.A, result.Pe.B, result.Pe.C, result.Pe.D, result.Pe.E, result.Pe.F));
        Assert.Equal(("5.9751", "2.2886", "38.8073", "3.4315", "5.8271", "9.4010"),
            (result.Pe.Close!.Value.ToString(), result.Pe.Start.ToString(), result.Pe.End.ToString(), result.Pe.Min.ToString(), result.Pe.Avg.ToString(), result.Pe.Max.ToString()));
        Assert.Equal(3, result.Pe.Status);
        Assert.Equal((468, 933, 1021, 1034, 659, 529),
            (result.Ps!.A, result.Ps.B, result.Ps.C, result.Ps.D, result.Ps.E, result.Ps.F));
        Assert.Equal("1.7531", result.Ps.Close!.Value.ToString());
        Assert.Equal(2, result.Ps.Status);
    }

    [Fact]
    public async Task Null_gauges_close_and_status_are_not_coerced_to_zero()
    {
        var client = Client(HttpStatusCode.OK, CyclicalWavesV2Fixtures.Read("gauge-details-null.json"));
        var result = await client.GetGaugeDetailsAsync("IRO1LOSS0001", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Null(result.Ps);
        Assert.Null(result.Pe!.Close);
        Assert.Null(result.Pe.Status);
        Assert.Equal("-4.125000000000000000001", result.Pe.Start.ToString());
        Assert.Equal("-1.50", result.Pe.Min.ToString());
    }

    [Fact]
    public async Task Equilibrium_values_keep_negative_sign_and_full_scale()
    {
        var client = Client(HttpStatusCode.OK, CyclicalWavesV2Fixtures.Read("equilibrium-details-negative.json"));

        var result = await client.GetEquilibriumRangeAsync("IRO1FOLD0001", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("-3236.7000000000000000001", result.Min.ToString());
        Assert.Equal("-7232.1700000000000000002", result.Avg.ToString());
        Assert.Equal("-11227.6500000000000000003", result.Max.ToString());
    }

    [Theory]
    [InlineData("فولاد", "/%D9%81%D9%88%D9%84%D8%A7%D8%AF")]
    [InlineData("IRO1FOLD0001", "/IRO1FOLD0001")]
    public async Task Detail_identifier_is_encoded_once(string identifier, string pathSuffix)
    {
        var handler = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.NotFound, "{}"));
        var client = CreateClient(handler);

        Assert.Null(await client.GetGaugeDetailsAsync(identifier, CancellationToken.None));

        var uri = Assert.Single(handler.Requests).Uri;
        Assert.EndsWith("/pe-ps-gauges/ticker" + pathSuffix, uri, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("%25", uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Equilibrium_404_is_unavailable_and_401_is_authentication_failure()
    {
        var notFound = Client(HttpStatusCode.NotFound, "{}");
        Assert.Null(await notFound.GetEquilibriumRangeAsync("IRO1FOLD0001", CancellationToken.None));

        var unauthorized = Client(HttpStatusCode.Unauthorized, "{}");
        var exception = await Assert.ThrowsAsync<FinancialProviderException>(() => unauthorized.GetEquilibriumRangeAsync("IRO1FOLD0001", CancellationToken.None));
        Assert.Equal(FinancialProviderErrorCode.Unauthorized, exception.Code);
    }

    [Theory]
    [InlineData("-1", "2")]
    [InlineData("0", "5")]
    public async Task Gauge_details_reject_negative_counts_and_invalid_status(string count, string status)
    {
        var payload = $"{{\"ticker\":\"X\",\"pe\":{{\"a\":{count},\"b\":0,\"c\":0,\"d\":0,\"e\":0,\"f\":0,\"close\":null,\"start\":0,\"end\":1,\"min\":0,\"avg\":0.5,\"max\":1,\"status\":{status}}},\"ps\":null}}";
        var client = Client(HttpStatusCode.OK, payload);

        var exception = await Assert.ThrowsAsync<FinancialProviderException>(() => client.GetGaugeDetailsAsync("X", CancellationToken.None));

        Assert.Equal(FinancialProviderErrorCode.InvalidResponse, exception.Code);
    }

    private static CyclicalWavesV2GaugeDataClient Client(HttpStatusCode status, string json) => CreateClient(
        new StubHttpHandler((_, _) => StubHttpHandler.Json(status, json)));

    private static CyclicalWavesV2GaugeDataClient CreateClient(StubHttpHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.tahlilapp.com/api/") });
}
