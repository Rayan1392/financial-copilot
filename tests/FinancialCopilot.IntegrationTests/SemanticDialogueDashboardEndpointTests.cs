using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FinancialCopilot.Application.AI.Evaluation;
using FinancialCopilot.Application.AI.Orchestration;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialCopilot.IntegrationTests;

public sealed class SemanticDialogueDashboardEndpointTests : IClassFixture<BackfillCyclicalWavesApiFactory>
{
    private readonly BackfillCyclicalWavesApiFactory factory;

    public SemanticDialogueDashboardEndpointTests(BackfillCyclicalWavesApiFactory factory) => this.factory = factory;

    [Fact]
    public void EveryEnabledCapability_HasExactlyOneProductionExecutor()
    {
        using var scope = factory.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IConversationalCapabilityRegistry>();
        var executors = scope.ServiceProvider.GetServices<IConversationalCapabilityExecutor>()
            .Select(item => item.CapabilityCode)
            .ToArray();

        Assert.Equal(registry.GetEnabled().Select(item => item.Code).OrderBy(item => item, StringComparer.Ordinal),
            executors.OrderBy(item => item, StringComparer.Ordinal));
        Assert.Equal(executors.Length, executors.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task MetricsDashboard_IsDataAdminProtectedAndReturnsCapabilityAggregates()
    {
        var correlationId = $"dashboard-{Guid.NewGuid():N}";
        factory.Services.GetRequiredService<ISemanticDialogueEventSink>().Record(new SemanticDialogueEvent(
            SemanticEventName.ExecutionCompleted, correlationId, "monthly_activity_trend", 1,
            "none", "web-ai", DateTimeOffset.UtcNow, "Executed"));

        using var regular = factory.CreateClient();
        regular.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", factory.CreateWebAppToken(includeTenant: true));
        using var forbidden = await regular.GetAsync("/api/v1/admin/ai/semantic-dialogue/metrics");

        using var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", factory.CreateDataAdminToken());
        using var allowed = await admin.GetAsync("/api/v1/admin/ai/semantic-dialogue/metrics");
        await using var stream = await allowed.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Contains(document.RootElement.GetProperty("metrics").EnumerateArray(), item =>
            item.GetProperty("capabilityCode").GetString() == "monthly_activity_trend" &&
            item.GetProperty("completed").GetInt32() >= 1);
        Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty("alerts").ValueKind);
    }
}
