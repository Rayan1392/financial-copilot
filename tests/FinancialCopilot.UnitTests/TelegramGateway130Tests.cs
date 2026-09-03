using System.Net;
using System.Net.Http.Json;
using FinancialCopilot.Application.Telegram;
using FinancialCopilot.TelegramGateway;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class TelegramGateway130Tests
{
    [Fact]
    public async Task PrimaryApiClient_posts_existing_endpoint_with_api_key_and_correlation()
    {
        HttpRequestMessage? observed = null;
        string? body = null;
        var factory = new NamedHttpClientFactory(async (_, request) =>
        {
            observed = request;
            body = await request.Content!.ReadAsStringAsync();
            return JsonResponse(HttpStatusCode.OK, AssistantResult());
        });
        var client = new PrimaryApiClient(factory, Options.Create(Settings()));

        await client.HandleUpdateAsync(Request(1301), CancellationToken.None);

        Assert.Equal(HttpMethod.Post, observed!.Method);
        Assert.Equal("/api/v1/telegram/assistant/updates", observed.RequestUri!.AbsolutePath);
        Assert.Equal("test-primary-key", Assert.Single(observed.Headers.GetValues("X-Api-Key")));
        Assert.Equal("telegram:1301", Assert.Single(observed.Headers.GetValues("X-Correlation-Id")));
        using var json = System.Text.Json.JsonDocument.Parse(body!);
        Assert.Equal("پرسش فارسی", json.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task PrimaryApiClient_authentication_failure_changes_health_until_success()
    {
        var status = HttpStatusCode.Unauthorized;
        var client = new PrimaryApiClient(
            new NamedHttpClientFactory((_, _) => Task.FromResult(JsonResponse(status, new { }))),
            Options.Create(Settings()));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.HandleUpdateAsync(Request(1302), CancellationToken.None));
        var unhealthy = await client.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Equal(HealthStatus.Unhealthy, unhealthy.Status);

        status = HttpStatusCode.OK;
        await client.HandleUpdateAsync(Request(1303), CancellationToken.None);
        var recovered = await client.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Healthy, recovered.Status);
    }

    [Fact]
    public void Polling_only_startup_accepts_no_hmac_but_requires_https_and_absolute_state_paths()
    {
        var settings = Settings(enabled: true);
        Assert.True(settings.IsValidForStartup());
        Assert.False(settings.HasInboundApiCredentials);

        settings.PrimaryApiBaseUrl = "http://api.example.test";
        Assert.False(settings.IsValidForStartup());
        settings.PrimaryApiBaseUrl = "https://api.example.test";
        settings.OffsetFilePath = "relative-offset.txt";
        Assert.False(settings.IsValidForStartup());
        settings.OffsetFilePath = Path.Combine(Path.GetTempPath(), "offset.txt");
        settings.ServiceId = "gateway";
        Assert.False(settings.IsValidForStartup());
        settings.ServiceSecret = "test-only-hmac";
        Assert.True(settings.IsValidForStartup());
        Assert.True(settings.HasInboundApiCredentials);
    }

    [Fact]
    public async Task Persian_text_update_maps_fields_and_advances_after_delivery()
    {
        TelegramAssistantUpdateRequest? observed = null;
        var fixture = Fixture(async (name, request) =>
        {
            if (name == "TelegramGateway.PrimaryApi")
            {
                observed = await request.Content!.ReadFromJsonAsync<TelegramAssistantUpdateRequest>();
                return JsonResponse(HttpStatusCode.OK, AssistantResult());
            }

            return TelegramSuccess();
        });

        var complete = await fixture.Worker.ProcessUpdatesAsync([MessageUpdate(1310)], CancellationToken.None);

        Assert.True(complete);
        Assert.Equal(1310, observed!.TelegramUpdateId);
        Assert.Equal(TelegramAssistantUpdateKind.Message, observed.Kind);
        Assert.Equal(9001, observed.TelegramUserId);
        Assert.Equal(8001, observed.TelegramChatId);
        Assert.Equal(7, observed.MessageThreadId);
        Assert.Equal(41, observed.TelegramMessageId);
        Assert.Equal("پرسش فارسی", observed.Text);
        Assert.Equal("fa-IR", observed.Locale);
        Assert.Equal("telegram:1310", observed.CorrelationId);
        Assert.Equal("1311", await File.ReadAllTextAsync(fixture.Settings.OffsetFilePath));
    }

    [Fact]
    public async Task Malformed_update_is_terminal_and_advances_offset()
    {
        var fixture = Fixture((_, _) => throw new InvalidOperationException("No HTTP call expected."));
        var malformed = new TelegramGatewayUpdate(
            1311,
            new TelegramGatewayMessage(42, null, new TelegramGatewayUser(1, null, "fa"), new TelegramGatewayChat(2, "private"), 1, null));

        var complete = await fixture.Worker.ProcessUpdatesAsync([malformed], CancellationToken.None);

        Assert.True(complete);
        Assert.Equal("1312", await File.ReadAllTextAsync(fixture.Settings.OffsetFilePath));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Retryable_primary_status_retains_offset(HttpStatusCode status)
    {
        var fixture = Fixture((name, _) => Task.FromResult(
            name == "TelegramGateway.PrimaryApi"
                ? JsonResponse(status, new { })
                : TelegramSuccess()));

        var complete = await fixture.Worker.ProcessUpdatesAsync([MessageUpdate(1312)], CancellationToken.None);

        Assert.False(complete);
        Assert.False(File.Exists(fixture.Settings.OffsetFilePath));
    }

    [Theory]
    [InlineData("network")]
    [InlineData("timeout")]
    public async Task Retryable_primary_transport_failure_retains_offset(string failure)
    {
        var fixture = Fixture((name, _) =>
        {
            if (name != "TelegramGateway.PrimaryApi") return Task.FromResult(TelegramSuccess());
            return failure == "network"
                ? Task.FromException<HttpResponseMessage>(new HttpRequestException("network"))
                : Task.FromException<HttpResponseMessage>(new TaskCanceledException("timeout"));
        });

        var complete = await fixture.Worker.ProcessUpdatesAsync([MessageUpdate(1313)], CancellationToken.None);

        Assert.False(complete);
        Assert.False(File.Exists(fixture.Settings.OffsetFilePath));
    }

    [Fact]
    public async Task Backend_2xx_TransientError_is_delivered_and_advances_offset()
    {
        var sent = new List<string>();
        var fixture = Fixture(async (name, request) =>
        {
            if (name == "TelegramGateway.PrimaryApi")
                return JsonResponse(HttpStatusCode.OK, AssistantResult(TelegramAssistantResultStatus.TransientError));
            if (request.RequestUri!.AbsolutePath.EndsWith("/sendMessage", StringComparison.Ordinal))
                sent.Add(await ReadTextAsync(request));
            return TelegramSuccess();
        });

        var complete = await fixture.Worker.ProcessUpdatesAsync([MessageUpdate(1314)], CancellationToken.None);

        Assert.True(complete);
        Assert.Single(sent);
        Assert.Equal("پاسخ", sent[0]);
        Assert.Equal("1315", await File.ReadAllTextAsync(fixture.Settings.OffsetFilePath));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Primary_authentication_failure_sends_generic_terminal_message_and_marks_unhealthy(HttpStatusCode status)
    {
        var sent = new List<string>();
        var fixture = Fixture(async (name, request) =>
        {
            if (name == "TelegramGateway.PrimaryApi") return JsonResponse(status, new { });
            if (request.RequestUri!.AbsolutePath.EndsWith("/sendMessage", StringComparison.Ordinal))
                sent.Add(await ReadTextAsync(request));
            return TelegramSuccess();
        });

        var complete = await fixture.Worker.ProcessUpdatesAsync([MessageUpdate(1315)], CancellationToken.None);
        var health = await fixture.PrimaryApi.CheckHealthAsync(new HealthCheckContext());

        Assert.True(complete);
        Assert.Equal(HealthStatus.Unhealthy, health.Status);
        Assert.Single(sent);
        Assert.Contains("موقت", sent[0], StringComparison.Ordinal);
        Assert.Equal("1316", await File.ReadAllTextAsync(fixture.Settings.OffsetFilePath));
    }

    [Fact]
    public async Task Multipart_delivery_is_ordered_and_confirmed_parts_are_skipped_on_replay()
    {
        var sent = new List<string>();
        var result = AssistantResult(messages:
        [
            new TelegramAssistantRenderedMessage(2, 2, "part-two"),
            new TelegramAssistantRenderedMessage(1, 2, "part-one")
        ]);
        var fixture = Fixture(async (name, request) =>
        {
            if (name == "TelegramGateway.PrimaryApi") return JsonResponse(HttpStatusCode.OK, result);
            if (request.RequestUri!.AbsolutePath.EndsWith("/sendMessage", StringComparison.Ordinal))
                sent.Add(await ReadTextAsync(request));
            return TelegramSuccess();
        });

        Assert.True(await fixture.Worker.ProcessUpdatesAsync([MessageUpdate(1316)], CancellationToken.None));
        Assert.True(await fixture.Worker.ProcessUpdatesAsync([MessageUpdate(1316)], CancellationToken.None));

        Assert.Equal(["part-one", "part-two"], sent);
        var reloaded = new GatewayIdempotencyStore(
            Options.Create(fixture.Settings),
            NullLogger<GatewayIdempotencyStore>.Instance);
        Assert.True(reloaded.TryGet("update:1316:part:1", out var confirmed));
        Assert.True(confirmed.Succeeded);
    }

    [Fact]
    public async Task Transient_telegram_part_retains_offset_and_replay_skips_confirmed_part()
    {
        var attempts = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = AssistantResult(messages:
        [
            new TelegramAssistantRenderedMessage(1, 2, "first"),
            new TelegramAssistantRenderedMessage(2, 2, "second")
        ]);
        var fixture = Fixture(async (name, request) =>
        {
            if (name == "TelegramGateway.PrimaryApi") return JsonResponse(HttpStatusCode.OK, result);
            if (!request.RequestUri!.AbsolutePath.EndsWith("/sendMessage", StringComparison.Ordinal)) return TelegramSuccess();
            var text = await ReadTextAsync(request);
            attempts[text] = attempts.GetValueOrDefault(text) + 1;
            return text == "second" && attempts[text] == 1
                ? JsonResponse(HttpStatusCode.ServiceUnavailable, new { ok = false })
                : TelegramSuccess();
        });

        Assert.False(await fixture.Worker.ProcessUpdatesAsync([MessageUpdate(1317)], CancellationToken.None));
        Assert.False(File.Exists(fixture.Settings.OffsetFilePath));
        Assert.True(await fixture.Worker.ProcessUpdatesAsync([MessageUpdate(1317)], CancellationToken.None));

        Assert.Equal(1, attempts["first"]);
        Assert.Equal(2, attempts["second"]);
        Assert.Equal("1318", await File.ReadAllTextAsync(fixture.Settings.OffsetFilePath));
    }

    [Fact]
    public async Task Permanent_telegram_rejection_is_terminal_and_advances_offset()
    {
        var fixture = Fixture((name, request) => Task.FromResult(
            name == "TelegramGateway.PrimaryApi"
                ? JsonResponse(HttpStatusCode.OK, AssistantResult())
                : request.RequestUri!.AbsolutePath.EndsWith("/sendMessage", StringComparison.Ordinal)
                    ? JsonResponse(HttpStatusCode.BadRequest, new { ok = false })
                    : TelegramSuccess()));

        var complete = await fixture.Worker.ProcessUpdatesAsync([MessageUpdate(1318)], CancellationToken.None);

        Assert.True(complete);
        Assert.Equal("1319", await File.ReadAllTextAsync(fixture.Settings.OffsetFilePath));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "RateLimited")]
    [InlineData(HttpStatusCode.RequestTimeout, "Timeout")]
    [InlineData(HttpStatusCode.BadGateway, "GatewayUnavailable")]
    [InlineData(HttpStatusCode.BadRequest, "TelegramError")]
    public async Task TelegramApiClient_classifies_delivery_status(HttpStatusCode status, string expectedCode)
    {
        var client = new TelegramApiClient(
            new NamedHttpClientFactory((_, _) => Task.FromResult(JsonResponse(status, new { ok = false }))),
            Options.Create(Settings()));

        var result = await client.SendMessageAsync(1, "text", null, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedCode, result.ErrorCode);
        Assert.DoesNotContain("text", result.RedactedError ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TelegramApiClient_omits_null_optional_send_message_fields()
    {
        string? body = null;
        var client = new TelegramApiClient(
            new NamedHttpClientFactory(async (_, request) =>
            {
                body = await request.Content!.ReadAsStringAsync();
                return TelegramSuccess();
            }),
            Options.Create(Settings()));

        var result = await client.SendMessageAsync(1, "text", null, null, CancellationToken.None);

        Assert.True(result.Succeeded);
        using var json = System.Text.Json.JsonDocument.Parse(body!);
        Assert.False(json.RootElement.TryGetProperty("parse_mode", out _));
        Assert.False(json.RootElement.TryGetProperty("reply_markup", out _));
    }

    [Fact]
    public async Task TelegramApiClient_plain_text_fallback_omits_optional_fields()
    {
        var bodies = new List<string>();
        var client = new TelegramApiClient(
            new NamedHttpClientFactory(async (_, request) =>
            {
                bodies.Add(await request.Content!.ReadAsStringAsync());
                return bodies.Count == 1
                    ? JsonResponse(HttpStatusCode.BadRequest, new { ok = false })
                    : TelegramSuccess();
            }),
            Options.Create(Settings()));

        var result = await client.SendMessageAsync(1, "text", "MarkdownV2", null, CancellationToken.None);

        Assert.True(result.Succeeded);
        using var fallback = System.Text.Json.JsonDocument.Parse(bodies[1]);
        Assert.False(fallback.RootElement.TryGetProperty("parse_mode", out _));
        Assert.False(fallback.RootElement.TryGetProperty("reply_markup", out _));
    }

    [Fact]
    public async Task GatewayIdempotencyStore_persists_only_confirmed_sends()
    {
        var settings = Settings();
        var store = new GatewayIdempotencyStore(Options.Create(settings), NullLogger<GatewayIdempotencyStore>.Instance);
        await store.SetAsync("update:1:part:1", new TelegramGatewayOperationResult(true, "10"), CancellationToken.None);

        var reloaded = new GatewayIdempotencyStore(Options.Create(settings), NullLogger<GatewayIdempotencyStore>.Instance);
        Assert.True(reloaded.TryGet("update:1:part:1", out _));
        await Assert.ThrowsAsync<ArgumentException>(() => reloaded.SetAsync(
            "update:1:part:2",
            new TelegramGatewayOperationResult(false, ErrorCode: "Timeout"),
            CancellationToken.None));
    }

    private static GatewayFixture Fixture(
        Func<string, HttpRequestMessage, Task<HttpResponseMessage>> responder)
    {
        var settings = Settings();
        var options = Options.Create(settings);
        var factory = new NamedHttpClientFactory(responder);
        var telegram = new TelegramApiClient(factory, options);
        var primary = new PrimaryApiClient(factory, options);
        var store = new GatewayIdempotencyStore(options, NullLogger<GatewayIdempotencyStore>.Instance);
        var worker = new TelegramGatewayPollingWorker(
            telegram,
            primary,
            store,
            options,
            NullLogger<TelegramGatewayPollingWorker>.Instance);
        return new GatewayFixture(worker, primary, settings);
    }

    private static TelegramGatewaySettings Settings(bool enabled = false)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"financial-copilot-130-{Guid.NewGuid():N}");
        return new TelegramGatewaySettings
        {
            Enabled = enabled,
            BotToken = "test-bot-token",
            PrimaryApiBaseUrl = "https://api.example.test",
            PrimaryApiKey = "test-primary-key",
            OffsetFilePath = Path.Combine(directory, "offset.txt"),
            IdempotencyFilePath = Path.Combine(directory, "idempotency.json")
        };
    }

    private static TelegramAssistantUpdateRequest Request(long updateId) =>
        new(
            updateId,
            TelegramAssistantUpdateKind.Message,
            9001,
            8001,
            7,
            41,
            null,
            null,
            "پرسش فارسی",
            "fa-IR",
            DateTimeOffset.FromUnixTimeSeconds(1_700_000_000),
            $"telegram:{updateId}");

    private static TelegramGatewayUpdate MessageUpdate(long updateId) =>
        new(
            updateId,
            new TelegramGatewayMessage(
                41,
                7,
                new TelegramGatewayUser(9001, "linked", "fa-IR"),
                new TelegramGatewayChat(8001, "private"),
                1_700_000_000,
                "  پرسش فارسی  "));

    private static TelegramAssistantResult AssistantResult(
        TelegramAssistantResultStatus status = TelegramAssistantResultStatus.Accepted,
        IReadOnlyList<TelegramAssistantRenderedMessage>? messages = null) =>
        new(
            status,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            messages ?? [new TelegramAssistantRenderedMessage(1, 1, "پاسخ")],
            "telegram:test");

    private static HttpResponseMessage TelegramSuccess() =>
        JsonResponse(HttpStatusCode.OK, new { ok = true, result = new { message_id = 100L } });

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, object value) =>
        new(status) { Content = JsonContent.Create(value) };

    private static async Task<string> ReadTextAsync(HttpRequestMessage request)
    {
        var payload = await request.Content!.ReadFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>();
        return payload!["text"].GetString()!;
    }

    private sealed record GatewayFixture(
        TelegramGatewayPollingWorker Worker,
        PrimaryApiClient PrimaryApi,
        TelegramGatewaySettings Settings);

    private sealed class NamedHttpClientFactory(
        Func<string, HttpRequestMessage, Task<HttpResponseMessage>> responder) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(new StubHandler(request => responder(name, request)), disposeHandler: true);
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responder(request);
    }
}
