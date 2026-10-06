using System.Net.Http.Headers;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Infrastructure.AI.ModelProviders;

namespace FinancialCopilot.IntegrationTests;

public sealed class SemanticProviderStructuredOutputContractTests
{
    private static readonly Guid TenantId = Guid.Parse("8c9be50e-01e9-428c-8510-fb88cd739003");

    [SkippableFact]
    public async Task RealOpenAiProvider_ReturnsValidatedSemanticProposalsForBoundedCorpus()
    {
        Skip.If(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY")),
            "Set OPENAI_API_KEY to run the provider-backed contract test.");
        Skip.If(!string.Equals(Environment.GetEnvironmentVariable("RUN_PROVIDER_BACKED_SMOKE"), "1", StringComparison.Ordinal),
            "Set RUN_PROVIDER_BACKED_SMOKE=1 to opt into billable provider-backed calls.");

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? "https://api.openai.com/v1/")
        };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", Environment.GetEnvironmentVariable("OPENAI_API_KEY"));

        var model = Environment.GetEnvironmentVariable("SEMANTIC_PROVIDER_MODEL") ?? "gpt-5.6-luna";
        var transport = new OpenAiHostedAiModelTransport(httpClient);
        var client = new ConfiguredHostedAiModelClient(
            new AiModelProviderDescriptor(
                "OpenAI", model, AiProviderHostingMode.Hosted,
                AiModelCapability.ChatCompletion | AiModelCapability.StructuredOutput,
                Enabled: true, Priority: 1),
            transport,
            TimeProvider.System);
        var execution = new AiModelExecutionService(
            new CapabilityBasedAiModelProviderResolver([client]),
            new JsonStructuredOutputValidator(),
            new NoOpTelemetrySink(),
            TimeProvider.System);
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var provider = new LlmQueryInterpretationProposalProvider(
            execution,
            registry,
            new SemanticRoutingOptions(SemanticInterpretationMaxOutputTokens: 384));

        var cases = new[]
        {
            ("Foolad hot products sales value", "product_sales_value"),
            ("Foolad hot products sales trend", "product_sales_trend"),
            ("Foolad product revenue composition", "product_revenue_mix"),
            ("P/E Foolad", "symbol_metric_lookup")
        };

        foreach (var (query, expectedCapability) in cases)
        {
            var proposal = await provider.ProposeAsync(
                query,
                TenantId,
                $"provider-contract-{expectedCapability}",
                CancellationToken.None);

            Assert.NotNull(proposal);
            Assert.Contains(expectedCapability, proposal!.CapabilityCodes);
        }
    }

    private sealed class NoOpTelemetrySink : IAiExecutionTelemetrySink
    {
        public Task RecordAttemptAsync(AiExecutionUsageFacts facts, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
