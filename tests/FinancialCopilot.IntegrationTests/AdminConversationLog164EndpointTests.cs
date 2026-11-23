using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using FinancialCopilot.API.Controllers;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Administration;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using FinancialCopilot.Infrastructure.Conversations.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Feature 164: authorization, validation and response shape of the admin conversation-log endpoints. The SQL needs
/// PostgreSQL, so the query service is a recording stub here; the real queries are covered by
/// <see cref="AdminConversationLog164PostgreSqlTests"/>.
/// </summary>
public sealed class AdminConversationLog164EndpointTests(AdminConversationLogApiFactory factory)
    : IClassFixture<AdminConversationLogApiFactory>
{
    private const string Route = "/api/v1/admin/ai-conversation-logs";
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Endpoints_require_authentication_and_the_admin_users_read_permission()
    {
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Route)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"{Route}/{Guid.NewGuid()}")).StatusCode);

        using var withoutPermission = Client(FinancialCopilotPermissions.AdminSecurityAuditRead);
        Assert.Equal(HttpStatusCode.Forbidden, (await withoutPermission.GetAsync(Route)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await withoutPermission.GetAsync($"{Route}/{Guid.NewGuid()}")).StatusCode);
    }

    [Theory]
    [InlineData("?pageSize=10", "pageSize")]
    [InlineData("?page=0", "page")]
    [InlineData("?status=bogus", "status")]
    [InlineData("?status=3", "status")]
    [InlineData("?fromUtc=2026-11-02T00:00:00Z&toUtc=2026-11-01T00:00:00Z", "fromUtc")]
    public async Task Invalid_list_queries_return_validation_problems(string queryString, string field)
    {
        factory.Queries.Reset();
        using var client = Client(FinancialCopilotPermissions.AdminUsersRead);

        using var response = await client.GetAsync(Route + queryString);
        using var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty(field, out _), json.RootElement.ToString());
        Assert.Null(factory.Queries.LastQuery);
    }

    [Fact]
    public async Task An_overlong_search_is_rejected_and_a_blank_one_means_no_search()
    {
        factory.Queries.Reset();
        using var client = Client(FinancialCopilotPermissions.AdminUsersRead);

        using var overlong = await client.GetAsync($"{Route}?q={new string('x', AdminConversationLogQueryRules.MaximumTextLength + 1)}");
        using var overlongJson = await ReadJsonAsync(overlong);
        Assert.Equal(HttpStatusCode.BadRequest, overlong.StatusCode);
        Assert.True(overlongJson.RootElement.GetProperty("errors").TryGetProperty("q", out _));
        Assert.Null(factory.Queries.LastQuery);

        // ASP.NET binds an empty or whitespace-only query value as absent, so it applies no text filter.
        using var blank = await client.GetAsync($"{Route}?q=%20%20&user=");
        Assert.Equal(HttpStatusCode.OK, blank.StatusCode);
        var query = Assert.IsType<AdminConversationLogQuery>(factory.Queries.LastQuery);
        Assert.Null(query.Text);
        Assert.Null(query.User);
    }

    [Fact]
    public async Task List_passes_trimmed_filters_and_returns_strings_for_status_and_channel()
    {
        var turnId = Guid.NewGuid();
        factory.Queries.Reset();
        factory.Queries.Page = new AdminConversationLogPage(
            [
                new AdminConversationLogItem(turnId, Guid.NewGuid(), "عنوان",
                    new AdminConversationLogUser(Guid.NewGuid(), "سارا", "sara@example.test", "•••2222", HasUserAccount: true),
                    "سؤال", "پاسخ", DateTimeOffset.Parse("2026-11-01T08:00:00Z"), DateTimeOffset.Parse("2026-11-01T08:00:00Z"),
                    AdminConversationLogStatus.Failed, AdminConversationLogChannel.Telegram)
            ],
            2, 50, 51, 2);
        using var client = Client(FinancialCopilotPermissions.AdminUsersRead);

        using var response = await client.GetAsync(
            $"{Route}?page=2&pageSize=50&q=%20سود%20&user=%20sara%20&status=failed&fromUtc=2026-10-01T00:00:00Z&toUtc=2026-11-01T00:00:00Z");
        using var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var query = Assert.IsType<AdminConversationLogQuery>(factory.Queries.LastQuery);
        Assert.Equal(new AdminConversationLogQuery(2, 50, "سود", "sara", DateTimeOffset.Parse("2026-10-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-11-01T00:00:00Z"), AdminConversationLogStatus.Failed), query);
        Assert.Equal(AuthenticationApiFactory.TenantId, factory.Queries.LastTenantId);

        var root = json.RootElement;
        Assert.Equal(51, root.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, root.GetProperty("totalPages").GetInt32());
        var item = Assert.Single(root.GetProperty("items").EnumerateArray());
        Assert.Equal(turnId, item.GetProperty("turnId").GetGuid());
        Assert.Equal("Failed", item.GetProperty("status").GetString());
        Assert.Equal("Telegram", item.GetProperty("channel").GetString());
        Assert.Equal("•••2222", item.GetProperty("user").GetProperty("maskedMobile").GetString());
        Assert.True(item.GetProperty("user").GetProperty("hasUserAccount").GetBoolean());
    }

    [Fact]
    public async Task A_too_broad_user_search_returns_a_coded_problem()
    {
        factory.Queries.Reset();
        factory.Queries.ThrowTooBroad = true;
        using var client = Client(FinancialCopilotPermissions.AdminUsersRead);

        using var response = await client.GetAsync($"{Route}?user=a");
        using var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("user_search_too_broad", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Detail_returns_404_for_an_unknown_turn()
    {
        factory.Queries.Reset();
        using var client = Client(FinancialCopilotPermissions.AdminUsersRead);

        using var response = await client.GetAsync($"{Route}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Detail_maps_the_answer_like_the_chat_and_hides_cost_plan_and_memory()
    {
        var turnId = Guid.NewGuid();
        var payload = new AssistantMessagePayload(
            2, default, false, null, "متن پاسخ", null, null, null, null, null,
            new UsageAccountingResult("AiQuery.Standard", "ProviderFailed", 0m, 9m, "v7", false,
                "openai", "gpt-test", 100, 20, 120, 0.42m, "agent"),
            [new FinancialCopilot.Application.Memory.MemoryUseDisclosure(default, default, "secret memory")],
            Outcome: DialogueOutcome.Failed,
            OutcomeReasonCode: "provider_failed");
        factory.Queries.Reset();
        factory.Queries.Turn = new AdminConversationLogTurn(
            turnId, Guid.NewGuid(), null, AdminConversationLogChannel.Web,
            new AdminConversationLogUser(Guid.NewGuid(), null, null, null, HasUserAccount: false),
            "سؤال کامل", DateTimeOffset.Parse("2026-11-01T08:00:00Z"), AdminConversationLogStatus.Failed,
            [
                new AdminConversationLogAnswer(Guid.NewGuid(), "متن پاسخ", DateTimeOffset.Parse("2026-11-01T08:00:00Z"),
                    AdminConversationLogStatus.Failed, "provider_failed",
                    new AdminConversationLogUsage("openai", "gpt-test", 100, 20, 120, 0m, "ProviderFailed", "agent"),
                    payload)
            ]);
        using var client = Client(FinancialCopilotPermissions.AdminUsersRead);

        using var response = await client.GetAsync($"{Route}/{turnId}");
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(turnId, factory.Queries.LastTurnId);
        var root = json.RootElement;
        Assert.Equal("Failed", root.GetProperty("status").GetString());
        Assert.Equal("Web", root.GetProperty("channel").GetString());
        Assert.False(root.GetProperty("user").GetProperty("hasUserAccount").GetBoolean());
        var answer = Assert.Single(root.GetProperty("answers").EnumerateArray());
        Assert.Equal("gpt-test", answer.GetProperty("usage").GetProperty("modelName").GetString());
        Assert.Equal(120, answer.GetProperty("usage").GetProperty("totalTokens").GetInt32());

        var expected = AiFacadeHttpMapper.MapAssistantContent(payload)! with { ScannerPlan = null, Usage = null, MemoryDisclosures = null };
        // Compared as JSON trees: the API writes Persian text unescaped, the default serializer escapes it.
        var expectedJson = JsonNode.Parse(JsonSerializer.Serialize(expected, WebJson));
        var actualJson = JsonNode.Parse(answer.GetProperty("assistantContent").GetRawText());
        Assert.True(JsonNode.DeepEquals(expectedJson, actualJson), actualJson?.ToJsonString());
        Assert.Equal("متن پاسخ", answer.GetProperty("assistantContent").GetProperty("textAnswer").GetString());
        Assert.DoesNotContain("estimatedCost", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pricingPolicyVersion", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret memory", body, StringComparison.Ordinal);
    }

    private HttpClient Client(params string[] permissions)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", factory.CreateWebAppToken(includeTenant: true, permissions: permissions));
        return client;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }
}

public sealed class AdminConversationLogApiFactory : AuthenticationApiFactory
{
    private readonly string databaseSuffix = Guid.NewGuid().ToString("N");

    public RecordingAdminConversationLogQueries Queries { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<AuthDbContext>();
            services.RemoveAll<DbContextOptions<AuthDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AuthDbContext>>();
            services.AddDbContext<AuthDbContext>(options => options.UseInMemoryDatabase($"logs-auth-{databaseSuffix}"));
            services.RemoveAll<BillingDbContext>();
            services.RemoveAll<DbContextOptions<BillingDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<BillingDbContext>>();
            services.AddDbContext<BillingDbContext>(options => options.UseInMemoryDatabase($"logs-billing-{databaseSuffix}"));
            services.RemoveAll<ConversationDbContext>();
            services.RemoveAll<DbContextOptions<ConversationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ConversationDbContext>>();
            services.AddDbContext<ConversationDbContext>(options => options.UseInMemoryDatabase($"logs-conversation-{databaseSuffix}"));
            services.RemoveAll<IAdminConversationLogQueries>();
            services.AddSingleton<IAdminConversationLogQueries>(Queries);
        });
    }
}

public sealed class RecordingAdminConversationLogQueries : IAdminConversationLogQueries
{
    public AdminConversationLogPage Page { get; set; } = new([], 1, 20, 0, 0);
    public AdminConversationLogTurn? Turn { get; set; }
    public bool ThrowTooBroad { get; set; }
    public AdminConversationLogQuery? LastQuery { get; private set; }
    public Guid? LastTenantId { get; private set; }
    public Guid? LastTurnId { get; private set; }

    public void Reset()
    {
        Page = new([], 1, 20, 0, 0);
        Turn = null;
        ThrowTooBroad = false;
        LastQuery = null;
        LastTenantId = null;
        LastTurnId = null;
    }

    public Task<AdminConversationLogPage> GetPageAsync(Guid tenantId, AdminConversationLogQuery query, CancellationToken cancellationToken)
    {
        LastTenantId = tenantId;
        LastQuery = query;
        if (ThrowTooBroad) throw new AdminConversationLogUserSearchTooBroadException();
        return Task.FromResult(Page);
    }

    public Task<AdminConversationLogTurn?> GetTurnAsync(
        Guid tenantId, Guid administratorId, string administratorType, string correlationId, Guid turnId,
        CancellationToken cancellationToken)
    {
        LastTenantId = tenantId;
        LastTurnId = turnId;
        return Task.FromResult(Turn?.TurnId == turnId ? Turn : null);
    }
}
