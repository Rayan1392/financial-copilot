using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FinancialCopilot.IntegrationTests;

public sealed class ExplainableAnswerEndpointTests : IClassFixture<ScannerExecutionApiFactory>
{
    private readonly ScannerExecutionApiFactory _factory;

    public ExplainableAnswerEndpointTests(ScannerExecutionApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
    }

    [Fact]
    public async Task AiQuery_ExplainableAnswer_PresentWhenScannerSucceeds()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "P/E below 6" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var explainable = document.RootElement.GetProperty("explainableAnswer");
        Assert.NotEqual(JsonValueKind.Null, explainable.ValueKind);
    }

    [Fact]
    public async Task AiQuery_ExplainableAnswer_FilterChipMatchesPlanCondition()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "P/E below 6" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        var chips = document.RootElement
            .GetProperty("explainableAnswer")
            .GetProperty("filterChips")
            .EnumerateArray()
            .ToList();

        Assert.Single(chips);
        var chip = chips[0];
        Assert.Equal("PE_TTM", chip.GetProperty("metricCode").GetString(), StringComparer.OrdinalIgnoreCase);
        Assert.Equal("<", chip.GetProperty("operatorSymbol").GetString());
        Assert.Equal("below", chip.GetProperty("operatorLabel").GetString());
        Assert.Equal(6.0m, chip.GetProperty("threshold").GetDecimal());
        Assert.False(chip.GetProperty("isInferred").GetBoolean());
    }

    [Fact]
    public async Task AiQuery_ExplainableAnswer_ConfidenceScoreIsBackendComputed()
    {
        // With clean seeded data (all metrics present, LIVE=Live price, FALLBACK=PreviousTradingDay,
        // no warnings, explicit conditions) the deterministic score must equal 1.0.
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "P/E below 6" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        var confidence = document.RootElement
            .GetProperty("explainableAnswer")
            .GetProperty("confidence");

        var score = confidence.GetProperty("score").GetDouble();
        Assert.Equal(1.0, score);
        Assert.Equal("v1", confidence.GetProperty("policyVersion").GetString());
    }

    [Fact]
    public async Task AiQuery_ExplainableAnswer_MetricEvidenceHasPolicyVersion()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "P/E below 6" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        var evidence = document.RootElement
            .GetProperty("explainableAnswer")
            .GetProperty("metricEvidence")
            .EnumerateArray()
            .ToList();

        Assert.Single(evidence);
        var ev = evidence[0];
        Assert.Equal("PE_TTM", ev.GetProperty("metricCode").GetString(), StringComparer.OrdinalIgnoreCase);
        Assert.Equal("v1", ev.GetProperty("metricVersion").GetString());
        Assert.False(string.IsNullOrEmpty(ev.GetProperty("calculationPolicyVersion").GetString()));
    }

    [Fact]
    public async Task AiQuery_ExplainableAnswer_HasSuggestedFollowUpQuestions()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "P/E below 6" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        var questions = document.RootElement
            .GetProperty("explainableAnswer")
            .GetProperty("suggestedFollowUpQuestions")
            .EnumerateArray()
            .ToList();

        Assert.NotEmpty(questions);
        Assert.All(questions, q => Assert.False(string.IsNullOrWhiteSpace(q.GetString())));
    }

    [Fact]
    public async Task AiQuery_ExplainableAnswer_ExplanationTextIsPresent()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "P/E below 6" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        var text = document.RootElement
            .GetProperty("explainableAnswer")
            .GetProperty("explanationText")
            .GetString();

        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
    }
}
