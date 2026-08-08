using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Insights;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialCopilot.IntegrationTests;

public sealed class V1PersonalizedInsightSemanticRouteTests : IClassFixture<V1PersonalizedInsightApiFactory>
{
    private readonly V1PersonalizedInsightApiFactory factory;
    public V1PersonalizedInsightSemanticRouteTests(V1PersonalizedInsightApiFactory factory)
    {
        this.factory = factory;
        factory.EnsureBillingSeeded();
        factory.UseCase.Reset();
    }

    [Fact]
    public async Task V1InsightContext_ExecutesSemanticRouteAndBillsExactlyOnce()
    {
        await AssertRouteAsync(factory);
    }

    internal static async Task AssertRouteAsync(PersonalizedInsightApiFactoryBase factory)
    {
        var insightId = Guid.NewGuid();
        var usageBefore = factory.ReadUsageEntries().Count;
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);
        using var response = await client.PostAsJsonAsync("/api/ai/v1/query",
            new { message = "explain this alert", context = new { insightEventId = insightId } });
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("PersonalizedInsightExplanation", root.GetProperty("intent").GetString());
        Assert.Equal($"verified insight {insightId:D}", root.GetProperty("textAnswer").GetString());
        Assert.Equal("personalized_insight_explanation", root.GetProperty("semanticCapabilityCode").GetString());
        Assert.Equal(1, root.GetProperty("semanticRegistryVersion").GetInt32());
        Assert.Equal(usageBefore + 1, factory.ReadUsageEntries().Count);
        Assert.Equal(1, factory.UseCase.CallCount);
        Assert.Equal(insightId, factory.UseCase.LastQuery!.InsightEventId);
        Assert.Equal(AuthenticationApiFactory.ClientId, factory.UseCase.LastQuery.Actor.ActorId);
    }
}

public sealed class V2PersonalizedInsightSemanticRouteTests : IClassFixture<V2PersonalizedInsightApiFactory>
{
    private readonly V2PersonalizedInsightApiFactory factory;
    public V2PersonalizedInsightSemanticRouteTests(V2PersonalizedInsightApiFactory factory)
    {
        this.factory = factory;
        factory.EnsureBillingSeeded();
        factory.UseCase.Reset();
    }

    [Fact]
    public Task V2InsightContext_ExecutesSemanticRouteAndBillsExactlyOnce() =>
        V1PersonalizedInsightSemanticRouteTests.AssertRouteAsync(factory);
}

public abstract class PersonalizedInsightApiFactoryBase : AiFacadeApiFactory
{
    public RecordingExplainInsightUseCase UseCase { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IExplainInsightUseCase>();
            services.AddSingleton<IExplainInsightUseCase>(UseCase);
        });
    }
}

public sealed class V1PersonalizedInsightApiFactory : PersonalizedInsightApiFactoryBase
{
}

public sealed class V2PersonalizedInsightApiFactory : PersonalizedInsightApiFactoryBase
{
    protected override bool ForceV1Orchestration => false;
}

public sealed class RecordingExplainInsightUseCase : IExplainInsightUseCase
{
    private int callCount;
    public int CallCount => callCount;
    public ExplainInsightQuery? LastQuery { get; private set; }

    public Task<string> ExecuteAsync(ExplainInsightQuery query, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref callCount);
        LastQuery = query;
        return Task.FromResult($"verified insight {query.InsightEventId:D}");
    }

    public void Reset()
    {
        Interlocked.Exchange(ref callCount, 0);
        LastQuery = null;
    }
}
