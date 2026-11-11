using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.CyclicalWavesV2;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Telegram;
using FinancialCopilot.Domain.Financial.CyclicalWavesV2;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWavesV2;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWavesV2;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.IntegrationTests;

/// <summary>Request helpers and provider-isolation assertions shared by the Feature 148 Chat E2E suites.</summary>
internal static class Feature148Chat
{
    public static async Task<JsonElement> AskAsync(WebApplicationFactory<Program> factory, string message, Guid? conversationId = null)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);
        using var response = await client.PostAsJsonAsync("/api/ai/v1/query", new { message, conversationId }, CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    public static async Task<JsonElement> MessagesAsync(WebApplicationFactory<Program> factory, Guid conversationId)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);
        using var response = await client.GetAsync($"/api/ai/v1/conversations/{conversationId}/messages", CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken.None)).RootElement.Clone();
    }

    /// <summary>Every row the request materialized came from the independent v2 tables.</summary>
    public static void ReadOnlyCyclicalWavesV2(Feature148TableReadRecorder reads)
    {
        Assert.NotEmpty(reads.EntityTypes);
        Assert.All(reads.EntityTypes, type => Assert.StartsWith("CyclicalWavesV2", type, StringComparison.Ordinal));
    }

    /// <summary>NoavaranAmin mode never touches the v2 tables.</summary>
    public static void ReadNoCyclicalWavesV2(Feature148TableReadRecorder reads) =>
        Assert.DoesNotContain(reads.EntityTypes, type => type.StartsWith("CyclicalWavesV2", StringComparison.Ordinal));

    /// <summary>No outbound HTTP at all: no legacy CyclicalWaves call (not even a blocked attempt) and no live v2 call.</summary>
    public static void NoOutboundCalls(Feature148OutboundHttpRecorder http) => Assert.Empty(http.Attempts);

    public static string Text(JsonElement root) => root.GetProperty("textAnswer").GetString() ?? string.Empty;

    public static string[] ScannerSymbols(JsonElement root) =>
        root.GetProperty("scannerTable").GetProperty("rows").EnumerateArray()
            .Select(row => row.GetProperty("symbolCode").GetString()!)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static string ScannerCell(JsonElement root, string symbol, string column) =>
        root.GetProperty("scannerTable").GetProperty("rows").EnumerateArray()
            .Single(row => row.GetProperty("symbolCode").GetString() == symbol)
            .GetProperty("cells").GetProperty(column).GetProperty("formattedValue").GetString()!;

    public static JsonElement LookupCell(JsonElement root, string symbol, string metricCode)
    {
        var table = root.GetProperty("symbolLookupTable");
        var identifier = table.GetProperty("columns").EnumerateArray()
            .Single(column => column.GetProperty("metricCode").GetString() == metricCode).GetProperty("identifier").GetString()!;
        return table.GetProperty("rows").EnumerateArray()
            .Single(row => row.GetProperty("symbolCode").GetString() == symbol)
            .GetProperty("cells").GetProperty(identifier);
    }

    public static decimal?[] QuarterlyRow(JsonElement root, int sourceItemId)
    {
        var quarterly = root.GetProperty("financialStatementTableResult").GetProperty("quarterlyResult");
        var labels = quarterly.GetProperty("periodsNewestFirst").EnumerateArray().Select(period => period.GetProperty("label").GetString()!).ToArray();
        var cells = quarterly.GetProperty("rows").EnumerateArray()
            .Single(row => row.GetProperty("sourceItemId").GetInt32() == sourceItemId).GetProperty("cells");
        return labels.Select(label => cells.TryGetProperty(label, out var cell) && cell.GetProperty("value").ValueKind == JsonValueKind.Number
            ? cell.GetProperty("value").GetDecimal()
            : (decimal?)null).ToArray();
    }
}

/// <summary>
/// T148-08.4, CyclicalWaves mode: the ten acceptance questions through POST /api/ai/v1/query (API-key auth, conversation,
/// MAF V2 orchestration, semantic routing, provider selection, executors, presentation, billing, persistence).
/// Where the exact wording is misrouted by the shared, provider-independent router (identical in NoavaranAmin mode and
/// unchanged by Feature 148), the test pins that behavior and a router-supported phrasing proves the CyclicalWaves
/// data path with exact values.
/// </summary>
public sealed class Feature148CyclicalWavesChatE2ETests : IClassFixture<Feature148CyclicalWavesChatApiFactory>
{
    private readonly Feature148CyclicalWavesChatApiFactory _factory;

    public Feature148CyclicalWavesChatE2ETests(Feature148CyclicalWavesChatApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
        factory.ResetRecorders();
    }

    [Fact]
    public async Task Q01_LatestMonthlySales_ComesFromTheCyclicalWavesScreener_AndIsPersistedForReplay()
    {
        var root = await Feature148Chat.AskAsync(_factory, "فروش آخرین ماه فولاد چقدر است؟");

        Assert.Equal("SymbolLookup", root.GetProperty("intent").GetString());
        Assert.Equal("symbol_metric_lookup", root.GetProperty("semanticCapabilityCode").GetString());
        Assert.False(root.GetProperty("clarificationRequired").GetBoolean(), root.ToString());
        var cell = Feature148Chat.LookupCell(root, Feature148ChatFixtures.Folad, "MONTHLY_SALES");
        Assert.Equal(639_418_160_000_000m, cell.GetProperty("value").GetDecimal()); // IRR, screener-folad.json lastMonthSale
        Assert.Equal("639,418,160", cell.GetProperty("formattedValue").GetString()); // million IRR
        Assert.Equal("CyclicalWaves", cell.GetProperty("sourceLabel").GetString());
        Assert.Equal("2026-09-22", cell.GetProperty("tradingDate").GetString());
        Assert.Contains("639,418,160", Feature148Chat.Text(root));
        Assert.Contains("میلیون ریال", Feature148Chat.Text(root));
        Assert.Equal(1m, root.GetProperty("usage").GetProperty("creditsCharged").GetDecimal());
        Feature148Chat.ReadOnlyCyclicalWavesV2(_factory.Reads);
        Feature148Chat.NoOutboundCalls(_factory.Http);

        // Conversation persistence and replay.
        var conversationId = root.GetProperty("conversationId").GetGuid();
        var messages = (await Feature148Chat.MessagesAsync(_factory, conversationId)).GetProperty("messages").EnumerateArray().ToArray();
        Assert.Contains(messages, message => message.GetProperty("role").GetString() == "User" &&
                                              message.GetProperty("content").GetString() == "فروش آخرین ماه فولاد چقدر است؟");
        Assert.Contains(messages, message => message.GetProperty("role").GetString() == "Assistant" &&
                                              message.GetProperty("content").GetString()!.Contains("639,418,160", StringComparison.Ordinal));
        var followUp = await Feature148Chat.AskAsync(_factory, "P/E فولاد چقدر است؟", conversationId);
        Assert.Equal(conversationId, followUp.GetProperty("conversationId").GetGuid());
        Assert.Equal(6.05m, Feature148Chat.LookupCell(followUp, Feature148ChatFixtures.Folad, "PE_TTM").GetProperty("value").GetDecimal());
    }

    [Fact]
    public async Task Q02_NetProfitLatestQuarter_ExactWording_ReturnsTheStoredQuarterlyValue()
    {
        var root = await Feature148Chat.AskAsync(_factory, "سود خالص آخرین فصل فولاد چقدر بوده؟");

        Assert.False(root.GetProperty("clarificationRequired").GetBoolean(), root.ToString());
        Assert.Equal("financial_statement_table", root.GetProperty("semanticCapabilityCode").GetString());
        Assert.Equal(471_272_793m, Feature148Chat.QuarterlyRow(root, 66)[0]);
        Assert.Contains("فولاد", Feature148Chat.Text(root));
        Assert.True(_factory.Reads.Read<CyclicalWavesV2IncomeStatementValueRow>());
        Feature148Chat.NoOutboundCalls(_factory.Http);
    }

    [Fact]
    public async Task Q02_Q03_LatestQuarterIncomeStatement_IsTheStoredCyclicalWavesStatement()
    {
        var root = await Feature148Chat.AskAsync(_factory, "صورت سود و زیان فصل اخیر فولاد را نمایش بده");

        Assert.Equal("FinancialStatementTableLookup", root.GetProperty("intent").GetString());
        Assert.Equal("financial_statement_table", root.GetProperty("semanticCapabilityCode").GetString());
        var quarterly = root.GetProperty("financialStatementTableResult").GetProperty("quarterlyResult");
        Assert.Equal("CyclicalWavesV2", quarterly.GetProperty("providerName").GetString());
        Assert.StartsWith(CyclicalWavesV2CanonicalEntityResolver.ExternalIdPrefix, quarterly.GetProperty("externalCompanyId").GetString());
        var latest = quarterly.GetProperty("periodsNewestFirst")[0];
        Assert.Equal((1405, 2), (latest.GetProperty("fiscalYear").GetInt32(), latest.GetProperty("quarter").GetInt32()));
        // statements-folad.json, million IRR: sales and net profit (the latest-quarter net profit of Q02).
        Assert.Equal(1_675_255_385m, Feature148Chat.QuarterlyRow(root, 44)[0]);
        Assert.Equal(471_272_793m, Feature148Chat.QuarterlyRow(root, 66)[0]);
        Assert.Contains("میلیون ریال", Feature148Chat.Text(root));
        Feature148Chat.ReadOnlyCyclicalWavesV2(_factory.Reads);
        Assert.True(_factory.Reads.Read<CyclicalWavesV2IncomeStatementValueRow>());
        Feature148Chat.NoOutboundCalls(_factory.Http);
    }

    [Fact]
    public async Task Q04_NetProfitTrend_ExactWording_InfersIncomeStatementAndReturnsFourStoredQuarters()
    {
        var root = await Feature148Chat.AskAsync(_factory, "روند سود خالص چهار فصل اخیر فولاد چطور بوده؟");
        Assert.False(root.GetProperty("clarificationRequired").GetBoolean());
        Assert.Equal("financial_statement_table", root.GetProperty("semanticCapabilityCode").GetString());
        Assert.Equal([471_272_793m, 120_356_493m, 560_156_183m, 188_930_866m], Feature148Chat.QuarterlyRow(root, 66));
        Assert.True(root.GetProperty("financialStatementTableResult").GetProperty("quarterlyResult").GetProperty("periodsNewestFirst").GetArrayLength() >= 4);
        Feature148Chat.ReadOnlyCyclicalWavesV2(_factory.Reads);
        Feature148Chat.NoOutboundCalls(_factory.Http);
    }

    [Theory]
    [InlineData("سهم‌هایی با P/E کمتر از ۵ را پیدا کن")]
    [InlineData("شرکت‌هایی با P/S کمتر از ۱")]
    public async Task Q05_Q06_ScreeningWithKamtarAz_ExactWording_RunsTheCyclicalWavesScanner(string question)
    {
        var root = await Feature148Chat.AskAsync(_factory, question);

        Assert.False(root.GetProperty("clarificationRequired").GetBoolean());
        Assert.Equal("stock_screening", root.GetProperty("semanticCapabilityCode").GetString());
        Assert.Equal("Scanner", root.GetProperty("intent").GetString());
        Assert.NotEmpty(Feature148Chat.ScannerSymbols(root));
        Feature148Chat.NoOutboundCalls(_factory.Http);
    }

    [Fact]
    public async Task Q05_PeBelowFive_RunsTheCyclicalWavesScanner()
    {
        var root = await Feature148Chat.AskAsync(_factory, "سهام با P/E زیر ۵");

        Assert.Equal("Scanner", root.GetProperty("intent").GetString());
        Assert.Equal("stock_screening", root.GetProperty("semanticCapabilityCode").GetString());
        // شستا 3.1 and فملی 4.2 match; فولاد 6.05 and کگل 7.5 do not; وبملت P/E 0 is "not computable".
        Assert.Equal(["شستا", "فملی"], Feature148Chat.ScannerSymbols(root));
        Assert.Equal("4.2", Feature148Chat.ScannerCell(root, "فملی", "PE_TTM"));
        var excluded = root.GetProperty("scannerTable").GetProperty("executionFacts").GetProperty("excludedByReason");
        Assert.Equal(1, excluded.GetProperty(CyclicalWavesV2ScannerExecutionService.PeNotComputableReason).GetInt32());
        Assert.DoesNotContain(_factory.Reads.EntityTypes, type => !type.StartsWith("CyclicalWavesV2", StringComparison.Ordinal));
        Feature148Chat.NoOutboundCalls(_factory.Http);
    }

    [Fact]
    public async Task Q06_PsBelowOne_RunsTheCyclicalWavesScanner()
    {
        var root = await Feature148Chat.AskAsync(_factory, "سهام با P/S زیر ۱");

        Assert.Equal(["فملی", "وبملت", "کگل"], Feature148Chat.ScannerSymbols(root));
        Assert.Equal("0.7", Feature148Chat.ScannerCell(root, "کگل", "PS_TTM"));
        Feature148Chat.NoOutboundCalls(_factory.Http);
    }

    [Fact]
    public async Task Q07_MonthlySalesGrowth_ExactWording_RunsTheCyclicalWavesScanner()
    {
        var root = await Feature148Chat.AskAsync(_factory, "نمادهایی با رشد فروش ماهانه بیشتر از ۲۰ درصد");
        // (current − previous) / |previous| × 100: فملی 33.33%, کگل 25%; فولاد 17.48% and شستا 10% fail; وبملت has no value.
        Assert.Equal("stock_screening", root.GetProperty("semanticCapabilityCode").GetString());
        Assert.Equal(["فولاد"], Feature148Chat.ScannerSymbols(root));
        Assert.Equal("139.12%", Feature148Chat.ScannerCell(root, "فولاد", "MONTHLY_SALES_GROWTH_PERCENT"));
        Feature148Chat.NoOutboundCalls(_factory.Http);
    }

    [Fact]
    public async Task Q08_LatestAnalyses_AreTheStoredCyclicalWavesSummaries()
    {
        var root = await Feature148Chat.AskAsync(_factory, "آخرین تحلیل‌های منتشرشده درباره فولاد چیست؟");

        Assert.Equal("comprehensive_analysis", root.GetProperty("semanticCapabilityCode").GetString());
        var item = Assert.Single(root.GetProperty("comprehensiveAnalysisResult").GetProperty("items").EnumerateArray().ToArray());
        Assert.Equal(148001, item.GetProperty("analysisId").GetInt64());
        // Feature 162 change: the provider summary is the whole paid text, so only a preview leaves the query;
        // this seeded text is shorter than the preview minimum, so no preview at all.
        Assert.Equal(string.Empty, item.GetProperty("plainTextSummary").GetString());
        Assert.Contains("فولاد- گزارش عملکرد شهریور ۱۴۰۵", Feature148Chat.Text(root));
        Assert.DoesNotContain("فروش شهریور فولاد ۱۷ درصد رشد کرد", root.GetRawText());
        Feature148Chat.ReadOnlyCyclicalWavesV2(_factory.Reads);
        Assert.False(_factory.Reads.Read<ComprehensiveAnalysisRow>());
        Feature148Chat.NoOutboundCalls(_factory.Http);
    }

    [Fact]
    public async Task Q09_IndustryMembership_IsAnsweredAsUnsupported()
    {
        var root = await Feature148Chat.AskAsync(_factory, "شرکت‌های صنعت فولاد کدام‌اند؟");

        Assert.Equal("Unknown", root.GetProperty("intent").GetString());
        Assert.StartsWith("این پرسش را نمی‌توانم به‌صورت قابل اتکا پاسخ بدهم.", Feature148Chat.Text(root));
        Assert.True(root.GetProperty("scannerTable").ValueKind == JsonValueKind.Null);
        Assert.False(_factory.Reads.Read<CyclicalWavesV2FinancialValueRow>());
        Assert.False(_factory.Reads.Read<NormalizedCompanyRow>());
        Feature148Chat.NoOutboundCalls(_factory.Http);
    }

    [Fact]
    public async Task Q10_ProductProduction_ReturnsUnavailableWithoutAskingForKnownSymbol()
    {
        // The agent's lookup path cannot resolve a product metric and asks again (same in NoavaranAmin mode); no value is invented.
        var root = await Feature148Chat.AskAsync(_factory, "تولید گندله کگل چقدر بوده؟");

        Assert.False(root.GetProperty("clarificationRequired").GetBoolean());
        Assert.Equal(ChatCapabilityAvailability.UnavailableForProviderMessageFa, Feature148Chat.Text(root));
        Assert.Equal(ChatCapabilityAvailability.UnavailableForProviderReasonCode, root.GetProperty("outcomeReasonCode").GetString());
        Assert.True(root.GetProperty("symbolLookupTable").ValueKind == JsonValueKind.Null ||
                    root.GetProperty("symbolLookupTable").GetProperty("rows").GetArrayLength() == 0);
        Assert.False(_factory.Reads.Read<CyclicalWavesV2FinancialValueRow>());
        Assert.DoesNotContain(_factory.Reads.EntityTypes, type => !type.StartsWith("CyclicalWavesV2", StringComparison.Ordinal));
        Feature148Chat.NoOutboundCalls(_factory.Http);
    }

    [Fact]
    public async Task NetProfitGrowth_IsComputedFromTheV2Screener()
    {
        var root = await Feature148Chat.AskAsync(_factory, "سهام با رشد سود خالص فصلی بالای ۵۰ درصد");

        // (471,272,793 − 120,356,493) / 120,356,493 = 291.56%; the synthetic tickers publish no net profit.
        Assert.Equal(["فولاد"], Feature148Chat.ScannerSymbols(root));
        Assert.Equal("291.56%", Feature148Chat.ScannerCell(root, "فولاد", "NET_PROFIT_GROWTH_QOQ"));
        Feature148Chat.NoOutboundCalls(_factory.Http);
    }

    [Fact]
    public async Task GaugeTool_AnswersPESemanticVariantsFromV2Rows()
    {
        foreach (var question in new[]
        {
            "وضعیت گیج P/E فولاد چگونه است؟",
            "P/E فولاد نسبت به میانگین تاریخی خودش در چه وضعیتی قرار دارد؟"
        })
        {
            _factory.ResetRecorders();
            var root = await Feature148Chat.AskAsync(_factory, question);
            Assert.Contains("مقدار فعلی 5.9751", Feature148Chat.Text(root), StringComparison.Ordinal);
            Assert.Contains("میانگین 5.8271", Feature148Chat.Text(root), StringComparison.Ordinal);
            Assert.Contains("CyclicalWaves v2", Feature148Chat.Text(root), StringComparison.Ordinal);
            Feature148Chat.ReadOnlyCyclicalWavesV2(_factory.Reads);
            Assert.True(_factory.Reads.Read<CyclicalWavesV2GaugeDetailRow>());
            Feature148Chat.NoOutboundCalls(_factory.Http);
        }
    }

    [Fact]
    public async Task Feature115StandalonePSGauge_UsesLatestV2CloseAndOmitsUnsupportedComponents()
    {
        var root = await Feature148Chat.AskAsync(_factory, "وضعیت گیج P/S کگل چیست؟");

        var gauge = root.GetProperty("psVisualizationResult");
        Assert.Equal("CyclicalWaves v2", gauge.GetProperty("providerName").GetString());
        Assert.Equal(0.8m, gauge.GetProperty("gaugeClose").GetProperty("value").GetDecimal());
        Assert.Equal(1.7614m, gauge.GetProperty("providerAverage").GetDecimal());
        Assert.Equal(0.5432m, gauge.GetProperty("providerBoundaryStart").GetDecimal());
        Assert.Equal(12.2804m, gauge.GetProperty("providerBoundaryEnd").GetDecimal());
        Assert.Equal((int)PsValueState.Missing, gauge.GetProperty("ttmPs").GetProperty("state").GetInt32());
        Assert.Equal((int)PsValueState.Missing, gauge.GetProperty("forwardPs").GetProperty("state").GetInt32());
        Assert.False(gauge.GetProperty("historyIncluded").GetBoolean());
        Assert.Equal((int)PsVisualizationStatus.NotRequested, gauge.GetProperty("historyStatus").GetInt32());
        Assert.Empty(gauge.GetProperty("historyPoints").EnumerateArray());
        Assert.True(_factory.Reads.Read<CyclicalWavesV2GaugeDetailRow>());
        Assert.DoesNotContain(_factory.Reads.EntityTypes, type => type is "CompanyPsHistoryPointRow" or "CompanyPsGaugeSnapshotRow");
        Feature148Chat.NoOutboundCalls(_factory.Http);
    }

    [Fact]
    public async Task GaugeTool_AnswersPSBoundaryAndEquilibriumQueriesExactly()
    {
        foreach (var (question, expected) in new[]
        {
            ("مقدار فعلی P/S فولاد نسبت به محدوده تاریخی آن کجاست؟", "مقدار فعلی 1.7531"),
            ("حداقل، میانگین و حداکثر قیمت تعادلی کچاد چقدر است؟", "-7232.1700000000000000002"),
            ("قیمت تعادلی فولاد چقدر است؟", "7232.1700000000000000002")
        })
        {
            _factory.ResetRecorders();
            var root = await Feature148Chat.AskAsync(_factory, question);
            Assert.Contains(expected, Feature148Chat.Text(root), StringComparison.Ordinal);
            if (question.Contains("تعادلی", StringComparison.Ordinal))
                Assert.Contains("ریال", Feature148Chat.Text(root), StringComparison.Ordinal);
            Feature148Chat.ReadOnlyCyclicalWavesV2(_factory.Reads);
            Feature148Chat.NoOutboundCalls(_factory.Http);
        }
    }

    [Fact]
    public async Task GaugeTool_ScreeningUsesOnlyApprovedPEAndPSConditions()
    {
        foreach (var (question, symbols) in new[]
        {
            ("کدام نمادها در وضعیت P/E زیر حداقل تاریخی قرار دارند؟", new[] { "کگل" }),
            ("کدام نمادها در وضعیت P/S زیر میانگین تاریخی قرار دارند؟", new[] { "فولاد", "کگل", "وبملت" })
        })
        {
            _factory.ResetRecorders();
            var root = await Feature148Chat.AskAsync(_factory, question);
            var answer = Feature148Chat.Text(root);
            foreach (var symbol in symbols) Assert.Contains(symbol, answer, StringComparison.Ordinal);
            Assert.DoesNotContain("فملی", answer, StringComparison.Ordinal); // null P/S close is excluded
            Feature148Chat.ReadOnlyCyclicalWavesV2(_factory.Reads);
            Feature148Chat.NoOutboundCalls(_factory.Http);
        }
    }
}

/// <summary>T148-08.4, NoavaranAmin mode: C1/C1-M exclusion, C2, no v2 reads, persistence.</summary>
public sealed class Feature148NoavaranAminChatE2ETests : IClassFixture<Feature148NoavaranAminChatApiFactory>
{
    private readonly Feature148NoavaranAminChatApiFactory _factory;

    public Feature148NoavaranAminChatE2ETests(Feature148NoavaranAminChatApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
        factory.ResetRecorders();
    }

    [Fact]
    public async Task LatestMonthlySales_IsTheValidNoavaranRow_NotTheNewerLegacyRow_AndIsPersisted()
    {
        var root = await Feature148Chat.AskAsync(_factory, "فروش آخرین ماه فولاد چقدر است؟");

        var cell = Feature148Chat.LookupCell(root, Feature148ChatFixtures.Folad, "MONTHLY_SALES");
        Assert.Equal(Feature148ChatFixtures.NoavaranMonthlySalesIrr, cell.GetProperty("value").GetDecimal());
        Assert.Equal("123,456,000", cell.GetProperty("formattedValue").GetString());
        Assert.DoesNotContain("999,000,000", Feature148Chat.Text(root));
        Assert.True(_factory.Reads.Read<DerivedMetricRow>());
        Feature148Chat.ReadNoCyclicalWavesV2(_factory.Reads);
        Feature148Chat.NoOutboundCalls(_factory.Http);

        var messages = (await Feature148Chat.MessagesAsync(_factory, root.GetProperty("conversationId").GetGuid()))
            .GetProperty("messages").EnumerateArray().ToArray();
        Assert.Contains(messages, message => message.GetProperty("role").GetString() == "Assistant" &&
                                              message.GetProperty("content").GetString()!.Contains("123,456,000", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PeLookup_SkipsTheNewerLegacyRow()
    {
        var root = await Feature148Chat.AskAsync(_factory, "P/E فولاد چقدر است؟");

        Assert.Equal(Feature148ChatFixtures.NoavaranPe, Feature148Chat.LookupCell(root, Feature148ChatFixtures.Folad, "PE_TTM").GetProperty("value").GetDecimal());
        Feature148Chat.ReadNoCyclicalWavesV2(_factory.Reads);
    }

    [Fact]
    public async Task GaugeRequestInNoavaranMode_DoesNotReadCyclicalWavesTablesOrCallProvider()
    {
        var root = await Feature148Chat.AskAsync(_factory, "قیمت تعادلی فولاد چقدر است؟");

        Feature148Chat.ReadNoCyclicalWavesV2(_factory.Reads);
        Assert.DoesNotContain("query_cyclicalwaves_gauges", _factory.Fake.ToolCalls);
        Feature148Chat.NoOutboundCalls(_factory.Http);
        Assert.DoesNotContain("7232.1700000000000000002", Feature148Chat.Text(root), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PeScanner_MatchesOnlyValidNoavaranRows()
    {
        var root = await Feature148Chat.AskAsync(_factory, "سهام با P/E زیر ۵");

        // فملی's NoavaranAmin P/E 3.3 matches; فولاد's only P/E below 5 is the legacy 4.44 row, which is excluded.
        Assert.Equal(["فملی"], Feature148Chat.ScannerSymbols(root));
        Assert.Equal("3.3", Feature148Chat.ScannerCell(root, "فملی", "PE_TTM"));
        Feature148Chat.ReadNoCyclicalWavesV2(_factory.Reads);
        Feature148Chat.NoOutboundCalls(_factory.Http);
    }

    [Fact]
    public async Task GrowthScanner_ExcludesTheMixedProvenanceRow()
    {
        var root = await Feature148Chat.AskAsync(_factory, "سهام با رشد سود خالص فصلی بالای ۵۰ درصد");

        // فولاد's 80% row mixes a CyclicalWaves and a NoavaranAmin operand: never valid evidence. فملی's 60% is NoavaranAmin.
        Assert.Equal(["فملی"], Feature148Chat.ScannerSymbols(root));
        Assert.True(_factory.Reads.Read<DerivedMetricRow>());
        Feature148Chat.ReadNoCyclicalWavesV2(_factory.Reads);
    }

    [Fact]
    public async Task ComprehensiveAnalysis_IsUnavailable_AndNoAnalysisTableIsRead()
    {
        var root = await Feature148Chat.AskAsync(_factory, "آخرین تحلیل‌های منتشرشده درباره فولاد چیست؟");

        Assert.Equal(ChatCapabilityAvailability.ComprehensiveAnalysisUnavailableMessageFa, Feature148Chat.Text(root));
        Assert.Equal(0m, root.GetProperty("usage").GetProperty("creditsCharged").GetDecimal());
        Assert.False(_factory.Reads.Read<ComprehensiveAnalysisRow>());
        Feature148Chat.ReadNoCyclicalWavesV2(_factory.Reads);
    }

    [Fact]
    public async Task HistoricalLegacyRowsAreStillStored()
    {
        await Feature148Chat.AskAsync(_factory, "P/E فولاد چقدر است؟");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
        var rows = await db.DerivedMetrics.AsNoTracking().Where(row => row.ExternalCompanyId == Feature148ChatFixtures.FoladNoavaranExternalId).ToListAsync();
        Assert.Contains(rows, row => row.MetricCode == "PE_TTM" && row.Value == Feature148ChatFixtures.LegacyPe);
        Assert.Contains(rows, row => row.MetricCode == "NET_PROFIT_GROWTH_QOQ" && row.Value == Feature148ChatFixtures.MixedNetProfitGrowthQoq);
        Assert.Contains(rows, row => row.MetricCode == "AVG_12M_MONTHLY_SALES" && row.Value == Feature148ChatFixtures.UnattributedLegacyAverage12m);
    }
}

/// <summary>Same questions, same ticker, deliberately different values per provider: the active provider alone decides.</summary>
public sealed class Feature148ProviderIsolationE2ETests(
    Feature148CyclicalWavesChatApiFactory cyclicalWaves,
    Feature148NoavaranAminChatApiFactory noavaranAmin,
    Feature148CyclicalWavesShadowChatApiFactory cyclicalWavesShadow)
    : IClassFixture<Feature148CyclicalWavesChatApiFactory>, IClassFixture<Feature148NoavaranAminChatApiFactory>,
        IClassFixture<Feature148CyclicalWavesShadowChatApiFactory>
{
    [Theory]
    [InlineData("P/E فولاد چقدر است؟", "PE_TTM", 6.05, 9.37)]
    [InlineData("فروش آخرین ماه فولاد چقدر است؟", "MONTHLY_SALES", 639_418_160_000_000.0, 123_456_000_000_000.0)]
    public async Task SwitchingTheProviderSwitchesTheValueAndItsSource(string question, string metric, double cyclicalWavesValue, double noavaranValue)
    {
        foreach (var factory in new Feature148CyclicalWavesChatApiFactory[] { cyclicalWaves, noavaranAmin, cyclicalWavesShadow })
        {
            factory.EnsureSeeded();
            factory.ResetRecorders();
        }

        var fromCyclicalWaves = await Feature148Chat.AskAsync(cyclicalWaves, question);
        var fromNoavaran = await Feature148Chat.AskAsync(noavaranAmin, question);
        var fromCyclicalWavesShadow = await Feature148Chat.AskAsync(cyclicalWavesShadow, question);

        var cw = Feature148Chat.LookupCell(fromCyclicalWaves, Feature148ChatFixtures.Folad, metric);
        var na = Feature148Chat.LookupCell(fromNoavaran, Feature148ChatFixtures.Folad, metric);
        Assert.Equal((decimal)cyclicalWavesValue, cw.GetProperty("value").GetDecimal());
        Assert.Equal("CyclicalWaves", cw.GetProperty("sourceLabel").GetString());
        Assert.Equal((decimal)noavaranValue, na.GetProperty("value").GetDecimal());
        Assert.NotEqual("CyclicalWaves", na.GetProperty("sourceLabel").GetString());
        // The routing mode docker-compose deploys (Shadow) reaches the same provider-selected data.
        Assert.Equal((decimal)cyclicalWavesValue, Feature148Chat.LookupCell(fromCyclicalWavesShadow, Feature148ChatFixtures.Folad, metric).GetProperty("value").GetDecimal());

        Feature148Chat.ReadOnlyCyclicalWavesV2(cyclicalWaves.Reads);
        Feature148Chat.ReadOnlyCyclicalWavesV2(cyclicalWavesShadow.Reads);
        Feature148Chat.ReadNoCyclicalWavesV2(noavaranAmin.Reads);
        Assert.False(cyclicalWaves.Reads.Read<DerivedMetricRow>());
        Feature148Chat.NoOutboundCalls(cyclicalWaves.Http);
        Feature148Chat.NoOutboundCalls(noavaranAmin.Http);
        Feature148Chat.NoOutboundCalls(cyclicalWavesShadow.Http);
        // Neither mode returns an obsolete CyclicalWaves value.
        Assert.DoesNotContain("4.44", Feature148Chat.Text(fromNoavaran));
        Assert.DoesNotContain("999,000,000", Feature148Chat.Text(fromNoavaran));
    }

    [Fact]
    public async Task TheAgentLookupRouteAlsoUsesTheSelectedProvider()
    {
        // V1-style direct legacy lookup tool path (agent route) in CyclicalWaves mode reads only v2.
        cyclicalWaves.EnsureSeeded();
        cyclicalWaves.ResetRecorders();

        var root = await Feature148Chat.AskAsync(cyclicalWaves, "تولید گندله کگل چقدر بوده؟");

        Assert.Contains("QueryInterpretationProposal_v2", cyclicalWaves.Fake.StructuredOutputSchemas);
        Assert.DoesNotContain(cyclicalWaves.Reads.EntityTypes, type => !type.StartsWith("CyclicalWavesV2", StringComparison.Ordinal));
        Feature148Chat.NoOutboundCalls(cyclicalWaves.Http);
        Assert.False(root.GetProperty("clarificationRequired").GetBoolean());
        Assert.Equal(ChatCapabilityAvailability.UnavailableForProviderMessageFa, Feature148Chat.Text(root));
        Assert.Equal(ChatCapabilityAvailability.UnavailableForProviderReasonCode, root.GetProperty("outcomeReasonCode").GetString());
    }
}

/// <summary>The V1 orchestrator cannot bypass provider selection either: its lookup is the CyclicalWaves service.</summary>
public sealed class Feature148V1OrchestrationIsolationE2ETests(Feature148CyclicalWavesV1ChatApiFactory factory)
    : IClassFixture<Feature148CyclicalWavesV1ChatApiFactory>
{
    [Fact]
    public async Task V1Lookup_ReadsOnlyCyclicalWaves()
    {
        factory.EnsureSeeded();
        factory.ResetRecorders();

        var root = await Feature148Chat.AskAsync(factory, "P/E فولاد چقدر است؟");

        Assert.Equal(6.05m, Feature148Chat.LookupCell(root, Feature148ChatFixtures.Folad, "PE_TTM").GetProperty("value").GetDecimal());
        Feature148Chat.ReadOnlyCyclicalWavesV2(factory.Reads);
        Feature148Chat.NoOutboundCalls(factory.Http);
    }
}

/// <summary>
/// T148-08.4 Telegram: gateway API key → POST /api/v1/telegram/assistant/updates → linked account → the real Telegram
/// adapter → the same Chat orchestrator and provider selection → rendered Telegram messages. Nothing is sent to Telegram.
/// </summary>
public sealed class Feature148TelegramE2ETests(Feature148TelegramApiFactory factory, Feature148TelegramNoavaranAminApiFactory noavaranFactory)
    : IClassFixture<Feature148TelegramApiFactory>, IClassFixture<Feature148TelegramNoavaranAminApiFactory>
{
    private static long _nextUpdateId = 148_000;

    [Fact]
    public async Task TelegramMetricLookup_ReadsCyclicalWaves_AndADuplicateUpdateIsReplayed()
    {
        var (gateway, telegramUserId) = await LinkAsync(factory, 1_480_001);
        var request = Update(telegramUserId, "فروش آخرین ماه فولاد چقدر است؟");

        var first = await SendAsync(gateway, request);
        var replay = await SendAsync(gateway, request);

        Assert.Equal(TelegramAssistantResultStatus.Accepted, first.Status);
        Assert.Equal(TelegramAssistantResultStatus.Replayed, replay.Status);
        Assert.Equal(DetectedIntent.SymbolLookup, first.AiResponse!.Intent);
        var cell = first.AiResponse.SymbolLookupTable!.Rows.Single().Cells.Values.Single(value => value.Value is not null);
        Assert.Equal(639_418_160_000_000m, cell.Value);
        Assert.Equal("CyclicalWaves", cell.SourceLabel);
        Assert.Contains("۶۳۹٬۴۱۸٬۱۶۰", Rendered(first));
        Feature148Chat.ReadOnlyCyclicalWavesV2(factory.Reads);
        Feature148Chat.NoOutboundCalls(factory.Http);
    }

    [Fact]
    public async Task TelegramQuarterlyStatement_IsTheStoredCyclicalWavesStatement()
    {
        var (gateway, telegramUserId) = await LinkAsync(factory, 1_480_002);

        var result = await SendAsync(gateway, Update(telegramUserId, "صورت سود و زیان فصل اخیر فولاد را نمایش بده"));

        Assert.Equal(TelegramAssistantResultStatus.Accepted, result.Status);
        var quarterly = result.AiResponse!.FinancialStatementTableResult!.QuarterlyResult!;
        Assert.Equal("CyclicalWavesV2", quarterly.ProviderName);
        Assert.Equal(471_272_793m, quarterly.Rows.Single(row => row.SourceItemId == 66).Cells[quarterly.PeriodsNewestFirst[0].Label].Value);
        Assert.NotEmpty(result.Messages);
        Feature148Chat.ReadOnlyCyclicalWavesV2(factory.Reads);
        Feature148Chat.NoOutboundCalls(factory.Http);
    }

    [Fact]
    public async Task TelegramScreening_RunsTheCyclicalWavesScanner()
    {
        var (gateway, telegramUserId) = await LinkAsync(factory, 1_480_003);

        var result = await SendAsync(gateway, Update(telegramUserId, "سهام با P/E زیر ۵"));

        Assert.Equal(TelegramAssistantResultStatus.Accepted, result.Status);
        Assert.Equal(["شستا", "فملی"], result.AiResponse!.ScannerTable!.Rows.Select(row => row.SymbolCode).Order(StringComparer.Ordinal).ToArray());
        Assert.Contains("فملی", Rendered(result));
        Feature148Chat.NoOutboundCalls(factory.Http);
    }

    [Fact]
    public async Task TelegramMonthlySalesGrowth_UsesTheCyclicalWavesScanner()
    {
        var (gateway, telegramUserId) = await LinkAsync(factory, 1_480_004);

        var result = await SendAsync(gateway, Update(telegramUserId, "نمادهایی با رشد فروش ماهانه بیشتر از ۲۰ درصد"));

        Assert.Equal(TelegramAssistantResultStatus.Accepted, result.Status);
        Assert.Equal(["فولاد"], result.AiResponse!.ScannerTable!.Rows.Select(row => row.SymbolCode).ToArray());
        Assert.DoesNotContain(ChatCapabilityAvailability.UnavailableForProviderMessageFa, Rendered(result));
        Feature148Chat.NoOutboundCalls(factory.Http);
    }

    [Fact]
    public async Task TelegramUsesTheSameProviderSelection_NoavaranAminModeReadsNoavaran()
    {
        var (gateway, telegramUserId) = await LinkAsync(noavaranFactory, 1_480_005);

        var result = await SendAsync(gateway, Update(telegramUserId, "فروش آخرین ماه فولاد چقدر است؟"));

        Assert.Equal(TelegramAssistantResultStatus.Accepted, result.Status);
        var cell = result.AiResponse!.SymbolLookupTable!.Rows.Single().Cells.Values.Single(value => value.Value is not null);
        Assert.Equal(Feature148ChatFixtures.NoavaranMonthlySalesIrr, cell.Value);
        Assert.Contains("۱۲۳٬۴۵۶٬۰۰۰", Rendered(result));
        Assert.DoesNotContain("۶۳۹٬۴۱۸٬۱۶۰", Rendered(result));
        Feature148Chat.ReadNoCyclicalWavesV2(noavaranFactory.Reads);
        Feature148Chat.NoOutboundCalls(noavaranFactory.Http);
    }

    /// <summary>The rendered Telegram text with MarkdownV2 escapes removed.</summary>
    private static string Rendered(TelegramAssistantResult result) =>
        string.Join("\n", result.Messages.Select(message => message.Text)).Replace("\\", string.Empty, StringComparison.Ordinal);

    private static object Update(long telegramUserId, string text) => new
    {
        telegramUpdateId = Interlocked.Increment(ref _nextUpdateId),
        kind = TelegramAssistantUpdateKind.Message,
        telegramUserId,
        telegramChatId = telegramUserId,
        telegramMessageId = 1L,
        text,
        locale = "fa-IR",
        receivedAtUtc = DateTimeOffset.UtcNow,
        correlationId = $"telegram:{telegramUserId}:{Guid.NewGuid():N}"
    };

    private static async Task<TelegramAssistantResult> SendAsync(HttpClient gateway, object request)
    {
        using var response = await gateway.PostAsJsonAsync("/api/v1/telegram/assistant/updates", request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<TelegramAssistantResult>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        })!;
    }

    private static async Task<(HttpClient Gateway, long TelegramUserId)> LinkAsync(Feature148TelegramApiFactory host, long telegramUserId)
    {
        host.EnsureSeeded();
        using var web = host.CreateClient();
        var email = $"feature-148-{Guid.NewGuid():N}@example.test";
        using (var register = await web.PostAsJsonAsync("/api/auth/v1/register", new { email, password = "StrongPassword!123" }))
            register.EnsureSuccessStatusCode();
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinancialCopilot.Infrastructure.Authentication.Persistence.AuthDbContext>();
            var user = await db.Users.SingleAsync(row => row.Email == email);
            user.EmailVerificationRequired = false;
            await db.SaveChangesAsync();
        }

        using (var login = await web.PostAsJsonAsync("/api/auth/v1/login", new { email, password = "StrongPassword!123" }))
        {
            using var session = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
            web.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", session.RootElement.GetProperty("accessToken").GetString());
        }

        string startParameter;
        using (var token = await web.PostAsync("/api/v1/telegram/link-token", null))
        {
            token.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await token.Content.ReadAsStringAsync());
            startParameter = new Uri(json.RootElement.GetProperty("deepLink").GetString()!).Query.Split("start=", 2)[1];
        }

        var gateway = host.CreateClient();
        gateway.DefaultRequestHeaders.Add("X-Api-Key", Feature148TelegramApiFactory.GatewayApiKey);
        using (var confirm = await gateway.PostAsJsonAsync("/api/v1/telegram/link/confirm",
                   new { startParameter, telegramUserId, telegramChatId = telegramUserId, username = "feature148", telegramUpdateId = Interlocked.Increment(ref _nextUpdateId) }))
            confirm.EnsureSuccessStatusCode();

        host.ResetRecorders();
        return (gateway, telegramUserId);
    }
}

/// <summary>Seed values shared by the Feature 148 Chat E2E suites. Both providers carry deliberately different numbers.</summary>
public static class Feature148ChatFixtures
{
    public const string Folad = "فولاد";
    public const string FoladNoavaranExternalId = "1480007";
    public const string FameliNoavaranExternalId = "1480008";

    // NoavaranAmin (DerivedMetrics) values for فولاد; none of them equals a CyclicalWaves value.
    public const decimal NoavaranPe = 9.37m;
    public const decimal NoavaranMonthlySalesIrr = 123_456_000_000_000m;
    public const decimal NoavaranNetProfitIrr = 77_700_000_000_000m;

    // Legacy CyclicalWaves-derived rows (newer periods), which must never be selected.
    public const decimal LegacyPe = 4.44m;
    public const decimal MixedMomGrowth = 66.6m;
    public const decimal MixedNetProfitGrowthQoq = 80m;
    public const decimal UnattributedLegacyAverage12m = 555_000_000_000_000m;

    public static readonly string[] Questions =
    [
        "فروش آخرین ماه فولاد چقدر است؟",
        "سود خالص آخرین فصل فولاد چقدر بوده؟",
        "صورت سود و زیان فصل اخیر فولاد را نمایش بده",
        "روند سود خالص چهار فصل اخیر فولاد چطور بوده؟",
        "سهم‌هایی با P/E کمتر از ۵ را پیدا کن",
        "شرکت‌هایی با P/S کمتر از ۱",
        "نمادهایی با رشد فروش ماهانه بیشتر از ۲۰ درصد",
        "آخرین تحلیل‌های منتشرشده درباره فولاد چیست؟",
        "شرکت‌های صنعت فولاد کدام‌اند؟",
        "تولید گندله کگل چقدر بوده؟"
    ];

    public static string FixturePath(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "tests", "FinancialCopilot.UnitTests")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "tests", "FinancialCopilot.UnitTests", "CyclicalWavesV2", "Fixtures", name);
    }

    public static JsonDocument Fixture(string name) => JsonDocument.Parse(File.ReadAllText(FixturePath(name)));
}

/// <summary>
/// Stands in for the external LLM only. For each scripted question it returns the structured interpretation proposal
/// and scanner parse a production model returns (capability, entities, metric hints, conditions); numbers are always
/// computed by the application, never by this fake. Unscripted requests get an empty proposal.
/// </summary>
public sealed class Feature148ScriptedAiModelClient : IAiModelClient
{
    private static string Proposal(string capability, string? entity = null, string[]? metrics = null, string? period = null,
        int? periodCount = null, string? presentation = null, string[]? unsupported = null) =>
        JsonSerializer.Serialize(new
        {
            capabilityCodes = new[] { capability },
            missingSlots = Array.Empty<string>(),
            presentation,
            confidence = 0.93m,
            evidence = Array.Empty<string>(),
            intent = capability,
            entities = entity is null ? [] : new[] { new { text = entity, entityType = "company", scope = (string?)null, confidence = 0.95m } },
            metricHints = metrics ?? [],
            period,
            comparison = (string?)null,
            periodCount,
            operations = Array.Empty<string>(),
            unsupportedParts = unsupported ?? []
        });

    private static string ScannerParse(string term, string op, decimal threshold, string? growth = null, string? periodHint = null) =>
        JsonSerializer.Serialize(new
        {
            detectedLanguage = "fa",
            conditions = new[]
            {
                new { userTerminology = term, language = "fa", @operator = op, threshold, periodHint, growthComparison = growth, inferredDefault = false, inferredReason = (string?)null }
            },
            requestedColumns = Array.Empty<string>(),
            clarificationRequired = false,
            clarificationMessage = (string?)null
        });

    private static string LookupParse(string symbol, string metric) =>
        JsonSerializer.Serialize(new
        {
            detectedLanguage = "fa",
            pairs = new[] { new { symbolName = symbol, metricTerm = metric } },
            clarificationRequired = false,
            clarificationMessage = (string?)null
        });

    private static (string Name, string Arguments) Tool(string name, string? symbol = null) =>
        name == "query_comprehensive_analysis"
            ? (name, JsonSerializer.Serialize(new { symbolNames = new[] { symbol }, topicTags = Array.Empty<string>(), fromDateIso = (string?)null, limit = 5 }))
            : (name, JsonSerializer.Serialize(new { query = symbol ?? string.Empty }));

    public sealed record Turn(string Proposal, string? ScannerParse = null, string? LookupParse = null, (string Name, string Arguments)? ToolCall = null);

    /// <summary>What a production model returns for each scripted question.</summary>
    public static readonly IReadOnlyDictionary<string, Turn> Script = new Dictionary<string, Turn>(StringComparer.Ordinal)
    {
        ["فروش آخرین ماه فولاد چقدر است؟"] = new(Proposal("symbol_metric_lookup", "فولاد", ["MONTHLY_SALES"], "latest_month"),
            LookupParse: LookupParse("فولاد", "فروش آخرین ماه"), ToolCall: Tool("lookup_symbol_metrics", "فروش آخرین ماه فولاد")),
        ["سود خالص آخرین فصل فولاد چقدر بوده؟"] = new(Proposal("symbol_metric_lookup", "فولاد", ["NET_PROFIT"], "latest_quarter"),
            LookupParse: LookupParse("فولاد", "سود خالص آخرین فصل"), ToolCall: Tool("lookup_symbol_metrics", "سود خالص آخرین فصل فولاد")),
        ["صورت سود و زیان فصل اخیر فولاد را نمایش بده"] = new(Proposal("financial_statement_table", "فولاد", presentation: "Table")),
        ["روند سود خالص چهار فصل اخیر فولاد چطور بوده؟"] = new(Proposal("financial_statement_table", "فولاد", ["NET_PROFIT"], "latest_quarter", periodCount: 4, presentation: "Table")),
        ["سهم‌هایی با P/E کمتر از ۵ را پیدا کن"] = new(Proposal("stock_screening", metrics: ["PE_TTM"]),
            ScannerParse: ScannerParse("P/E", "LessThan", 5m), ToolCall: Tool("screen_stocks", "P/E کمتر از ۵")),
        ["شرکت‌هایی با P/S کمتر از ۱"] = new(Proposal("stock_screening", metrics: ["PS_TTM"]),
            ScannerParse: ScannerParse("P/S", "LessThan", 1m), ToolCall: Tool("screen_stocks", "P/S کمتر از ۱")),
        ["نمادهایی با رشد فروش ماهانه بیشتر از ۲۰ درصد"] = new(Proposal("stock_screening", metrics: ["MONTHLY_SALES_GROWTH_MOM"]),
            ScannerParse: ScannerParse("رشد فروش ماهانه", "GreaterThan", 20m, "MonthOverMonth", "Monthly"), ToolCall: Tool("screen_stocks", "رشد فروش ماهانه بیشتر از ۲۰ درصد")),
        ["آخرین تحلیل‌های منتشرشده درباره فولاد چیست؟"] = new(Proposal("comprehensive_analysis", "فولاد", presentation: "Summary"),
            ToolCall: Tool("query_comprehensive_analysis", "فولاد")),
        // The model finds no supported capability for industry membership and calls no tool.
        ["شرکت‌های صنعت فولاد کدام‌اند؟"] = new(JsonSerializer.Serialize(new
        {
            capabilityCodes = Array.Empty<string>(), missingSlots = Array.Empty<string>(), presentation = (string?)null, confidence = 0.4m,
            evidence = Array.Empty<string>(), intent = (string?)null, entities = Array.Empty<object>(), metricHints = Array.Empty<string>(),
            period = (string?)null, comparison = (string?)null, periodCount = (int?)null, operations = Array.Empty<string>(),
            unsupportedParts = new[] { "industry membership" }
        })),
        ["تولید گندله کگل چقدر بوده؟"] = new(Proposal("product_sales_value", "کگل", ["MONTHLY_PRODUCTION_QUANTITY"], "latest_month"),
            LookupParse: LookupParse("کگل", "تولید گندله"), ToolCall: Tool("lookup_symbol_metrics", "تولید گندله کگل")),
        // Phrasings the existing router supports for the same five scenario types.
        ["سود خالص فصل اخیر فولاد چقدر است؟"] = new(Proposal("symbol_metric_lookup", "فولاد", ["NET_PROFIT"], "latest_quarter"),
            LookupParse: LookupParse("فولاد", "سود خالص فصل اخیر")),
        ["صورت سود و زیان چهار فصل اخیر فولاد را نمایش بده"] = new(Proposal("financial_statement_table", "فولاد", ["NET_PROFIT"], periodCount: 4, presentation: "Table")),
        ["سهام با P/E زیر ۵"] = new(Proposal("stock_screening", metrics: ["PE_TTM"]), ScannerParse: ScannerParse("P/E", "LessThan", 5m)),
        ["سهام با P/S زیر ۱"] = new(Proposal("stock_screening", metrics: ["PS_TTM"]), ScannerParse: ScannerParse("P/S", "LessThan", 1m)),
        ["سهام با رشد فروش ماهانه بالای ۲۰ درصد نسبت به ماه قبل"] = new(Proposal("stock_screening", metrics: ["MONTHLY_SALES_GROWTH_MOM"]),
            ScannerParse: ScannerParse("رشد فروش ماهانه", "GreaterThan", 20m, "MonthOverMonth", "Monthly")),
        ["P/E فولاد چقدر است؟"] = new(Proposal("symbol_metric_lookup", "فولاد", ["PE_TTM"]), LookupParse: LookupParse("فولاد", "P/E")),
        ["متوسط فروش ۱۲ ماهه فولاد چقدر است؟"] = new(Proposal("symbol_metric_lookup", "فولاد", ["AVG_12M_MONTHLY_SALES"]),
            LookupParse: LookupParse("فولاد", "متوسط فروش ۱۲ ماهه")),
        ["رشد فروش ماهانه فولاد چقدر است؟"] = new(Proposal("symbol_metric_lookup", "فولاد", ["MONTHLY_SALES_GROWTH_MOM"]),
            LookupParse: LookupParse("فولاد", "رشد فروش ماهانه")),
        ["P/E و رشد فروش ماهانه فولاد چقدر است؟"] = new(Proposal("symbol_metric_lookup", "فولاد", ["PE_TTM", "MONTHLY_SALES_GROWTH_MOM"]),
            LookupParse: LookupParse("فولاد", "رشد فروش ماهانه")),
        ["سهام با رشد سود خالص فصلی بالای ۵۰ درصد"] = new(Proposal("stock_screening", metrics: ["NET_PROFIT_GROWTH_QOQ"]),
            ScannerParse: ScannerParse("رشد سود خالص فصلی", "GreaterThan", 50m, "QuarterOverQuarter", "Quarterly")),
        ["سهام با NET_PROFIT_GROWTH_QOQ بالای ۵۰"] = new(Proposal("stock_screening", metrics: ["NET_PROFIT_GROWTH_QOQ"]),
            ScannerParse: ScannerParse("NET_PROFIT_GROWTH_QOQ", "GreaterThan", 50m, "QuarterOverQuarter", "Quarterly")),
        ["سهام با رشد فروش ماهانه بالای ۵۰ درصد"] = new(Proposal("stock_screening", metrics: ["MONTHLY_SALES_GROWTH_MOM"]),
            ScannerParse: ScannerParse("رشد فروش ماهانه", "GreaterThan", 50m, "MonthOverMonth", "Monthly")),
        ["وضعیت گیج P/E فولاد چگونه است؟"] = new(Proposal("comprehensive_analysis", "فولاد"),
            ToolCall: Tool("query_cyclicalwaves_gauges", "وضعیت گیج P/E فولاد چگونه است؟")),
        ["P/E فولاد نسبت به میانگین تاریخی خودش در چه وضعیتی قرار دارد؟"] = new(Proposal("comprehensive_analysis", "فولاد"),
            ToolCall: Tool("query_cyclicalwaves_gauges", "P/E فولاد نسبت به میانگین تاریخی خودش در چه وضعیتی قرار دارد؟")),
        ["مقدار فعلی P/S فولاد نسبت به محدوده تاریخی آن کجاست؟"] = new(Proposal("comprehensive_analysis", "فولاد"),
            ToolCall: Tool("query_cyclicalwaves_gauges", "مقدار فعلی P/S فولاد نسبت به محدوده تاریخی آن کجاست؟")),
        ["حداقل، میانگین و حداکثر قیمت تعادلی کچاد چقدر است؟"] = new(Proposal("comprehensive_analysis", "کچاد"),
            ToolCall: Tool("query_cyclicalwaves_gauges", "حداقل، میانگین و حداکثر قیمت تعادلی کچاد چقدر است؟")),
        // BUG-022: the model proposes another capability for «تحلیل X»; the dialogue gate must keep the analysis route.
        ["تحلیل فولاد"] = new(Proposal("symbol_metric_lookup", "فولاد", ["PE_TTM"]), LookupParse: LookupParse("فولاد", "P/E")),
        ["قیمت تعادلی فولاد چقدر است؟"] = new(Proposal("comprehensive_analysis", "فولاد"),
            ToolCall: Tool("query_cyclicalwaves_gauges", "قیمت تعادلی فولاد چقدر است؟")),
        ["کدام نمادها در وضعیت P/E زیر حداقل تاریخی قرار دارند؟"] = new(Proposal("comprehensive_analysis"),
            ToolCall: Tool("query_cyclicalwaves_gauges", "کدام نمادها در وضعیت P/E زیر حداقل تاریخی قرار دارند؟")),
        ["کدام نمادها در وضعیت P/S زیر میانگین تاریخی قرار دارند؟"] = new(Proposal("comprehensive_analysis"),
            ToolCall: Tool("query_cyclicalwaves_gauges", "کدام نمادها در وضعیت P/S زیر میانگین تاریخی قرار دارند؟"))
    };


    /// <summary>Tool calls the model made, in order (agent route only).</summary>
    public ConcurrentQueue<string> ToolCalls { get; } = new();

    private readonly ConcurrentQueue<string> _schemas = new();

    public IReadOnlyCollection<string> StructuredOutputSchemas => _schemas.ToArray();

    public void Reset()
    {
        _schemas.Clear();
        ToolCalls.Clear();
    }

    public AiModelProviderDescriptor Descriptor { get; } = new(
        "Feature148ScriptedFake", "fake-v2", AiProviderHostingMode.Fake,
        AiModelCapability.ChatCompletion | AiModelCapability.ToolCalling | AiModelCapability.StructuredOutput |
        AiModelCapability.UsageReporting | AiModelCapability.HealthCheck,
        Enabled: true, Priority: 1);

    public Task<AiModelResult> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken)
    {
        var usage = new AiExecutionUsageFacts(request.CorrelationId, Descriptor.ProviderKey, Descriptor.ModelKey,
            AiExecutionStatus.Completed, TimeSpan.Zero, AttemptNumber: 0, InputTokens: 10, OutputTokens: 4, UsedTools: false);
        var schema = request.StructuredOutput?.SchemaName;
        if (schema is not null) _schemas.Enqueue(schema);
        var text = string.Join("\n", request.Messages.Where(message => message.Role == AiMessageRole.User).Select(message => message.Content));
        // Telegram normalizes text (digits, ZWNJ, spacing) before the orchestrator, so match on a normalized form.
        var normalizedText = Normalize(text);
        var turn = Script.FirstOrDefault(entry => normalizedText.Contains(Normalize(entry.Key), StringComparison.Ordinal)).Value;
        if (request.Tools is { Count: > 0 })
        {
            // Agent route: call the scripted tool once, then answer with fixed text (assertions never use it).
            var answeredTool = request.PreviousResponseId is not null || request.Messages.Any(message => message.Role == AiMessageRole.Tool);
            if (!answeredTool && turn?.ToolCall is { } call && request.Tools.Any(tool => tool.Name == call.Name))
            {
                ToolCalls.Enqueue(call.Name);
                return Task.FromResult(new AiModelResult(null, null,
                    [new AiToolCall($"call-{request.CorrelationId}", call.Name, call.Arguments)], usage, ResponseId: $"resp-{request.CorrelationId}"));
            }

            return Task.FromResult(new AiModelResult("پاسخ مدل", null, [], usage));
        }

        var json = schema switch
        {
            "QueryInterpretationProposal_v2" => turn?.Proposal ?? "{}",
            "ScannerParseOutput" => turn?.ScannerParse ?? "{}",
            "SymbolLookupParseOutput" => turn?.LookupParse ?? "{}",
            _ => "{}"
        };
        return Task.FromResult(new AiModelResult(null, json, [], usage));
    }

    private static string Normalize(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value.Replace('\u200c', ' ').Replace('ي', 'ی').Replace('ك', 'ک'))
        {
            builder.Append(ch is >= '۰' and <= '۹' ? (char)('0' + (ch - '۰')) : ch is >= '٠' and <= '٩' ? (char)('0' + (ch - '٠')) : ch);
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public IAsyncEnumerable<AiStreamingChunk> StreamAsync(AiModelRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<AiEmbeddingResult> CreateEmbeddingsAsync(AiEmbeddingRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<AiProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AiProviderHealthResult(Descriptor.ProviderKey, Descriptor.ModelKey, true, DateTimeOffset.UtcNow, "OK"));
}

/// <summary>Records every entity type the ingestion context materializes, so a test can prove which tables were read.</summary>
public sealed class Feature148TableReadRecorder : IMaterializationInterceptor
{
    private readonly ConcurrentDictionary<string, int> _types = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> EntityTypes => _types.Keys.Order(StringComparer.Ordinal).ToArray();

    public bool Read<TRow>() => _types.ContainsKey(typeof(TRow).Name);

    public void Reset() => _types.Clear();

    public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
    {
        _types.AddOrUpdate(entity.GetType().Name, 1, (_, count) => count + 1);
        return entity;
    }
}

/// <summary>
/// Records every outbound HTTP attempt from any <see cref="IHttpClientFactory"/> client, before any other handler
/// (so a call blocked by the legacy guard is still recorded), and never transmits.
/// </summary>
public sealed class Feature148OutboundHttpRecorder : IHttpMessageHandlerBuilderFilter
{
    private readonly ConcurrentQueue<string> _attempts = new();

    public IReadOnlyCollection<string> Attempts => _attempts.ToArray();

    public void Reset() => _attempts.Clear();

    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
    {
        next(builder);
        builder.AdditionalHandlers.Insert(0, new AttemptHandler(builder.Name ?? "default", _attempts));
        builder.PrimaryHandler = new NeverTransmitHandler();
    };

    private sealed class AttemptHandler(string clientName, ConcurrentQueue<string> attempts) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            attempts.Enqueue($"{clientName} {request.Method} {request.RequestUri?.Host}{request.RequestUri?.AbsolutePath}");
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class NeverTransmitHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}

/// <summary>
/// Full production Chat pipeline (API key auth, conversation, MAF V2 orchestration, semantic routing, provider
/// selection, capability executors, presentation, billing, persistence) with the provider chosen by
/// <see cref="ChatProvider"/>. Only the LLM is a fake; the ingestion store is an in-memory EF database seeded through the
/// real CyclicalWaves v2 parser and store from recorded provider payloads.
/// </summary>
public class Feature148CyclicalWavesChatApiFactory : AiFacadeApiFactory
{
    private readonly string _dbName = $"feature148-chat-{Guid.NewGuid():N}";
    private readonly object _seedLock = new();
    private bool _seeded;

    public Feature148ScriptedAiModelClient Fake { get; } = new();
    public Feature128ExecutorProbe ExecutorProbe { get; } = new();
    public Feature148TableReadRecorder Reads { get; } = new();
    public Feature148OutboundHttpRecorder Http { get; } = new();

    protected override bool ForceV1Orchestration => false;
    protected virtual string ChatProvider => "CyclicalWaves";
    protected virtual string SemanticDefaultMode => "SemanticPrimary";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        Feature148ChatHost.Configure(builder, ChatProvider, SemanticDefaultMode, _dbName, Fake, Reads, Http, ExecutorProbe);
    }

    public void ResetRecorders()
    {
        Reads.Reset();
        Http.Reset();
        ExecutorProbe.Reset();
        Fake.Reset();
    }

    public void EnsureSeeded()
    {
        EnsureBillingSeeded();
        if (_seeded) return;
        lock (_seedLock)
        {
            if (_seeded) return;
            Feature148ChatHost.Seed(Services);
            _seeded = true;
        }

        ResetRecorders();
    }
}

/// <summary>Feature 148 E2E host wiring and seed, shared by the Chat API and Telegram factories.</summary>
public static class Feature148ChatHost
{
    public static void Configure(IWebHostBuilder builder, string chatProvider, string semanticDefaultMode, string databaseName,
        IAiModelClient fake, Feature148TableReadRecorder reads, Feature148OutboundHttpRecorder http, Feature128ExecutorProbe probe)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AiOrchestration:Mode"] = "MicrosoftAgentFrameworkV2",
            ["SemanticRouting:DefaultMode"] = semanticDefaultMode,
            ["CyclicalWavesPsVisualization:Enabled"] = "true",
            ["CyclicalWavesPsVisualization:EnableStandaloneGauge"] = "true",
            ["CyclicalWavesPsVisualization:IncludeHistoryInStandaloneGauge"] = "false",
            ["CyclicalWavesPsVisualization:IncludeGaugeInMonthlySalesTrendChart"] = "false"
        }));
        // Read while services are composed, so it must be a host setting.
        builder.UseSetting("FinancialData:ChatProvider", chatProvider);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<FinancialIngestionDbContext>();
            services.RemoveAll<DbContextOptions<FinancialIngestionDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<FinancialIngestionDbContext>>();
            services.AddDbContext<FinancialIngestionDbContext>(options => options
                .UseInMemoryDatabase(databaseName)
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .AddInterceptors(reads));
            services.RemoveAll<IAiModelClient>();
            services.AddSingleton<IAiModelClient>(fake);
            services.RemoveAll<ISemanticCapabilityExecutionObserver>();
            services.AddSingleton<ISemanticCapabilityExecutionObserver>(probe);
            services.AddSingleton<IHttpMessageHandlerBuilderFilter>(http);
        });
    }

    public static void Seed(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
        db.Database.EnsureCreated();
        SeedCyclicalWavesV2(db).GetAwaiter().GetResult();
        SeedNoavaranAmin(db);
    }

    private static async Task SeedCyclicalWavesV2(FinancialIngestionDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var store = new EfCoreCyclicalWavesV2Store(db, NullLogger<EfCoreCyclicalWavesV2Store>.Instance);
        await store.UpsertCatalogAsync(["فولاد", "فملی", "شستا", "کگل", "وبملت", "کچاد"], now, CancellationToken.None);

        using (var lines = Feature148ChatFixtures.Fixture("lines.json"))
            await store.UpsertLineDefinitionsAsync(CyclicalWavesV2PayloadParser.ParseLineDefinitions(lines.RootElement), now, CancellationToken.None);
        using (var screener = Feature148ChatFixtures.Fixture("screener-folad.json"))
            await store.ApplyScreenerAsync(CyclicalWavesV2PayloadParser.ParseScreener(screener.RootElement), now, CancellationToken.None);
        using (var statements = Feature148ChatFixtures.Fixture("statements-folad.json"))
            await store.ApplyIncomeStatementsAsync(CyclicalWavesV2PayloadParser.ParseIncomeStatements(statements.RootElement), now, CancellationToken.None);

        // Synthetic screener rows (IRR) that make the scanners discriminate.
        await store.ApplyScreenerAsync(Screener("فملی", pe: 4.2m, ps: 0.9m, lastMonthSale: 400_000_000_000_000m, penultimateMonthSale: 300_000_000_000_000m), now, CancellationToken.None);
        await store.ApplyScreenerAsync(Screener("شستا", pe: 3.1m, ps: 2.5m, lastMonthSale: 110_000_000_000_000m, penultimateMonthSale: 100_000_000_000_000m), now, CancellationToken.None);
        await store.ApplyScreenerAsync(Screener("کگل", pe: 7.5m, ps: 0.7m, lastMonthSale: 250_000_000_000_000m, penultimateMonthSale: 200_000_000_000_000m), now, CancellationToken.None);
        await store.ApplyScreenerAsync(Screener("وبملت", pe: 0m, ps: 0.4m, lastMonthSale: null, penultimateMonthSale: null), now, CancellationToken.None);

        var tickerIds = await db.CyclicalWavesV2Tickers.ToDictionaryAsync(row => row.Ticker, row => row.Id);
        db.CyclicalWavesV2GaugeAvailability.AddRange(tickerIds.Select(entry => new CyclicalWavesV2GaugeAvailabilityRow
        {
            TickerId = entry.Value,
            HasPe = entry.Key is "فولاد" or "فملی" or "کگل",
            HasPs = entry.Key is "فولاد" or "فملی" or "شستا" or "کگل" or "وبملت",
            LastFetchedAtUtc = now
        }));
        db.CyclicalWavesV2GaugeDetails.AddRange(
            Gauge(tickerIds["فولاد"], "PE", "5.9751", "2.2886", "38.8073", "3.4315", "5.8271", "9.4010", 3, now),
            Gauge(tickerIds["فولاد"], "PS", "1.7531", "0.5432", "12.2804", "0.9774", "1.7614", "2.7264", 2, now),
            Gauge(tickerIds["فملی"], "PE", null, "1", "10", "2", "4", "8", null, now),
            Gauge(tickerIds["فملی"], "PS", null, "1", "10", "2", "4", "8", null, now),
            Gauge(tickerIds["شستا"], "PS", "5", "0.5432", "12.2804", "0.9774", "1.7614", "2.7264", 4, now),
            Gauge(tickerIds["کگل"], "PE", "0.5", "1", "10", "2", "4", "8", 1, now),
            Gauge(tickerIds["کگل"], "PS", "0.8", "0.5432", "12.2804", "0.9774", "1.7614", "2.7264", 1, now),
            Gauge(tickerIds["وبملت"], "PS", "0.4", "0.5432", "12.2804", "0.9774", "1.7614", "2.7264", 1, now));
        db.CyclicalWavesV2EquilibriumRanges.AddRange(
            new CyclicalWavesV2EquilibriumRangeRow
            {
                TickerId = tickerIds["فولاد"], HasRange = true, Min = ExactDecimal.Parse("3236.7000000000000000001"),
                Avg = ExactDecimal.Parse("7232.1700000000000000002"), Max = ExactDecimal.Parse("11227.6500000000000000003"), FetchedAtUtc = now
            },
            new CyclicalWavesV2EquilibriumRangeRow
            {
                TickerId = tickerIds["کچاد"], HasRange = true, Min = ExactDecimal.Parse("-3236.7000000000000000001"),
                Avg = ExactDecimal.Parse("-7232.1700000000000000002"), Max = ExactDecimal.Parse("-11227.6500000000000000003"), FetchedAtUtc = now
            });

        db.CyclicalWavesV2Analyses.Add(new CyclicalWavesV2AnalysisRow
        {
            Id = 148001,
            Title = "فولاد- گزارش عملکرد شهریور ۱۴۰۵",
            Summary = "<p>فروش شهریور فولاد ۱۷ درصد رشد کرد</p>",
            PlainTextSummary = "فروش شهریور فولاد ۱۷ درصد رشد کرد",
            PublishedAt = now.AddDays(-2),
            ContentHash = new string('1', 64),
            FirstSyncedAt = now,
            LastSyncedAt = now,
            UpdatedAt = now
        });
        db.CyclicalWavesV2AnalysisTags.Add(new CyclicalWavesV2AnalysisTagRow
        {
            AnalysisId = 148001, ProviderTagId = 7, Name = "فولاد", NormalizedName = "فولاد"
        });
        await db.SaveChangesAsync();
    }

    private static CyclicalWavesV2GaugeDetailRow Gauge(Guid tickerId, string type, string? close, string start, string end,
        string min, string avg, string max, int? status, DateTimeOffset now) => new()
    {
        TickerId = tickerId, GaugeType = type, A = 414, B = 848, C = 1490, D = 851, E = 534, F = 506,
        Close = close is null ? null : ExactDecimal.Parse(close), Start = ExactDecimal.Parse(start), End = ExactDecimal.Parse(end),
        Min = ExactDecimal.Parse(min), Avg = ExactDecimal.Parse(avg), Max = ExactDecimal.Parse(max), Status = status, FetchedAtUtc = now
    };

    private static CyclicalWavesV2ScreenerSnapshot Screener(string ticker, decimal pe, decimal ps, decimal? lastMonthSale, decimal? penultimateMonthSale) =>
        new(ticker, null, new Dictionary<string, decimal?>
        {
            ["pe"] = pe,
            ["ps"] = ps,
            ["lastMonthSale"] = lastMonthSale,
            ["penultimateMonthSale"] = penultimateMonthSale
        }, new DateOnly(2026, 9, 22), new DateOnly(2026, 9, 22), DateTimeOffset.UtcNow);

    private static void SeedNoavaranAmin(FinancialIngestionDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.Parse("14800000-0000-0000-0000-000000000007"),
            ProviderName = ProviderSources.NoavaranCurrentApiName,
            ExternalCompanyId = Feature148ChatFixtures.FoladNoavaranExternalId,
            CompanySymbol = Feature148ChatFixtures.Folad,
            TseSymbol = Feature148ChatFixtures.Folad,
            Name = "فولاد مبارکه اصفهان",
            LastSynchronizedAt = now
        });

        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.Parse("14800000-0000-0000-0000-000000000008"),
            ProviderName = ProviderSources.NoavaranCurrentApiName,
            ExternalCompanyId = Feature148ChatFixtures.FameliNoavaranExternalId,
            CompanySymbol = "فملی",
            TseSymbol = "فملی",
            Name = "ملی صنایع مس ایران",
            LastSynchronizedAt = now
        });

        const string noavaran = "[{\"sourceProvider\":\"NoavaranCurrentApi\"}]";
        const string legacy = "[{\"sourceProvider\":\"CyclicalWaves\"}]";
        const string mixed = "[{\"sourceProvider\":\"CyclicalWaves\"},{\"sourceProvider\":\"NoavaranCurrentApi\"}]";
        db.DerivedMetrics.AddRange(
            Derived("PE_TTM", Feature148ChatFixtures.NoavaranPe, "Ratio", new DateOnly(2026, 6, 21), noavaran, "vendor-pe-ratio-passthrough-v1"),
            Derived("PE_TTM", Feature148ChatFixtures.LegacyPe, "Ratio", new DateOnly(2026, 9, 22), legacy, "vendor-pe-ratio-passthrough-v1"),
            Derived("MONTHLY_SALES", Feature148ChatFixtures.NoavaranMonthlySalesIrr, "IRR", new DateOnly(2026, 8, 22), noavaran, "monthly-sales-source-v1"),
            Derived("MONTHLY_SALES", 999_000_000_000_000m, "IRR", new DateOnly(2026, 9, 22), legacy, "monthly-sales-source-v1"),
            Derived("MONTHLY_SALES_GROWTH_MOM", Feature148ChatFixtures.MixedMomGrowth, "Percent", new DateOnly(2026, 9, 22), mixed, "mom-monthly-sales-v1"),
            Derived("NET_PROFIT_GROWTH_QOQ", Feature148ChatFixtures.MixedNetProfitGrowthQoq, "Percent", new DateOnly(2026, 9, 22), mixed, "qoq-net-profit-v1"),
            Derived("AVG_12M_MONTHLY_SALES", Feature148ChatFixtures.UnattributedLegacyAverage12m, "IRR", new DateOnly(2026, 9, 22), "[]", "cw-avg-12m-monthly-sales-passthrough-v1"),
            Derived("NET_PROFIT", Feature148ChatFixtures.NoavaranNetProfitIrr, "IRR", new DateOnly(2026, 6, 21), noavaran, "cw-net-profit-passthrough-v1"),
            Derived("NET_PROFIT", 888_000_000_000_000m, "IRR", new DateOnly(2026, 9, 22), legacy, "cw-net-profit-passthrough-v1"),
            // Positive controls: valid NoavaranAmin rows that must stay selectable.
            Derived("PE_TTM", 3.3m, "Ratio", new DateOnly(2026, 6, 21), noavaran, "vendor-pe-ratio-passthrough-v1", Feature148ChatFixtures.FameliNoavaranExternalId),
            Derived("NET_PROFIT_GROWTH_QOQ", 60m, "Percent", new DateOnly(2026, 6, 21), noavaran, "qoq-net-profit-v1", Feature148ChatFixtures.FameliNoavaranExternalId));
        db.SaveChanges();
    }

    private static DerivedMetricRow Derived(string code, decimal value, string unit, DateOnly periodEnd, string evidence, string policy,
        string externalCompanyId = Feature148ChatFixtures.FoladNoavaranExternalId) => new()
    {
        Id = Guid.NewGuid(),
        ExternalCompanyId = externalCompanyId,
        MetricCode = code,
        MetricVersion = "v1",
        CalculationPolicyVersion = policy,
        PeriodType = code.StartsWith("MONTHLY", StringComparison.Ordinal) || code.StartsWith("AVG_12M", StringComparison.Ordinal) ? "Monthly" : "ThreeMonths",
        PeriodStart = periodEnd.AddMonths(code.StartsWith("MONTHLY", StringComparison.Ordinal) ? -1 : -3).AddDays(1),
        PeriodEnd = periodEnd,
        Value = value,
        Unit = unit,
        ObservedAt = DateTimeOffset.UtcNow.AddDays(-1),
        LastSynchronizedAt = DateTimeOffset.UtcNow.AddDays(-1),
        SourceEvidenceJson = evidence,
        DependencyEvidenceJson = "[]"
    };
}

public sealed class Feature148NoavaranAminChatApiFactory : Feature148CyclicalWavesChatApiFactory
{
    protected override string ChatProvider => "NoavaranAmin";
}

/// <summary>The V1 orchestrator (still selectable through <c>AiOrchestration:Mode</c>) with the CyclicalWaves provider.</summary>
public sealed class Feature148CyclicalWavesV1ChatApiFactory : Feature148CyclicalWavesChatApiFactory
{
    protected override bool ForceV1Orchestration => true;
}

/// <summary>The routing mode docker-compose deploys (<c>SemanticRouting__DefaultMode=Shadow</c>): the V2 agent route runs.</summary>
public sealed class Feature148CyclicalWavesShadowChatApiFactory : Feature148CyclicalWavesChatApiFactory
{
    protected override string SemanticDefaultMode => "Shadow";
}

public sealed class Feature148NoavaranAminShadowChatApiFactory : Feature148CyclicalWavesChatApiFactory
{
    protected override string ChatProvider => "NoavaranAmin";
    protected override string SemanticDefaultMode => "Shadow";
}

/// <summary>
/// Telegram gateway → <c>POST /api/v1/telegram/assistant/updates</c> → the real Telegram adapter → the same Chat
/// orchestrator and provider selection, with the Feature 148 seed. Only the LLM is a fake; nothing is sent to Telegram.
/// </summary>
public class Feature148TelegramApiFactory : OwnedIdentityApiFactory
{
    public const string GatewayApiKey = "feature-148-telegram-gateway-key";
    private readonly string _dbName = $"feature148-telegram-{Guid.NewGuid():N}";
    private readonly string _conversationDatabaseName = $"feature148-telegram-conversations-{Guid.NewGuid():N}";
    private readonly string _memoryDatabaseName = $"feature148-telegram-memory-{Guid.NewGuid():N}";
    private readonly string _authDatabaseName = $"feature148-telegram-auth-{Guid.NewGuid():N}";
    private readonly object _seedLock = new();
    private bool _seeded;

    public Feature148ScriptedAiModelClient Fake { get; } = new();
    public Feature128ExecutorProbe ExecutorProbe { get; } = new();
    public Feature148TableReadRecorder Reads { get; } = new();
    public Feature148OutboundHttpRecorder Http { get; } = new();

    protected virtual string ChatProvider => "CyclicalWaves";
    protected virtual string SemanticDefaultMode => "Shadow";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var keyHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(GatewayApiKey)));
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:ApiKeys:Clients:2:ClientId"] = "14800000-0000-0000-0000-000000000148",
                ["Authentication:ApiKeys:Clients:2:TenantId"] = "11111111-1111-1111-1111-111111111111",
                ["Authentication:ApiKeys:Clients:2:Name"] = "Feature 148 TelegramGateway",
                ["Authentication:ApiKeys:Clients:2:KeySha256"] = keyHash,
                ["Authentication:ApiKeys:Clients:2:IsActive"] = "true",
                ["Authentication:ApiKeys:Clients:2:AllowedPathPrefixes:0"] = "/api/v1/telegram/assistant/updates",
                ["Authentication:ApiKeys:Clients:2:AllowedPathPrefixes:1"] = "/api/v1/telegram/link/confirm"
            });
        });
        Feature148ChatHost.Configure(builder, ChatProvider, SemanticDefaultMode, _dbName, Fake, Reads, Http, ExecutorProbe);
        builder.ConfigureTestServices(services =>
        {
            // As in the Feature 130 Telegram tests: the SQLite auth store cannot generate the link-token row version.
            services.RemoveAll<FinancialCopilot.Infrastructure.Authentication.Persistence.AuthDbContext>();
            services.RemoveAll<DbContextOptions<FinancialCopilot.Infrastructure.Authentication.Persistence.AuthDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<FinancialCopilot.Infrastructure.Authentication.Persistence.AuthDbContext>>();
            services.AddDbContext<FinancialCopilot.Infrastructure.Authentication.Persistence.AuthDbContext>(options =>
                options.UseInMemoryDatabase(_authDatabaseName));
            services.RemoveAll<FinancialCopilot.Infrastructure.Conversations.Persistence.ConversationDbContext>();
            services.RemoveAll<DbContextOptions<FinancialCopilot.Infrastructure.Conversations.Persistence.ConversationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<FinancialCopilot.Infrastructure.Conversations.Persistence.ConversationDbContext>>();
            services.AddDbContext<FinancialCopilot.Infrastructure.Conversations.Persistence.ConversationDbContext>(options =>
                options.UseInMemoryDatabase(_conversationDatabaseName));
            services.RemoveAll<FinancialCopilot.Infrastructure.Memory.Persistence.MemoryDbContext>();
            services.RemoveAll<DbContextOptions<FinancialCopilot.Infrastructure.Memory.Persistence.MemoryDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<FinancialCopilot.Infrastructure.Memory.Persistence.MemoryDbContext>>();
            services.AddDbContext<FinancialCopilot.Infrastructure.Memory.Persistence.MemoryDbContext>(options =>
                options.UseInMemoryDatabase(_memoryDatabaseName));
            services.RemoveAll<FinancialCopilot.Application.Scanner.IScannerCache>();
            services.AddSingleton<FinancialCopilot.Application.Scanner.IScannerCache, FinancialCopilot.Application.Scanner.NoOpScannerCache>();
            services.RemoveAll<IAiModelProviderRoutingPolicy>();
            services.AddSingleton<IAiModelProviderRoutingPolicy>(new TestAiModelProviderRoutingPolicy());
        });
    }

    public void ResetRecorders()
    {
        Reads.Reset();
        Http.Reset();
        ExecutorProbe.Reset();
        Fake.Reset();
    }

    public void EnsureSeeded()
    {
        if (_seeded) return;
        lock (_seedLock)
        {
            if (_seeded) return;
            Feature148ChatHost.Seed(Services);
            _seeded = true;
        }

        ResetRecorders();
    }
}

public sealed class Feature148TelegramNoavaranAminApiFactory : Feature148TelegramApiFactory
{
    protected override string ChatProvider => "NoavaranAmin";
}
