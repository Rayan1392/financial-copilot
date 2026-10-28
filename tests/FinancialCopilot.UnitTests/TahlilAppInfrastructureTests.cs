using System.Net;
using System.Text;
using FinancialCopilot.Application.Authentication.TahlilApp;
using FinancialCopilot.Infrastructure.Authentication.TahlilApp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class TahlilAppExchangeParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyDictionary<string, string> Plans = new Dictionary<string, string>
    {
        ["sapio_plus"] = "Plus",
        ["sapio_pro"] = "Pro"
    };

    [Fact]
    public void OfficialResponse_ParsesSnakeCaseAndNullableName()
    {
        var result = Parse("""{"user_id":"u-1","name":null,"subscription":{"active":true,"expires_at":"2026-12-16T08:06:31.000Z","plans":[{"slug":"sapio_plus","starts_at":"2026-11-16T08:06:31.000Z","expires_at":"2026-12-16T08:06:31.000Z"}]}}""");
        Assert.NotNull(result);
        Assert.Null(result!.Name);
        Assert.Equal("Plus", result.ExternalPlan);
        var item = result.PlanSubscriptions![0];
        Assert.Equal($"sapio_plus:{item.StartsAt.ToUnixTimeMilliseconds()}:{item.ExpiresAt.ToUnixTimeMilliseconds()}", item.IdentityKey);
    }

    [Fact]
    public void ConcurrentPlusAndPro_AreBothPreservedAndProWins()
    {
        var result = Parse("""{"user_id":"u-2","subscription":{"plans":[{"slug":"sapio_plus","starts_at":"2026-11-01T00:00:00Z","expires_at":"2026-12-20T00:00:00Z"},{"slug":"sapio_pro","starts_at":"2026-11-15T00:00:00Z","expires_at":"2026-12-01T00:00:00Z"}]}}""");
        Assert.NotNull(result);
        Assert.Equal(2, result!.PlanSubscriptions!.Count);
        var decision = FinancialCopilot.Domain.Identity.TahlilApp.TahlilAppSubscriptionPolicy.Decide(
            result.PlanSubscriptions.Select(item => (item.Slug, item.StartsAt, item.ExpiresAt)).ToArray(), Now, Plans);
        Assert.Equal("Pro", decision.PlanCode);
    }

    [Fact]
    public void InvalidTimestampOrUnknownUserShapeIsRejected()
    {
        Assert.Null(Parse("""{"user_id":"u-3","subscription":{"plans":[{"slug":"sapio_pro","starts_at":"2026-11-01T00:00:00+03:30","expires_at":"2026-12-01T00:00:00Z"}]}}"""));
        Assert.Null(Parse("""{"user":{"user_id":"u-3"},"subscription":{"plans":[]}}"""));
    }

    private static FinancialCopilot.Application.Authentication.TahlilApp.TahlilAppExchangeSnapshot? Parse(string json) =>
        TahlilAppExchangeParser.TryParse(Encoding.UTF8.GetBytes(json), Now);
}

public sealed class TahlilAppStatusParserTests
{
    [Fact]
    public void StatusResponseMatchesStableUserIdAndReturnsPlanSet()
    {
        var json = Encoding.UTF8.GetBytes("""{"user_id":"u-status","subscription":{"plans":[{"slug":"sapio_pro","starts_at":"2026-11-01T00:00:00Z","expires_at":"2026-12-01T00:00:00Z"}]}}""");
        var result = TahlilAppStatusParser.TryParse(json, "u-status", DateTimeOffset.Parse("2026-11-20T00:00:00Z"), out var rejected);
        Assert.NotNull(result);
        Assert.Null(rejected);
        Assert.Equal("Pro", result!.PlanSubscriptions![0].PlanCode);
        Assert.Null(TahlilAppStatusParser.TryParse(json, "other", DateTimeOffset.UtcNow, out _));
    }
}

public sealed class TahlilAppIdentityGatewayTests
{
    private const string Code = "AbCdEfGhIjKlMnOpQrStUvWx";

    [Fact]
    public async Task ExchangeUsesOfficialPathAndBasicAuthentication()
    {
        var variable = UniqueVariable();
        Environment.SetEnvironmentVariable(variable, "test-secret");
        try
        {
            var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"user_id":"u-1","name":null,"subscription":{"plans":[]}}"""));
            var result = await CreateGateway(handler, variable).ExchangeAsync(Code, "corr-9", CancellationToken.None);
            Assert.Equal(TahlilAppExchangeOutcome.Succeeded, result.Outcome);
            Assert.Equal("https://api.tahlilapp.example.test/api/partners/exchange", handler.LastRequestUri);
            Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("financial-copilot:test-secret")), handler.LastRequestHeaders!["Authorization"]);
            Assert.DoesNotContain("X-Api-Key", handler.LastRequestHeaders.Keys);
            Assert.DoesNotContain("X-SapioAI-Contract-Version", handler.LastRequestHeaders.Keys);
            Assert.Contains(Code, handler.LastRequestBody);
        }
        finally { Environment.SetEnvironmentVariable(variable, null); }
    }

    [Fact]
    public async Task StatusUsesOfficialPathAndBasicAuthentication()
    {
        var variable = UniqueVariable();
        Environment.SetEnvironmentVariable(variable, "test-secret");
        try
        {
            var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"user_id":"user:1","subscription":{"plans":[]}}"""));
            var result = await CreateGateway(handler, variable).GetStatusAsync("user:1", "corr", CancellationToken.None);
            Assert.Equal(TahlilAppStatusOutcome.Succeeded, result.Outcome);
            Assert.Equal("https://api.tahlilapp.example.test/api/partners/users/user%3A1/status", handler.LastRequestUri);
            Assert.StartsWith("Basic ", handler.LastRequestHeaders!["Authorization"]);
        }
        finally { Environment.SetEnvironmentVariable(variable, null); }
    }

    [Fact]
    public async Task MissingSecretDoesNotSendRequest()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "{}"));
        var result = await CreateGateway(handler, UniqueVariable()).ExchangeAsync(Code, "c", CancellationToken.None);
        Assert.Equal(TahlilAppExchangeOutcome.Unavailable, result.Outcome);
        Assert.Equal(0, handler.Requests);
    }

    private static string UniqueVariable() => $"TAHLILAPP_TEST_{Guid.NewGuid():N}";
    private static TahlilAppIdentityGateway CreateGateway(StubHandler handler, string variable) => new(
        new HttpClient(handler),
        Options.Create(new TahlilAppOptions
        {
            Enabled = true,
            BaseUrl = "https://api.tahlilapp.example.test/api/",
            ClientSecretEnvironmentVariable = variable,
            ExchangeTimeoutSeconds = 2
        }),
        TimeProvider.System,
        NullLogger<TahlilAppIdentityGateway>.Instance);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public Dictionary<string, string>? LastRequestHeaders { get; private set; }
        public string LastRequestBody { get; private set; } = string.Empty;
        public string? LastRequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            LastRequestUri = request.RequestUri?.ToString();
            LastRequestHeaders = request.Headers.ToDictionary(header => header.Key,
                header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase);
            LastRequestBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }
}

public sealed class TahlilAppOptionsValidatorTests
{
    private static readonly TahlilAppOptionsValidator Validator = new();

    [Fact]
    public void DisabledConfigurationIsValid() => Assert.True(Validator.Validate(null, new TahlilAppOptions()).Succeeded);

    [Fact]
    public void EnabledRequiresHttpsAndWritableKeyRing()
    {
        var result = Validator.Validate(null, new TahlilAppOptions { Enabled = true, BaseUrl = "http://tahlilapp.example.test" });
        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, failure => failure.Contains("HTTPS"));
        Assert.Contains(result.Failures!, failure => failure.Contains("KeyRingPath"));
    }

    [Fact]
    public void PlanMapTargetMustBeCanonical()
    {
        var options = new TahlilAppOptions { PlanMap = new Dictionary<string, string> { ["sapio_plus"] = "Enterprise" } };
        var result = Validator.Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, failure => failure.Contains("canonical"));
    }
}
