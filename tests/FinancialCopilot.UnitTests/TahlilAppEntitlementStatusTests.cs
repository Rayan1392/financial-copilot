using System.Net;
using System.Text;
using FinancialCopilot.Application.Authentication.TahlilApp;
using FinancialCopilot.Infrastructure.Authentication.TahlilApp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class TahlilAppEntitlementStatusTests
{
    private const string ExternalId = "tahlil-user-1";
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Success_ParsesOfficialPlanSet_AndSendsAuthenticatedGet()
    {
        var variable = UniqueVariable();
        Environment.SetEnvironmentVariable(variable, "test-secret");
        try
        {
            var handler = new StubHandler(_ => Json(HttpStatusCode.OK, Body()));
            var result = await CreateGateway(handler, variable).GetStatusAsync(ExternalId, "corr-7", CancellationToken.None);

            Assert.Equal(TahlilAppStatusOutcome.Succeeded, result.Outcome);
            Assert.Equal(ExternalId, result.Snapshot!.ExternalUserId);
            Assert.Equal(2, result.Snapshot.PlanSubscriptions!.Count);
            Assert.True(result.Snapshot.IsSubscriptionActive);
            Assert.Equal(Now.AddDays(90), result.Snapshot.SubscriptionExpiresAt);
            Assert.Equal(HttpMethod.Get, handler.LastMethod);
            Assert.Equal("https://tahlilapp.example.test/api/partners/users/tahlil-user-1/status", handler.LastUri);
            Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("financial-copilot:test-secret")),
                handler.Authorization);
            Assert.False(handler.LastRequestHeaders!.ContainsKey("X-Api-Key"));
            Assert.False(handler.LastRequestHeaders.ContainsKey("X-Correlation-Id"));
            Assert.Equal(1, handler.Requests);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public async Task NotFound_IsUnavailableAndIsNotRetried()
    {
        var variable = UniqueVariable();
        Environment.SetEnvironmentVariable(variable, "test-secret");
        try
        {
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
            var result = await CreateGateway(handler, variable).GetStatusAsync(ExternalId, "c", CancellationToken.None);
            Assert.Equal(TahlilAppStatusOutcome.Unavailable, result.Outcome);
            Assert.Equal(1, handler.Requests);
        }
        finally { Environment.SetEnvironmentVariable(variable, null); }
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task StatusFailures_AreUnavailableAndAreNotRetried(HttpStatusCode status)
    {
        var variable = UniqueVariable();
        Environment.SetEnvironmentVariable(variable, "test-secret");
        try
        {
            var handler = new StubHandler(_ => new HttpResponseMessage(status));
            var result = await CreateGateway(handler, variable).GetStatusAsync(ExternalId, "c", CancellationToken.None);
            Assert.Equal(TahlilAppStatusOutcome.Unavailable, result.Outcome);
            Assert.Equal(1, handler.Requests);
        }
        finally { Environment.SetEnvironmentVariable(variable, null); }
    }

    [Fact]
    public async Task MissingSecret_DoesNotSendRequest()
    {
        var variable = UniqueVariable();
        Environment.SetEnvironmentVariable(variable, null);
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, Body()));
        var result = await CreateGateway(handler, variable).GetStatusAsync(ExternalId, "c", CancellationToken.None);
        Assert.Equal(TahlilAppStatusOutcome.Unavailable, result.Outcome);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public void Parser_RejectsAnotherUsersIdWithoutReturningIt()
    {
        var snapshot = TahlilAppStatusParser.TryParse(Encoding.UTF8.GetBytes(Body()), "someone-else", Now, out var field);
        Assert.Null(snapshot);
        Assert.Equal("user_id", field);
    }

    [Theory]
    [InlineData("not-a-timestamp", "2026-12-01T00:00:00Z")]
    [InlineData("2026-10-10T00:00:00+03:30", "2026-12-01T00:00:00Z")]
    [InlineData("2026-11-01T00:00:00Z", "2026-10-30T00:00:00Z")]
    public void Parser_RejectsMalformedNonUtcOrInvertedPlanWindow(string startsAt, string expiresAt)
    {
        var json = $$$"""{"user_id":"{{{ExternalId}}}","subscription":{"plans":[{"slug":"sapio_pro","starts_at":"{{{startsAt}}}","expires_at":"{{{expiresAt}}}"}]}}""";
        var snapshot = TahlilAppStatusParser.TryParse(Encoding.UTF8.GetBytes(json), ExternalId, Now, out var field);
        Assert.Null(snapshot);
        Assert.Equal("subscription.plans", field);
    }

    [Fact]
    public void Parser_IgnoresProfileFieldsAndKeepsOnlyContractFields()
    {
        var json = Body().Replace("{\"user_id\"", "{\"mobile\":\"09121234567\",\"user_id\"", StringComparison.Ordinal);
        var snapshot = TahlilAppStatusParser.TryParse(Encoding.UTF8.GetBytes(json), ExternalId, Now, out var field);
        Assert.NotNull(snapshot);
        Assert.Null(field);
        Assert.Equal(2, snapshot!.PlanSubscriptions!.Count);
    }

    [Fact]
    public void Options_DefaultOfficialStatusPathIsValid()
    {
        var result = new TahlilAppOptionsValidator().Validate(null, new TahlilAppOptions());
        Assert.True(result.Succeeded, string.Join("; ", result.Failures ?? []));
        Assert.Equal("partners/users/{externalUserId}/status", new TahlilAppOptions().StatusPathTemplate);
        Assert.Equal(3, new TahlilAppOptions().StatusTimeoutSeconds);
        Assert.False(new TahlilAppOptions().Revalidation.Enabled);
    }

    [Theory]
    [InlineData("/api/status")]
    [InlineData("api/{externalUserId}/x/{externalUserId}")]
    [InlineData("api/{externalUserId}/status/../other")]
    [InlineData("api/{externalUserId}/status?x=1")]
    public void Options_RejectsUnsafeStatusPaths(string template)
    {
        var result = new TahlilAppOptionsValidator().Validate(null, new TahlilAppOptions { StatusPathTemplate = template });
        Assert.False(result.Succeeded);
    }

    private static string UniqueVariable() => $"TAHLILAPP_TEST_{Guid.NewGuid():N}";

    private static string Body() => $$$"""
        {"user_id":"{{{ExternalId}}}","subscription":{"plans":[
          {"slug":"sapio_plus","starts_at":"{{{Now.AddDays(-2):O}}}","expires_at":"{{{Now.AddDays(30):O}}}"},
          {"slug":"sapio_pro","starts_at":"{{{Now.AddDays(-1):O}}}","expires_at":"{{{Now.AddDays(90):O}}}"}
        ]}}
        """;

    private static TahlilAppIdentityGateway CreateGateway(StubHandler handler, string variable)
    {
        var options = new TahlilAppOptions
        {
            Enabled = true,
            BaseUrl = "https://tahlilapp.example.test/api",
            ClientSecretEnvironmentVariable = variable,
            StatusTimeoutSeconds = 3,
            MaximumResponseBytes = 8192
        };
        return new TahlilAppIdentityGateway(new HttpClient(handler), Options.Create(options), TimeProvider.System,
            NullLogger<TahlilAppIdentityGateway>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public HttpMethod? LastMethod { get; private set; }
        public string? LastUri { get; private set; }
        public string? Authorization { get; private set; }
        public Dictionary<string, string>? LastRequestHeaders { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            LastMethod = request.Method;
            LastUri = request.RequestUri?.ToString();
            Authorization = request.Headers.Authorization?.ToString();
            LastRequestHeaders = request.Headers.ToDictionary(header => header.Key,
                header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(respond(request));
        }
    }
}
