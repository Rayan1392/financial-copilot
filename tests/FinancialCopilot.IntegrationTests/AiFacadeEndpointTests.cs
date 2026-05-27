using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Infrastructure.Conversations.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialCopilot.IntegrationTests;

public sealed class AiFacadeEndpointTests : IClassFixture<AiFacadeApiFactory>
{
    private readonly AiFacadeApiFactory _factory;

    public AiFacadeEndpointTests(AiFacadeApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AiQuery_WithScannerIntent_ReturnsConversationAndScannerPlan()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "P/E below 6" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(Guid.Empty, document.RootElement.GetProperty("conversationId").GetGuid());
        Assert.Equal("Scanner", document.RootElement.GetProperty("intent").GetString());
        Assert.False(document.RootElement.GetProperty("clarificationRequired").GetBoolean());
        var scannerPlan = document.RootElement.GetProperty("scannerPlan");
        Assert.NotEqual(Guid.Empty, scannerPlan.GetProperty("planId").GetGuid());
        Assert.Equal(1, scannerPlan.GetProperty("conditionCount").GetInt32());
    }

    [Fact]
    public async Task AiQuery_WithMissingMessage_ReturnsBadRequest()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AiQuery_CreatesConversation_ThenConversationAppearsInHistory()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var queryResponse = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "P/E below 6" },
            CancellationToken.None);
        var queryDocument = await ReadJsonAsync(queryResponse);
        var conversationId = queryDocument.RootElement.GetProperty("conversationId").GetGuid();

        using var listResponse = await client.GetAsync("/api/ai/v1/conversations", CancellationToken.None);
        var listDocument = await ReadJsonAsync(listResponse);
        var conversations = listDocument.RootElement.EnumerateArray().ToList();

        Assert.Equal(HttpStatusCode.OK, queryResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Contains(conversations, c => c.GetProperty("conversationId").GetGuid() == conversationId);
    }

    [Fact]
    public async Task AiQuery_ContinuesExistingConversation_WhenConversationIdProvided()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var firstResponse = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "P/E below 6" },
            CancellationToken.None);
        var firstDocument = await ReadJsonAsync(firstResponse);
        var conversationId = firstDocument.RootElement.GetProperty("conversationId").GetGuid();

        using var secondResponse = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "P/E below 6", conversationId },
            CancellationToken.None);
        var secondDocument = await ReadJsonAsync(secondResponse);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal(conversationId, secondDocument.RootElement.GetProperty("conversationId").GetGuid());
    }

    [Fact]
    public async Task Messages_ReturnsUserAndAssistantMessagesForConversation()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var queryResponse = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "P/E below 6" },
            CancellationToken.None);
        var queryDocument = await ReadJsonAsync(queryResponse);
        var conversationId = queryDocument.RootElement.GetProperty("conversationId").GetGuid();

        using var messagesResponse = await client.GetAsync(
            $"/api/ai/v1/conversations/{conversationId}/messages",
            CancellationToken.None);
        var messagesDocument = await ReadJsonAsync(messagesResponse);
        var messages = messagesDocument.RootElement.GetProperty("messages").EnumerateArray().ToList();

        Assert.Equal(HttpStatusCode.OK, messagesResponse.StatusCode);
        Assert.Equal(2, messages.Count);
        Assert.Equal("User", messages[0].GetProperty("role").GetString());
        Assert.Equal("Assistant", messages[1].GetProperty("role").GetString());
        Assert.True(messages[1].GetProperty("hasScannerPlan").GetBoolean());
    }

    [Fact]
    public async Task Conversation_NotFoundForDifferentTenant_ReturnsNotFound()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.GetAsync(
            $"/api/ai/v1/conversations/{Guid.NewGuid()}",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AiQuery_UnknownTerm_SetsClarificationRequired()
    {
        using var client = _factory.CreateUnknownTermClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "supersecret_metric_xyz above 100" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(document.RootElement.GetProperty("clarificationRequired").GetBoolean());
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
    }
}

public sealed class AiFacadeApiFactory : AuthenticationApiFactory
{
    private readonly string _billingDatabaseName = $"ai-facade-billing-{Guid.NewGuid():N}";
    private readonly string _conversationDatabaseName = $"ai-facade-conversations-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<FinancialCopilot.Infrastructure.Billing.Persistence.BillingDbContext>();
            services.RemoveAll<DbContextOptions<FinancialCopilot.Infrastructure.Billing.Persistence.BillingDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<FinancialCopilot.Infrastructure.Billing.Persistence.BillingDbContext>>();
            services.AddDbContext<FinancialCopilot.Infrastructure.Billing.Persistence.BillingDbContext>(options =>
                options.UseInMemoryDatabase(_billingDatabaseName));

            services.RemoveAll<ConversationDbContext>();
            services.RemoveAll<DbContextOptions<ConversationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ConversationDbContext>>();
            services.AddDbContext<ConversationDbContext>(options =>
                options.UseInMemoryDatabase(_conversationDatabaseName));

            // Replace all registered AI model clients with a single scanner-aware fake.
            services.RemoveAll<IAiModelClient>();
            services.AddSingleton<IAiModelClient>(_ =>
                new ScannerAwareFakeAiModelClient(returnUnknownTerm: false));
        });
    }

    public HttpClient CreateUnknownTermClient()
    {
        var client = WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAiModelClient>();
                services.AddSingleton<IAiModelClient>(_ =>
                    new ScannerAwareFakeAiModelClient(returnUnknownTerm: true));
            });
        }).CreateClient();

        return client;
    }
}

// Returns structured JSON shaped for either intent detection or scanner parsing based on schema name.
internal sealed class ScannerAwareFakeAiModelClient(bool returnUnknownTerm) : IAiModelClient
{
    public AiModelProviderDescriptor Descriptor { get; } = new(
        "ScannerAwareFake",
        "fake-v1",
        AiProviderHostingMode.Fake,
        AiModelCapability.ChatCompletion | AiModelCapability.StructuredOutput |
        AiModelCapability.UsageReporting | AiModelCapability.HealthCheck,
        Enabled: true,
        Priority: 1);

    public Task<AiModelResult> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken)
    {
        var json = BuildJson(request.StructuredOutput?.SchemaName);
        return Task.FromResult(new AiModelResult(
            Text: null,
            StructuredJson: json,
            ToolCalls: [],
            Usage: new AiExecutionUsageFacts(
                request.CorrelationId,
                Descriptor.ProviderKey,
                Descriptor.ModelKey,
                AiExecutionStatus.Completed,
                TimeSpan.Zero,
                AttemptNumber: 0,
                InputTokens: 10,
                OutputTokens: 4)));
    }

    public IAsyncEnumerable<AiStreamingChunk> StreamAsync(
        AiModelRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<AiEmbeddingResult> CreateEmbeddingsAsync(
        AiEmbeddingRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<AiProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AiProviderHealthResult(
            Descriptor.ProviderKey,
            Descriptor.ModelKey,
            Available: true,
            DateTimeOffset.UtcNow,
            "OK"));

    private string BuildJson(string? schemaName) =>
        schemaName switch
        {
            "IntentDetectionOutput" =>
                "{\"intent\":\"Scanner\",\"confidence\":0.95}",
            "ScannerParseOutput" when returnUnknownTerm =>
                """{"detectedLanguage":"en","conditions":[{"userTerminology":"supersecret_metric_xyz","language":"en","operator":"GreaterThan","threshold":100,"periodHint":null,"growthComparison":null,"inferredDefault":false,"inferredReason":null}],"requestedColumns":[],"clarificationRequired":false,"clarificationMessage":null}""",
            "ScannerParseOutput" =>
                """{"detectedLanguage":"en","conditions":[{"userTerminology":"P/E","language":"en","operator":"LessThan","threshold":6.0,"periodHint":null,"growthComparison":null,"inferredDefault":false,"inferredReason":null}],"requestedColumns":[],"clarificationRequired":false,"clarificationMessage":null}""",
            _ =>
                "{}"
        };
}
