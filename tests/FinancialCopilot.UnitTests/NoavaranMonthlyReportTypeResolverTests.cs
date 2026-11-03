using System.Text.Json;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;

namespace FinancialCopilot.UnitTests;

public sealed class NoavaranMonthlyReportTypeResolverTests
{
    private readonly NoavaranMonthlyReportTypeResolver _resolver = new();

    [Theory]
    [InlineData(1_000_000, NoavaranMonthlyReportRouteOutcome.ProductSales, NoavaranMonthlyReportProviderType.ProductSales)]
    [InlineData(1_000_008, NoavaranMonthlyReportRouteOutcome.ProductSales, NoavaranMonthlyReportProviderType.ProductSales)]
    [InlineData(1_000_005, NoavaranMonthlyReportRouteOutcome.ServiceSales, NoavaranMonthlyReportProviderType.ServiceSales)]
    public void Supported_codes_resolve_to_the_authoritative_endpoint(int reportingType, NoavaranMonthlyReportRouteOutcome outcome, NoavaranMonthlyReportProviderType providerType)
    {
        var result = _resolver.Resolve(reportingType);

        Assert.Equal(outcome, result.Outcome);
        Assert.Equal(providerType, result.ProviderType);
        Assert.Equal(reportingType, result.ReportingType);
    }

    [Theory]
    [InlineData(1_000_001)]
    [InlineData(1_000_002)]
    [InlineData(1_000_003)]
    [InlineData(1_000_004)]
    [InlineData(1_000_006)]
    [InlineData(1_000_007)]
    [InlineData(1_000_009)]
    public void Known_unsupported_codes_have_no_route(int reportingType)
    {
        var result = _resolver.Resolve(reportingType);

        Assert.Equal(NoavaranMonthlyReportRouteOutcome.Unsupported, result.Outcome);
        Assert.Null(result.ProviderType);
        Assert.Equal(reportingType, result.ReportingType);
    }

    [Fact]
    public void Missing_and_future_codes_have_no_route_and_preserve_future_code()
    {
        var missing = _resolver.Resolve((int?)null);
        var future = _resolver.Resolve(1_000_010);

        Assert.Equal(NoavaranMonthlyReportRouteOutcome.Missing, missing.Outcome);
        Assert.Null(missing.ProviderType);
        Assert.Equal(NoavaranMonthlyReportRouteOutcome.Unknown, future.Outcome);
        Assert.Null(future.ProviderType);
        Assert.Equal(1_000_010, future.ReportingType);
    }

    [Fact]
    public void Malformed_provider_value_is_not_classified()
    {
        var result = _resolver.Resolve("not-a-number");

        Assert.Equal(NoavaranMonthlyReportRouteOutcome.Malformed, result.Outcome);
        Assert.Null(result.ProviderType);
    }

    [Fact]
    public void Company_contract_deserializes_numeric_null_and_unknown_reporting_types()
    {
        var numeric = JsonSerializer.Deserialize<NadpcoApiCompanyRecord>("{\"coID\":13226,\"reportingType\":1000008}");
        var missing = JsonSerializer.Deserialize<NadpcoApiCompanyRecord>("{\"coID\":13226,\"reportingType\":null}");
        var future = JsonSerializer.Deserialize<NadpcoApiCompanyRecord>("{\"coID\":13226,\"reportingType\":1000010}");

        Assert.Equal(1_000_008, numeric!.ReportingType);
        Assert.Null(missing!.ReportingType);
        Assert.Equal(1_000_010, future!.ReportingType);
    }

    [Fact]
    public void Malformed_reporting_type_payload_is_rejected_by_the_provider_contract()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<NadpcoApiCompanyRecord>("{\"coID\":13226,\"reportingType\":\"1000008\"}"));
    }
}
