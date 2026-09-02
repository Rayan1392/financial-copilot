using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.Telegram;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialCopilot.IntegrationTests;

public sealed class TelegramAssistant130IntegrationTests : IClassFixture<TelegramAssistant130ApiFactory>
{
    private readonly TelegramAssistant130ApiFactory factory;

    public TelegramAssistant130IntegrationTests(TelegramAssistant130ApiFactory factory) =>
        this.factory = factory;

    [Fact]
    public async Task Linked_update_uses_existing_orchestrator_conversation_rendering_and_duplicate_replay()
    {
        const long telegramUserId = 1_300_001;
        using var web = factory.CreateClient();
        await RegisterAsync(web);
        var startParameter = await CreateStartParameterAsync(web);
        using var gateway = CreateGatewayClient();
        using var confirm = await gateway.PostAsJsonAsync(
            "/api/v1/telegram/link/confirm",
            new
            {
                startParameter,
                telegramUserId,
                telegramChatId = telegramUserId,
                username = "feature130",
                telegramUpdateId = 13_000L
            });
        confirm.EnsureSuccessStatusCode();

        var request = new
        {
            telegramUpdateId = 13_001L,
            kind = TelegramAssistantUpdateKind.Message,
            telegramUserId,
            telegramChatId = telegramUserId,
            messageThreadId = 7,
            telegramMessageId = 81L,
            text = "P/E شغدیر چقدر است؟",
            locale = "fa-IR",
            receivedAtUtc = DateTimeOffset.UtcNow,
            correlationId = "telegram:13001"
        };

        using var firstResponse = await gateway.PostAsJsonAsync("/api/v1/telegram/assistant/updates", request);
        using var replayResponse = await gateway.PostAsJsonAsync("/api/v1/telegram/assistant/updates", request);
        var first = await firstResponse.Content.ReadFromJsonAsync<TelegramAssistantResult>();
        var replay = await replayResponse.Content.ReadFromJsonAsync<TelegramAssistantResult>();
        var orchestrator = factory.Services.GetRequiredService<RecordingTelegramOrchestrationService>();

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        Assert.Equal(TelegramAssistantResultStatus.Accepted, first!.Status);
        Assert.Equal(TelegramAssistantResultStatus.Replayed, replay!.Status);
        Assert.Equal(first.ConversationId, replay.ConversationId);
        Assert.Contains("پاسخ یکپارچه", Assert.Single(first.Messages).Text, StringComparison.Ordinal);
        Assert.Equal(1, orchestrator.CallCount);
        Assert.Equal("telegram:1300001", orchestrator.LastRequest!.ExternalUserId);
        Assert.NotEqual(Guid.Empty, orchestrator.LastRequest.ActorId);
        Assert.NotNull(orchestrator.LastRequest.ConversationId);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        Assert.Single(await db.TelegramConversationBindings.ToListAsync());
        Assert.Single(await db.TelegramProcessedUpdates.Where(row => row.TelegramUpdateId == 13_001L).ToListAsync());
    }

    [Fact]
    public async Task Assistant_endpoint_rejects_missing_invalid_web_and_path_disallowed_credentials()
    {
        var request = new
        {
            telegramUpdateId = 13_101L,
            kind = TelegramAssistantUpdateKind.Message,
            telegramUserId = 1_301_001L,
            telegramChatId = 1_301_001L,
            text = "پرسش",
            correlationId = "telegram:13101"
        };

        using var anonymous = factory.CreateClient();
        using var missing = await anonymous.PostAsJsonAsync("/api/v1/telegram/assistant/updates", request);

        using var invalidClient = factory.CreateClient();
        invalidClient.DefaultRequestHeaders.Add("X-Api-Key", "invalid-feature-130-key");
        using var invalid = await invalidClient.PostAsJsonAsync("/api/v1/telegram/assistant/updates", request);

        using var web = factory.CreateClient();
        await RegisterAsync(web);
        using var forbidden = await web.PostAsJsonAsync("/api/v1/telegram/assistant/updates", request);

        using var gateway = CreateGatewayClient();
        using var disallowed = await gateway.GetAsync("/api/v1/telegram/entitlement/me");

        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, disallowed.StatusCode);
    }

    private HttpClient CreateGatewayClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TelegramAssistant130ApiFactory.GatewayApiKey);
        return client;
    }

    private static async Task RegisterAsync(HttpClient client)
    {
        using var register = await client.PostAsJsonAsync(
            "/api/auth/v1/register",
            new { email = $"feature-130-{Guid.NewGuid():N}@example.test", password = "StrongPassword!123" });
        using var session = await ReadJsonAsync(register);
        register.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            session.RootElement.GetProperty("accessToken").GetString());
    }

    private static async Task<string> CreateStartParameterAsync(HttpClient client)
    {
        using var response = await client.PostAsync("/api/v1/telegram/link-token", null);
        using var json = await ReadJsonAsync(response);
        response.EnsureSuccessStatusCode();
        return new Uri(json.RootElement.GetProperty("deepLink").GetString()!)
            .Query.Split("start=", 2)[1];
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }
}

public sealed class TelegramAssistant130ApiFactory : OwnedIdentityApiFactory
{
    public const string GatewayApiKey = "feature-130-integration-key";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(GatewayApiKey)));
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:ApiKeys:Clients:2:ClientId"] = "13000000-0000-0000-0000-000000000001",
                ["Authentication:ApiKeys:Clients:2:TenantId"] = "11111111-1111-1111-1111-111111111111",
                ["Authentication:ApiKeys:Clients:2:Name"] = "Feature 130 TelegramGateway",
                ["Authentication:ApiKeys:Clients:2:KeySha256"] = keyHash,
                ["Authentication:ApiKeys:Clients:2:IsActive"] = "true",
                ["Authentication:ApiKeys:Clients:2:AllowedPathPrefixes:0"] = "/api/v1/telegram/assistant/updates",
                ["Authentication:ApiKeys:Clients:2:AllowedPathPrefixes:1"] = "/api/v1/telegram/link/confirm"
            });
        });
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiQueryOrchestrationService>();
            services.AddSingleton<RecordingTelegramOrchestrationService>();
            services.AddSingleton<IAiQueryOrchestrationService>(provider =>
                provider.GetRequiredService<RecordingTelegramOrchestrationService>());
        });
    }
}

public sealed class RecordingTelegramOrchestrationService : IAiQueryOrchestrationService
{
    public int CallCount { get; private set; }
    public AiQueryRequest? LastRequest { get; private set; }

    public Task<AiQueryResponse> ExecuteAsync(AiQueryRequest request, CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequest = request;
        return Task.FromResult(new AiQueryResponse(
            request.ConversationId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            DetectedIntent.SymbolLookup,
            null,
            null,
            null,
            null,
            null,
            "پاسخ یکپارچه Feature 130",
            false,
            null,
            new UsageAccountingResult("AiQuery.StockAnalysis", "Completed", 1, 9, "v1", false)));
    }
}
