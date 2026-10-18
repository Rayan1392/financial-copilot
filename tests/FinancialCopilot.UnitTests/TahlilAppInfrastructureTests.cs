using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FinancialCopilot.Application.Authentication.TahlilApp;
using FinancialCopilot.Domain.Identity.TahlilApp;
using FinancialCopilot.Infrastructure.Authentication.TahlilApp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class TahlilAppExchangeParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ValidResponse_ProducesNormalisedSnapshot()
    {
        var snapshot = Parse("""
            {"user":{"externalUserId":"u-1","firstName":" Ali ","lastName":"Ahmadi","nationalId":"۰۰۱۲۳۴۵۶۷۸","mobile":"09121234567","status":"Active"},
             "subscription":{"plan":"Plus","isActive":true,"expiresAt":"2026-12-01T00:00:00Z","revision":17},
             "permissions":["a","b"],"unknownField":{"x":1}}
            """);

        Assert.NotNull(snapshot);
        Assert.Equal("u-1", snapshot!.ExternalUserId);
        Assert.False(snapshot.IsSuspended);
        Assert.Equal(IncomingProfileFieldKind.Valid, snapshot.FirstName.Kind);
        Assert.Equal("Ali", snapshot.FirstName.NormalizedValue);
        Assert.Equal("0012345678", snapshot.NationalId.NormalizedValue);
        Assert.Equal("+989121234567", snapshot.Mobile.NormalizedValue);
        Assert.Equal("Plus", snapshot.ExternalPlan);
        Assert.True(snapshot.IsSubscriptionActive);
        Assert.Equal(17, snapshot.ExternalRevision);
        Assert.Equal(2, snapshot.ExternalPermissionCount);
    }

    [Fact]
    public void InvalidOptionalFields_AreDroppedToInvalid_AndAbsentOrNullStayDistinct()
    {
        var snapshot = Parse("""
            {"user":{"externalUserId":"u-2","firstName":"","lastName":null,"nationalId":"123","status":"Active"},
             "subscription":{"plan":"Pro","isActive":true,"expiresAt":"2026-12-01T00:00:00Z"}}
            """);

        Assert.NotNull(snapshot);
        Assert.Equal(IncomingProfileFieldKind.Invalid, snapshot!.FirstName.Kind);
        Assert.Equal(IncomingProfileFieldKind.Null, snapshot.LastName.Kind);
        Assert.Equal(IncomingProfileFieldKind.Invalid, snapshot.NationalId.Kind);
        Assert.Equal(IncomingProfileFieldKind.Absent, snapshot.Mobile.Kind);
    }

    [Fact]
    public void Suspended_IsRecognised()
    {
        var snapshot = Parse("""{"user":{"externalUserId":"u-3","status":"Suspended"},"subscription":{"plan":"Free","isActive":false}}""");
        Assert.True(snapshot!.IsSuspended);
    }

    [Theory]
    [InlineData("""{"user":{"externalUserId":"u-4","status":"Deleted"},"subscription":{"plan":"Free","isActive":false}}""")]
    [InlineData("""{"user":{"externalUserId":"bad id!","status":"Active"},"subscription":{"plan":"Free","isActive":false}}""")]
    [InlineData("""{"user":{"status":"Active"},"subscription":{"plan":"Free","isActive":false}}""")]
    [InlineData("""{"user":{"externalUserId":"u-5","status":"Active"}}""")]
    [InlineData("""["not","an","object"]""")]
    [InlineData("{\"user\":{\"externalUserId\":\"u-6\",\"status\":\"Active\"},\"subscription\":{\"plan\":\"Free\"")]
    public void StructuralViolations_ReturnNull(string json) =>
        Assert.Null(Parse(json));

    [Fact]
    public void MissingIsActive_FailsClosed()
    {
        var snapshot = Parse("""{"user":{"externalUserId":"u-7","status":"Active"},"subscription":{"plan":"Pro","expiresAt":"2026-12-01T00:00:00Z"}}""");
        Assert.False(snapshot!.IsSubscriptionActive);
    }

    [Fact]
    public void ImplausibleExpiry_IsIgnoredAsMissing()
    {
        var snapshot = Parse("""{"user":{"externalUserId":"u-8","status":"Active"},"subscription":{"plan":"Pro","isActive":true,"expiresAt":"2099-01-01T00:00:00Z"}}""");
        Assert.Null(snapshot!.SubscriptionExpiresAt);
    }

    private static TahlilAppExchangeSnapshot? Parse(string json) =>
        TahlilAppExchangeParser.TryParse(Encoding.UTF8.GetBytes(json), Now);
}

public sealed class TahlilAppIdentityGatewayTests
{
    private const string Code = "AbCdEfGhIjKlMnOpQrStUvWx";

    [Fact]
    public async Task Success_ReturnsValidatedSnapshot_AndSendsCredentialsOnlyAsHeaders()
    {
        var variable = UniqueVariable();
        Environment.SetEnvironmentVariable(variable, "test-credential");
        try
        {
            var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"user":{"externalUserId":"u-1","status":"Active"},"subscription":{"plan":"Free","isActive":false}}"""));
            var gateway = CreateGateway(handler, variable);

            var result = await gateway.ExchangeAsync(Code, "corr-9", CancellationToken.None);

            Assert.Equal(TahlilAppExchangeOutcome.Succeeded, result.Outcome);
            Assert.Equal("u-1", result.Snapshot!.ExternalUserId);
            Assert.Equal(1, handler.Requests);
            Assert.Equal("test-credential", handler.LastRequestHeaders!["X-Api-Key"]);
            Assert.Equal("corr-9", handler.LastRequestHeaders["X-Correlation-Id"]);
            Assert.Contains(Code, handler.LastRequestBody);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public async Task BadRequest_IsInvalidCode()
    {
        var variable = UniqueVariable();
        Environment.SetEnvironmentVariable(variable, "test-credential");
        try
        {
            var handler = new StubHandler(_ => Json(HttpStatusCode.BadRequest, """{"error":"invalid_code"}"""));
            var result = await CreateGateway(handler, variable).ExchangeAsync(Code, "c", CancellationToken.None);
            Assert.Equal(TahlilAppExchangeOutcome.InvalidCode, result.Outcome);
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
    [InlineData(HttpStatusCode.Redirect)]
    public async Task ServerFailuresAndRedirects_AreUnavailable_AndNeverRetried(HttpStatusCode status)
    {
        var variable = UniqueVariable();
        Environment.SetEnvironmentVariable(variable, "test-credential");
        try
        {
            var handler = new StubHandler(_ => new HttpResponseMessage(status));
            var result = await CreateGateway(handler, variable).ExchangeAsync(Code, "c", CancellationToken.None);

            Assert.Equal(TahlilAppExchangeOutcome.Unavailable, result.Outcome);
            Assert.Equal(1, handler.Requests);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public async Task UnexpectedContentType_IsUnavailable()
    {
        var variable = UniqueVariable();
        Environment.SetEnvironmentVariable(variable, "test-credential");
        try
        {
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html/>", Encoding.UTF8, "text/html")
            });
            var result = await CreateGateway(handler, variable).ExchangeAsync(Code, "c", CancellationToken.None);
            Assert.Equal(TahlilAppExchangeOutcome.Unavailable, result.Outcome);
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
            var body = "{\"user\":{\"externalUserId\":\"u-1\",\"status\":\"Active\",\"pad\":\"" + padding +
                "\"},\"subscription\":{\"plan\":\"Free\",\"isActive\":false}}";
            var handler = new StubHandler(_ => Json(HttpStatusCode.OK, body));
            var result = await CreateGateway(handler, variable).ExchangeAsync(Code, "c", CancellationToken.None);
            Assert.Equal(TahlilAppExchangeOutcome.Unavailable, result.Outcome);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public async Task MissingCredential_IsUnavailable_AndSendsNothing()
    {
        var variable = UniqueVariable();
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "{}"));
        var result = await CreateGateway(handler, variable).ExchangeAsync(Code, "c", CancellationToken.None);

        Assert.Equal(TahlilAppExchangeOutcome.Unavailable, result.Outcome);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task Timeout_IsUnavailable_AndNeverRetried()
    {
        var variable = UniqueVariable();
        Environment.SetEnvironmentVariable(variable, "test-credential");
        try
        {
            var handler = new StubHandler(_ => throw new TaskCanceledException("timeout"));
            var result = await CreateGateway(handler, variable).ExchangeAsync(Code, "c", CancellationToken.None);

            Assert.Equal(TahlilAppExchangeOutcome.Unavailable, result.Outcome);
            Assert.Equal(1, handler.Requests);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    private static string UniqueVariable() => $"TAHLILAPP_TEST_{Guid.NewGuid():N}";

    private static TahlilAppIdentityGateway CreateGateway(StubHandler handler, string variable)
    {
        var options = new TahlilAppOptions
        {
            Enabled = true,
            BaseUrl = "https://tahlilapp.example.test",
            ApiKeyEnvironmentVariable = variable,
            ExchangeTimeoutSeconds = 2,
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
        public Dictionary<string, string>? LastRequestHeaders { get; private set; }
        public string LastRequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            LastRequestHeaders = request.Headers.ToDictionary(
                header => header.Key,
                header => string.Join(",", header.Value),
                StringComparer.OrdinalIgnoreCase);
            LastRequestBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }
}

public sealed class TahlilAppOptionsValidatorTests
{
    private static readonly TahlilAppOptionsValidator Validator = new();

    [Fact]
    public void DisabledWithCanonicalPlansIsValid()
    {
        Assert.True(Validator.Validate(null, new TahlilAppOptions()).Succeeded);
    }

    [Fact]
    public void EnabledRequiresHttpsBaseUrlAndKeyRingPath()
    {
        var result = Validator.Validate(null, new TahlilAppOptions { Enabled = true, BaseUrl = "http://tahlilapp.example.test" });
        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, failure => failure.Contains("HTTPS"));
        Assert.Contains(result.Failures!, failure => failure.Contains("KeyRingPath"));
    }

    [Fact]
    public void EnabledRejectsNonWritableKeyRing()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}");
        var result = Validator.Validate(null, new TahlilAppOptions
        {
            Enabled = true,
            BaseUrl = "https://tahlilapp.example.test",
            DataProtectionKeyRingPath = missing
        });
        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, failure => failure.Contains("writable"));
    }

    [Fact]
    public void PlanMapTargetMustBeCanonical()
    {
        var options = new TahlilAppOptions { PlanMap = new Dictionary<string, string> { ["Gold"] = "Enterprise" } };
        var result = Validator.Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, failure => failure.Contains("canonical"));
    }
}
