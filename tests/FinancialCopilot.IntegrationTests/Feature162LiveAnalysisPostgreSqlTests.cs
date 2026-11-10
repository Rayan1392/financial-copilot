using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Domain.Financial.Services;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWavesV2;
using FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWavesV2;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Feature 162 AC9 (live Development check): real TahlilApp analyses, captured from
/// <c>GET https://api.tahlilapp.com/api/blogs?filter[type]=analysis</c> into the file named by
/// <c>FEATURE162_LIVE_ANALYSES_JSON</c>, go through the real parser, store and query on an isolated PostgreSQL
/// (<c>FINANCIAL_COPILOT_TEST_POSTGRES_CONNECTION_STRING</c>). Skipped unless both are set. The rendered cards are
/// written next to the capture for review.
/// </summary>
[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class Feature162LiveAnalysisPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
{
    private const string CaptureVariable = "FEATURE162_LIVE_ANALYSES_JSON";
    private const string Url = "https://tahlilapp.com/app/analysis";

    [SkippableTheory]
    [InlineData("شگل")]
    [InlineData("وتجارت")]
    [InlineData("اخابر")]
    public async Task RealAnalyses_AreFoundByTickerInTitleAndRenderedAsTheCard(string ticker)
    {
        var capture = Environment.GetEnvironmentVariable(CaptureVariable);
        Skip.If(string.IsNullOrWhiteSpace(capture) || !File.Exists(capture), $"{CaptureVariable} is not set to a captured analyses file.");
        await using var database = await fixture.CreateDatabaseAsync();
        await using var db = database.CreateContext();

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(capture!));
        var page = CyclicalWavesV2PayloadParser.ParseAnalysesPage(document.RootElement);
        var store = new EfCoreCyclicalWavesV2AnalysisStore(db, new HtmlAgilityPackTextStripper());
        await store.ApplyAnalysesPageAsync(page.Items, DateTimeOffset.UtcNow, CancellationToken.None);

        var expected = page.Items
            .Where(article => CyclicalWavesV2ComprehensiveAnalysisQuery.MatchesTicker(article.Title, PersianSymbolNormalizer.Normalize(ticker)))
            .OrderByDescending(article => article.PublishedAt).ThenByDescending(article => article.Id)
            .Take(3)
            .Select(article => article.Id)
            .ToArray();
        var response = await new CyclicalWavesV2ComprehensiveAnalysisQuery(db)
            .ExecuteAsync(new ComprehensiveAnalysisQueryRequest([ticker], [], null, 3), CancellationToken.None);

        Assert.NotEmpty(expected);
        Assert.Equal(expected, response.Items.Select(item => item.AnalysisId));
        var card = ComprehensiveAnalysisCardComposer.ComposeMarkdown(response.Items, response.UnresolvedSymbols, Url);
        Assert.EndsWith($"[{ComprehensiveAnalysisCardComposer.LinkTextFa}]({Url})", card);
        Assert.StartsWith($"### {response.Items[0].Title.Trim()}", card);
        // Only a preview leaves the query: at most 30% of the stored text and 300 characters.
        var stored = await db.CyclicalWavesV2Analyses.FindAsync(response.Items[0].AnalysisId);
        var whole = stored!.PlainTextSummary ?? string.Empty;
        var preview = response.Items[0].PlainTextSummary;
        Assert.True(preview.Length <= 302 && preview.Length <= whole.Length * 30 / 100 + 2, $"{preview.Length} of {whole.Length}");
        if (whole.Length >= 150) Assert.DoesNotContain(whole, card);
        await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(capture!)!, $"card-{ticker}.md"), card);
    }
}
