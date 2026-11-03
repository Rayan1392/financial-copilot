using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.FinancialData.Semantics.Intent;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Workflow;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace FinancialCopilot.UnitTests;

public sealed class Feature141DeveloperUatAuthorizationTests
{
    [Fact]
    public void UserIdentityRequiresDevelopmentOptInAllowlistAndMatchingServerActor()
    {
        var userId = Guid.NewGuid();
        var config = Configuration(enabled: true, userIds: [userId.ToString()]);
        var development = Environment("Development");
        var authorized = new AiQueryRequest("financial question", Guid.NewGuid(), userId, "c",
            UserId: userId, ActorType: ActorType.User, AuthenticationMode: AuthenticationMode.WebAppUser);
        Assert.True(Feature141DeveloperUatAuthorizationPolicy.IsAuthorized(config, development, authorized));

        var mismatchedActor = authorized with { ActorId = Guid.NewGuid() };
        Assert.False(Feature141DeveloperUatAuthorizationPolicy.IsAuthorized(config, development, mismatchedActor));
        Assert.False(Feature141DeveloperUatAuthorizationPolicy.IsAuthorized(config, Environment("Production"), authorized));
        Assert.False(Feature141DeveloperUatAuthorizationPolicy.IsAuthorized(Configuration(enabled: false, userIds: [userId.ToString()]), development, authorized));
        Assert.False(Feature141DeveloperUatAuthorizationPolicy.IsAuthorized(Configuration(enabled: true), development, authorized));
    }

    [Fact]
    public void ApiClientMustMatchConfiguredApiClientAllowlistAndActorMode()
    {
        var clientId = Guid.NewGuid();
        var config = Configuration(enabled: true, clientIds: [clientId.ToString()]);
        var request = new AiQueryRequest("financial question", Guid.NewGuid(), clientId, "c",
            ApiClientId: clientId, ActorType: ActorType.ApiClient, AuthenticationMode: AuthenticationMode.ApiClient);

        Assert.True(Feature141DeveloperUatAuthorizationPolicy.IsAuthorized(config, Environment("Development"), request));
        Assert.False(Feature141DeveloperUatAuthorizationPolicy.IsAuthorized(config, Environment("Development"),
            request with { AuthenticationMode = AuthenticationMode.WebAppUser }));
        Assert.False(Feature141DeveloperUatAuthorizationPolicy.IsAuthorized(config, Environment("Development"),
            request with { ApiClientId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task CanonicalExtractorCanShareChatCorrelationForExistingUsageAccounting()
    {
        const string response = """
            {"contractVersion":2,"measures":[],"operation":{"kind":"retrieve"},"comparison":{"mode":"none"},"period":{"kind":"latestAvailable"},"entityScope":{"kind":"namedCompanies","companyLabels":["فولاد"]},"products":[],"categories":[],"filters":null,"aggregation":null,"ranking":null,"output":{"kind":"scalar","requestedFields":[]},"constraints":[],"ambiguity":{"requiresClarification":false,"questions":[],"unresolvedPoints":[]}}
            """;
        const string correlationId = "chat-correlation-141";
        var model = new CapturingModelExecutionService(response);
        var extractor = new LlmCanonicalSemanticIntentExtractor(model, new CanonicalSemanticIntentValidator(),
            tenantId: Guid.NewGuid(), correlationId: correlationId);

        var result = await extractor.ExtractAsync("Ø³ÙˆØ§Ù„", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(correlationId, model.Request!.CorrelationId);
    }

    private static IConfiguration Configuration(bool enabled = false, string[]? userIds = null, string[]? clientIds = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Feature141:DeveloperUatEnabled"] = enabled.ToString(),
        };
        if (userIds is not null)
            for (var index = 0; index < userIds.Length; index++) values[$"Feature141:DeveloperUat:AllowedUserIds:{index}"] = userIds[index];
        if (clientIds is not null)
            for (var index = 0; index < clientIds.Length; index++) values[$"Feature141:DeveloperUat:AllowedApiClientIds:{index}"] = clientIds[index];
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static IHostEnvironment Environment(string name) => new StubHostEnvironment(name);

    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Feature141Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class CapturingModelExecutionService(string response) : IAiModelExecutionService
    {
        public AiModelRequest? Request { get; private set; }

        public Task<AiModelResult> ExecuteAsync(AiModelSelectionRequest selection, AiModelRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new AiModelResult(null, response, [],
                new AiExecutionUsageFacts(request.CorrelationId, "test", "test-model", AiExecutionStatus.Completed,
                    TimeSpan.FromMilliseconds(1), 1, 10, 10)));
        }
    }
}
