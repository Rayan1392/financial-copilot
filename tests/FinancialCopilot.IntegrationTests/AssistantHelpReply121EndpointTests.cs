using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Infrastructure.Conversations.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialCopilot.IntegrationTests;

/// <summary>Feature 121 change: the greeting and help reply on the V2 path. In-memory databases only.</summary>
public sealed class AssistantHelpReply121EndpointTests : IClassFixture<AssistantHelpReply121ApiFactory>
{
    private const string PersianReply =
        "سلام! من ساپیو، دستیار هوشمند بازار هستم و می‌توانم فروش ماهانه، گزارش فصلی، ترازنامه و ترکیب فروش شرکت‌های بورسی را نشان بدهم. برای نمونه بپرسید:\n" +
        "• روند فروش ماهانه کگل را نشان بده\n" +
        "• گزارش فصلی کگل را نشان بده\n" +
        "• ترازنامه کگل را نشان بده\n" +
        "• ترکیب فروش محصولات کگل";

    private readonly AssistantHelpReply121ApiFactory _factory;

    public AssistantHelpReply121EndpointTests(AssistantHelpReply121ApiFactory factory)
    {
        _factory = factory;
        factory.EnsureBillingSeeded();
    }

    [Fact]
    public async Task AiQuery_Greeting_ReturnsIntroductionAnsweredFreeWithFourSamples()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync("/api/ai/v1/query", new { message = "سلام" });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.Equal("Answered", root.GetProperty("outcome").GetString());
        Assert.Equal("assistant_help", root.GetProperty("outcomeReasonCode").GetString());
        Assert.Equal("assistant_help", root.GetProperty("semanticCapabilityCode").GetString());
        Assert.Equal(PersianReply, root.GetProperty("textAnswer").GetString());
        Assert.Equal(0m, root.GetProperty("usage").GetProperty("creditsCharged").GetDecimal());
        Assert.Equal(
            ["assistant_help:monthly_sales", "assistant_help:quarterly_report", "assistant_help:balance_sheet", "assistant_help:product_mix"],
            root.GetProperty("suggestedActions").EnumerateArray().Select(action => action.GetProperty("id").GetString()!));
        Assert.Equal(0, AssistantHelpReply121ProposalProvider.Calls);

        using var scope = _factory.Services.CreateScope();
        var conversations = scope.ServiceProvider.GetRequiredService<ConversationDbContext>();
        var conversationId = root.GetProperty("conversationId").GetGuid();
        var payloadJson = await conversations.Messages.AsNoTracking()
            .Where(message => message.ConversationId == conversationId && message.AssistantPayloadJson != null)
            .Select(message => message.AssistantPayloadJson!)
            .SingleAsync();
        using var payload = JsonDocument.Parse(payloadJson);
        Assert.Equal((int)DialogueOutcome.Answered, payload.RootElement.GetProperty("Outcome").GetInt32());
        Assert.Equal("assistant_help", payload.RootElement.GetProperty("OutcomeReasonCode").GetString());
        Assert.Equal("assistant_help", payload.RootElement.GetProperty("SemanticCapabilityCode").GetString());
    }
}

public sealed class AssistantHelpReply121ApiFactory : AiFacadeApiFactory
{
    protected override bool ForceV1Orchestration => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AiOrchestration:Mode"] = "MicrosoftAgentFrameworkV2"
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IQueryInterpretationProposalProvider>();
            services.AddSingleton<IQueryInterpretationProposalProvider, AssistantHelpReply121ProposalProvider>();
        });
    }
}

internal sealed class AssistantHelpReply121ProposalProvider : IQueryInterpretationProposalProvider
{
    private static int _calls;

    public static int Calls => _calls;

    public Task<QueryInterpretationProposal?> ProposeAsync(
        string originalText,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult<QueryInterpretationProposal?>(new QueryInterpretationProposal([], [], null, 0.9m, []));
    }
}
