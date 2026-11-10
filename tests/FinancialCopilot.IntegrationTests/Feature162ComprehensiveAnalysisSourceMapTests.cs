using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.AspNetCore.Hosting;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Feature 162 through POST /api/ai/v1/query: base provider NoavaranAmin with
/// <c>FinancialData:CapabilitySources:comprehensive_analysis = CyclicalWaves</c>. Comprehensive analysis reads only the
/// three v2 analysis tables; every other capability keeps reading NoavaranAmin data.
/// </summary>
public sealed class Feature162ComprehensiveAnalysisSourceMapTests : IClassFixture<Feature162MappedNoavaranAminChatApiFactory>
{
    private const string Link = "[ادامهٔ تحلیل‌ها در تحلیل‌اپ](https://tahlilapp.com/app/analysis)";

    private static readonly string[] AnalysisTables =
    [
        nameof(CyclicalWavesV2AnalysisRow), nameof(CyclicalWavesV2AnalysisTagRow), nameof(CyclicalWavesV2AnalysisCategoryRow)
    ];

    private readonly Feature162MappedNoavaranAminChatApiFactory _factory;

    public Feature162ComprehensiveAnalysisSourceMapTests(Feature162MappedNoavaranAminChatApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
        factory.ResetRecorders();
    }

    [Theory]
    [InlineData("تحلیل جامع فولاد")]
    [InlineData("آخرین تحلیل‌های منتشرشده درباره فولاد چیست؟")]
    public async Task MappedAnalysis_ShowsTheV2CardAndReadsOnlyTheAnalysisTablesFromV2(string question)
    {
        var root = await Feature148Chat.AskAsync(_factory, question);

        Assert.Equal("comprehensive_analysis", root.GetProperty("semanticCapabilityCode").GetString());
        var text = Feature148Chat.Text(root);
        Assert.Contains("### فولاد- گزارش عملکرد شهریور ۱۴۰۵", text);
        Assert.Contains("فروش شهریور فولاد ۱۷ درصد رشد کرد", text);
        Assert.EndsWith(Link, text.TrimEnd());
        Assert.Equal(1m, root.GetProperty("usage").GetProperty("creditsCharged").GetDecimal());
        Assert.True(_factory.Reads.Read<CyclicalWavesV2AnalysisRow>());
        AssertOnlyAnalysisTablesFromV2();
        Feature148Chat.NoOutboundCalls(_factory.Http);
    }

    [Fact]
    public async Task MappedAnalysis_NoAnalysisForTheSymbol_SaysSoFreeAndWithoutAnotherSource()
    {
        var root = await Feature148Chat.AskAsync(_factory, "تحلیل جامع فملی");

        // The turn stays the analysis capability: the registered financial source (Feature 141) did not answer.
        Assert.Equal("comprehensive_analysis", root.GetProperty("semanticCapabilityCode").GetString());
        var text = Feature148Chat.Text(root);
        Assert.Contains("برای نماد فملی تحلیلی در تحلیل‌اپ پیدا نشد.", text);
        Assert.EndsWith(Link, text.TrimEnd());
        Assert.Equal(0m, root.GetProperty("usage").GetProperty("creditsCharged").GetDecimal());
        AssertOnlyAnalysisTablesFromV2();
    }

    [Fact]
    public async Task MappedMode_PeLookupStillReadsOnlyNoavaranAmin()
    {
        var root = await Feature148Chat.AskAsync(_factory, "P/E فولاد چقدر است؟");

        Assert.Equal(Feature148ChatFixtures.NoavaranPe, Feature148Chat.LookupCell(root, Feature148ChatFixtures.Folad, "PE_TTM").GetProperty("value").GetDecimal());
        Feature148Chat.ReadNoCyclicalWavesV2(_factory.Reads);
    }

    [Fact]
    public async Task MappedMode_GaugeRequestStillReadsNoV2Table()
    {
        await Feature148Chat.AskAsync(_factory, "قیمت تعادلی فولاد چقدر است؟");

        Feature148Chat.ReadNoCyclicalWavesV2(_factory.Reads);
        Assert.DoesNotContain("query_cyclicalwaves_gauges", _factory.Fake.ToolCalls);
        Feature148Chat.NoOutboundCalls(_factory.Http);
    }

    private void AssertOnlyAnalysisTablesFromV2() =>
        Assert.All(
            _factory.Reads.EntityTypes.Where(type => type.StartsWith("CyclicalWavesV2", StringComparison.Ordinal)),
            type => Assert.Contains(type, AnalysisTables));
}

public sealed class Feature162MappedNoavaranAminChatApiFactory : Feature148CyclicalWavesChatApiFactory
{
    protected override string ChatProvider => "NoavaranAmin";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        // Read while services are composed, so it must be a host setting.
        builder.UseSetting("FinancialData:CapabilitySources:comprehensive_analysis", "CyclicalWaves");
    }
}
