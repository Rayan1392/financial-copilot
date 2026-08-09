using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialCopilot.IntegrationTests;

// â”€â”€â”€ V2 Scanner tests â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

public sealed class V2ScannerEndpointTests : IClassFixture<V2ScannerApiFactory>
{
    private readonly V2ScannerApiFactory _factory;

    public V2ScannerEndpointTests(V2ScannerApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
    }

    [Fact]
    public async Task V2AiQuery_ScannerTool_ReturnsScannerTableWithMatchingSymbols()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "P/E below 6" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Scanner", document.RootElement.GetProperty("intent").GetString());
        Assert.False(document.RootElement.GetProperty("clarificationRequired").GetBoolean());

        var table = document.RootElement.GetProperty("scannerTable");
        Assert.NotEqual(Guid.Empty, table.GetProperty("planId").GetGuid());

        var rows = table.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        var symbols = rows.Select(r => r.GetProperty("symbolCode").GetString()!).ToHashSet();
        Assert.Contains("LIVE", symbols);
        Assert.Contains("FALLBACK", symbols);
    }

    [Fact]
    public async Task V2AiQuery_ScannerTool_ReturnsConversationId()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "P/E below 6" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(Guid.Empty, document.RootElement.GetProperty("conversationId").GetGuid());
        Assert.True(document.RootElement.GetProperty("usage").GetProperty("creditsCharged").GetDecimal() >= 0);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
    }
}

// â”€â”€â”€ V2 Symbol Lookup tests â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

public sealed class V2SymbolLookupEndpointTests : IClassFixture<V2SymbolLookupApiFactory>
{
    private readonly V2SymbolLookupApiFactory _factory;

    public V2SymbolLookupEndpointTests(V2SymbolLookupApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
        factory.Fake.Reset();
    }

    [Fact]
    public async Task V2AiQuery_LookupTool_ReturnsSymbolLookupTable()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "PE Ø­ÙØ§Ø±ÛŒ Ú†Ù‚Ø¯Ø± Ø§Ø³ØªØŸ" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("SymbolLookup", document.RootElement.GetProperty("intent").GetString());
        Assert.False(document.RootElement.GetProperty("clarificationRequired").GetBoolean());

        var table = document.RootElement.GetProperty("symbolLookupTable");
        Assert.NotEqual(JsonValueKind.Null, table.ValueKind);
        Assert.NotEqual(Guid.Empty, table.GetProperty("planId").GetGuid());

        var rows = table.GetProperty("rows").EnumerateArray().ToList();
        Assert.Single(rows);
        Assert.Equal("HAF_TSE", rows[0].GetProperty("symbolCode").GetString());

        var confidence = document.RootElement.GetProperty("confidenceScore");
        Assert.True(confidence.GetProperty("score").GetDouble() >= 0.95);
        Assert.Equal("v1", confidence.GetProperty("policyVersion").GetString());
    }

    [Theory]
    [InlineData("آخرین قیمت کگل؟", "کگل", "2,110", "LatestDailyFallback")]
    [InlineData("آخرین قیمت کچاد؟", "کچاد", "26,350", "IntradayToday")]
    [InlineData("قیمت امروز کگل؟", "کگل", "2,110", "LatestDailyFallback")]
    [InlineData("قیمت پایانی کچاد؟", "کچاد", "26,350", "IntradayToday")]
    public async Task V2AiQuery_DirectPriceQuestion_UsesDirectSymbolLookupRoute(
        string message,
        string expectedSymbol,
        string expectedFormattedValue,
        string expectedSourceLabel)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = document.RootElement;
        Assert.Equal("SymbolLookup", root.GetProperty("intent").GetString());
        Assert.False(root.GetProperty("clarificationRequired").GetBoolean());
        Assert.Equal(0, _factory.Fake.OuterToolSelectionCalls);

        var row = Assert.Single(root.GetProperty("symbolLookupTable").GetProperty("rows").EnumerateArray());
        Assert.Equal(expectedSymbol, row.GetProperty("symbolCode").GetString());
        var priceCell = row.GetProperty("cells").GetProperty("LATEST_PRICE");
        Assert.Equal(expectedFormattedValue, priceCell.GetProperty("formattedValue").GetString());
        Assert.NotEqual("Missing", priceCell.GetProperty("freshnessStatus").GetString());
        Assert.Equal(expectedSourceLabel, priceCell.GetProperty("sourceLabel").GetString());
        Assert.Equal(FormatJalaliDate(ParseTradingDate(priceCell.GetProperty("tradingDate").GetString()!)),
            priceCell.GetProperty("tradingDatePersian").GetString());
        Assert.Contains(expectedFormattedValue, root.GetProperty("textAnswer").GetString());
    }

    [Theory]
    [InlineData("تغییر قیمت کگل؟", "کگل", "+5.5%", "LatestDailyFallback")]
    [InlineData("درصد تغییر قیمت کگل؟", "کگل", "+5.5%", "LatestDailyFallback")]
    [InlineData("درصد تغییر روزانه کگل؟", "کگل", "+5.5%", "LatestDailyFallback")]
    public async Task V2AiQuery_DirectDailyChangeQuestion_UsesDirectSymbolLookupRoute(
        string message,
        string expectedSymbol,
        string expectedFormattedValue,
        string expectedSourceLabel)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = document.RootElement;
        Assert.Equal("SymbolLookup", root.GetProperty("intent").GetString());
        Assert.False(root.GetProperty("clarificationRequired").GetBoolean());
        Assert.Equal(0, _factory.Fake.OuterToolSelectionCalls);

        var row = Assert.Single(root.GetProperty("symbolLookupTable").GetProperty("rows").EnumerateArray());
        Assert.Equal(expectedSymbol, row.GetProperty("symbolCode").GetString());
        var changeCell = row.GetProperty("cells").GetProperty("DAILY_CHANGE_PCT");
        Assert.Equal(expectedFormattedValue, changeCell.GetProperty("formattedValue").GetString());
        Assert.NotEqual("Missing", changeCell.GetProperty("freshnessStatus").GetString());
        Assert.Equal(expectedSourceLabel, changeCell.GetProperty("sourceLabel").GetString());
        Assert.Equal(FormatJalaliDate(ParseTradingDate(changeCell.GetProperty("tradingDate").GetString()!)),
            changeCell.GetProperty("tradingDatePersian").GetString());
        Assert.Contains(expectedFormattedValue, root.GetProperty("textAnswer").GetString());
    }

    [Fact]
    public async Task V2AiQuery_PeLookup_UsesQuoteFallbackContext()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "pe کگل؟" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = Assert.Single(document.RootElement.GetProperty("symbolLookupTable").GetProperty("rows").EnumerateArray());
        Assert.Equal("4.12", row.GetProperty("cells").GetProperty("PE_TTM").GetProperty("formattedValue").GetString());

        var priceCell = row.GetProperty("cells").GetProperty("LATEST_PRICE");
        Assert.Equal("2,110", priceCell.GetProperty("formattedValue").GetString());
        Assert.Equal("LatestDailyFallback", priceCell.GetProperty("sourceLabel").GetString());

        var changeCell = row.GetProperty("cells").GetProperty("DAILY_CHANGE_PCT");
        Assert.Equal("+5.5%", changeCell.GetProperty("formattedValue").GetString());
        Assert.Equal("LatestDailyFallback", changeCell.GetProperty("sourceLabel").GetString());
    }

    [Fact]
    public async Task V2AiQuery_DirectYtdFollowup_UsesPreviousConversationSymbol()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var firstResponse = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "\u0641\u0631\u0648\u0634 \u0645\u0627\u0647\u0627\u0646\u0647 \u06a9\u0686\u0627\u062f\u061f" },
            CancellationToken.None);
        using var firstDocument = await ReadJsonAsync(firstResponse);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var conversationId = firstDocument.RootElement.GetProperty("conversationId").GetGuid();

        using var followupResponse = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "\u0641\u0631\u0648\u0634 YTD \u0686\u0642\u062f\u0631 \u0628\u0648\u062f\u0647\u061f", conversationId },
            CancellationToken.None);
        using var followupDocument = await ReadJsonAsync(followupResponse);

        Assert.Equal(HttpStatusCode.OK, followupResponse.StatusCode);
        var root = followupDocument.RootElement;
        Assert.Equal("SymbolLookup", root.GetProperty("intent").GetString());
        Assert.False(root.GetProperty("clarificationRequired").GetBoolean());
        Assert.Equal(0, _factory.Fake.OuterToolSelectionCalls);

        var textAnswer = root.GetProperty("textAnswer").GetString();
        Assert.Contains("787,016,400", textAnswer);

        var row = Assert.Single(root.GetProperty("symbolLookupTable").GetProperty("rows").EnumerateArray());
        var cells = row.GetProperty("cells");
        Assert.Equal("787,016,400", cells.GetProperty("MONTHLY_SALES_YTD").GetProperty("formattedValue").GetString());
        Assert.True(root.GetProperty("confidenceScore").GetProperty("score").GetDouble() > 0);
    }

    [Fact]
    public async Task V2AiQuery_PendingYtdMetricThenCompanyName_UsesStructuredLookup()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var firstResponse = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "\u0641\u0631\u0648\u0634 YTD \u0686\u0642\u062f\u0631 \u0628\u0648\u062f\u0647\u061f" },
            CancellationToken.None);
        using var firstDocument = await ReadJsonAsync(firstResponse);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var conversationId = firstDocument.RootElement.GetProperty("conversationId").GetGuid();

        using var followupResponse = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "\u0686\u0627\u062f\u0631\u0645\u0644\u0648", conversationId },
            CancellationToken.None);
        using var followupDocument = await ReadJsonAsync(followupResponse);

        Assert.Equal(HttpStatusCode.OK, followupResponse.StatusCode);
        var root = followupDocument.RootElement;
        Assert.Equal("SymbolLookup", root.GetProperty("intent").GetString());
        Assert.False(root.GetProperty("clarificationRequired").GetBoolean());
        Assert.Equal(0, _factory.Fake.OuterToolSelectionCalls);

        var textAnswer = root.GetProperty("textAnswer").GetString();
        Assert.Contains("787,016,400", textAnswer);
        Assert.DoesNotContain("415,830,370", textAnswer);

        var row = Assert.Single(root.GetProperty("symbolLookupTable").GetProperty("rows").EnumerateArray());
        var cells = row.GetProperty("cells");
        Assert.Equal("787,016,400", cells.GetProperty("MONTHLY_SALES_YTD").GetProperty("formattedValue").GetString());
        Assert.True(root.GetProperty("confidenceScore").GetProperty("score").GetDouble() > 0);
    }

    [Theory]
    [InlineData("\u067e\u06cc \u0628\u0647 \u0627\u06cc \u06af\u0644 \u06af\u0647\u0631", "\u06a9\u06af\u0644", "4.12")]
    [InlineData("\u067e\u06cc \u0628\u0647 \u0627\u06cc \u06af\u0644\u06af\u0647\u0631", "\u06a9\u06af\u0644", "4.12")]
    public async Task V2AiQuery_PeCompanyNameSpacingVariant_ResolvesToKgol(
        string message,
        string expectedSymbol,
        string expectedFormattedValue)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = document.RootElement;
        var row = Assert.Single(root.GetProperty("symbolLookupTable").GetProperty("rows").EnumerateArray());
        Assert.Equal(expectedSymbol, row.GetProperty("symbolCode").GetString());
        Assert.Equal(expectedFormattedValue, row.GetProperty("cells").GetProperty("PE_TTM").GetProperty("formattedValue").GetString());
    }

    [Fact]
    public async Task V2AiQuery_ExplicitPeFollowup_BypassesLegacyParserAndUsesSemanticFrame()
    {
        _factory.Fake.Reset();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var firstResponse = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "\u0622\u062e\u0631\u06cc\u0646 \u0641\u0631\u0648\u0634 \u0686\u0627\u062f\u0631\u0645\u0644\u0648\u061f" },
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var conversationId = (await ReadJsonAsync(firstResponse)).RootElement.GetProperty("conversationId").GetGuid();

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "pe \u06a9\u0686\u0627\u062f", conversationId },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(_factory.Fake.LastParserUserMessage);
        Assert.Equal(0, _factory.Fake.OuterToolSelectionCalls);

        var row = Assert.Single(document.RootElement.GetProperty("symbolLookupTable").GetProperty("rows").EnumerateArray());
        Assert.Equal("\u06a9\u0686\u0627\u062f", row.GetProperty("symbolCode").GetString());
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
    }

    private static DateOnly ParseTradingDate(string value) => DateOnly.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    private static string FormatJalaliDate(DateOnly date)
    {
        var calendar = new System.Globalization.PersianCalendar();
        var dateTime = date.ToDateTime(TimeOnly.MinValue);
        return $"{calendar.GetYear(dateTime):0000}/{calendar.GetMonth(dateTime):00}/{calendar.GetDayOfMonth(dateTime):00}";
    }
}

public sealed class V2MonthlySalesRoutingEndpointTests : IClassFixture<V2MonthlySalesRoutingApiFactory>
{
    private readonly V2MonthlySalesRoutingApiFactory _factory;

    public V2MonthlySalesRoutingEndpointTests(V2MonthlySalesRoutingApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
    }

    [Theory]
    [InlineData("\u0645\u062a\u0648\u0633\u0637 \u0641\u0631\u0648\u0634 12 \u0645\u0627\u0647\u0647 \u06a9\u0686\u0627\u062f \u0686\u0642\u062f\u0631 \u0628\u0648\u062f\u0647 \u0627\u0633\u062a", "AVG_12M_MONTHLY_SALES", "57,549,287")]
    [InlineData("\u0641\u0631\u0648\u0634 YTD \u06a9\u0686\u0627\u062f\u061f", "MONTHLY_SALES_YTD", "787,016,400")]
    [InlineData("\u0641\u0631\u0648\u0634 YTD \u062a\u0627 \u0645\u0627\u0647 \u0642\u0628\u0644 \u06a9\u0686\u0627\u062f\u061f", "MONTHLY_SALES_YTD_PREVIOUS_MONTH", "605,344,668")]
    public async Task V2AiQuery_DirectMonthlySalesCompanionMetric_UsesRequestedMetricInProse(
        string message,
        string expectedMetricCode,
        string expectedFormattedValue)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = document.RootElement;
        Assert.Equal("SymbolLookup", root.GetProperty("intent").GetString());
        Assert.Equal(0, _factory.Fake.OuterToolSelectionCalls);

        var textAnswer = root.GetProperty("textAnswer").GetString();
        Assert.Contains(expectedFormattedValue, textAnswer);
        Assert.DoesNotContain("90,879,722", textAnswer);

        var table = root.GetProperty("symbolLookupTable");
        var row = Assert.Single(table.GetProperty("rows").EnumerateArray());
        var cells = row.GetProperty("cells");
        Assert.Equal(expectedFormattedValue, cells.GetProperty(expectedMetricCode).GetProperty("formattedValue").GetString());

        var columns = table.GetProperty("columns").EnumerateArray()
            .Select(c => c.GetProperty("identifier").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("LATEST_PRICE", columns);
        Assert.DoesNotContain("DAILY_CHANGE_PCT", columns);
        Assert.True(root.GetProperty("confidenceScore").GetProperty("score").GetDouble() > 0);
    }

    [Theory]
    [InlineData("\u0622\u062e\u0631\u06cc\u0646 \u0641\u0631\u0648\u0634 \u0686\u0627\u062f\u0631\u0645\u0644\u0648\u061f", "MONTHLY_SALES", "90,879,722")]
    [InlineData("\u0641\u0631\u0648\u0634 \u0645\u0627\u0647\u0627\u0646\u0647 \u0686\u0627\u062f\u0631\u0645\u0644\u0648\u061f", "MONTHLY_SALES", "90,879,722")]
    [InlineData("\u0645\u062a\u0648\u0633\u0637 \u0641\u0631\u0648\u0634 12 \u0645\u0627\u0647\u0647 \u0686\u0627\u062f\u0631\u0645\u0644\u0648\u061f", "AVG_12M_MONTHLY_SALES", "57,549,287")]
    [InlineData("\u0641\u0631\u0648\u0634 YTD \u0686\u0627\u062f\u0631\u0645\u0644\u0648\u061f", "MONTHLY_SALES_YTD", "787,016,400")]
    public async Task V2AiQuery_DirectMonthlySalesCompanyName_ResolvesThroughCompaniesTable(
        string message,
        string expectedMetricCode,
        string expectedFormattedValue)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = document.RootElement;
        Assert.Equal("SymbolLookup", root.GetProperty("intent").GetString());
        Assert.False(root.GetProperty("clarificationRequired").GetBoolean());
        Assert.Equal(0, _factory.Fake.OuterToolSelectionCalls);

        var row = Assert.Single(root.GetProperty("symbolLookupTable").GetProperty("rows").EnumerateArray());
        Assert.Equal("\u06a9\u0686\u0627\u062f", row.GetProperty("symbolCode").GetString());
        Assert.Equal("\u0645\u0639\u062f\u0646\u06cc \u0648 \u0635\u0646\u0639\u062a\u06cc \u0686\u0627\u062f\u0631\u0645\u0644\u0648", row.GetProperty("companyName").GetString());
        Assert.Equal(
            expectedFormattedValue,
            row.GetProperty("cells").GetProperty(expectedMetricCode).GetProperty("formattedValue").GetString());

        var textAnswer = root.GetProperty("textAnswer").GetString();
        Assert.Contains(expectedFormattedValue, textAnswer);
        Assert.DoesNotContain("Found metric data for 0 symbol(s). 1 unresolved.", textAnswer);
    }

    [Theory]
    [InlineData("\u0622\u062e\u0631\u06cc\u0646 \u0641\u0631\u0648\u0634 \u06af\u0644 \u06af\u0647\u0631\u061f")]
    [InlineData("\u0622\u062e\u0631\u06cc\u0646 \u0641\u0631\u0648\u0634 \u06af\u0644\u06af\u0647\u0631\u061f")]
    public async Task V2AiQuery_DirectMonthlySalesKgolCompanyName_ResolvesToKgol(string message)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = document.RootElement;
        var row = Assert.Single(root.GetProperty("symbolLookupTable").GetProperty("rows").EnumerateArray());
        Assert.Equal("\u06a9\u06af\u0644", row.GetProperty("symbolCode").GetString());
        Assert.Equal("\u0645\u0639\u062f\u0646\u06cc \u0648 \u0635\u0646\u0639\u062a\u06cc \u06af\u0644 \u06af\u0647\u0631", row.GetProperty("companyName").GetString());
        Assert.Equal("61,234,567", row.GetProperty("cells").GetProperty("MONTHLY_SALES").GetProperty("formattedValue").GetString());
    }

    [Fact]
    public async Task V2AiQuery_DirectMonthlySalesCompanyName_MatchesDirectSymbolResult()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var symbolResponse = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "\u0622\u062e\u0631\u06cc\u0646 \u0641\u0631\u0648\u0634 \u06a9\u0686\u0627\u062f\u061f" },
            CancellationToken.None);
        using var symbolDocument = await ReadJsonAsync(symbolResponse);

        using var companyResponse = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "\u0622\u062e\u0631\u06cc\u0646 \u0641\u0631\u0648\u0634 \u0686\u0627\u062f\u0631\u0645\u0644\u0648\u061f" },
            CancellationToken.None);
        using var companyDocument = await ReadJsonAsync(companyResponse);

        var symbolRow = Assert.Single(symbolDocument.RootElement.GetProperty("symbolLookupTable").GetProperty("rows").EnumerateArray());
        var companyRow = Assert.Single(companyDocument.RootElement.GetProperty("symbolLookupTable").GetProperty("rows").EnumerateArray());

        Assert.Equal(
            symbolRow.GetProperty("cells").GetProperty("MONTHLY_SALES").GetProperty("formattedValue").GetString(),
            companyRow.GetProperty("cells").GetProperty("MONTHLY_SALES").GetProperty("formattedValue").GetString());
        Assert.Equal("\u06a9\u0686\u0627\u062f", companyRow.GetProperty("symbolCode").GetString());
    }

    [Fact]
    public async Task V2AiQuery_UnresolvedMonthlySalesQuery_ReturnsDeterministicPersianMessage()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "\u0622\u062e\u0631\u06cc\u0646 \u0641\u0631\u0648\u0634 \u0646\u0627\u0645\u0648\u062c\u0648\u062f\u061f" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = document.RootElement;
        Assert.Equal("SymbolLookup", root.GetProperty("intent").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("symbolLookupTable").ValueKind);

        var textAnswer = root.GetProperty("textAnswer").GetString();
        Assert.Equal("DisambiguationNeeded", root.GetProperty("outcome").GetString());
        Assert.Equal("entity_not_found", root.GetProperty("outcomeReasonCode").GetString());
        Assert.Contains("\u0646\u0645\u0627\u062f \u062f\u0642\u06cc\u0642", textAnswer);
        Assert.DoesNotContain("Found metric data for 0 symbol(s). 1 unresolved.", textAnswer);
        Assert.DoesNotContain("Clarification needed", textAnswer);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
    }
}

public sealed class V2ShgolDirectPriceRegressionEndpointTests : IClassFixture<V2ShgolDirectPriceRegressionApiFactory>
{
    private readonly V2ShgolDirectPriceRegressionApiFactory _factory;

    public V2ShgolDirectPriceRegressionEndpointTests(V2ShgolDirectPriceRegressionApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
        factory.Fake.Reset();
    }

    [Fact]
    public async Task V2AiQuery_DirectPriceQuestion_Shgol_ShouldReturnLatestDailyFallbackQuote()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "آخرین قیمت شگل" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = document.RootElement;
        Assert.Equal("SymbolLookup", root.GetProperty("intent").GetString());
        Assert.False(root.GetProperty("clarificationRequired").GetBoolean());
        Assert.Equal(0, _factory.Fake.OuterToolSelectionCalls);

        var row = Assert.Single(root.GetProperty("symbolLookupTable").GetProperty("rows").EnumerateArray());
        Assert.Equal("شگل", row.GetProperty("symbolCode").GetString());
        Assert.Equal("گلتاش", row.GetProperty("companyName").GetString());

        var priceCell = row.GetProperty("cells").GetProperty("LATEST_PRICE");
        Assert.Equal("3,934", priceCell.GetProperty("formattedValue").GetString());
        Assert.Equal("PreviousTradingDay", priceCell.GetProperty("freshnessStatus").GetString());
        Assert.Equal("LatestDailyFallback", priceCell.GetProperty("sourceLabel").GetString());
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2).ToString("yyyy-MM-dd"), priceCell.GetProperty("tradingDate").GetString());
        Assert.Equal(FormatJalaliDate(ParseTradingDate(priceCell.GetProperty("tradingDate").GetString()!)),
            priceCell.GetProperty("tradingDatePersian").GetString());

        var changeCell = row.GetProperty("cells").GetProperty("DAILY_CHANGE_PCT");
        Assert.Equal("+2.98%", changeCell.GetProperty("formattedValue").GetString());
        Assert.Equal("LatestDailyFallback", changeCell.GetProperty("sourceLabel").GetString());
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
    }

    private static DateOnly ParseTradingDate(string value) => DateOnly.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    private static string FormatJalaliDate(DateOnly date)
    {
        var calendar = new System.Globalization.PersianCalendar();
        var dateTime = date.ToDateTime(TimeOnly.MinValue);
        return $"{calendar.GetYear(dateTime):0000}/{calendar.GetMonth(dateTime):00}/{calendar.GetDayOfMonth(dateTime):00}";
    }
}

public sealed class V2MonthlySalesRoutingApiFactory : AiFacadeApiFactory
{
    private readonly string _dbName = $"v2-monthly-sales-routing-{Guid.NewGuid():N}";
    private bool _seeded;
    private readonly object _seedLock = new();

    public V2MonthlySalesRoutingFakeAiModelClient Fake { get; } = new();

    protected override bool ForceV1Orchestration => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiOrchestration:Mode"] = "MicrosoftAgentFrameworkV2"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            ReplaceIngestionDbContext(services, _dbName);
            services.RemoveAll<IAiModelClient>();
            services.AddSingleton<IAiModelClient>(Fake);
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
            SeedLookupData(db);
            db.SaveChanges();
            _seeded = true;
        }
    }

    private static void SeedLookupData(FinancialIngestionDbContext db)
    {
        var now = DateTimeOffset.Parse("2026-06-10T08:00:00Z");
        db.Companies.AddRange(
            new NormalizedCompanyRow
            {
                Id = Guid.Parse("52000000-0000-0000-0000-000000000001"),
                Name = "\u0645\u0639\u062f\u0646\u06cc \u0648 \u0635\u0646\u0639\u062a\u06cc \u0686\u0627\u062f\u0631\u0645\u0644\u0648",
                ProviderName = "CyclicalWaves",
                ExternalCompanyId = "3",
                CompanySymbol = "\u06a9\u0686\u0627\u062f",
                TseSymbol = "\u06a9\u0686\u0627\u062f",
                LastSynchronizedAt = now
            },
            new NormalizedCompanyRow
            {
                Id = Guid.Parse("52000000-0000-0000-0000-000000000002"),
                Name = "\u0645\u0639\u062f\u0646\u06cc \u0648 \u0635\u0646\u0639\u062a\u06cc \u06af\u0644 \u06af\u0647\u0631",
                ProviderName = "CyclicalWaves",
                ExternalCompanyId = "5",
                CompanySymbol = "\u06a9\u06af\u0644",
                TseSymbol = "\u06a9\u06af\u0644",
                LastSynchronizedAt = now
            });

        db.DerivedMetrics.AddRange(
            MonthlyMetric("MONTHLY_SALES", "monthly-sales-source-v1", 90_879_722_000_000m, now),
            MonthlyMetric("AVG_12M_MONTHLY_SALES", "avg-12m-monthly-sales-source-v1", 57_549_287_000_000m, now),
            MonthlyMetric("MONTHLY_SALES_YTD", "monthly-sales-ytd-source-v1", 787_016_400_000_000m, now),
            MonthlyMetric("MONTHLY_SALES_YTD_PREVIOUS_MONTH", "monthly-sales-ytd-previous-month-source-v1", 605_344_668_000_000m, now),
            MonthlyMetric("MONTHLY_SALES", "monthly-sales-source-v1", 61_234_567_000_000m, now, externalCompanyId: "5"),
            MonthlyMetric("AVG_12M_MONTHLY_SALES", "avg-12m-monthly-sales-source-v1", 48_765_432_000_000m, now, externalCompanyId: "5"),
            MonthlyMetric("MONTHLY_SALES_YTD", "monthly-sales-ytd-source-v1", 512_345_678_000_000m, now, externalCompanyId: "5"),
            MonthlyMetric("MONTHLY_SALES_YTD_PREVIOUS_MONTH", "monthly-sales-ytd-previous-month-source-v1", 430_123_456_000_000m, now, externalCompanyId: "5"));
    }

    private static DerivedMetricRow MonthlyMetric(
        string metricCode,
        string policyVersion,
        decimal value,
        DateTimeOffset now,
        string externalCompanyId = "3") =>
        new()
        {
            Id = Guid.NewGuid(),
            ExternalCompanyId = externalCompanyId,
            MetricCode = metricCode,
            MetricVersion = "v1",
            CalculationPolicyVersion = policyVersion,
            PeriodType = "Monthly",
            PeriodStart = new DateOnly(2026, 5, 1),
            PeriodEnd = new DateOnly(2026, 5, 31),
            Value = value,
            Unit = "Amount",
            ObservedAt = now,
            LastSynchronizedAt = now,
            WarningsJson = "[]",
            SourceEvidenceJson = "[{\"source\":\"CyclicalWaves\"}]",
            DependencyEvidenceJson = "[]"
        };
}

public sealed class V2ProductRevenueMixEndpointTests : IClassFixture<V2ProductRevenueMixApiFactory>
{
    private readonly V2ProductRevenueMixApiFactory _factory;

    public V2ProductRevenueMixEndpointTests(V2ProductRevenueMixApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
    }

    [Theory]
    [InlineData("پرفروش‌ترین محصول کچاد؟", "کچاد")]
    [InlineData("پرفروش ترین محصول کچاد؟", "کچاد")]
    [InlineData("پرفروشترین محصول کچاد؟", "کچاد")]
    [InlineData("پرفروش‌ترین محصولات کچاد؟", "کچاد")]
    [InlineData("مهم‌ترین محصول کچاد چیست؟", "کچاد")]
    [InlineData("کگل بیشتر از چه محصولی درآمد دارد؟", "کگل")]
    [InlineData("ترکیب فروش محصولات فملی را نشان بده", "فملی")]
    [InlineData("رکیب فروش محصولات کچاد؟", "کچاد")]
    public async Task V2AiQuery_ProductRevenueMixQueries_ReturnProductRevenueMixAndChargeCredits(
        string message,
        string expectedSymbol)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = document.RootElement;
        Assert.Equal("ProductRevenueMix", root.GetProperty("intent").GetString());
        Assert.False(root.GetProperty("clarificationRequired").GetBoolean());
        Assert.Equal(0, _factory.Fake.OuterToolSelectionCalls);
        Assert.True(root.GetProperty("usage").GetProperty("creditsCharged").GetDecimal() > 0m);

        var textAnswer = root.GetProperty("textAnswer").GetString();
        Assert.Contains(expectedSymbol, textAnswer);
        Assert.Contains("ترکیب درآمد محصولات", textAnswer);
    }

    [Fact]
    public async Task V2AiQuery_FundamentalAnalysis_ReturnsPersistedAnalysisWhenLiveMetricsFail()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "تحلیل بنیادی فولاژ؟" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = document.RootElement;
        Assert.Equal("ComprehensiveAnalysis", root.GetProperty("intent").GetString());
        Assert.Equal("comprehensive_analysis", root.GetProperty("semanticCapabilityCode").GetString());
        Assert.False(root.GetProperty("clarificationRequired").GetBoolean());
        Assert.True(root.GetProperty("usage").GetProperty("creditsCharged").GetDecimal() > 0m);
        Assert.Equal(0, _factory.Fake.OuterToolSelectionCalls);

        var analyses = root.GetProperty("comprehensiveAnalysisResult").GetProperty("items").EnumerateArray().ToArray();
        var analysis = Assert.Single(analyses);
        Assert.Equal("تحلیل بنیادی فولاژ", analysis.GetProperty("title").GetString());
        Assert.Equal("P/E فعلی 5.4 و ارزش ذاتی 3753 تومان", analysis.GetProperty("plainTextSummary").GetString());
        var textAnswer = root.GetProperty("textAnswer").GetString();
        Assert.Contains("P/E فعلی 5.4 و ارزش ذاتی 3753 تومان", textAnswer);
        Assert.DoesNotContain("Found metric data for 0 symbol(s)", textAnswer);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
    }
}

public sealed class V2MonthlyActivityTrendEndpointTests : IClassFixture<V2MonthlyActivityTrendApiFactory>
{
    private readonly V2MonthlyActivityTrendApiFactory _factory;

    public V2MonthlyActivityTrendEndpointTests(V2MonthlyActivityTrendApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
    }

    [Theory]
    [InlineData("روند فروش ماهانه کهمدا را نشان بده")]
    [InlineData("چارت فروش ماهانه کهمدا")]
    [InlineData("روند فروش کهمدا")]
    [InlineData("روند تولید و فروش کهمدا")]
    [InlineData("نمودار تولید و فروش ماهانه کهمدا")]
    [InlineData("نمودار فروش کهمدا")]
    [InlineData("نمودار فروش ماهانه کهمدا")]
    public async Task V2AiQuery_MonthlyActivityTrendQueries_ReturnChartPayloadWithoutToolLoop(string message)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = document.RootElement;
        Assert.Equal("MonthlyActivityTrend", root.GetProperty("intent").GetString());
        Assert.Equal("monthly_activity_trend", root.GetProperty("semanticCapabilityCode").GetString());
        Assert.Equal(1, root.GetProperty("semanticRegistryVersion").GetInt32());
        Assert.False(root.GetProperty("clarificationRequired").GetBoolean());
        Assert.Equal(0, _factory.Fake.OuterToolSelectionCalls);

        var trend = root.GetProperty("monthlyActivityTrendResult");
        Assert.Equal("کهمدا", trend.GetProperty("companySymbol").GetString());
        Assert.Equal("هماتیت", trend.GetProperty("companyName").GetString());
        Assert.Equal("میلیارد تومان", trend.GetProperty("unitLabelFa").GetString());
        Assert.Equal("نوآوران امین", trend.GetProperty("sourceProviderName").GetString());

        var chartPoints = trend.GetProperty("chartPoints").EnumerateArray().ToList();
        Assert.Equal(12, chartPoints.Count);
        Assert.Equal(0.095m, chartPoints[0].GetProperty("average12MonthSalesAmount").GetDecimal());
        Assert.Equal(JsonValueKind.Null, chartPoints[3].GetProperty("currentFiscalYearSalesAmount").ValueKind);

        var textAnswer = root.GetProperty("textAnswer").GetString();
        Assert.Contains("روند فروش ماهانه", textAnswer);
        Assert.Contains("منبع: نوآوران امین", textAnswer);
        Assert.Contains("محاسبه: 1405/04/03", textAnswer);
        Assert.DoesNotContain("NoavaranCurrentApi", textAnswer);
        Assert.DoesNotContain("محاسبه: 2026/07/07", textAnswer);
        Assert.DoesNotContain("آخرین قیمت", textAnswer);
        Assert.DoesNotContain("DAILY_CHANGE_PCT", textAnswer);
    }

    [Theory]
    [InlineData("چارت فروش ماهانه کهمدا")]
    [InlineData("روند فروش کهمدا")]
    [InlineData("روند تولید و فروش کهمدا")]
    [InlineData("نمودار تولید و فروش ماهانه کهمدا")]
    [InlineData("نمودار فروش کهمدا")]
    [InlineData("نمودار فروش ماهانه کهمدا")]
    public async Task V2AiQuery_CanonicalAliases_ReturnTheSameMonthlyTrendPayload(string aliasMessage)
    {
        var canonicalPayload = await GetMonthlyTrendPayloadAsync("روند فروش ماهانه کهمدا");
        var aliasPayload = await GetMonthlyTrendPayloadAsync(aliasMessage);

        Assert.Equal(canonicalPayload, aliasPayload);
        Assert.Equal(0, _factory.Fake.OuterToolSelectionCalls);
    }

    private async Task<string> GetMonthlyTrendPayloadAsync(string message)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = document.RootElement;
        Assert.Equal("MonthlyActivityTrend", root.GetProperty("intent").GetString());
        Assert.False(root.GetProperty("clarificationRequired").GetBoolean());
        return root.GetProperty("monthlyActivityTrendResult").GetRawText();
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
    }
}

public sealed class V2MonthlyActivityTrendApiFactory : AiFacadeApiFactory
{
    private readonly string _dbName = $"v2-monthly-activity-trend-{Guid.NewGuid():N}";
    private bool _seeded;
    private readonly object _seedLock = new();

    public V2MonthlySalesRoutingFakeAiModelClient Fake { get; } = new();

    protected override bool ForceV1Orchestration => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiOrchestration:Mode"] = "MicrosoftAgentFrameworkV2"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            ReplaceIngestionDbContext(services, _dbName);
            services.RemoveAll<IAiModelClient>();
            services.AddSingleton<IAiModelClient>(Fake);
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
            SeedTrendData(db);
            db.SaveChanges();
            _seeded = true;
        }
    }

    private static void SeedTrendData(FinancialIngestionDbContext db)
    {
        var now = DateTimeOffset.Parse("2026-06-24T08:00:00Z");

        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.Parse("56000000-0000-0000-0000-000000000001"),
            Name = "هماتیت",
            ProviderName = "NoavaranCurrentApi",
            ExternalCompanyId = "EXT-001",
            CompanySymbol = "کهمدا",
            TseSymbol = "کهمدا",
            LastSynchronizedAt = now
        });

        for (byte month = 1; month <= 12; month++)
        {
            db.CompanyMonthlyActivityTrendSnapshots.Add(new CompanyMonthlyActivityTrendSnapshotRow
            {
                Id = Guid.NewGuid(),
                ExternalCompanyId = "EXT-001",
                CompanySymbol = "کهمدا",
                CompanyName = "هماتیت",
                ReportYear = 1403,
                ReportMonth = month,
                FiscalYear = 1403,
                FiscalMonthIndex = month,
                FiscalMonthNameFa = PersianMonthName(month),
                MonthlySalesAmount = 800m + (month * 10m),
                SameMonthPreviousYearSalesAmount = null,
                Average12MonthSalesAmount = 900m,
                Average12MonthPeriodCount = 12,
                YtdSalesAmount = 5_000m,
                YtdPreviousMonthSalesAmount = 4_100m,
                SalesAmountYoYGrowthPercent = null,
                SourceProviderName = "NoavaranCurrentApi",
                IsComparablePreviousYearAvailable = false,
                IsAverage12MonthComplete = true,
                DataCompletenessScore = 1m,
                CalculatedAtUtc = now
            });
        }

        for (byte month = 1; month <= 3; month++)
        {
            var previousAmount = 800m + (month * 10m);
            var currentAmount = month switch
            {
                1 => 910m,
                2 => 940m,
                _ => 1_000m
            };

            db.CompanyMonthlyActivityTrendSnapshots.Add(new CompanyMonthlyActivityTrendSnapshotRow
            {
                Id = Guid.NewGuid(),
                ExternalCompanyId = "EXT-001",
                CompanySymbol = "کهمدا",
                CompanyName = "هماتیت",
                ReportYear = 1404,
                ReportMonth = month,
                FiscalYear = 1404,
                FiscalMonthIndex = month,
                FiscalMonthNameFa = PersianMonthName(month),
                MonthlySalesAmount = currentAmount,
                SameMonthPreviousYearSalesAmount = previousAmount,
                Average12MonthSalesAmount = 950m,
                Average12MonthPeriodCount = 12,
                YtdSalesAmount = 2_850m,
                YtdPreviousMonthSalesAmount = 1_850m,
                SalesAmountYoYGrowthPercent = previousAmount == 0m
                    ? null
                    : (currentAmount - previousAmount) / previousAmount * 100m,
                SourceProviderName = "NoavaranCurrentApi",
                IsComparablePreviousYearAvailable = true,
                IsAverage12MonthComplete = true,
                DataCompletenessScore = 1m,
                CalculatedAtUtc = now
            });
        }
    }

    private static string PersianMonthName(byte month) => month switch
    {
        1 => "فروردین",
        2 => "اردیبهشت",
        3 => "خرداد",
        4 => "تیر",
        5 => "مرداد",
        6 => "شهریور",
        7 => "مهر",
        8 => "آبان",
        9 => "آذر",
        10 => "دی",
        11 => "بهمن",
        12 => "اسفند",
        _ => throw new ArgumentOutOfRangeException(nameof(month))
    };
}

public sealed class V2FinancialStatementAnalysisEndpointTests : IClassFixture<V2FinancialStatementAnalysisApiFactory>
{
    private readonly V2FinancialStatementAnalysisApiFactory _factory;

    public V2FinancialStatementAnalysisEndpointTests(V2FinancialStatementAnalysisApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
    }

    [Theory]
    [InlineData("صورت مالی غالبر را تحلیل کن", false, "NonConsolidated")]
    [InlineData("صورت مالی تلفیقی غالبر را تحلیل کن", true, "Consolidated")]
    public async Task V2AiQuery_FinancialStatementQueries_SelectExpectedVariantWithoutToolLoop(
        string message,
        bool expectedComposing,
        string expectedVariant)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = document.RootElement;
        Assert.Equal("FinancialStatementPeriodAnalysis", root.GetProperty("intent").GetString());
        Assert.False(root.GetProperty("clarificationRequired").GetBoolean());
        Assert.Equal(0, _factory.Fake.OuterToolSelectionCalls);

        var result = root.GetProperty("financialStatementAnalysisResult");
        Assert.Equal("غالبر", result.GetProperty("companySymbol").GetString());
        Assert.Equal(expectedVariant, result.GetProperty("selectedVariant").GetString());
        Assert.Equal(12, result.GetProperty("selectedPeriodMonths").GetInt32());

        var firstSource = result.GetProperty("sourceReferences").EnumerateArray().First();
        Assert.Equal(expectedComposing, firstSource.GetProperty("isComposing").GetBoolean());

        var textAnswer = root.GetProperty("textAnswer").GetString()!;
        if (expectedComposing)
            Assert.Contains("تلفیقی", textAnswer);
        else
            Assert.DoesNotContain("تلفیقی", textAnswer);
    }

    [Fact]
    public async Task V2AiQuery_FullIncomeStatementTable_ReturnsStructuredTableWithoutToolLoop()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "آخرین صورت سود و زیان غالبر را نشان بده" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = document.RootElement;
        Assert.Equal("FinancialStatementTableLookup", root.GetProperty("intent").GetString());
        Assert.False(root.GetProperty("clarificationRequired").GetBoolean());
        Assert.Equal(0, _factory.Fake.OuterToolSelectionCalls);

        var table = root.GetProperty("financialStatementTableResult");
        Assert.Equal("غالبر", table.GetProperty("source").GetProperty("companySymbol").GetString());
        Assert.Equal("IncomeStatement", table.GetProperty("source").GetProperty("statementType").GetString());
        Assert.Equal("نوآوران امین", table.GetProperty("source").GetProperty("providerName").GetString());
        Assert.True(table.GetProperty("lineItems").GetArrayLength() >= 5);
        Assert.Contains("| ردیف | شرح | مبلغ |", root.GetProperty("textAnswer").GetString());
    }

    [Theory]
    [InlineData("آخرین ترازنامه غالبر", "BalanceSheet")]
    [InlineData("آخرین جریان وجه نقد غالبر را نمایش بده", "CashFlow")]
    public async Task V2AiQuery_FullStatementTables_ReturnExpectedStatementTypeWithoutToolLoop(
        string message,
        string expectedStatementType)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = document.RootElement;
        Assert.Equal("FinancialStatementTableLookup", root.GetProperty("intent").GetString());
        Assert.Equal(0, _factory.Fake.OuterToolSelectionCalls);

        var table = root.GetProperty("financialStatementTableResult");
        Assert.Equal(expectedStatementType, table.GetProperty("source").GetProperty("statementType").GetString());
        Assert.True(table.GetProperty("lineItems").GetArrayLength() > 0);

        if (expectedStatementType == "BalanceSheet")
            Assert.True(table.GetProperty("balanceSheetRows").GetArrayLength() > 0);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
    }
}

public sealed class V2FinancialStatementAnalysisApiFactory : AiFacadeApiFactory
{
    private readonly string _dbName = $"v2-financial-statement-analysis-{Guid.NewGuid():N}";
    private bool _seeded;
    private readonly object _seedLock = new();

    public V2MonthlySalesRoutingFakeAiModelClient Fake { get; } = new();

    protected override bool ForceV1Orchestration => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiOrchestration:Mode"] = "MicrosoftAgentFrameworkV2",
                ["NoavaranCurrentApi:ProviderName"] = "NadpcoApi"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            ReplaceIngestionDbContext(services, _dbName);
            services.RemoveAll<IAiModelClient>();
            services.AddSingleton<IAiModelClient>(Fake);
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
            SeedFinancialStatementData(db);
            db.SaveChanges();
            _seeded = true;
        }
    }

    private static void SeedFinancialStatementData(FinancialIngestionDbContext db)
    {
        var now = DateTimeOffset.Parse("2026-06-30T08:00:00Z");

        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.Parse("57000000-0000-0000-0000-000000000001"),
            Name = "غالبر",
            ProviderName = "NoavaranCurrentApi",
            ExternalCompanyId = "FS-001",
            CompanySymbol = "غالبر",
            TseSymbol = "غالبر",
            LastSynchronizedAt = now
        });

        AddStatement(db, Guid.Parse("57100000-0000-0000-0000-000000000001"), "inc-parent-1404", FinancialStatementType.IncomeStatement, false, "1404/12/29", "1405/04/09 09:23:24", new Dictionary<string, decimal?>
        {
            ["REVENUE"] = 4_170_440m,
            ["GROSS_PROFIT"] = 520_000m,
            ["OPERATING_PROFIT"] = 454_967m,
            ["NET_PROFIT"] = -189_548m,
            ["EPS"] = -410m
        });
        AddStatement(db, Guid.Parse("57100000-0000-0000-0000-000000000002"), "bs-parent-1404", FinancialStatementType.BalanceSheet, false, "1404/12/29", "1405/04/09 09:23:24", new Dictionary<string, decimal?>
        {
            ["TOTAL_ASSETS"] = 7_500_000m,
            ["TOTAL_LIABILITIES"] = 4_200_000m,
            ["TOTAL_EQUITY"] = 3_300_000m,
            ["CURRENT_ASSETS"] = 2_500_000m,
            ["CURRENT_LIABILITIES"] = 1_100_000m
        });
        AddStatement(db, Guid.Parse("57100000-0000-0000-0000-000000000003"), "inc-cons-1404", FinancialStatementType.IncomeStatement, true, "1404/12/29", "1405/04/09 09:23:24", new Dictionary<string, decimal?>
        {
            ["REVENUE"] = 5_000_000m,
            ["GROSS_PROFIT"] = 650_000m,
            ["OPERATING_PROFIT"] = 600_000m,
            ["NET_PROFIT"] = 100_000m,
            ["EPS"] = 120m
        });
        AddStatement(db, Guid.Parse("57100000-0000-0000-0000-000000000004"), "bs-cons-1404", FinancialStatementType.BalanceSheet, true, "1404/12/29", "1405/04/09 09:23:24", new Dictionary<string, decimal?>
        {
            ["TOTAL_ASSETS"] = 9_000_000m,
            ["TOTAL_LIABILITIES"] = 4_800_000m,
            ["TOTAL_EQUITY"] = 4_200_000m,
            ["CURRENT_ASSETS"] = 3_000_000m,
            ["CURRENT_LIABILITIES"] = 1_400_000m
        });
        AddStatement(db, Guid.Parse("57100000-0000-0000-0000-000000000007"), "cf-parent-1404", FinancialStatementType.CashFlow, false, "1404/12/29", "1405/04/09 09:23:24", new Dictionary<string, decimal?>
        {
            ["OPERATING_CASH_FLOW"] = 350_000m,
            ["INVESTING_CASH_FLOW"] = -120_000m,
            ["FINANCING_CASH_FLOW"] = 75_000m,
            ["NET_CASH_FLOW"] = 305_000m
        });
        AddStatement(db, Guid.Parse("57100000-0000-0000-0000-000000000005"), "inc-parent-1403", FinancialStatementType.IncomeStatement, false, "1403/12/29", "1404/04/09 09:23:24", new Dictionary<string, decimal?>
        {
            ["REVENUE"] = 9_801_948m,
            ["GROSS_PROFIT"] = 610_000m,
            ["OPERATING_PROFIT"] = 346_086m,
            ["NET_PROFIT"] = -6_130m,
            ["EPS"] = -12m
        });
        AddStatement(db, Guid.Parse("57100000-0000-0000-0000-000000000006"), "bs-parent-1403", FinancialStatementType.BalanceSheet, false, "1403/12/29", "1404/04/09 09:23:24", new Dictionary<string, decimal?>
        {
            ["TOTAL_ASSETS"] = 6_800_000m,
            ["TOTAL_LIABILITIES"] = 3_900_000m,
            ["TOTAL_EQUITY"] = 2_900_000m,
            ["CURRENT_ASSETS"] = 2_200_000m,
            ["CURRENT_LIABILITIES"] = 1_000_000m
        });
    }

    private static void AddStatement(
        FinancialIngestionDbContext db,
        Guid id,
        string externalStatementId,
        FinancialStatementType statementType,
        bool isComposing,
        string jalaliPeriodEnd,
        string jalaliAnnouncementDate,
        IReadOnlyDictionary<string, decimal?> lineItems)
    {
        var row = new NormalizedFinancialStatementRow
        {
            Id = id,
            ProviderName = "NadpcoApi",
            ExternalCompanyId = "FS-001",
            ExternalStatementId = externalStatementId,
            StatementType = statementType.ToString(),
            PeriodType = "TwelveMonths",
            PeriodStart = statementType == FinancialStatementType.BalanceSheet
                ? new DateOnly(2025, 3, 21)
                : new DateOnly(2025, 3, 21),
            PeriodEnd = jalaliPeriodEnd == "1404/12/29" ? new DateOnly(2026, 3, 20) : new DateOnly(2025, 3, 20),
            SourcePayloadChecksum = externalStatementId,
            LastSynchronizedAt = DateTimeOffset.UtcNow,
            IsAudited = false,
            IsRepresented = false,
            IsComposing = isComposing,
            WarningsJson = BuildWarningsJson(jalaliPeriodEnd, jalaliAnnouncementDate, isComposing)
        };
        db.FinancialStatements.Add(row);

        foreach (var (metricCode, value) in lineItems)
        {
            db.FinancialStatementLineItems.Add(new NormalizedFinancialStatementLineItemRow
            {
                Id = Guid.NewGuid(),
                FinancialStatementId = row.Id,
                MetricCode = metricCode,
                Value = value
            });
        }
    }

    private static string BuildWarningsJson(
        string jalaliPeriodEnd,
        string jalaliAnnouncementDate,
        bool isComposing) =>
        $$"""
          [
            { "code": "JalaliFiscalYearEnd", "evidence": "{{jalaliPeriodEnd}}" },
            { "code": "JalaliPeriodEnd", "evidence": "{{jalaliPeriodEnd}}" },
            { "code": "JalaliAnouncementDate", "evidence": "{{jalaliAnnouncementDate}}" },
            { "code": "AnouncementDate", "evidence": "{{DateTimeOffset.Parse("2026-06-30T08:00:00Z"):O}}" },
            { "code": "IsAudited", "evidence": false },
            { "code": "IsRepresented", "evidence": false },
            { "code": "IsComposing", "evidence": {{isComposing.ToString().ToLowerInvariant()}} }
          ]
          """;
}

public sealed class V2ProductRevenueMixApiFactory : AiFacadeApiFactory
{
    private readonly string _dbName = $"v2-product-revenue-mix-{Guid.NewGuid():N}";
    private bool _seeded;
    private readonly object _seedLock = new();

    public V2MonthlySalesRoutingFakeAiModelClient Fake { get; } = new();

    protected override bool ForceV1Orchestration => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiOrchestration:Mode"] = "MicrosoftAgentFrameworkV2"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            ReplaceIngestionDbContext(services, _dbName);
            services.RemoveAll<IAiModelClient>();
            services.AddSingleton<IAiModelClient>(Fake);
            services.RemoveAll<ISymbolMetricLookupService>();
            services.AddSingleton<ISymbolMetricLookupService, ThrowingSymbolMetricLookupService>();
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
            SeedProductRevenueMixData(db);
            db.SaveChanges();
            _seeded = true;
        }
    }

    private static void SeedProductRevenueMixData(FinancialIngestionDbContext db)
    {
        var now = DateTimeOffset.Parse("2026-06-10T08:00:00Z");

        db.Companies.AddRange(
            new NormalizedCompanyRow
            {
                Id = Guid.Parse("54000000-0000-0000-0000-000000000003"),
                Name = "معدنی و صنعتی چادرملو",
                ProviderName = "NoavaranCurrentApi",
                ExternalCompanyId = "3",
                CompanySymbol = "کچاد",
                TseSymbol = "کچاد",
                LastSynchronizedAt = now
            },
            new NormalizedCompanyRow
            {
                Id = Guid.Parse("54000000-0000-0000-0000-000000000005"),
                Name = "معدنی و صنعتی گل گهر",
                ProviderName = "NoavaranCurrentApi",
                ExternalCompanyId = "5",
                CompanySymbol = "کگل",
                TseSymbol = "کگل",
                LastSynchronizedAt = now
            },
            new NormalizedCompanyRow
            {
                Id = Guid.Parse("54000000-0000-0000-0000-000000000001"),
                Name = "ملی صنایع مس ایران",
                ProviderName = "NoavaranCurrentApi",
                ExternalCompanyId = "1",
                CompanySymbol = "فملی",
                TseSymbol = "فملی",
                LastSynchronizedAt = now
            },
            new NormalizedCompanyRow
            {
                Id = Guid.Parse("54000000-0000-0000-0000-000000000006"),
                Name = "فولاد آلیاژی ایران",
                ProviderName = "NoavaranCurrentApi",
                ExternalCompanyId = "6",
                CompanySymbol = "فولاژ",
                TseSymbol = "فولاژ",
                Ticker = "فولاژ",
                LastSynchronizedAt = DateTimeOffset.UtcNow
            });

        var analysisNow = DateTimeOffset.UtcNow;
        db.ComprehensiveAnalyses.Add(new ComprehensiveAnalysisRow
        {
            Id = 75001,
            Title = "تحلیل بنیادی فولاژ",
            Summary = "<p>P/E فعلی 5.4 و ارزش ذاتی 3753 تومان</p>",
            PlainTextSummary = "P/E فعلی 5.4 و ارزش ذاتی 3753 تومان",
            CreatedAt = analysisNow,
            PersianCreatedAt = "1405/05/17",
            AuthorId = 75,
            AuthorName = "تحلیلگر",
            SyncedAt = analysisNow
        });
        db.ComprehensiveAnalysisTags.Add(new ComprehensiveAnalysisTagRow
        {
            AnalysisId = 75001,
            TagId = 75001,
            TagName = "فولاژ",
            TagSlug = "folaj",
            TagTypeId = 1,
            IsAnalytic = false
        });

        db.CompanyProductRevenueMix.AddRange(
            new CompanyProductRevenueMixRow
            {
                Id = Guid.NewGuid(),
                ExternalCompanyId = "3",
                CompanySymbol = "کچاد",
                CompanyName = "معدنی و صنعتی چادرملو",
                ReportYear = 1403,
                ReportMonth = 3,
                FiscalEndDate = "1403/12/29",
                ProductName = "گندله سنگ آهن",
                ProductionQuantity = 900_000m,
                SalesQuantity = 880_000m,
                SalesRate = 68_000m,
                SalesAmount = 60_000_000_000_000m,
                TotalCompanySalesAmount = 100_000_000_000_000m,
                RevenueSharePercentage = 60m,
                ProductRank = 1,
                IsDominantProduct = true,
                SourceProviderName = "NoavaranCurrentApi",
                CalculatedAtUtc = now
            },
            new CompanyProductRevenueMixRow
            {
                Id = Guid.NewGuid(),
                ExternalCompanyId = "3",
                CompanySymbol = "کچاد",
                CompanyName = "معدنی و صنعتی چادرملو",
                ReportYear = 1403,
                ReportMonth = 3,
                FiscalEndDate = "1403/12/29",
                ProductName = "کنسانتره سنگ آهن",
                ProductionQuantity = 450_000m,
                SalesQuantity = 430_000m,
                SalesRate = 62_000m,
                SalesAmount = 25_000_000_000_000m,
                TotalCompanySalesAmount = 100_000_000_000_000m,
                RevenueSharePercentage = 25m,
                ProductRank = 2,
                IsDominantProduct = false,
                SourceProviderName = "NoavaranCurrentApi",
                CalculatedAtUtc = now
            },
            new CompanyProductRevenueMixRow
            {
                Id = Guid.NewGuid(),
                ExternalCompanyId = "3",
                CompanySymbol = "کچاد",
                CompanyName = "معدنی و صنعتی چادرملو",
                ReportYear = 1403,
                ReportMonth = 3,
                FiscalEndDate = "1403/12/29",
                ProductName = "سنگ آهن دانه‌بندی",
                ProductionQuantity = 250_000m,
                SalesQuantity = 240_000m,
                SalesRate = 58_000m,
                SalesAmount = 15_000_000_000_000m,
                TotalCompanySalesAmount = 100_000_000_000_000m,
                RevenueSharePercentage = 15m,
                ProductRank = 3,
                IsDominantProduct = false,
                SourceProviderName = "NoavaranCurrentApi",
                CalculatedAtUtc = now
            },
            new CompanyProductRevenueMixRow
            {
                Id = Guid.NewGuid(),
                ExternalCompanyId = "5",
                CompanySymbol = "کگل",
                CompanyName = "معدنی و صنعتی گل گهر",
                ReportYear = 1403,
                ReportMonth = 3,
                FiscalEndDate = "1403/12/29",
                ProductName = "کنسانتره سنگ آهن",
                ProductionQuantity = 1_100_000m,
                SalesQuantity = 1_080_000m,
                SalesRate = 52_000m,
                SalesAmount = 55_000_000_000_000m,
                TotalCompanySalesAmount = 100_000_000_000_000m,
                RevenueSharePercentage = 55m,
                ProductRank = 1,
                IsDominantProduct = true,
                SourceProviderName = "NoavaranCurrentApi",
                CalculatedAtUtc = now
            },
            new CompanyProductRevenueMixRow
            {
                Id = Guid.NewGuid(),
                ExternalCompanyId = "5",
                CompanySymbol = "کگل",
                CompanyName = "معدنی و صنعتی گل گهر",
                ReportYear = 1403,
                ReportMonth = 3,
                FiscalEndDate = "1403/12/29",
                ProductName = "گندله",
                ProductionQuantity = 600_000m,
                SalesQuantity = 590_000m,
                SalesRate = 49_000m,
                SalesAmount = 30_000_000_000_000m,
                TotalCompanySalesAmount = 100_000_000_000_000m,
                RevenueSharePercentage = 30m,
                ProductRank = 2,
                IsDominantProduct = false,
                SourceProviderName = "NoavaranCurrentApi",
                CalculatedAtUtc = now
            },
            new CompanyProductRevenueMixRow
            {
                Id = Guid.NewGuid(),
                ExternalCompanyId = "5",
                CompanySymbol = "کگل",
                CompanyName = "معدنی و صنعتی گل گهر",
                ReportYear = 1403,
                ReportMonth = 3,
                FiscalEndDate = "1403/12/29",
                ProductName = "دانه‌بندی",
                ProductionQuantity = 300_000m,
                SalesQuantity = 295_000m,
                SalesRate = 47_000m,
                SalesAmount = 15_000_000_000_000m,
                TotalCompanySalesAmount = 100_000_000_000_000m,
                RevenueSharePercentage = 15m,
                ProductRank = 3,
                IsDominantProduct = false,
                SourceProviderName = "NoavaranCurrentApi",
                CalculatedAtUtc = now
            },
            new CompanyProductRevenueMixRow
            {
                Id = Guid.NewGuid(),
                ExternalCompanyId = "1",
                CompanySymbol = "فملی",
                CompanyName = "ملی صنایع مس ایران",
                ReportYear = 1403,
                ReportMonth = 3,
                FiscalEndDate = "1403/12/29",
                ProductName = "کاتد مس",
                ProductionQuantity = 2_100_000m,
                SalesQuantity = 2_080_000m,
                SalesRate = 110_000m,
                SalesAmount = 70_000_000_000_000m,
                TotalCompanySalesAmount = 100_000_000_000_000m,
                RevenueSharePercentage = 70m,
                ProductRank = 1,
                IsDominantProduct = true,
                SourceProviderName = "NoavaranCurrentApi",
                CalculatedAtUtc = now
            },
            new CompanyProductRevenueMixRow
            {
                Id = Guid.NewGuid(),
                ExternalCompanyId = "1",
                CompanySymbol = "فملی",
                CompanyName = "ملی صنایع مس ایران",
                ReportYear = 1403,
                ReportMonth = 3,
                FiscalEndDate = "1403/12/29",
                ProductName = "اسلب مس",
                ProductionQuantity = 700_000m,
                SalesQuantity = 690_000m,
                SalesRate = 102_000m,
                SalesAmount = 20_000_000_000_000m,
                TotalCompanySalesAmount = 100_000_000_000_000m,
                RevenueSharePercentage = 20m,
                ProductRank = 2,
                IsDominantProduct = false,
                SourceProviderName = "NoavaranCurrentApi",
                CalculatedAtUtc = now
            },
            new CompanyProductRevenueMixRow
            {
                Id = Guid.NewGuid(),
                ExternalCompanyId = "1",
                CompanySymbol = "فملی",
                CompanyName = "ملی صنایع مس ایران",
                ReportYear = 1403,
                ReportMonth = 3,
                FiscalEndDate = "1403/12/29",
                ProductName = "آند مس",
                ProductionQuantity = 300_000m,
                SalesQuantity = 290_000m,
                SalesRate = 98_000m,
                SalesAmount = 10_000_000_000_000m,
                TotalCompanySalesAmount = 100_000_000_000_000m,
                RevenueSharePercentage = 10m,
                ProductRank = 3,
                IsDominantProduct = false,
                SourceProviderName = "NoavaranCurrentApi",
                CalculatedAtUtc = now
            });
    }

    private sealed class ThrowingSymbolMetricLookupService : ISymbolMetricLookupService
    {
        public Task<SymbolLookupTableResult> LookupAsync(
            SymbolLookupRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("simulated live-metric outage");
    }
}

public sealed class V2ShgolDirectPriceRegressionApiFactory : AiFacadeApiFactory
{
    private readonly string _dbName = $"v2-shgol-direct-price-{Guid.NewGuid():N}";
    private bool _seeded;
    private readonly object _seedLock = new();
    internal V2SymbolLookupFakeAiModelClient Fake { get; } = new();

    protected override bool ForceV1Orchestration => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiOrchestration:Mode"] = "MicrosoftAgentFrameworkV2"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            ReplaceIngestionDbContext(services, _dbName);
            services.RemoveAll<IAiModelClient>();
            services.AddSingleton<IAiModelClient>(_ => Fake);
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
            SeedLookupData(db);
            db.SaveChanges();
            _seeded = true;
        }
    }

    private static void SeedLookupData(FinancialIngestionDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var companyId = Guid.Parse("53000000-0000-0000-0000-000000000167");
        var instrumentId = Guid.Parse("92990a92-e853-47e3-a682-bb8794b22999");

        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = companyId,
            Name = "گلتاش",
            ProviderName = "NoavaranCurrentApi",
            ExternalCompanyId = "167",
            Ticker = null,
            TseSymbol = "شگل",
            CompanySymbol = "شگل",
            InstrumentCode = "44153164692325703",
            LastSynchronizedAt = now
        });

        db.TradingInstruments.Add(new TradingInstrumentRow
        {
            Id = instrumentId,
            ProviderName = "TsetmcWebService",
            ExternalInstrumentId = Guid.NewGuid(),
            InstrumentCode = 44153164692325703,
            Symbol = "شگل",
            Name = "گلتاش",
            NormalizedCompanyId = companyId,
            IsActive = true,
            SourceChangedAt = now,
            LastSynchronizedAt = now
        });

        db.DailyInstrumentTrades.Add(new DailyInstrumentTradeRow
        {
            Id = Guid.NewGuid(),
            ProviderName = "TsetmcWebService",
            ExternalTradeId = Guid.NewGuid(),
            TradingInstrumentId = instrumentId,
            TradingDate = today.AddDays(-2),
            ClosingPrice = 3911m,
            LastTradedPrice = 3934m,
            PriceChange = 91m,
            PriceYesterday = 3820m,
            SourceInsertedAt = now.AddDays(-1)
        });

        db.LatestMarketQuotes.Add(new LatestMarketQuoteRow
        {
            Id = Guid.NewGuid(),
            ProviderName = "TsetmcWebService",
            TradingInstrumentId = instrumentId,
            LatestPrice = 3934m,
            PriceChangePercentage = 2.9842931937172800m,
            SourceKind = "Intraday",
            TradingDate = today.AddDays(-2),
            AsOf = now.AddDays(-2)
        });
    }
}

public sealed class V2ScannerApiFactory : ScannerExecutionApiFactory
{
    // Let V2 orchestration stand â€” do not replace with V1.
    protected override bool ForceV1Orchestration => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // Override V1 mode set by AiFacadeApiFactory with V2. Later ConfigureAppConfiguration
        // registrations have higher priority in Microsoft.Extensions.Configuration.
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiOrchestration:Mode"] = "MicrosoftAgentFrameworkV2"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiModelClient>();
            services.AddSingleton<IAiModelClient>(_ => new V2ScannerFakeAiModelClient());
        });
    }
}

// â”€â”€â”€ V2 Symbol Lookup factory â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

public sealed class V2SymbolLookupApiFactory : AiFacadeApiFactory
{
    private readonly string _dbName = $"v2-lookup-ingestion-{Guid.NewGuid():N}";
    private bool _seeded;
    private readonly object _seedLock = new();
    internal V2SymbolLookupFakeAiModelClient Fake { get; } = new();

    // Let V2 orchestration stand â€” do not replace with V1.
    protected override bool ForceV1Orchestration => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // Override V1 mode set by AiFacadeApiFactory with V2. Later ConfigureAppConfiguration
        // registrations have higher priority in Microsoft.Extensions.Configuration.
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiOrchestration:Mode"] = "MicrosoftAgentFrameworkV2"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            ReplaceIngestionDbContext(services, _dbName);
            services.RemoveAll<IAiModelClient>();
            services.AddSingleton<IAiModelClient>(_ => Fake);
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
            SeedLookupData(db);
            db.SaveChanges();
            _seeded = true;
        }
    }

    private static void SeedLookupData(FinancialIngestionDbContext db)
    {
        var companyHafariId = Guid.Parse("50000000-0000-0000-0000-100000000001");
        var companyKchadId = Guid.Parse("50000000-0000-0000-0000-100000000002");
        var companyKgolId = Guid.Parse("50000000-0000-0000-0000-100000000003");
        var companyShpnaId = Guid.Parse("50000000-0000-0000-0000-100000000004");
        var companyShbandarId = Guid.Parse("50000000-0000-0000-0000-100000000005");
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var periodStart = new DateOnly(2025, 1, 1);
        var periodEnd = new DateOnly(2025, 12, 31);

        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = companyHafariId,
            Name = "\u062d\u0641\u0627\u0631\u06cc \u0634\u0645\u0627\u0644",
            ProviderName = "test",
            ExternalCompanyId = "hafari-v2-001",
            Ticker = "\u062d\u0641\u0627\u0631\u06cc",
            TseSymbol = "HAF_TSE",
            CompanySymbol = "HAFARI",
            LastSynchronizedAt = now
        });

        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = companyKchadId,
            Name = "\u0645\u0639\u062f\u0646\u06cc \u0648 \u0635\u0646\u0639\u062a\u06cc \u0686\u0627\u062f\u0631\u0645\u0644\u0648",
            ProviderName = "test",
            ExternalCompanyId = "kchad-v2-001",
            Ticker = "\u06a9\u0686\u0627\u062f",
            TseSymbol = "\u06a9\u0686\u0627\u062f",
            CompanySymbol = "\u06a9\u0686\u0627\u062f",
            LastSynchronizedAt = now
        });

        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = companyKgolId,
            Name = "\u0645\u0639\u062f\u0646\u06cc \u0648 \u0635\u0646\u0639\u062a\u06cc \u06af\u0644 \u06af\u0647\u0631",
            ProviderName = "test",
            ExternalCompanyId = "kgol-v2-001",
            Ticker = "\u06a9\u06af\u0644",
            TseSymbol = "\u06a9\u06af\u0644",
            CompanySymbol = "\u06a9\u06af\u0644",
            LastSynchronizedAt = now
        });

        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = companyShpnaId,
            Name = "\u067e\u0627\u0644\u0627\u06cc\u0634 \u0646\u0641\u062a \u0627\u0635\u0641\u0647\u0627\u0646",
            ProviderName = "test",
            ExternalCompanyId = "shpna-v2-001",
            Ticker = "\u0634\u067e\u0646\u0627",
            TseSymbol = "\u0634\u067e\u0646\u0627",
            CompanySymbol = "\u0634\u067e\u0646\u0627",
            LastSynchronizedAt = now
        });

        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = companyShbandarId,
            Name = "\u067e\u0627\u0644\u0627\u06cc\u0634 \u0646\u0641\u062a \u0628\u0646\u062f\u0631\u0639\u0628\u0627\u0633",
            ProviderName = "test",
            ExternalCompanyId = "shbandar-v2-001",
            Ticker = "\u0634\u0628\u0646\u062f\u0631",
            TseSymbol = "\u0634\u0628\u0646\u062f\u0631",
            CompanySymbol = "\u0634\u0628\u0646\u062f\u0631",
            LastSynchronizedAt = now
        });

        var kchadInstrumentId = Guid.Parse("51000000-0000-0000-0000-100000000002");
        var kgolInstrumentId = Guid.Parse("51000000-0000-0000-0000-100000000003");
        var shpnaInstrumentId = Guid.Parse("51000000-0000-0000-0000-100000000004");
        var shbandarInstrumentId = Guid.Parse("51000000-0000-0000-0000-100000000005");

        db.TradingInstruments.AddRange(
            new TradingInstrumentRow
            {
                Id = kchadInstrumentId,
                ProviderName = "StockMarketDb",
                ExternalInstrumentId = Guid.NewGuid(),
                InstrumentCode = 2002,
                Symbol = "KCHAD",
                Name = "KCHAD",
                NormalizedCompanyId = companyKchadId,
                IsActive = true,
                SourceChangedAt = now,
                LastSynchronizedAt = now
            },
            new TradingInstrumentRow
            {
                Id = kgolInstrumentId,
                ProviderName = "StockMarketDb",
                ExternalInstrumentId = Guid.NewGuid(),
                InstrumentCode = 2003,
                Symbol = "KGOL",
                Name = "KGOL",
                NormalizedCompanyId = companyKgolId,
                IsActive = true,
                SourceChangedAt = now,
                LastSynchronizedAt = now
            },
            new TradingInstrumentRow
            {
                Id = shpnaInstrumentId,
                ProviderName = "StockMarketDb",
                ExternalInstrumentId = Guid.NewGuid(),
                InstrumentCode = 2004,
                Symbol = "SHPNA",
                Name = "SHPNA",
                NormalizedCompanyId = companyShpnaId,
                IsActive = true,
                SourceChangedAt = now,
                LastSynchronizedAt = now
            },
            new TradingInstrumentRow
            {
                Id = shbandarInstrumentId,
                ProviderName = "StockMarketDb",
                ExternalInstrumentId = Guid.NewGuid(),
                InstrumentCode = 2005,
                Symbol = "SHBANDAR",
                Name = "SHBANDAR",
                NormalizedCompanyId = companyShbandarId,
                IsActive = true,
                SourceChangedAt = now,
                LastSynchronizedAt = now
            });

        db.IntradayTradeSnapshots.Add(new IntradayTradeSnapshotRow
        {
            Id = Guid.NewGuid(),
            ProviderName = "StockMarketDb",
            ExternalSnapshotId = Guid.NewGuid(),
            TradingInstrumentId = kchadInstrumentId,
            TradingDate = today,
            TradingTime = new TimeOnly(12, 30),
            ClosingPrice = 10_115m,
            LastTradedPrice = 26_350m,
            PriceChange = 115m,
            PriceYesterday = 10_000m,
            ReceivedAt = now
        });

        db.DailyInstrumentTrades.AddRange(
            new DailyInstrumentTradeRow
            {
                Id = Guid.NewGuid(),
                ProviderName = "StockMarketDb",
                ExternalTradeId = Guid.NewGuid(),
                TradingInstrumentId = kgolInstrumentId,
                TradingDate = today.AddDays(-1),
                ClosingPrice = 2_025m,
                LastTradedPrice = 2_110m,
                PriceChange = 25m,
                PriceYesterday = 2_000m,
                SourceInsertedAt = now.AddDays(-1)
            },
            new DailyInstrumentTradeRow
            {
                Id = Guid.NewGuid(),
                ProviderName = "StockMarketDb",
                ExternalTradeId = Guid.NewGuid(),
                TradingInstrumentId = shpnaInstrumentId,
                TradingDate = today.AddDays(-1),
                ClosingPrice = 1_0088m,
                LastTradedPrice = 8_120m,
                PriceChange = 88m,
                PriceYesterday = 10_000m,
                SourceInsertedAt = now.AddDays(-1)
            },
            new DailyInstrumentTradeRow
            {
                Id = Guid.NewGuid(),
                ProviderName = "StockMarketDb",
                ExternalTradeId = Guid.NewGuid(),
                TradingInstrumentId = shbandarInstrumentId,
                TradingDate = today.AddDays(-1),
                ClosingPrice = 10_035m,
                LastTradedPrice = 7_340m,
                PriceChange = 35m,
                PriceYesterday = 10_000m,
                SourceInsertedAt = now.AddDays(-1)
            });

        db.DerivedMetrics.Add(new DerivedMetricRow
        {
            Id = Guid.NewGuid(),
            ExternalCompanyId = "hafari-v2-001",
            MetricCode = "PE_TTM",
            MetricVersion = "v1",
            CalculationPolicyVersion = "PE_TTM_v1",
            PeriodType = "TrailingTwelveMonths",
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Value = 5.2m,
            Unit = "Ratio",
            ObservedAt = now,
            LastSynchronizedAt = now,
            WarningsJson = "[]",
            SourceEvidenceJson = "[]",
            DependencyEvidenceJson = "[]"
        });

        db.DerivedMetrics.Add(new DerivedMetricRow
        {
            Id = Guid.NewGuid(),
            ExternalCompanyId = "kchad-v2-001",
            MetricCode = "PE_TTM",
            MetricVersion = "v1",
            CalculationPolicyVersion = "PE_TTM_v1",
            PeriodType = "TrailingTwelveMonths",
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Value = 9.73m,
            Unit = "Ratio",
            ObservedAt = now,
            LastSynchronizedAt = now,
            WarningsJson = "[]",
            SourceEvidenceJson = "[]",
            DependencyEvidenceJson = "[]"
        });

        db.DerivedMetrics.Add(new DerivedMetricRow
        {
            Id = Guid.NewGuid(),
            ExternalCompanyId = "kchad-v2-001",
            MetricCode = "LATEST_PRICE",
            MetricVersion = "v1",
            CalculationPolicyVersion = "LATEST_PRICE_v1",
            PeriodType = "TrailingTwelveMonths",
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Value = 26350m,
            Unit = "Price",
            ObservedAt = now,
            LastSynchronizedAt = now,
            WarningsJson = "[]",
            SourceEvidenceJson = "[]",
            DependencyEvidenceJson = "[]"
        });

        db.DerivedMetrics.Add(new DerivedMetricRow
        {
            Id = Guid.NewGuid(),
            ExternalCompanyId = "kchad-v2-001",
            MetricCode = "DAILY_CHANGE_PCT",
            MetricVersion = "v1",
            CalculationPolicyVersion = "DAILY_CHANGE_PCT_v1",
            PeriodType = "TrailingTwelveMonths",
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Value = 1.15m,
            Unit = "Percent",
            ObservedAt = now,
            LastSynchronizedAt = now,
            WarningsJson = "[]",
            SourceEvidenceJson = "[]",
            DependencyEvidenceJson = "[]"
        });

        db.DerivedMetrics.Add(new DerivedMetricRow
        {
            Id = Guid.NewGuid(),
            ExternalCompanyId = "kchad-v2-001",
            MetricCode = "MONTHLY_SALES_YTD",
            MetricVersion = "v1",
            CalculationPolicyVersion = "monthly-sales-ytd-source-v1",
            PeriodType = "Monthly",
            PeriodStart = new DateOnly(2026, 5, 1),
            PeriodEnd = new DateOnly(2026, 5, 31),
            Value = 787_016_400_000_000m,
            Unit = "Amount",
            ObservedAt = now,
            LastSynchronizedAt = now,
            WarningsJson = "[]",
            SourceEvidenceJson = "[]",
            DependencyEvidenceJson = "[]"
        });

        db.DerivedMetrics.Add(new DerivedMetricRow
        {
            Id = Guid.NewGuid(),
            ExternalCompanyId = "kchad-v2-001",
            MetricCode = "MONTHLY_SALES_YTD_PREVIOUS_MONTH",
            MetricVersion = "v1",
            CalculationPolicyVersion = "monthly-sales-ytd-previous-month-source-v1",
            PeriodType = "Monthly",
            PeriodStart = new DateOnly(2026, 5, 1),
            PeriodEnd = new DateOnly(2026, 5, 31),
            Value = 605_344_668_000_000m,
            Unit = "Amount",
            ObservedAt = now,
            LastSynchronizedAt = now,
            WarningsJson = "[]",
            SourceEvidenceJson = "[]",
            DependencyEvidenceJson = "[]"
        });

        db.DerivedMetrics.Add(new DerivedMetricRow
        {
            Id = Guid.NewGuid(),
            ExternalCompanyId = "kgol-v2-001",
            MetricCode = "PE_TTM",
            MetricVersion = "v1",
            CalculationPolicyVersion = "PE_TTM_v1",
            PeriodType = "TrailingTwelveMonths",
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Value = 4.12m,
            Unit = "Ratio",
            ObservedAt = now,
            LastSynchronizedAt = now,
            WarningsJson = "[]",
            SourceEvidenceJson = "[]",
            DependencyEvidenceJson = "[]"
        });

        db.DerivedMetrics.Add(new DerivedMetricRow
        {
            Id = Guid.NewGuid(),
            ExternalCompanyId = "kgol-v2-001",
            MetricCode = "LATEST_PRICE",
            MetricVersion = "v1",
            CalculationPolicyVersion = "LATEST_PRICE_v1",
            PeriodType = "TrailingTwelveMonths",
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Value = 2110m,
            Unit = "Price",
            ObservedAt = now,
            LastSynchronizedAt = now,
            WarningsJson = "[]",
            SourceEvidenceJson = "[]",
            DependencyEvidenceJson = "[]"
        });

        db.DerivedMetrics.Add(new DerivedMetricRow
        {
            Id = Guid.NewGuid(),
            ExternalCompanyId = "kgol-v2-001",
            MetricCode = "DAILY_CHANGE_PCT",
            MetricVersion = "v1",
            CalculationPolicyVersion = "DAILY_CHANGE_PCT_v1",
            PeriodType = "TrailingTwelveMonths",
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Value = 1.25m,
            Unit = "Percent",
            ObservedAt = now,
            LastSynchronizedAt = now,
            WarningsJson = "[]",
            SourceEvidenceJson = "[]",
            DependencyEvidenceJson = "[]"
        });

        db.DerivedMetrics.Add(new DerivedMetricRow
        {
            Id = Guid.NewGuid(),
            ExternalCompanyId = "shpna-v2-001",
            MetricCode = "PE_TTM",
            MetricVersion = "v1",
            CalculationPolicyVersion = "PE_TTM_v1",
            PeriodType = "TrailingTwelveMonths",
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Value = 5.17m,
            Unit = "Ratio",
            ObservedAt = now,
            LastSynchronizedAt = now,
            WarningsJson = "[]",
            SourceEvidenceJson = "[]",
            DependencyEvidenceJson = "[]"
        });

        db.DerivedMetrics.Add(new DerivedMetricRow
        {
            Id = Guid.NewGuid(),
            ExternalCompanyId = "shpna-v2-001",
            MetricCode = "LATEST_PRICE",
            MetricVersion = "v1",
            CalculationPolicyVersion = "LATEST_PRICE_v1",
            PeriodType = "TrailingTwelveMonths",
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Value = 8120m,
            Unit = "Price",
            ObservedAt = now,
            LastSynchronizedAt = now,
            WarningsJson = "[]",
            SourceEvidenceJson = "[]",
            DependencyEvidenceJson = "[]"
        });

        db.DerivedMetrics.Add(new DerivedMetricRow
        {
            Id = Guid.NewGuid(),
            ExternalCompanyId = "shpna-v2-001",
            MetricCode = "DAILY_CHANGE_PCT",
            MetricVersion = "v1",
            CalculationPolicyVersion = "DAILY_CHANGE_PCT_v1",
            PeriodType = "TrailingTwelveMonths",
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Value = 0.88m,
            Unit = "Percent",
            ObservedAt = now,
            LastSynchronizedAt = now,
            WarningsJson = "[]",
            SourceEvidenceJson = "[]",
            DependencyEvidenceJson = "[]"
        });

        db.DerivedMetrics.Add(new DerivedMetricRow
        {
            Id = Guid.NewGuid(),
            ExternalCompanyId = "shbandar-v2-001",
            MetricCode = "PE_TTM",
            MetricVersion = "v1",
            CalculationPolicyVersion = "PE_TTM_v1",
            PeriodType = "TrailingTwelveMonths",
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Value = 5.06m,
            Unit = "Ratio",
            ObservedAt = now,
            LastSynchronizedAt = now,
            WarningsJson = "[]",
            SourceEvidenceJson = "[]",
            DependencyEvidenceJson = "[]"
        });

        db.DerivedMetrics.Add(new DerivedMetricRow
        {
            Id = Guid.NewGuid(),
            ExternalCompanyId = "shbandar-v2-001",
            MetricCode = "LATEST_PRICE",
            MetricVersion = "v1",
            CalculationPolicyVersion = "LATEST_PRICE_v1",
            PeriodType = "TrailingTwelveMonths",
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Value = 7340m,
            Unit = "Price",
            ObservedAt = now,
            LastSynchronizedAt = now,
            WarningsJson = "[]",
            SourceEvidenceJson = "[]",
            DependencyEvidenceJson = "[]"
        });

        db.DerivedMetrics.Add(new DerivedMetricRow
        {
            Id = Guid.NewGuid(),
            ExternalCompanyId = "shbandar-v2-001",
            MetricCode = "DAILY_CHANGE_PCT",
            MetricVersion = "v1",
            CalculationPolicyVersion = "DAILY_CHANGE_PCT_v1",
            PeriodType = "TrailingTwelveMonths",
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Value = 0.35m,
            Unit = "Percent",
            ObservedAt = now,
            LastSynchronizedAt = now,
            WarningsJson = "[]",
            SourceEvidenceJson = "[]",
            DependencyEvidenceJson = "[]"
        });
    }
}

// â”€â”€â”€ V2 inconsistent-lookup factory (consistency guardrail) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

public sealed class V2InconsistentLookupApiFactory : AiFacadeApiFactory
{
    private readonly string _dbName = $"v2-inconsistent-lookup-{Guid.NewGuid():N}";
    private bool _seeded;
    private readonly object _seedLock = new();

    protected override bool ForceV1Orchestration => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiOrchestration:Mode"] = "MicrosoftAgentFrameworkV2"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            ReplaceIngestionDbContext(services, _dbName);
            services.RemoveAll<IAiModelClient>();
            services.AddSingleton<IAiModelClient>(_ => new V2InconsistentLookupFakeAiModelClient());
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
            SeedLookupData(db);
            db.SaveChanges();
            _seeded = true;
        }
    }

    private static void SeedLookupData(FinancialIngestionDbContext db)
    {
        var companyId = Guid.Parse("50000000-0000-0000-0000-200000000001");
        var now = DateTimeOffset.UtcNow;

        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = companyId,
            Name = "Ù¾ØªØ±ÙˆØ´ÛŒÙ…ÛŒ Ø¨Ù†Ø¯Ø±Ø§Ù…Ø§Ù…",
            ProviderName = "test",
            ExternalCompanyId = "shabandar-v2-001",
            TseSymbol = "Ø´Ø¨Ù†Ø¯Ø±",
            CompanySymbol = "Ø´Ø¨Ù†Ø¯Ø±",
            LastSynchronizedAt = now
        });

        db.DerivedMetrics.Add(new DerivedMetricRow
        {
            Id = Guid.NewGuid(),
            ExternalCompanyId = "shabandar-v2-001",
            MetricCode = "PE_TTM",
            MetricVersion = "v1",
            CalculationPolicyVersion = "PE_TTM_v1",
            PeriodType = "ThreeMonths",
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 3, 31),
            Value = 5.06m,
            Unit = "Ratio",
            ObservedAt = now,
            LastSynchronizedAt = now,
            WarningsJson = "[]",
            SourceEvidenceJson = "[]",
            DependencyEvidenceJson = "[]"
        });
    }
}

// Fake whose turn-2 prose contradicts the deterministic table (states "7.88" instead of 5.06).
internal sealed class V2InconsistentLookupFakeAiModelClient : IAiModelClient
{
    public AiModelProviderDescriptor Descriptor { get; } = new(
        "V2InconsistentLookupFake",
        "fake-v2",
        AiProviderHostingMode.Fake,
        AiModelCapability.ChatCompletion | AiModelCapability.ToolCalling |
        AiModelCapability.StructuredOutput | AiModelCapability.UsageReporting |
        AiModelCapability.HealthCheck,
        Enabled: true,
        Priority: 1);

    public Task<AiModelResult> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken)
    {
        // Turn 2: continuation after tool execution â€” author CONFLICTING prose.
        if (request.PreviousResponseId is not null)
        {
            return Task.FromResult(new AiModelResult(
                Text: "Ù†Ø³Ø¨Øª P/E Ù†Ù…Ø§Ø¯ Ø´Ø¨Ù†Ø¯Ø± Ø¨Ø±Ø§Ø¨Ø± Ø§Ø³Øª Ø¨Ø§ 7.88",
                StructuredJson: null,
                ToolCalls: [],
                Usage: MakeUsage(request)));
        }

        // Turn 1: fire the lookup tool.
        if (request.Tools is { Count: > 0 })
        {
            return Task.FromResult(new AiModelResult(
                Text: null,
                StructuredJson: null,
                ToolCalls: [new AiToolCall(
                    "v2-inconsistent-call-1",
                    "lookup_symbol_metrics",
                    "{\"query\":\"pe Ø´Ø¨Ù†Ø¯Ø± Ú†Ù‚Ø¯Ø± Ø§Ø³ØªØŸ\"}")],
                Usage: MakeUsage(request, usedTools: true),
                ResponseId: $"fake-v2-resp-{request.CorrelationId}"));
        }

        var json = request.StructuredOutput?.SchemaName switch
        {
            "SymbolLookupParseOutput" =>
                """{"detectedLanguage":"fa","pairs":[{"symbolName":"Ø´Ø¨Ù†Ø¯Ø±","metricTerm":"pe"}],"clarificationRequired":false,"clarificationMessage":null}""",
            _ => "{}"
        };

        return Task.FromResult(new AiModelResult(
            Text: null,
            StructuredJson: json,
            ToolCalls: [],
            Usage: MakeUsage(request)));
    }

    public IAsyncEnumerable<AiStreamingChunk> StreamAsync(
        AiModelRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<AiEmbeddingResult> CreateEmbeddingsAsync(
        AiEmbeddingRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<AiProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AiProviderHealthResult(
            Descriptor.ProviderKey, Descriptor.ModelKey,
            Available: true, DateTimeOffset.UtcNow, "OK"));

    private AiExecutionUsageFacts MakeUsage(AiModelRequest request, bool usedTools = false) =>
        new(request.CorrelationId, Descriptor.ProviderKey, Descriptor.ModelKey,
            AiExecutionStatus.Completed, TimeSpan.Zero, AttemptNumber: 0,
            InputTokens: 10, OutputTokens: 4, UsedTools: usedTools);

}

// â”€â”€â”€ V2 composite fake for scanner â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
// Handles the three call types that occur in a V2 scanner flow:
//   1. V2 outer agent, turn 1 â€” tools present, no PreviousResponseId â†’ return screen_stocks tool call
//   2. V2 outer agent, turn 2 â€” PreviousResponseId set â†’ return final text
//   3. Internal ScannerParsing / ExplanationGeneration structured output calls
public sealed class V2MonthlySalesRoutingFakeAiModelClient : IAiModelClient
{
    private int _outerToolSelectionCalls;

    public int OuterToolSelectionCalls => _outerToolSelectionCalls;

    public AiModelProviderDescriptor Descriptor { get; } = new(
        "V2MonthlySalesRoutingFake",
        "fake-v2",
        AiProviderHostingMode.Fake,
        AiModelCapability.ChatCompletion | AiModelCapability.ToolCalling |
        AiModelCapability.StructuredOutput | AiModelCapability.UsageReporting |
        AiModelCapability.HealthCheck,
        Enabled: true,
        Priority: 1);

    public Task<AiModelResult> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken)
    {
        if (request.Tools is { Count: > 0 })
        {
            Interlocked.Increment(ref _outerToolSelectionCalls);
            return Task.FromResult(new AiModelResult(
                Text: "Monthly sales did not return directly. If you want, I can clarify the metric.",
                StructuredJson: null,
                ToolCalls: [],
                Usage: MakeUsage(request)));
        }

        var json = request.StructuredOutput?.SchemaName switch
        {
            "SymbolLookupParseOutput" => BuildSymbolLookupParseJson(request),
            _ => "{}"
        };

        return Task.FromResult(new AiModelResult(
            Text: null,
            StructuredJson: json,
            ToolCalls: [],
            Usage: MakeUsage(request)));
    }

    public IAsyncEnumerable<AiStreamingChunk> StreamAsync(
        AiModelRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<AiEmbeddingResult> CreateEmbeddingsAsync(
        AiEmbeddingRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<AiProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AiProviderHealthResult(
            Descriptor.ProviderKey, Descriptor.ModelKey,
            Available: true, DateTimeOffset.UtcNow, "OK"));

    private AiExecutionUsageFacts MakeUsage(AiModelRequest request) =>
        new(request.CorrelationId, Descriptor.ProviderKey, Descriptor.ModelKey,
            AiExecutionStatus.Completed, TimeSpan.Zero, AttemptNumber: 0,
            InputTokens: 10, OutputTokens: 4, UsedTools: false);

    private static string BuildSymbolLookupParseJson(AiModelRequest request)
    {
        var userMessage = request.Messages.LastOrDefault(m => m.Role == AiMessageRole.User)?.Content ?? string.Empty;
        var metricTerm =
            userMessage.Contains("YTD \u062a\u0627 \u0645\u0627\u0647 \u0642\u0628\u0644", StringComparison.OrdinalIgnoreCase)
                ? "\u0641\u0631\u0648\u0634 YTD \u062a\u0627 \u0645\u0627\u0647 \u0642\u0628\u0644"
                : userMessage.Contains("YTD", StringComparison.OrdinalIgnoreCase)
                    ? "\u0641\u0631\u0648\u0634 YTD"
                    : userMessage.Contains("\u0645\u062a\u0648\u0633\u0637 \u0641\u0631\u0648\u0634", StringComparison.OrdinalIgnoreCase)
                        ? "\u0645\u062a\u0648\u0633\u0637 \u0641\u0631\u0648\u0634 12 \u0645\u0627\u0647\u0647"
                        : "\u0641\u0631\u0648\u0634 \u0645\u0627\u0647\u0627\u0646\u0647";

        var symbolName =
            userMessage.Contains("\u0686\u0627\u062f\u0631\u0645\u0644\u0648", StringComparison.OrdinalIgnoreCase)
                ? "\u0686\u0627\u062f\u0631\u0645\u0644\u0648"
                : userMessage.Contains("\u06a9\u0686\u0627\u062f", StringComparison.OrdinalIgnoreCase)
                    ? "\u06a9\u0686\u0627\u062f"
                    : userMessage.Contains("\u06af\u0644 \u06af\u0647\u0631", StringComparison.OrdinalIgnoreCase)
                        ? "\u06af\u0644 \u06af\u0647\u0631"
                        : userMessage.Contains("\u06af\u0644\u06af\u0647\u0631", StringComparison.OrdinalIgnoreCase)
                            ? "\u06af\u0644\u06af\u0647\u0631"
                            : userMessage.Contains("\u06a9\u06af\u0644", StringComparison.OrdinalIgnoreCase)
                                ? "\u06a9\u06af\u0644"
                    : userMessage.Contains("\u0646\u0627\u0645\u0648\u062c\u0648\u062f", StringComparison.OrdinalIgnoreCase)
                        ? "\u0646\u0627\u0645\u0648\u062c\u0648\u062f"
                        : null;

        if (symbolName is null)
        {
            return JsonSerializer.Serialize(new
            {
                detectedLanguage = "fa",
                pairs = Array.Empty<object>(),
                clarificationRequired = true,
                clarificationMessage = "\u0644\u0637\u0641\u0627\u064b \u0646\u0645\u0627\u062f \u0631\u0627 \u0645\u0634\u062e\u0635 \u06a9\u0646\u06cc\u062f."
            });
        }

        return JsonSerializer.Serialize(new
        {
            detectedLanguage = "fa",
            pairs = new[] { new { symbolName, metricTerm } },
            clarificationRequired = false,
            clarificationMessage = (string?)null
        });
    }
}

internal sealed class V2ScannerFakeAiModelClient : IAiModelClient
{
    public AiModelProviderDescriptor Descriptor { get; } = new(
        "V2ScannerFake",
        "fake-v2",
        AiProviderHostingMode.Fake,
        AiModelCapability.ChatCompletion | AiModelCapability.ToolCalling |
        AiModelCapability.StructuredOutput | AiModelCapability.UsageReporting |
        AiModelCapability.HealthCheck,
        Enabled: true,
        Priority: 1);

    public Task<AiModelResult> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken)
    {
        // Turn 2: continuation after tool execution via previous_response_id
        if (request.PreviousResponseId is not null)
        {
            return Task.FromResult(new AiModelResult(
                Text: "I found 2 stocks matching your P/E criteria.",
                StructuredJson: null,
                ToolCalls: [],
                Usage: MakeUsage(request)));
        }

        // Turn 1: V2 outer agent turn â€” fire the screen_stocks tool
        if (request.Tools is { Count: > 0 })
        {
            return Task.FromResult(new AiModelResult(
                Text: null,
                StructuredJson: null,
                ToolCalls: [new AiToolCall(
                    "v2-scanner-call-1",
                    "screen_stocks",
                    "{\"query\":\"P/E below 6\"}")],
                Usage: MakeUsage(request, usedTools: true),
                ResponseId: $"fake-v2-resp-{request.CorrelationId}"));
        }

        // Internal structured output calls (ScannerParsing, ExplanationGeneration)
        var json = request.StructuredOutput?.SchemaName switch
        {
            "ScannerParseOutput" =>
                """{"detectedLanguage":"en","conditions":[{"userTerminology":"P/E","language":"en","operator":"LessThan","threshold":6.0,"periodHint":null,"growthComparison":null,"inferredDefault":false,"inferredReason":null}],"requestedColumns":[],"clarificationRequired":false,"clarificationMessage":null}""",
            "ScannerExplanationOutput" =>
                """{"explanationText":"Found 2 stocks with P/E below 6.","suggestedFollowUpQuestions":["Show ROE above 15","Filter market cap above 1B"]}""",
            _ => "{}"
        };

        return Task.FromResult(new AiModelResult(
            Text: null,
            StructuredJson: json,
            ToolCalls: [],
            Usage: MakeUsage(request)));
    }

    public IAsyncEnumerable<AiStreamingChunk> StreamAsync(
        AiModelRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<AiEmbeddingResult> CreateEmbeddingsAsync(
        AiEmbeddingRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<AiProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AiProviderHealthResult(
            Descriptor.ProviderKey, Descriptor.ModelKey,
            Available: true, DateTimeOffset.UtcNow, "OK"));

    private AiExecutionUsageFacts MakeUsage(AiModelRequest request, bool usedTools = false) =>
        new(request.CorrelationId, Descriptor.ProviderKey, Descriptor.ModelKey,
            AiExecutionStatus.Completed, TimeSpan.Zero, AttemptNumber: 0,
            InputTokens: 10, OutputTokens: 4, UsedTools: usedTools);

}

// â”€â”€â”€ V2 composite fake for symbol lookup â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
// Handles the three call types that occur in a V2 symbol lookup flow:
//   1. V2 outer agent, turn 1 â€” tools present, no PreviousResponseId â†’ return lookup_symbol_metrics call
//   2. V2 outer agent, turn 2 â€” PreviousResponseId set â†’ return final text
//   3. Internal SymbolLookupParsing structured output call
internal sealed class V2SymbolLookupFakeAiModelClient : IAiModelClient
{
    private int _outerToolSelectionCalls;
    private int _forceParserClarificationForChadormalu;
    private string? _lastParserUserMessage;

    public int OuterToolSelectionCalls => _outerToolSelectionCalls;
    public string? LastParserUserMessage => Volatile.Read(ref _lastParserUserMessage);

    public AiModelProviderDescriptor Descriptor { get; } = new(
        "V2LookupFake",
        "fake-v2",
        AiProviderHostingMode.Fake,
        AiModelCapability.ChatCompletion | AiModelCapability.ToolCalling |
        AiModelCapability.StructuredOutput | AiModelCapability.UsageReporting |
        AiModelCapability.HealthCheck,
        Enabled: true,
        Priority: 1);

    public Task<AiModelResult> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken)
    {
        // Turn 2: continuation after tool execution via previous_response_id
        if (request.PreviousResponseId is not null)
        {
            return Task.FromResult(new AiModelResult(
                Text: "Here are the P/E metrics for Ø­ÙØ§Ø±ÛŒ.",
                StructuredJson: null,
                ToolCalls: [],
                Usage: MakeUsage(request)));
        }

        // Turn 1: V2 outer agent turn â€” fire the lookup_symbol_metrics tool
        if (request.Tools is { Count: > 0 })
        {
            Interlocked.Increment(ref _outerToolSelectionCalls);
            return Task.FromResult(new AiModelResult(
                Text: null,
                StructuredJson: null,
                ToolCalls: [new AiToolCall(
                    "v2-lookup-call-1",
                    "lookup_symbol_metrics",
                    "{\"query\":\"PE Ø­ÙØ§Ø±ÛŒ Ú†Ù‚Ø¯Ø± Ø§Ø³ØªØŸ\"}")],
                Usage: MakeUsage(request, usedTools: true),
                ResponseId: $"fake-v2-resp-{request.CorrelationId}"));
        }

        // Internal structured output calls (SymbolLookupParsing)
        var json = request.StructuredOutput?.SchemaName switch
        {
            "SymbolLookupParseOutput" => BuildSymbolLookupParseJson(request),
            _ => "{}"
        };

        return Task.FromResult(new AiModelResult(
            Text: null,
            StructuredJson: json,
            ToolCalls: [],
            Usage: MakeUsage(request)));
    }

    public IAsyncEnumerable<AiStreamingChunk> StreamAsync(
        AiModelRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<AiEmbeddingResult> CreateEmbeddingsAsync(
        AiEmbeddingRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<AiProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AiProviderHealthResult(
            Descriptor.ProviderKey, Descriptor.ModelKey,
            Available: true, DateTimeOffset.UtcNow, "OK"));

    private AiExecutionUsageFacts MakeUsage(AiModelRequest request, bool usedTools = false) =>
        new(request.CorrelationId, Descriptor.ProviderKey, Descriptor.ModelKey,
            AiExecutionStatus.Completed, TimeSpan.Zero, AttemptNumber: 0,
            InputTokens: 10, OutputTokens: 4, UsedTools: usedTools);

    public void Reset()
    {
        Interlocked.Exchange(ref _outerToolSelectionCalls, 0);
        Interlocked.Exchange(ref _forceParserClarificationForChadormalu, 0);
        Volatile.Write(ref _lastParserUserMessage, null);
    }

    public void ForceParserClarificationForChadormalu() =>
        Interlocked.Exchange(ref _forceParserClarificationForChadormalu, 1);

    private string BuildSymbolLookupParseJson(AiModelRequest request)
    {
        var userMessage = request.Messages.LastOrDefault(m => m.Role == AiMessageRole.User)?.Content ?? string.Empty;
        Volatile.Write(ref _lastParserUserMessage, userMessage);
        if (Volatile.Read(ref _forceParserClarificationForChadormalu) == 1 &&
            userMessage.Contains("\u0686\u0627\u062f\u0631\u0645\u0644\u0648", StringComparison.OrdinalIgnoreCase))
        {
            return JsonSerializer.Serialize(new
            {
                detectedLanguage = "fa",
                pairs = Array.Empty<object>(),
                clarificationRequired = true,
                clarificationMessage = "\u0644\u0637\u0641\u0627\u064b \u0646\u0645\u0627\u062f \u0631\u0627 \u0645\u0634\u062e\u0635 \u06a9\u0646\u06cc\u062f."
            });
        }

        var symbol = userMessage.Contains("\u0686\u0627\u062f\u0631\u0645\u0644\u0648", StringComparison.OrdinalIgnoreCase)
            ? "\u0686\u0627\u062f\u0631\u0645\u0644\u0648"
            : userMessage.Contains("\u06a9\u0686\u0627\u062f", StringComparison.OrdinalIgnoreCase)
                ? "\u06a9\u0686\u0627\u062f"
                : userMessage.Contains("\u0634\u06af\u0644", StringComparison.OrdinalIgnoreCase)
                    ? "\u0634\u06af\u0644"
                : userMessage.Contains("\u06af\u0644 \u06af\u0647\u0631", StringComparison.OrdinalIgnoreCase)
                    ? "\u06af\u0644 \u06af\u0647\u0631"
                    : userMessage.Contains("\u06af\u0644\u06af\u0647\u0631", StringComparison.OrdinalIgnoreCase)
                        ? "\u06af\u0644\u06af\u0647\u0631"
                        : userMessage.Contains("\u06a9\u06af\u0644", StringComparison.OrdinalIgnoreCase)
                            ? "\u06a9\u06af\u0644"
                            : userMessage.Contains("\u067e\u0627\u0644\u0627\u06cc\u0634 \u0646\u0641\u062a \u0627\u0635\u0641\u0647\u0627\u0646", StringComparison.OrdinalIgnoreCase)
                                ? "\u067e\u0627\u0644\u0627\u06cc\u0634 \u0646\u0641\u062a \u0627\u0635\u0641\u0647\u0627\u0646"
                                : userMessage.Contains("\u0634\u067e\u0646\u0627", StringComparison.OrdinalIgnoreCase)
                                    ? "\u0634\u067e\u0646\u0627"
                                    : userMessage.Contains("\u067e\u0627\u0644\u0627\u06cc\u0634 \u0646\u0641\u062a \u0628\u0646\u062f\u0631\u0639\u0628\u0627\u0633", StringComparison.OrdinalIgnoreCase)
                                        ? "\u067e\u0627\u0644\u0627\u06cc\u0634 \u0646\u0641\u062a \u0628\u0646\u062f\u0631\u0639\u0628\u0627\u0633"
                                        : userMessage.Contains("\u0634\u0628\u0646\u062f\u0631", StringComparison.OrdinalIgnoreCase)
                                            ? "\u0634\u0628\u0646\u062f\u0631"
                                            : "\u062d\u0641\u0627\u0631\u06cc";

        var metricTerm =
            userMessage.Contains("\u062f\u0631\u0635\u062f \u062a\u063a\u06cc\u06cc\u0631 \u0642\u06cc\u0645\u062a", StringComparison.OrdinalIgnoreCase)
            || userMessage.Contains("\u062f\u0631\u0635\u062f \u062a\u063a\u06cc\u06cc\u0631 \u0631\u0648\u0632\u0627\u0646\u0647", StringComparison.OrdinalIgnoreCase)
            || userMessage.Contains("\u062a\u063a\u06cc\u06cc\u0631 \u0631\u0648\u0632\u0627\u0646\u0647", StringComparison.OrdinalIgnoreCase)
            || userMessage.Contains("\u062a\u063a\u06cc\u06cc\u0631 \u0642\u06cc\u0645\u062a", StringComparison.OrdinalIgnoreCase)
                ? "\u062f\u0631\u0635\u062f \u062a\u063a\u06cc\u06cc\u0631 \u0642\u06cc\u0645\u062a"
                : userMessage.Contains("\u0622\u062e\u0631\u06cc\u0646 \u0642\u06cc\u0645\u062a", StringComparison.OrdinalIgnoreCase)
                  || userMessage.Contains("\u0642\u06cc\u0645\u062a \u0627\u0645\u0631\u0648\u0632", StringComparison.OrdinalIgnoreCase)
                  || userMessage.Contains("\u0642\u06cc\u0645\u062a \u067e\u0627\u06cc\u0627\u0646\u06cc", StringComparison.OrdinalIgnoreCase)
                  || (userMessage.Contains("\u0642\u06cc\u0645\u062a", StringComparison.OrdinalIgnoreCase)
                      && !userMessage.Contains("\u0642\u06cc\u0645\u062a \u0628\u0647 \u0633\u0648\u062f", StringComparison.OrdinalIgnoreCase)
                      && !userMessage.Contains("\u0646\u0633\u0628\u062a \u0642\u06cc\u0645\u062a \u0628\u0647 \u0633\u0648\u062f", StringComparison.OrdinalIgnoreCase))
                    ? "\u0622\u062e\u0631\u06cc\u0646 \u0642\u06cc\u0645\u062a"
                    : "\u0646\u0633\u0628\u062a \u067e\u06cc \u0628\u0647 \u0627\u06cc";

        return $$"""{"detectedLanguage":"fa","pairs":[{"symbolName":"{{symbol}}","metricTerm":"{{metricTerm}}"}],"clarificationRequired":false,"clarificationMessage":null}""";
    }
}


