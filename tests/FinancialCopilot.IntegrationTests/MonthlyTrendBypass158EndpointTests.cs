using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Conversations.Persistence;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// BUG-019: a monthly-trend question with a semantic frame takes the legacy branch of the V2 workflow.
/// These tests run the whole answer path over HTTP. In-memory databases only; questions and rows are invented.
/// </summary>
public sealed class MonthlyTrendBypass158EndpointTests : IClassFixture<MonthlyTrendBypass158ApiFactory>
{
    private readonly MonthlyTrendBypass158ApiFactory _factory;

    public MonthlyTrendBypass158EndpointTests(MonthlyTrendBypass158ApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
    }

    [Fact]
    public async Task NoRouteCompany_GetsNotCoveredReply_RecordedAsNoData_FreeOfCharge()
    {
        var root = await AskAsync("روند فروش ماهانه ساختا");

        Assert.Equal(
            "پوشش گزارش ماهانهٔ فروش برای شرکت‌های ساختمانی در منبع دادهٔ فعلی فراهم نیست، بنابراین برای «ساختا» نمودار فروش ماهانه نداریم.",
            root.GetProperty("textAnswer").GetString());
        await AssertRecordedAsync(root, "NoData", DialogueOutcome.NoData, "monthly_report_not_covered");
    }

    [Fact]
    public async Task NonEligibleCompany_GetsOutsideScopeSentence_RecordedAsNoData_FreeOfCharge()
    {
        var root = await AskAsync("روند فروش ماهانه حقتق");

        Assert.Equal("این نماد در فهرست شرکت‌های تحت پوشش گزارش ماهانه نیست.", root.GetProperty("textAnswer").GetString());
        await AssertRecordedAsync(root, "NoData", DialogueOutcome.NoData, "data_stale_or_ineligible");
    }

    [Fact]
    public async Task TwoEligibleRowsWithOneSymbol_AskToDisambiguate_FreeOfCharge()
    {
        var root = await AskAsync("روند فروش ماهانه دوتا");

        Assert.Equal("نام نماد یا شرکت به‌طور قطعی مشخص نشد. لطفاً نام نماد دقیق را وارد کنید.", root.GetProperty("textAnswer").GetString());
        Assert.True(root.GetProperty("clarificationRequired").GetBoolean());
        await AssertRecordedAsync(root, "DisambiguationNeeded", DialogueOutcome.DisambiguationNeeded, "entity_ambiguous");
    }

    [Theory]
    [InlineData("روند فروش ماهانه بیداده", "بیداده")]
    [InlineData("روند فروش ماهانه ناشناس", "ناشناس")]
    public async Task HasRouteOrUnknown_KeepsLegacySentence_RecordedAsNoData_FreeOfCharge(string question, string symbol)
    {
        var root = await AskAsync(question);

        Assert.Equal($"اطلاعات روند فروش ماهانه برای نماد «{symbol}» در پایگاه داده یافت نشد.", root.GetProperty("textAnswer").GetString());
        await AssertRecordedAsync(root, "NoData", DialogueOutcome.NoData, "supported_but_no_rows");
    }

    [Fact]
    public async Task NoRouteCompany_EnglishQuestion_GetsEnglishReply()
    {
        var root = await AskAsync("monthly sales trend buildx");

        Assert.Equal(
            "Monthly sales report coverage for construction companies is not available from the current data source, so there is no monthly sales chart for this symbol.",
            root.GetProperty("textAnswer").GetString());
        await AssertRecordedAsync(root, "NoData", DialogueOutcome.NoData, "monthly_report_not_covered");
    }

    [Theory]
    [InlineData("نمودار فروش کگل و شپنا")] // Matches the monthly-trend rule, so it takes the bypass today.
    [InlineData("روند فروش ماهانه کگل و شپنا")] // Guard: reads as a product trend, so it already takes the semantic route.
    [InlineData("روند فروش ماهانه کگل/شپنا")] // Guard: same.
    public async Task TwoCompanies_GetTheClarification_FreeOfCharge(string question)
    {
        var root = await AskAsync(question);

        Assert.Equal("این پرسش فقط برای یک نماد قابل پاسخ است. لطفاً نماد موردنظرتان را بنویسید.", root.GetProperty("textAnswer").GetString());
        Assert.True(root.GetProperty("clarificationRequired").GetBoolean());
        await AssertRecordedAsync(root, "ClarificationNeeded", DialogueOutcome.ClarificationNeeded, "multiple_companies_not_supported");
    }

    [Fact]
    public async Task QuestionWithData_SuccessPathIsUnchanged_AndCoverageReaderIsNeverCalled()
    {
        MonthlyTrendBypass158CoverageSpy.Reset();

        var root = await AskAsync("روند فروش ماهانه دارا");

        Assert.Equal(0, MonthlyTrendBypass158CoverageSpy.Calls);
        Assert.Equal(SuccessGolden.TextAnswer, root.GetProperty("textAnswer").GetString()!.ReplaceLineEndings("\n"));
        Assert.Equal("Answered", root.GetProperty("outcome").GetString());
        Assert.Equal("none", root.GetProperty("outcomeReasonCode").GetString());
        Assert.Equal("MonthlyActivityTrend", root.GetProperty("intent").GetString());
        Assert.Equal("monthly_activity_trend", root.GetProperty("semanticCapabilityCode").GetString());
        Assert.False(root.GetProperty("clarificationRequired").GetBoolean());
        Assert.Equal(SuccessGolden.CreditsCharged, root.GetProperty("usage").GetProperty("creditsCharged").GetDecimal());
        Assert.Equal(SuccessGolden.TrendHash, Hash(root.GetProperty("monthlyActivityTrendResult").GetRawText()));
        Assert.Equal(SuccessGolden.ActionsHash, Hash(root.GetProperty("suggestedActions").GetRawText()));
        Assert.Equal(SuccessGolden.GaugeKind, root.GetProperty("psVisualizationResult").ValueKind);
    }

    private async Task<JsonElement> AskAsync(string question)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);
        using var response = await client.PostAsJsonAsync("/api/ai/v1/query", new { message = question });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private async Task AssertRecordedAsync(JsonElement root, string outcome, DialogueOutcome persisted, string reason)
    {
        Assert.Equal(outcome, root.GetProperty("outcome").GetString());
        Assert.Equal(reason, root.GetProperty("outcomeReasonCode").GetString());
        Assert.Equal(0m, root.GetProperty("usage").GetProperty("creditsCharged").GetDecimal());

        using var scope = _factory.Services.CreateScope();
        var conversations = scope.ServiceProvider.GetRequiredService<ConversationDbContext>();
        var conversationId = root.GetProperty("conversationId").GetGuid();
        var payloadJson = await conversations.Messages.AsNoTracking()
            .Where(message => message.ConversationId == conversationId && message.AssistantPayloadJson != null)
            .Select(message => message.AssistantPayloadJson!)
            .SingleAsync();
        using var payload = JsonDocument.Parse(payloadJson);
        Assert.Equal((int)persisted, payload.RootElement.GetProperty("Outcome").GetInt32());
        Assert.Equal(reason, payload.RootElement.GetProperty("OutcomeReasonCode").GetString());
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>Captured from the workflow before the BUG-019 fix; the owner requires the success path not to change.</summary>
    private static class SuccessGolden
    {
        public const string TextAnswer =
            "### روند فروش ماهانه - شرکت دارا (دارا)\nآخرین دوره گزارش: 1405/04 | واحد: میلیارد تومان\n\n**خلاصه آخرین ماه:** فروش 100 میلیارد تومان\n**تغییر نسبت به ماه قبل:** دادهٔ ماه قبل موجود نیست.\n**میانگین ۱۲ ماهه:** 100 میلیارد تومان (+0.0٪ نسبت به میانگین)\n\n**نکات تحلیلی:**\n- داده سال قبل برای مقایسه یکسان‌ماهه موجود نیست.\n- فروش این ماه نسبت به میانگین ۱۲ ماهه +0.0٪ بالاتر است (میانگین با 1 دوره موجود محاسبه شده است).\n\n*داده‌های ناقص: 12 دوره موجود نیست.*\n\n*منبع: نوآوران امین | محاسبه: 1405/07/09*";
        public const decimal CreditsCharged = 1m;
        public const string TrendHash = "B674C8309073F3781022C4FE779FA90905451CEE9EBF19CD42650F7F43CB569D";
        public const string ActionsHash = "4F53CDA18C2BAA0C0354BB5F9A3ECBE5ED12AB4D8E11BA873C2F11161202B945";
        public const JsonValueKind GaugeKind = JsonValueKind.Null;
    }
}

public sealed class MonthlyTrendBypass158RollbackEndpointTests : IClassFixture<MonthlyTrendBypass158RollbackApiFactory>
{
    private readonly MonthlyTrendBypass158RollbackApiFactory _factory;

    public MonthlyTrendBypass158RollbackEndpointTests(MonthlyTrendBypass158RollbackApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
    }

    [Fact]
    public async Task RollbackMode_KeepsTodaysTextAndStatus_AndCoverageReaderIsNeverCalled()
    {
        MonthlyTrendBypass158CoverageSpy.Reset();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync("/api/ai/v1/query", new { message = "روند فروش ماهانه ساختا" });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.Equal(0, MonthlyTrendBypass158CoverageSpy.Calls);
        Assert.Equal("اطلاعات روند فروش ماهانه برای نماد «ساختا» در پایگاه داده یافت نشد.", root.GetProperty("textAnswer").GetString());
        Assert.Equal(RollbackGolden.Outcome, root.GetProperty("outcome").GetString());
        Assert.Equal(RollbackGolden.Reason, root.GetProperty("outcomeReasonCode").GetString());
        Assert.Equal(0m, root.GetProperty("usage").GetProperty("creditsCharged").GetDecimal());
    }

    /// <summary>Captured from the workflow before the BUG-019 fix.</summary>
    private static class RollbackGolden
    {
        public const string Outcome = "NoData";
        public const string Reason = "supported_but_no_rows";
    }
}

public class MonthlyTrendBypass158ApiFactory : AiFacadeApiFactory
{
    private readonly object _seedLock = new();
    private bool _seeded;

    protected override bool ForceV1Orchestration => false;

    protected virtual string SemanticDefaultMode => "SemanticPrimary";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AiOrchestration:Mode"] = "MicrosoftAgentFrameworkV2",
            ["SemanticRouting:DefaultMode"] = SemanticDefaultMode
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiModelClient>();
            services.AddSingleton<IAiModelClient>(new V2MonthlySalesRoutingFakeAiModelClient());
            services.RemoveAll<IQueryInterpretationProposalProvider>();
            services.AddSingleton<IQueryInterpretationProposalProvider, MonthlyTrendBypass158ProposalProvider>();
            services.AddScoped<NoavaranMonthlyReportCoverageReader>();
            services.RemoveAll<IMonthlyReportCoverageReader>();
            services.AddScoped<IMonthlyReportCoverageReader>(provider =>
                new MonthlyTrendBypass158CoverageSpy(provider.GetRequiredService<NoavaranMonthlyReportCoverageReader>()));
        });
    }

    public void EnsureSeeded()
    {
        EnsureBillingSeeded();
        if (_seeded) return;
        lock (_seedLock)
        {
            if (_seeded) return;
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
            db.Companies.AddRange(
                Company("ساختا", "1", 1_000_001, enTicker: "BUILDX"),
                Company("حقتق", "2", 1_000_003, precedencyRight: 1),
                Company("دوتا", "3", 1_000_000),
                Company("دوتا", "4", 1_000_000),
                Company("بیداده", "5", 1_000_000),
                Company("ناشناس", "6", null),
                Company("کگل", "7", 1_000_000),
                Company("شپنا", "8", 1_000_000),
                Company("دارا", "9", 1_000_000));
            db.CompanyMonthlyActivityTrendSnapshots.Add(new CompanyMonthlyActivityTrendSnapshotRow
            {
                Id = Guid.Parse("15800000-0000-0000-0000-000000000001"),
                ExternalCompanyId = "9",
                CompanySymbol = "دارا",
                CompanyName = "شرکت دارا",
                ReportYear = 1405,
                ReportMonth = 4,
                MonthlySalesAmount = 1_000_000m,
                CurrentMonthOutputType = 0,
                SourceProviderName = "NoavaranCurrentApi",
                CalculatedAtUtc = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero)
            });
            db.SaveChanges();
            _seeded = true;
        }
    }

    private static NormalizedCompanyRow Company(string symbol, string externalId, int? reportingType, int precedencyRight = 0, string? enTicker = null) => new()
    {
        Id = Guid.NewGuid(),
        ProviderName = "NoavaranCurrentApi",
        ExternalCompanyId = externalId,
        Name = "شرکت " + symbol + " " + externalId,
        TseSymbol = symbol,
        CompanySymbol = symbol,
        EnTicker = enTicker,
        PrecedencyRight = precedencyRight,
        MarketId = NoavaranCompanyScope.BourseMarketId,
        ReportingType = reportingType
    };
}

public sealed class MonthlyTrendBypass158RollbackApiFactory : MonthlyTrendBypass158ApiFactory
{
    protected override string SemanticDefaultMode => "Rollback";
}

internal sealed class MonthlyTrendBypass158ProposalProvider : IQueryInterpretationProposalProvider
{
    private static readonly string[] KnownSymbols = ["ساختا", "حقتق", "دوتا", "بیداده", "ناشناس", "کگل", "شپنا", "دارا", "buildx"];

    public Task<QueryInterpretationProposal?> ProposeAsync(
        string originalText,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var entities = KnownSymbols
            .Where(originalText.Contains)
            .Select(symbol => new SemanticEntityProposal(symbol, "company", null, 0.9m))
            .ToArray();
        return Task.FromResult<QueryInterpretationProposal?>(
            new QueryInterpretationProposal(["monthly_activity_trend"], [], null, 0.9m, ["test"], null, entities, []));
    }
}

internal sealed class MonthlyTrendBypass158CoverageSpy(IMonthlyReportCoverageReader inner) : IMonthlyReportCoverageReader
{
    private static int _calls;

    public static int Calls => Volatile.Read(ref _calls);

    public static void Reset() => Interlocked.Exchange(ref _calls, 0);

    public Task<MonthlyReportCoverage> ReadAsync(string symbol, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return inner.ReadAsync(symbol, cancellationToken);
    }
}
