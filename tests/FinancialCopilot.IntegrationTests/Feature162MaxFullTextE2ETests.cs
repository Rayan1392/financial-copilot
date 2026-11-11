using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Feature 162 change «max-plan-full-analysis-text» through POST /api/ai/v1/query in the production shape (NoavaranAmin,
/// comprehensive analysis from CyclicalWaves v2): a Max user reads the whole analysis text, everyone else the preview.
/// Own fixture instance: the tests change the test user's plan.
/// </summary>
public sealed class Feature162MaxFullTextE2ETests : IClassFixture<Feature162MappedNoavaranAminChatApiFactory>
{
    private const string Question = "تحلیل جامع فولاد";
    private const string Title = "فولاد- تحلیل نمونهٔ کامل آبان";
    private const string FirstParagraph = "بند اول تحلیل: فروش شرکت در ماه اخیر نسبت به ماه قبل رشد کرده و حاشیهٔ سود ناخالص ثابت مانده است.";
    private const string LastParagraph = "بند پایانی تحلیل: این جمله فقط در متن کامل دیده می‌شود و هرگز در پیش‌نمایش نمی‌آید.";
    private static readonly string WholeText = string.Join("\n",
        FirstParagraph,
        "بند دوم تحلیل: نسبت قیمت به فروش شرکت حدود 0.88 است و نسبت به میانگین بلندمدت پایین‌تر قرار دارد.",
        "بند سوم تحلیل: موجودی کالا و حساب‌های دریافتنی در فصل اخیر بیشتر شده و جریان نقد عملیاتی مثبت مانده است.",
        LastParagraph);

    private readonly Feature162MappedNoavaranAminChatApiFactory _factory;

    public Feature162MaxFullTextE2ETests(Feature162MappedNoavaranAminChatApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
        SeedLatestFoladAnalysis(factory);
        SetPlan(factory, plan: null, effectiveTo: null);
        SetFullTextRow(factory, enabled: true);
        factory.ResetRecorders();
    }

    [Fact]
    public async Task MaxUser_ReadsTheWholeText_WithTheSectionLink_AndNoReadMoreLine()
    {
        SetPlan(_factory, "Max", DateTimeOffset.UtcNow.AddDays(10));

        var text = await AskAsUserAsync();

        Assert.StartsWith($"### {Title}", text);
        Assert.Contains(FirstParagraph, text);
        Assert.Contains(LastParagraph, text);
        Assert.DoesNotContain(ComprehensiveAnalysisCardComposer.ReadMoreFa, text);
        Assert.EndsWith($"[{ComprehensiveAnalysisCardComposer.AllAnalysesLinkTextFa}]({ComprehensiveAnalysisPresentationOptions.DefaultTahlilAppAnalysesUrl})", text);
    }

    [Fact]
    public async Task FreeUser_ReadsThePreview()
    {
        var text = await AskAsUserAsync();

        AssertPreview(text);
    }

    [Fact]
    public async Task ExpiredMax_ReadsThePreview()
    {
        SetPlan(_factory, "Max", DateTimeOffset.UtcNow.AddDays(-1));

        AssertPreview(await AskAsUserAsync());
    }

    [Fact]
    public async Task MaxUser_WithTheRowDisabled_ReadsThePreview()
    {
        SetPlan(_factory, "Max", DateTimeOffset.UtcNow.AddDays(10));
        SetFullTextRow(_factory, enabled: false);

        AssertPreview(await AskAsUserAsync());
    }

    [Fact]
    public async Task ApiClient_ReadsThePreview()
    {
        SetPlan(_factory, "Max", DateTimeOffset.UtcNow.AddDays(10));

        var root = await Feature148Chat.AskAsync(_factory, Question);

        AssertPreview(Feature148Chat.Text(root));
    }

    private static void AssertPreview(string text)
    {
        Assert.StartsWith($"### {Title}", text);
        Assert.DoesNotContain(LastParagraph, text);
        Assert.Contains(ComprehensiveAnalysisCardComposer.ReadMoreFa, text);
        Assert.EndsWith($"[{ComprehensiveAnalysisCardComposer.LinkTextFa}]({ComprehensiveAnalysisPresentationOptions.DefaultTahlilAppAnalysesUrl})", text);
    }

    private async Task<string> AskAsUserAsync()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateWebAppToken(includeTenant: true));
        using var response = await client.PostAsJsonAsync("/api/ai/v1/query", new { message = Question }, CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");
        using var json = JsonDocument.Parse(body);
        Assert.Equal("comprehensive_analysis", json.RootElement.GetProperty("semanticCapabilityCode").GetString());
        return json.RootElement.GetProperty("textAnswer").GetString() ?? string.Empty;
    }

    private static void SeedLatestFoladAnalysis(Feature162MappedNoavaranAminChatApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
        if (db.CyclicalWavesV2Analyses.Any(row => row.Id == 162001))
            return;
        var now = DateTimeOffset.UtcNow;
        db.CyclicalWavesV2Analyses.Add(new CyclicalWavesV2AnalysisRow
        {
            Id = 162001, Title = Title, Summary = $"<p>{WholeText}</p>", PlainTextSummary = WholeText,
            PublishedAt = now.AddMinutes(-5), ContentHash = "162001".PadLeft(64, '0'),
            FirstSyncedAt = now, LastSyncedAt = now, UpdatedAt = now
        });
        db.SaveChanges();
    }

    private static void SetPlan(Feature162MappedNoavaranAminChatApiFactory factory, string? plan, DateTimeOffset? effectiveTo)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var account = db.CustomerAccounts.Single(row => row.Id == AiFacadeApiFactory.IndividualAccountId);
        account.SubscriptionPlanCode = plan;
        account.SubscriptionEffectiveTo = effectiveTo;
        db.SaveChanges();
    }

    private static void SetFullTextRow(Feature162MappedNoavaranAminChatApiFactory factory, bool enabled)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var row = db.PlanCapabilities.Single(item =>
            item.PlanCode == "Max" && item.CapabilityCode == ComprehensiveAnalysisFullText.CapabilityCode);
        row.IsEnabled = enabled;
        db.SaveChanges();
    }
}
