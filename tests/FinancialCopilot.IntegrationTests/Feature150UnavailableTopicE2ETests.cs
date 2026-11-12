using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Feature 150 change «unavailable-capability-refusals-and-answer-correctness», through POST /api/ai/v1/query in the
/// production shape: MAF V2, NoavaranAmin, SemanticRouting default Shadow. Only the LLM is a fake (unscripted
/// questions get an empty proposal, so the deterministic interpreter decides).
/// </summary>
public sealed class Feature150UnavailableTopicE2ETests : IClassFixture<Feature148NoavaranAminShadowChatApiFactory>
{
    private const string UnitNote = "واحد: میلیون ریال";
    private readonly Feature148NoavaranAminShadowChatApiFactory _factory;

    public Feature150UnavailableTopicE2ETests(Feature148NoavaranAminShadowChatApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
        factory.ResetRecorders();
    }

    // R5 (BUG-007): فولاد has no NoavaranAmin P/S row, so the asked metric has no value.
    [Fact]
    public async Task MetricWithNoValue_IsNoData_NotCharged_WithTheDeterministicSentence()
    {
        var root = await Feature148Chat.AskAsync(_factory, "P/S فولاد چقدر است؟");

        Assert.Equal("symbol_metric_lookup", root.GetProperty("semanticCapabilityCode").GetString());
        Assert.Equal("NoData", root.GetProperty("outcome").GetString());
        Assert.Equal(0m, root.GetProperty("usage").GetProperty("creditsCharged").GetDecimal());
        var text = Feature148Chat.Text(root);
        Assert.Contains("مقدار قابل اتکایی", text);
        Assert.DoesNotContain(UnitNote, text);
    }

    // R0 in Shadow routing (the compose default): the refusal still runs.
    [Fact]
    public async Task TechnicalQuestion_InShadowMode_IsRefusedFree()
    {
        var root = await Feature148Chat.AskAsync(_factory, "تکنیکال فولاد");

        Assert.Equal("unavailable_topic", root.GetProperty("semanticCapabilityCode").GetString());
        Assert.Equal("Unsupported", root.GetProperty("outcome").GetString());
        Assert.Equal(0m, root.GetProperty("usage").GetProperty("creditsCharged").GetDecimal());
    }

    // Owner report 2026-11-11: an earlier price question in the same session must not turn the topic question into a
    // price lookup of the new symbol.
    [Fact]
    public async Task TechnicalQuestion_AfterAPriceQuestionInTheSameSession_IsStillRefused()
    {
        var conversationId = (await Feature148Chat.AskAsync(_factory, "قیمت فولاد چقدر است؟")).GetProperty("conversationId").GetGuid();

        var root = await Feature148Chat.AskAsync(_factory, "تکنیکال فولاد", conversationId);

        Assert.Equal("unavailable_topic", root.GetProperty("semanticCapabilityCode").GetString());
        Assert.Equal(0m, root.GetProperty("usage").GetProperty("creditsCharged").GetDecimal());
        Assert.DoesNotContain("آخرین قیمت نماد", Feature148Chat.Text(root));
    }

    // R4 (BUG-006) through the API.
    [Fact]
    public async Task PriceAnswer_HasNoMillionRialNote()
    {
        var root = await Feature148Chat.AskAsync(_factory, "قیمت فولاد چقدر است؟");

        Assert.NotEqual("unavailable_topic", root.GetProperty("semanticCapabilityCode").GetString());
        Assert.DoesNotContain(UnitNote, Feature148Chat.Text(root));
    }

    // R5 control: a value exists, so the answer is unchanged and charged.
    [Fact]
    public async Task MetricWithValue_IsAnswered_AndCharged()
    {
        var root = await Feature148Chat.AskAsync(_factory, "P/E فولاد چقدر است؟");

        Assert.Equal("Answered", root.GetProperty("outcome").GetString());
        Assert.Equal(1m, root.GetProperty("usage").GetProperty("creditsCharged").GetDecimal());
        Assert.Contains("9.37", Feature148Chat.Text(root));
        Assert.DoesNotContain(UnitNote, Feature148Chat.Text(root));
    }
}

/// <summary>
/// The production shape after Feature 159: NoavaranAmin base provider, comprehensive analysis from CyclicalWaves.
/// Covers the refusals (AC-1 to AC-4, AC-7) and the owner's Gate B conditions C1–C3.
/// </summary>
public sealed class Feature150UnavailableTopicMappedAnalysisE2ETests : IClassFixture<Feature159MappedNoavaranAminChatApiFactory>
{
    private readonly Feature159MappedNoavaranAminChatApiFactory _factory;

    public Feature150UnavailableTopicMappedAnalysisE2ETests(Feature159MappedNoavaranAminChatApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
        SeedTechnicalAnalyses(factory);
        factory.ResetRecorders();
    }

    // BUG-020 seed, in this fixture's own database only: an older technical-titled فولاد analysis (the latest فولاد
    // analysis stays the شهریور report), a شستا analysis tagged technical only, and a non-technical کگل analysis. The two
    // companies carry their symbol in TseSymbol/CompanySymbol, not Ticker, as Noavaran rows do.
    private static readonly object SeedLock = new();

    private static void SeedTechnicalAnalyses(Feature159MappedNoavaranAminChatApiFactory factory)
    {
        lock (SeedLock)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
            if (db.CyclicalWavesV2Analyses.Any(row => row.Id == 150001))
                return;

            var now = DateTimeOffset.UtcNow;
            db.Companies.AddRange(
                Company("15000000-0000-0000-0000-000000000001", "1500001", "کگل", "گل گهر"),
                Company("15000000-0000-0000-0000-000000000002", "1500002", "شستا", "سرمایه گذاری تامین اجتماعی"));
            db.CyclicalWavesV2Analyses.AddRange(
                Analysis(150001, "فولاد - تحلیل تکنیکال و پیوت‌های مهم", now.AddDays(-10)),
                Analysis(150002, "کگل- گزارش عملکرد مهر ۱۴۰۵", now.AddDays(-1)),
                Analysis(150003, "شستا- گزارش ماهانه", now.AddDays(-1)));
            db.CyclicalWavesV2AnalysisTags.Add(new CyclicalWavesV2AnalysisTagRow
            {
                // Stored as ingestion stores it (EfCoreCyclicalWavesV2AnalysisStore).
                AnalysisId = 150003, ProviderTagId = 1501, Name = "تحلیل_تکنیکال",
                NormalizedName = FinancialCopilot.Domain.Financial.Services.PersianSymbolNormalizer.Normalize("تحلیل_تکنیکال")
            });
            db.SaveChanges();

            NormalizedCompanyRow Company(string id, string externalId, string symbol, string name) => new()
            {
                Id = Guid.Parse(id), ProviderName = ProviderSources.NoavaranCurrentApiName, ExternalCompanyId = externalId,
                CompanySymbol = symbol, TseSymbol = symbol, Name = name, LastSynchronizedAt = now
            };

            CyclicalWavesV2AnalysisRow Analysis(long id, string title, DateTimeOffset publishedAt) => new()
            {
                Id = id, Title = title, Summary = $"<p>{title}</p>", PlainTextSummary = title, PublishedAt = publishedAt,
                ContentHash = id.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(64, '0'),
                FirstSyncedAt = now, LastSyncedAt = now, UpdatedAt = now
            };
        }
    }

    // BUG-020: a technical question shows the technical analysis, found by title or by tag, exactly like «تحلیل جامع X».
    [Theory]
    [InlineData("تحلیل تکنیکال فولاد", "### فولاد - تحلیل تکنیکال و پیوت‌های مهم")]
    [InlineData("تکنیکال فولاد", "### فولاد - تحلیل تکنیکال و پیوت‌های مهم")]
    [InlineData("پیوت های فولاد را نشان بده", "### فولاد - تحلیل تکنیکال و پیوت‌های مهم")]
    [InlineData("تحلیل تکنیکال شستا", "### شستا- گزارش ماهانه")]
    public async Task TechnicalQuestion_ShowsTheTechnicalAnalysisCard(string question, string titleLine)
    {
        var root = await Feature148Chat.AskAsync(_factory, question);

        Assert.Equal("comprehensive_analysis", root.GetProperty("semanticCapabilityCode").GetString());
        var text = Feature148Chat.Text(root);
        Assert.StartsWith(titleLine, text);
        Assert.Contains("متن کامل این تحلیل را در بخش تحلیل جامع، تحلیل اپ بخوانید", text);
        Assert.DoesNotContain("تحلیل تکنیکالی در تحلیل‌اپ پیدا نشد", text);
        Assert.DoesNotContain("Found metric data", text);
    }

    // BUG-020 (owner, 2026-11-11): no technical analysis → say so, then the latest analysis card of the symbol.
    [Theory]
    [InlineData("تحلیل تکنیکال کگل")]
    [InlineData("تکنیکال کگل")]
    public async Task TechnicalQuestion_WithoutATechnicalAnalysis_SaysSo_ThenShowsTheLatestAnalysis(string question)
    {
        var root = await Feature148Chat.AskAsync(_factory, question);

        Assert.Equal("comprehensive_analysis", root.GetProperty("semanticCapabilityCode").GetString());
        var text = Feature148Chat.Text(root);
        Assert.StartsWith("برای نماد کگل تحلیل تکنیکالی در تحلیل‌اپ پیدا نشد.\n\n### کگل- گزارش عملکرد مهر ۱۴۰۵", text);
        Assert.DoesNotContain("Found metric data", text);
    }

    // Owner report 2026-11-11: in an existing session «تکنیکال کگهر» inherited the previous lookup (price of the new
    // symbol plus the old metric table). The topic question must answer the same as in a new session.
    [Theory]
    [InlineData("قیمت تعادلی فولاد چقدر است؟", "تکنیکال کگل")]
    [InlineData("قیمت فولاد چقدر است؟", "تکنیکال کگل")]
    [InlineData("P/E فولاد چقدر است؟", "پیوت های کگل را نشان بده")]
    public async Task TechnicalQuestion_AfterAnotherQuestionInTheSameSession_IsTheSameAnswerAsInANewSession(string first, string technical)
    {
        var conversationId = (await Feature148Chat.AskAsync(_factory, first)).GetProperty("conversationId").GetGuid();

        var root = await Feature148Chat.AskAsync(_factory, technical, conversationId);

        Assert.Equal(conversationId, root.GetProperty("conversationId").GetGuid());
        Assert.Equal("comprehensive_analysis", root.GetProperty("semanticCapabilityCode").GetString());
        var text = Feature148Chat.Text(root);
        Assert.StartsWith("برای نماد کگل تحلیل تکنیکالی در تحلیل‌اپ پیدا نشد.\n\n### کگل- گزارش عملکرد مهر ۱۴۰۵", text);
        Assert.DoesNotContain("آخرین قیمت نماد", text);
    }

    [Theory]
    [InlineData("قیمت فولاد چقدر است؟", "قیمت تعادلی کگل چقدر است؟", "valuation_unavailable")]
    [InlineData("قیمت فولاد چقدر است؟", "کگل ارزش خرید داره؟", "investment_advice_unavailable")]
    public async Task TopicQuestion_AfterAPriceQuestionInTheSameSession_IsStillRefused(string first, string topic, string reasonCode)
    {
        var conversationId = (await Feature148Chat.AskAsync(_factory, first)).GetProperty("conversationId").GetGuid();

        var root = await Feature148Chat.AskAsync(_factory, topic, conversationId);

        Assert.Equal("unavailable_topic", root.GetProperty("semanticCapabilityCode").GetString());
        Assert.Equal(reasonCode, root.GetProperty("outcomeReasonCode").GetString());
        // The refusal copy offers «آخرین قیمت» as an alternative; the price answer's sentence must not appear.
        Assert.DoesNotContain("آخرین قیمت نماد", Feature148Chat.Text(root));
        Assert.Equal(0m, root.GetProperty("usage").GetProperty("creditsCharged").GetDecimal());
    }

    [Fact]
    public async Task TechnicalQuestion_ForASymbolWithNoAnalysisAtAll_IsTheNoAnalysisCard()
    {
        var root = await Feature148Chat.AskAsync(_factory, "تحلیل تکنیکال فملی");

        Assert.StartsWith("برای نماد فملی تحلیلی در تحلیل‌اپ پیدا نشد.", Feature148Chat.Text(root));
        Assert.Equal(0m, root.GetProperty("usage").GetProperty("creditsCharged").GetDecimal());
    }

    [Theory]
    [InlineData("قیمت تعادلی فولاد چقدر است؟", "valuation_unavailable", "قیمت تعادلی و ارزش ذاتی هنوز در دستیار محاسبه نمی‌شوند.")]
    [InlineData("ارزش ذاتی فولاد چقدر است؟", "valuation_unavailable", "قیمت تعادلی و ارزش ذاتی هنوز در دستیار محاسبه نمی‌شوند.")]
    [InlineData("فولاد ارزش خرید داره؟", "investment_advice_unavailable", "توصیهٔ خرید یا فروش ارائه نمی‌کنم.")]
    [InlineData("فولاد در این قیمت خوبه؟", "investment_advice_unavailable", "توصیهٔ خرید یا فروش ارائه نمی‌کنم.")]
    public async Task TopicQuestion_GetsTheRefusal_Free_WithSymbolFollowUps(string question, string reasonCode, string textStart)
    {
        var root = await Feature148Chat.AskAsync(_factory, question);

        Assert.Equal("unavailable_topic", root.GetProperty("semanticCapabilityCode").GetString());
        Assert.Equal("Unsupported", root.GetProperty("outcome").GetString());
        Assert.Equal(reasonCode, root.GetProperty("outcomeReasonCode").GetString());
        Assert.StartsWith(textStart, Feature148Chat.Text(root));
        Assert.Equal(0m, root.GetProperty("usage").GetProperty("creditsCharged").GetDecimal());
        var followUps = root.GetProperty("suggestedActions").EnumerateArray().Select(action => action.GetProperty("message").GetString()).ToArray();
        Assert.Contains("روند فروش فولاد", followUps);
        Assert.Contains("گزارش فصلی فولاد", followUps);
        Feature148Chat.ReadNoCyclicalWavesV2(_factory.Reads);
        Feature148Chat.NoOutboundCalls(_factory.Http);
    }

    [Fact]
    public async Task TopicQuestion_ForACompanyWithoutTicker_FillsTheSymbolFromItsSymbolColumns()
    {
        var root = await Feature148Chat.AskAsync(_factory, "شستا ارزش خرید داره؟");

        Assert.Equal("unavailable_topic", root.GetProperty("semanticCapabilityCode").GetString());
        var followUps = root.GetProperty("suggestedActions").EnumerateArray().Select(action => action.GetProperty("message").GetString()).ToArray();
        Assert.Contains("روند فروش شستا", followUps);
    }

    [Fact]
    public async Task TopicQuestion_WithAnUnknownName_StillRefuses_WithExampleFollowUps()
    {
        var root = await Feature148Chat.AskAsync(_factory, "تکنیکال نمونه");

        Assert.Equal("unavailable_topic", root.GetProperty("semanticCapabilityCode").GetString());
        Assert.False(root.GetProperty("clarificationRequired").GetBoolean());
        Assert.NotEmpty(root.GetProperty("suggestedActions").EnumerateArray());
    }

    // Gate B C1: the analysis card stays.
    [Theory]
    [InlineData("تحلیل جامع فولاد")]
    [InlineData("تحلیل فولاد")]
    [InlineData("تحلیل بنیادی فولاد")]
    public async Task AnalysisQuestion_KeepsTheTahlilAppCard(string question)
    {
        var root = await Feature148Chat.AskAsync(_factory, question);

        Assert.Equal("comprehensive_analysis", root.GetProperty("semanticCapabilityCode").GetString());
        Assert.Contains("### فولاد- گزارش عملکرد شهریور ۱۴۰۵", Feature148Chat.Text(root));
    }

    // BUG-020 F1: every analysis reply is the Persian card only (found or not), never the lookup's English summary.
    [Theory]
    [InlineData("تحلیل جامع فملی", "برای نماد فملی تحلیلی در تحلیل‌اپ پیدا نشد.")]
    [InlineData("تحلیل جامع فولاد", "### فولاد- گزارش عملکرد شهریور ۱۴۰۵")]
    public async Task AnalysisReply_StartsWithTheCard_WithoutEnglishText(string question, string start)
    {
        var root = await Feature148Chat.AskAsync(_factory, question);

        Assert.StartsWith(start, Feature148Chat.Text(root));
        Assert.DoesNotContain("Found metric data", Feature148Chat.Text(root));
    }

    // C3 (OQ-072): an analysis word with another topic word keeps the analysis route.
    [Fact]
    public async Task AnalysisQuestionWithAValuationWord_KeepsTheAnalysisRoute()
    {
        var root = await Feature148Chat.AskAsync(_factory, "تحلیل ارزش ذاتی فولاد");

        Assert.Equal("comprehensive_analysis", root.GetProperty("semanticCapabilityCode").GetString());
    }

    // AC-5: these keep their route.
    [Theory]
    [InlineData("قیمت فولاد چقدر است؟")]
    [InlineData("P/E فولاد چقدر است؟")]
    [InlineData("فولاد")]
    public async Task OtherQuestion_IsNotRefused(string question)
    {
        var root = await Feature148Chat.AskAsync(_factory, question);

        Assert.NotEqual("unavailable_topic", root.GetProperty("semanticCapabilityCode").GetString());
    }
}

/// <summary>AC-6: in CyclicalWaves mode the rule is off; that mode answers the equilibrium range itself.</summary>
public sealed class Feature150UnavailableTopicCyclicalWavesE2ETests : IClassFixture<Feature148CyclicalWavesChatApiFactory>
{
    private readonly Feature148CyclicalWavesChatApiFactory _factory;

    public Feature150UnavailableTopicCyclicalWavesE2ETests(Feature148CyclicalWavesChatApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
        factory.ResetRecorders();
    }

    [Theory]
    [InlineData("تکنیکال فولاد")]
    [InlineData("قیمت تعادلی فولاد چقدر است؟")]
    public async Task TopicQuestion_IsNotRefused(string question)
    {
        var root = await Feature148Chat.AskAsync(_factory, question);

        Assert.NotEqual("unavailable_topic", root.GetProperty("semanticCapabilityCode").GetString());
    }
}
