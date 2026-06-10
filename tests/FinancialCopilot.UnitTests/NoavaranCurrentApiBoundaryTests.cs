using System.Net;
using System.Text;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Spec 053 — the per-run current-API Shamsi start boundary override lowers statement/index
/// <c>fromYear</c> and the monthly <c>fromDate</c>, while monthly stays clamped to the vendor-permitted
/// 1404 boundary.
/// </summary>
public sealed class NoavaranCurrentApiBoundaryTests
{
    [Fact]
    public async Task Override_LowersStatementFromYear_AndClampsMonthlyToTheBoundary()
    {
        var requests = new List<(string Uri, string Body)>();
        using var httpClient = StubClient(requests);
        var boundary = new NoavaranCurrentApiBoundaryOverride();
        boundary.Set(1401); // backfill override below the monthly-permitted 1404

        var client = CreateClient(httpClient, boundary);

        await client.FetchFinancialStatementsAsync("3", CancellationToken.None);
        await client.FetchMonthlyReportsAsync("3", CancellationToken.None);

        // Statements honor the override directly.
        Assert.Contains(requests, r => r.Uri.Contains("FS/") && r.Uri.Contains("fromYear=1401"));
        // Monthly is raised to the 1404 access boundary even though the override asked for 1401.
        // Live-verified contract (spec 057): Shamsi bounds travel as year+month query-string
        // tokens; the JSON body must not carry dates (v3 ServiceSales 500s otherwise).
        var monthly = requests.Where(r => r.Uri.Contains("MonthlyActivity")).ToArray();
        Assert.NotEmpty(monthly);
        Assert.All(monthly, r =>
        {
            Assert.Contains("fromDate=140401", r.Uri);
            Assert.DoesNotContain("fromDate", r.Body);
            Assert.Contains("\"companyIds\":[3]", r.Body);
        });
    }

    [Fact]
    public async Task MonthlyActivityWindow_BoundsBothEndpointsToOneShamsiMonthInTheQueryString()
    {
        var requests = new List<(string Uri, string Body)>();
        using var httpClient = StubClient(requests);
        var boundary = new NoavaranCurrentApiBoundaryOverride();
        boundary.SetMonthlyActivityWindow("1405/02/01", "1405/02/31");

        var client = CreateClient(httpClient, boundary);
        await client.FetchMonthlyReportsAsync("13150", CancellationToken.None);

        var monthly = requests.Where(r => r.Uri.Contains("MonthlyActivity")).ToArray();
        Assert.Equal(2, monthly.Length); // ProductSales (v2) + ServiceSales (v3)
        Assert.All(monthly, r =>
        {
            Assert.Contains("fromDate=140502", r.Uri);
            Assert.Contains("toDate=140502", r.Uri);
            Assert.DoesNotContain("fromDate", r.Body);
            Assert.Contains("\"companyIds\":[13150]", r.Body);
        });
    }

    [Fact]
    public async Task MonthlyActivity_ServiceSalesFailure_IsIsolatedAndKeepsProductSales()
    {
        var requests = new List<(string Uri, string Body)>();
        using var httpClient = new HttpClient(new StubHandler(async request =>
        {
            requests.Add((
                request.RequestUri!.OriginalString,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync()));
            return request.RequestUri!.OriginalString.Contains("ServiceSales")
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("boom", Encoding.UTF8, "application/json")
                }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                };
        }))
        {
            BaseAddress = new Uri("https://data3.nadpco.com/")
        };

        var client = CreateClient(httpClient, new NoavaranCurrentApiBoundaryOverride());
        var payload = await client.FetchMonthlyReportsAsync("3", CancellationToken.None);

        // The envelope is stored with an empty service-sales part instead of failing the fetch.
        Assert.Contains("\"productSales\":\"[]\"", payload.Payload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"serviceSales\":\"[]\"", payload.Payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NoOverride_UsesConfiguredFromYear()
    {
        var requests = new List<(string Uri, string Body)>();
        using var httpClient = StubClient(requests);
        var client = CreateClient(httpClient, new NoavaranCurrentApiBoundaryOverride()); // unset

        await client.FetchFinancialStatementsAsync("3", CancellationToken.None);

        Assert.Contains(requests, r => r.Uri.Contains("FS/") && r.Uri.Contains("fromYear=1403"));
    }

    private static HttpClient StubClient(List<(string Uri, string Body)> requests) =>
        new(new StubHandler(async request =>
        {
            requests.Add((
                request.RequestUri!.OriginalString,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync()));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            };
        }))
        {
            BaseAddress = new Uri("https://data3.nadpco.com/")
        };

    private static NadpcoApiDataProviderClient CreateClient(
        HttpClient httpClient,
        INoavaranCurrentApiBoundaryOverride boundary) =>
        new(
            httpClient,
            new ProviderRawPayloadStore(CreateProviderDbContext()),
            new SequenceTokenProvider("token"),
            Options.Create(new NadpcoApiProviderOptions
            {
                StatementFromYear = 1403,
                FundamentalIndexFromYear = 1403,
                MonthlyActivityFromDate = "1404/01/01"
            }),
            TimeProvider.System,
            NullLogger<NadpcoApiDataProviderClient>.Instance,
            boundary);

    private static FinancialProviderDbContext CreateProviderDbContext() =>
        new(new DbContextOptionsBuilder<FinancialProviderDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
    }

    private sealed class SequenceTokenProvider(string token) : INadpcoApiTokenProvider
    {
        public Task<string> GetTokenAsync(bool forceRefresh, CancellationToken cancellationToken) =>
            Task.FromResult(token);

        public void Invalidate() { }
    }
}
