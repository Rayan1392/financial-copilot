using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Application.AI.ReportDiscussion;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Infrastructure.Conversations.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Feature 162 — Report Discussion through the real V2 endpoint, workflow, billing and conversation persistence, with
/// Feature 154's seeded balance sheets and quarterly income statements. The discussion model is scripted.
/// </summary>
public sealed class ReportDiscussion162EndpointTests : IClassFixture<ReportDiscussion162ApiFactory>
{
    private readonly ReportDiscussion162ApiFactory _factory;

    public ReportDiscussion162EndpointTests(ReportDiscussion162ApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
        factory.Model.Reset();
    }

    private HttpClient Client()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);
        return client;
    }

    private static async Task<JsonElement> Post(HttpClient client, string message, Guid? conversationId = null)
    {
        object body = conversationId is null ? new { message } : new { message, conversationId };
        using var response = await client.PostAsJsonAsync("/api/ai/v1/query", body, CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static string Intent(JsonElement root) => root.GetProperty("intent").GetString() ?? string.Empty;
    private static string Text(JsonElement root) => root.GetProperty("textAnswer").GetString() ?? string.Empty;
    private static decimal Credits(JsonElement root) => root.GetProperty("usage").GetProperty("creditsCharged").GetDecimal();
    private static string Outcome(JsonElement root) => root.GetProperty("outcome").GetString() ?? string.Empty;

    private ReportDiscussionMarker? PersistedMarker(Guid assistantMessageId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ConversationDbContext>();
        var json = db.Messages.AsNoTracking().Single(message => message.Id == assistantMessageId).AssistantPayloadJson;
        return JsonSerializer.Deserialize<AssistantMessagePayload>(json!)!.ReportDiscussion;
    }

    private static ReportDiscussionModelReply EchoCalculation(ReportDiscussionPrompt prompt, string labelPrefix)
    {
        var calculation = prompt.Data.Calculations.First(item => item.Label.StartsWith(labelPrefix, StringComparison.Ordinal));
        return new ReportDiscussionModelReply(ReportDiscussionDisposition.Answered,
            $"{calculation.Label} در {calculation.Period} برابر {calculation.Value.ToString("0.##", CultureInfo.InvariantCulture)} است.",
            [calculation.EvidenceId]);
    }

    [Fact] // AC-1, AC-2, AC-7, AC-14: answered without the normal route; charged one credit; marker persisted.
    public async Task QuestionAfterBalanceSheet_IsAnsweredByReportDiscussion()
    {
        using var client = Client();
        var report = await Post(client, "ترازنامه شگل");
        var conversation = report.GetProperty("conversationId").GetGuid();
        Assert.Equal(0, _factory.Model.Calls);

        // The seeded شگل statement has no current-asset/current-liability totals, so it has no current ratio;
        // the debt ratio (total liabilities ÷ total assets) is computable.
        _factory.Model.Reply = prompt => EchoCalculation(prompt, "نسبت بدهی");
        var answer = await Post(client, "نسبت بدهی این شرکت چطور است؟", conversation);

        Assert.Equal("ReportDiscussion", Intent(answer));
        Assert.Equal("Answered", Outcome(answer));
        Assert.StartsWith("نسبت بدهی", Text(answer), StringComparison.Ordinal);
        Assert.Equal(2m, Credits(answer)); // change price-all-report-discussions-at-two-credits
        Assert.Equal(1, _factory.Model.Calls);
        var prompt = _factory.Model.Prompts.Single();
        Assert.Equal(ReportFamily.BalanceSheet, prompt.Data.Family);
        Assert.DoesNotContain(prompt.Data.Calculations, item => item.Label == "نسبت جاری");
        Assert.Equal("شگل", prompt.Data.CompanySymbol);
        Assert.True(prompt.EstimatedInputTokens <= 5000);
        Assert.Contains("<question>\nنسبت بدهی این شرکت چطور است؟\n</question>", prompt.UserContent, StringComparison.Ordinal);

        var marker = PersistedMarker(answer.GetProperty("assistantMessageId").GetGuid());
        Assert.Equal(report.GetProperty("assistantMessageId").GetGuid(), marker!.SourceAssistantMessageId);
        Assert.Equal("Answered", marker.Disposition);
        Assert.Equal(0, answer.GetProperty("suggestedActions").GetArrayLength());
    }

    [Fact] // AC-1 for the quarterly income statement.
    public async Task QuestionAfterIncomeStatement_UsesQuarterlyFacts()
    {
        using var client = Client();
        var report = await Post(client, "صورت سود و زیان وبملت");
        var conversation = report.GetProperty("conversationId").GetGuid();
        _factory.Model.Reply = prompt => new ReportDiscussionModelReply(ReportDiscussionDisposition.Answered,
            $"{prompt.Data.Facts[0].Label} در {prompt.Data.Facts[0].Period} برابر {prompt.Data.Facts[0].Value.ToString("0.##", CultureInfo.InvariantCulture)} است.",
            [prompt.Data.Facts[0].EvidenceId]);

        var answer = await Post(client, "این عدد را برایم توضیح بده", conversation);

        Assert.Equal("ReportDiscussion", Intent(answer));
        Assert.Equal(ReportFamily.QuarterlyIncomeStatement, _factory.Model.Prompts.Single().Data.Family);
        Assert.Equal(2m, Credits(answer));
    }

    [Fact] // AC-3, AC-7: missing item is a zero-credit message and the report stays active.
    public async Task MissingInReport_IsFreeAndKeepsTheReport()
    {
        using var client = Client();
        var conversation = (await Post(client, "ترازنامه شگل")).GetProperty("conversationId").GetGuid();
        _factory.Model.Reply = _ => new ReportDiscussionModelReply(ReportDiscussionDisposition.MissingInReport, "", []);

        var missing = await Post(client, "هزینه تحقیق و توسعه چقدر بود؟", conversation);
        Assert.Equal("ReportDiscussion", Intent(missing));
        Assert.Equal("NoData", Outcome(missing));
        Assert.Equal(0m, Credits(missing));
        Assert.Contains("وجود ندارد", Text(missing), StringComparison.Ordinal);

        _factory.Model.Reply = prompt => EchoCalculation(prompt, "نسبت بدهی");
        var next = await Post(client, "نسبت بدهی چقدر است؟", conversation);
        Assert.Equal("ReportDiscussion", Intent(next));
        Assert.Equal(2, _factory.Model.Calls);
    }

    [Fact] // FR-7, AC-7: an answer with a number that is not in the report is replaced and free.
    public async Task UngroundedAnswer_IsReplacedAndFree()
    {
        using var client = Client();
        var conversation = (await Post(client, "ترازنامه شگل")).GetProperty("conversationId").GetGuid();
        _factory.Model.Reply = _ => new ReportDiscussionModelReply(ReportDiscussionDisposition.Answered, "نسبت جاری ۹۸۷٫۶۵ برابر است.", []);

        var answer = await Post(client, "نسبت جاری؟", conversation);
        Assert.Equal("NoData", Outcome(answer));
        Assert.Equal(0m, Credits(answer));
        Assert.DoesNotContain("۹۸۷", Text(answer), StringComparison.Ordinal);
    }

    [Fact] // AC-4: another company hands back; the normal route answers; the new report becomes active.
    public async Task DifferentCompany_HandsBackAndNewReportBecomesActive()
    {
        using var client = Client();
        var conversation = (await Post(client, "ترازنامه شگل")).GetProperty("conversationId").GetGuid();
        _factory.Model.Reply = _ => new ReportDiscussionModelReply(ReportDiscussionDisposition.NeedsDifferentData, "", []);

        var handedBack = await Post(client, "ترازنامه وبملت", conversation);
        Assert.NotEqual("ReportDiscussion", Intent(handedBack));
        var balance = handedBack.GetProperty("financialStatementTableResult").GetProperty("balanceSheetReport");
        Assert.Equal("وبملت", balance.GetProperty("company").GetProperty("symbol").GetString());
        Assert.Equal("NeedsDifferentData", PersistedMarker(handedBack.GetProperty("assistantMessageId").GetGuid())!.Disposition);

        _factory.Model.Reply = prompt => EchoCalculation(prompt, "نسبت مالکانه");
        var next = await Post(client, "نسبت مالکانه چقدر است؟", conversation);
        Assert.Equal("ReportDiscussion", Intent(next));
        Assert.Equal("وبملت", _factory.Model.Prompts.Last().Data.CompanySymbol);
        Assert.DoesNotContain("شگل", _factory.Model.Prompts.Last().UserContent.Split("<recent_messages>")[0], StringComparison.Ordinal);
    }

    [Fact] // AC-5: an unrelated question hands back and deactivates the report.
    public async Task OutsideReport_DeactivatesTheReport()
    {
        using var client = Client();
        var conversation = (await Post(client, "ترازنامه شگل")).GetProperty("conversationId").GetGuid();
        _factory.Model.Reply = _ => new ReportDiscussionModelReply(ReportDiscussionDisposition.OutsideReport, "", []);

        var outside = await Post(client, "سلام", conversation);
        Assert.NotEqual("ReportDiscussion", Intent(outside));
        Assert.Equal(1, _factory.Model.Calls);

        await Post(client, "نسبت جاری چقدر است؟", conversation);
        Assert.Equal(1, _factory.Model.Calls);
    }

    [Fact] // AC-12: provider failure is free, observable as temporarily unavailable, and keeps the report.
    public async Task ProviderFailure_IsFree()
    {
        using var client = Client();
        var conversation = (await Post(client, "ترازنامه شگل")).GetProperty("conversationId").GetGuid();
        _factory.Model.Reply = _ => throw new ReportDiscussionModelUnavailableException("timeout");

        var failed = await Post(client, "نسبت جاری؟", conversation);
        Assert.Equal("ReportDiscussion", Intent(failed));
        Assert.Equal("TemporarilyUnavailable", Outcome(failed));
        Assert.Equal(0m, Credits(failed));
        Assert.Equal("ProviderUnavailable", PersistedMarker(failed.GetProperty("assistantMessageId").GetGuid())!.Disposition);
    }

    [Fact] // AC-10: the message reader never returns another actor's conversation.
    public async Task MessageReader_IsScopedToTheOwner()
    {
        using var client = Client();
        var report = await Post(client, "ترازنامه شگل");
        var conversation = report.GetProperty("conversationId").GetGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ConversationDbContext>();
        var owner = db.Conversations.AsNoTracking().Single(row => row.Id == conversation);
        var reader = scope.ServiceProvider.GetRequiredService<IReportDiscussionMessageReader>();

        Assert.Equal(2, (await reader.ListRecentAsync(conversation, owner.TenantId, owner.ActorId, 20, CancellationToken.None)).Count);
        Assert.Empty(await reader.ListRecentAsync(conversation, owner.TenantId, Guid.NewGuid(), 20, CancellationToken.None));
        Assert.Empty(await reader.ListRecentAsync(conversation, Guid.NewGuid(), owner.ActorId, 20, CancellationToken.None));
    }
}

public sealed class ReportDiscussion162ApiFactory : BalanceSheetReport154ApiFactory
{
    public ScriptedReportDiscussionModel Model { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        // These tests cover the plain (pre-analysis) Report Discussion path, so grounded analysis is pinned off: a local
        // appsettings.json with GroundedAnalysis:Enabled=true must not change what they assert. The analysis path has
        // its own fixtures (GroundedAnalysis162EndpointTests, AnalysisRecencyWindow159EndpointTests).
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReportDiscussion:Enabled"] = "true",
                ["GroundedAnalysis:Enabled"] = "false"
            }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IReportDiscussionModel>();
            services.AddSingleton<IReportDiscussionModel>(Model);
        });
    }
}

public sealed class ScriptedReportDiscussionModel : IReportDiscussionModel
{
    private readonly object _gate = new();
    private readonly List<ReportDiscussionPrompt> _prompts = [];

    public Func<ReportDiscussionPrompt, ReportDiscussionModelReply> Reply { get; set; } = Unexpected;

    public int Calls { get { lock (_gate) return _prompts.Count; } }

    public IReadOnlyList<ReportDiscussionPrompt> Prompts { get { lock (_gate) return _prompts.ToArray(); } }

    public void Reset()
    {
        lock (_gate) _prompts.Clear();
        Reply = Unexpected;
    }

    public Task<ReportDiscussionModelReply> AnswerAsync(ReportDiscussionPrompt prompt, Guid tenantId, string correlationId, CancellationToken cancellationToken)
    {
        lock (_gate) _prompts.Add(prompt);
        return Task.FromResult(Reply(prompt));
    }

    private static ReportDiscussionModelReply Unexpected(ReportDiscussionPrompt prompt) =>
        throw new InvalidOperationException("The discussion model was not expected to be called.");
}
