using System.Net;
using System.Text;
using FinancialCopilot.Application.Authentication.TahlilApp;
using FinancialCopilot.Infrastructure.Authentication.TahlilApp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Status call (Design §9.1, UNVERIFIED with TahlilApp) through the production gateway and parser.
/// Only the outbound HTTP is replaced by a stub; no value from a response is logged or asserted as PII.
/// </summary>
public sealed class TahlilAppEntitlementStatusTests
{
    private const string ExternalId = "tahlil-user-1";
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task Success_ParsesOnlyStatusAndSubscription_AndSendsAGetWithHeaders()
    {
        var variable = UniqueVariable();
        Environment.SetEnvironmentVariable(variable, "test-credential");
        try
        {
            var expiry = Now.AddDays(30).ToString("O");
            var handler = new StubHandler(_ => Json(HttpStatusCode.OK, $$$"""
                {"user":{"externalUserId":"{{{ExternalId}}}","status":"Active","firstName":"Ali"},
                 "subscription":{"plan":"Pro","isActive":true,"expiresAt":"{{{expiry}}}","revision":42}}
                """));

            var result = await CreateGateway(handler, variable).GetStatusAsync(ExternalId, "corr-7", CancellationToken.None);

            Assert.Equal(TahlilAppStatusOutcome.Succeeded, result.Outcome);
            Assert.Equal(ExternalId, result.Snapshot!.ExternalUserId);
            Assert.False(result.Snapshot.IsSuspended);
            Assert.Equal("Pro", result.Snapshot.ExternalPlan);
            Assert.True(result.Snapshot.IsSubscriptionActive);
            Assert.Equal(42, result.Snapshot.ExternalRevision);
            Assert.NotNull(result.Snapshot.SubscriptionExpiresAt);
            Assert.Equal(HttpMethod.Get, handler.LastMethod);
            Assert.Equal("https://tahlilapp.example.test/api/financial-copilot/users/tahlil-user-1/status", handler.LastUri);
            Assert.Equal("test-credential", handler.LastRequestHeaders!["X-Api-Key"]);
            Assert.Equal("corr-7", handler.LastRequestHeaders["X-Correlation-Id"]);
            Assert.Equal(1, handler.Requests);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public async Task Suspended_IsReportedAsSuspended()
    {
        var variable = UniqueVariable();
        Environment.SetEnvironmentVariable(variable, "test-credential");
        try
        {
            var handler = new StubHandler(_ => Json(HttpStatusCode.OK, Body(status: "Suspended")));

            var result = await CreateGateway(handler, variable).GetStatusAsync(ExternalId, "c", CancellationToken.None);

            Assert.Equal(TahlilAppStatusOutcome.Succeeded, result.Outcome);
            Assert.True(result.Snapshot!.IsSuspended);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public async Task NonBooleanIsActive_FailsClosedToInactive()
    {
        var variable = UniqueVariable();
        Environment.SetEnvironmentVariable(variable, "test-credential");
        try
        {
            var body = $$$"""{"user":{"externalUserId":"{{{ExternalId}}}","status":"Active"},"subscription":{"plan":"Pro","isActive":"yes","expiresAt":"{{{Now.AddDays(3):O}}}","revision":1}}""";
            var result = await CreateGateway(new StubHandler(_ => Json(HttpStatusCode.OK, body)), variable)
                .GetStatusAsync(ExternalId, "c", CancellationToken.None);

            Assert.Equal(TahlilAppStatusOutcome.Succeeded, result.Outcome);
            Assert.False(result.Snapshot!.IsSubscriptionActive);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public async Task NotFound_IsTheNotFoundOutcome_AndNotRetried()
    {
        var variable = UniqueVariable();
        Environment.SetEnvironmentVariable(variable, "test-credential");
        try
        {
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

            var result = await CreateGateway(handler, variable).GetStatusAsync(ExternalId, "c", CancellationToken.None);

            Assert.Equal(TahlilAppStatusOutcome.NotFound, result.Outcome);
            Assert.Null(result.Snapshot);
            Assert.Equal(1, handler.Requests);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Redirect)]
    public async Task EveryOtherStatus_IsUnavailable_AndNeverRetried(HttpStatusCode status)
    {
        var variable = UniqueVariable();
        Environment.SetEnvironmentVariable(variable, "test-credential");
        try
        {
            var handler = new StubHandler(_ => new HttpResponseMessage(status));

            var result = await CreateGateway(handler, variable).GetStatusAsync(ExternalId, "c", CancellationToken.None);

            Assert.Equal(TahlilAppStatusOutcome.Unavailable, result.Outcome);
            Assert.Equal(1, handler.Requests);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public async Task Timeout_IsUnavailable_AndNeverRetried()
    {
        var variable = UniqueVariable();
        Environment.SetEnvironmentVariable(variable, "test-credential");
        try
        {
            var handler = new StubHandler(_ => throw new TaskCanceledException("timeout"));

            var result = await CreateGateway(handler, variable).GetStatusAsync(ExternalId, "c", CancellationToken.None);

            Assert.Equal(TahlilAppStatusOutcome.Unavailable, result.Outcome);
            Assert.Equal(1, handler.Requests);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public async Task WrongContentType_IsUnavailable()
    {
        var variable = UniqueVariable();
        Environment.SetEnvironmentVariable(variable, "test-credential");
        try
        {
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Body(), Encoding.UTF8, "text/html")
            });

            var result = await CreateGateway(handler, variable).GetStatusAsync(ExternalId, "c", CancellationToken.None);

            Assert.Equal(TahlilAppStatusOutcome.Unavailable, result.Outcome);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public async Task OversizedBody_IsUnavailable()
    {
        var variable = UniqueVariable();
        Environment.SetEnvironmentVariable(variable, "test-credential");
        try
        {
            var padding = new string('x', 9000);
            var handler = new StubHandler(_ => Json(HttpStatusCode.OK, Body(extraUserField: $"\"pad\":\"{padding}\"")));

            var result = await CreateGateway(handler, variable).GetStatusAsync(ExternalId, "c", CancellationToken.None);

            Assert.Equal(TahlilAppStatusOutcome.Unavailable, result.Outcome);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Theory]
    [InlineData("nationalId")]
    [InlineData("mobile")]
    public async Task ResponseWithAProfileField_IsRejected_AndTheValueIsNeverReturned(string field)
    {
        var variable = UniqueVariable();
        Environment.SetEnvironmentVariable(variable, "test-credential");
        try
        {
            var handler = new StubHandler(_ => Json(HttpStatusCode.OK, Body(extraUserField: $"\"{field}\":\"0012345678\"")));

            var result = await CreateGateway(handler, variable).GetStatusAsync(ExternalId, "c", CancellationToken.None);

            Assert.Equal(TahlilAppStatusOutcome.Unavailable, result.Outcome);
            Assert.Null(result.Snapshot);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void Parser_ReportsTheRejectedFieldName_NeverItsValue()
    {
        var body = Encoding.UTF8.GetBytes(Body(extraUserField: "\"mobile\":\"09121234567\""));

        var snapshot = TahlilAppStatusParser.TryParse(body, ExternalId, Now, out var rejectedField);

        Assert.Null(snapshot);
        Assert.Equal("mobile", rejectedField);
    }

    [Fact]
    public void Parser_RejectsAProfileFieldAtRootLevel()
    {
        var body = Encoding.UTF8.GetBytes(Body().Replace("{\"user\"", "{\"nationalId\":\"0012345678\",\"user\"", StringComparison.Ordinal));

        var snapshot = TahlilAppStatusParser.TryParse(body, ExternalId, Now, out var rejectedField);

        Assert.Null(snapshot);
        Assert.Equal("nationalId", rejectedField);
    }

    [Fact]
    public void Parser_RejectsAnotherUsersId()
    {
        var snapshot = TahlilAppStatusParser.TryParse(Encoding.UTF8.GetBytes(Body()), "someone-else", Now, out var rejectedField);

        Assert.Null(snapshot);
        Assert.Equal("user.externalUserId", rejectedField);
    }

    [Theory]
    [InlineData("Revoked")]
    [InlineData("")]
    public void Parser_RejectsAnUnknownUserStatus(string status)
    {
        var snapshot = TahlilAppStatusParser.TryParse(Encoding.UTF8.GetBytes(Body(status: status)), ExternalId, Now, out var rejectedField);

        Assert.Null(snapshot);
        Assert.Equal("user.status", rejectedField);
    }

    [Fact]
    public void Parser_RejectsMalformedJson()
    {
        var snapshot = TahlilAppStatusParser.TryParse("{not json"u8, ExternalId, Now, out var rejectedField);

        Assert.Null(snapshot);
        Assert.Equal("json", rejectedField);
    }

    [Fact]
    public void Options_DefaultStatusSettings_AreValid()
    {
        var result = new TahlilAppOptionsValidator().Validate(null, new TahlilAppOptions());

        Assert.True(result.Succeeded, string.Join("; ", result.Failures ?? []));
        Assert.Equal("/api/financial-copilot/users/{externalUserId}/status", new TahlilAppOptions().StatusPathTemplate);
        Assert.Equal(3, new TahlilAppOptions().StatusTimeoutSeconds);
        Assert.False(new TahlilAppOptions().Revalidation.Enabled);
    }

    [Theory]
    [InlineData("/api/status")]
    [InlineData("/api/{externalUserId}/x/{externalUserId}")]
    [InlineData("api/{externalUserId}/status")]
    [InlineData("//host/{externalUserId}/status")]
    [InlineData("/api/{externalUserId}/status?x=1")]
    public void Options_RejectAStatusPathTemplateWithoutOneSafePlaceholder(string template)
    {
        var result = new TahlilAppOptionsValidator().Validate(null, new TahlilAppOptions { StatusPathTemplate = template });

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData(0, 100, 15, 6)]
    [InlineData(3, 0, 15, 6)]
    [InlineData(3, 101, 15, 6)]
    [InlineData(3, 100, 0, 6)]
    [InlineData(3, 100, 15, 0)]
    public void Options_RejectOutOfRangeTimeoutsAndRevalidationBounds(int statusTimeout, int batchSize, int ttlMinutes, int graceHours)
    {
        var options = new TahlilAppOptions { StatusTimeoutSeconds = statusTimeout };
        options.Revalidation.BatchSize = batchSize;
        options.Revalidation.StatusTtlMinutes = ttlMinutes;
        options.Revalidation.StaleGraceHours = graceHours;

        Assert.False(new TahlilAppOptionsValidator().Validate(null, options).Succeeded);
    }

    private static string UniqueVariable() => $"TAHLILAPP_TEST_{Guid.NewGuid():N}";

    private static string Body(string status = "Active", string? extraUserField = null)
    {
        var extra = extraUserField is null ? string.Empty : "," + extraUserField;
        return $$$"""{"user":{"externalUserId":"{{{ExternalId}}}","status":"{{{status}}}"{{{extra}}}},"subscription":{"plan":"Free","isActive":false}}""";
    }

    private static TahlilAppIdentityGateway CreateGateway(StubHandler handler, string variable)
    {
        var options = new TahlilAppOptions
        {
            Enabled = true,
            BaseUrl = "https://tahlilapp.example.test",
            ApiKeyEnvironmentVariable = variable,
            StatusTimeoutSeconds = 3,
            MaximumResponseBytes = 8192
        };
        return new TahlilAppIdentityGateway(
            new HttpClient(handler),
            Options.Create(options),
            TimeProvider.System,
            NullLogger<TahlilAppIdentityGateway>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public HttpMethod? LastMethod { get; private set; }
        public string? LastUri { get; private set; }
        public Dictionary<string, string>? LastRequestHeaders { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            LastMethod = request.Method;
            LastUri = request.RequestUri?.ToString();
            LastRequestHeaders = request.Headers.ToDictionary(
                header => header.Key,
                header => string.Join(",", header.Value),
                StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(respond(request));
        }
    }
}
