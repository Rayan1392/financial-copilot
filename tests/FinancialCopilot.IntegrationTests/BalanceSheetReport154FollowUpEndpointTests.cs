using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Feature 154 review regressions (F154-R-01..R-04, R-08, R-13) through the real V2 endpoint with Feature 120
/// conversation state. Reproductions are taken from the independent review probes.
/// </summary>
public sealed class BalanceSheetReport154FollowUpEndpointTests : IClassFixture<BalanceSheetReport154ApiFactory>
{
    private readonly BalanceSheetReport154ApiFactory _factory;

    public BalanceSheetReport154FollowUpEndpointTests(BalanceSheetReport154ApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
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

    private static JsonElement? BalanceSheet(JsonElement root) =>
        root.TryGetProperty("financialStatementTableResult", out var table) && table.ValueKind == JsonValueKind.Object &&
        table.GetProperty("balanceSheetReport").ValueKind == JsonValueKind.Object
            ? table.GetProperty("balanceSheetReport")
            : null;

    private static JsonElement? Quarterly(JsonElement root) =>
        root.TryGetProperty("financialStatementTableResult", out var table) && table.ValueKind == JsonValueKind.Object &&
        table.TryGetProperty("quarterlyResult", out var quarterly) && quarterly.ValueKind == JsonValueKind.Object
            ? quarterly
            : null;

    private async Task<(HttpClient Client, Guid ConversationId)> Start(string first)
    {
        var client = Client();
        var root = await Post(client, first);
        return (client, root.GetProperty("conversationId").GetGuid());
    }

    [Fact] // F154-R-01 (AC-R04): an explicit income alias overrides the carried balance-sheet type.
    public async Task FinancialPerformancePossessive_AfterBalanceSheet_OpensIncomeStatementForSameCompany()
    {
        var (client, conversation) = await Start("ترازنامه وبملت");
        var second = await Post(client, "عملکرد مالی‌اش", conversation);
        Assert.Null(BalanceSheet(second));
        var quarterly = Quarterly(second);
        Assert.NotNull(quarterly);
        Assert.Equal("وبملت", quarterly!.Value.GetProperty("companySymbol").GetString());
        Assert.Equal("IncomeStatement", quarterly.Value.GetProperty("statementType").GetString());
    }

    [Theory] // F154-R-02 (AC-R04)
    [InlineData("وضعیت مالی‌اش را نشان بده")]
    [InlineData("ترازنامه‌اش")]
    public async Task BalanceSheetPossessive_AfterIncomeStatement_OpensFeature154ForSameCompany(string followUp)
    {
        var (client, conversation) = await Start("صورت سود و زیان وبملت");
        var report = BalanceSheet(await Post(client, followUp, conversation));
        Assert.NotNull(report);
        Assert.Equal("available", report!.Value.GetProperty("outcome").GetString());
        Assert.Equal("وبملت", report.Value.GetProperty("company").GetProperty("symbol").GetString());
    }

    [Fact] // F154-R-03 (AC-R04)
    public async Task RepeatForAnotherCompany_RepeatsFeature154()
    {
        var (client, conversation) = await Start("ترازنامه وبملت");
        var report = BalanceSheet(await Post(client, "همین را برای شگل", conversation));
        Assert.NotNull(report);
        Assert.Equal("available", report!.Value.GetProperty("outcome").GetString());
        Assert.Equal("شگل", report.Value.GetProperty("company").GetProperty("symbol").GetString());
        Assert.Equal("GeneralBalanceSheet", report.Value.GetProperty("templateId").GetString());
    }

    [Theory] // F154-R-04 (spec §4.4): baseline cues continue the active Feature 154 task.
    [InlineData("با گزارش قبلی مقایسه کن", "previous_report")]
    [InlineData("با پایان سال مالی قبل مقایسه کن", "fiscal_year_end")]
    [InlineData("با فصل قبل مقایسه کن", "previous_fiscal_quarter_end")]
    [InlineData("با مدت مشابه سال قبل مقایسه کن", "same_stage_previous_year")]
    [InlineData("با ۱۴۰۵/۰۳/۳۱ مقایسه کن", "explicit")]
    [InlineData("با ۱۴۰۴/۱۲/۲۹ مقایسه کن", "explicit")]
    public async Task ComparisonFollowUp_OpensComparisonForSameCompany(string followUp, string baseline)
    {
        var (client, conversation) = await Start("ترازنامه وبملت");
        var report = BalanceSheet(await Post(client, followUp, conversation));
        Assert.NotNull(report);
        Assert.Equal("comparison", report!.Value.GetProperty("requestedView").GetString());
        Assert.Equal("وبملت", report.Value.GetProperty("company").GetProperty("symbol").GetString());
        Assert.Equal(baseline, report.Value.GetProperty("comparison").GetProperty("selectedBaselineKind").GetString());
    }

    private static string Intent(JsonElement root) => root.GetProperty("intent").GetString() ?? string.Empty;

    [Theory] // F154-R-14: a message with its own subject keeps its fresh-conversation route inside an active Feature 154 task.
    [InlineData("فروش ماهانه وبملت را با سال قبل مقایسه کن")]
    [InlineData("روند فروش وبملت در مقایسه با دوره قبل")]
    [InlineData("فروش وبملت در ۱۴۰۴/۱۲/۲۹ چقدر بود")]
    [InlineData("قیمت وبملت نسبت به هفته قبل")]
    public async Task MessageWithOwnSubject_IsNotForcedIntoTheActiveBalanceSheetTask(string message)
    {
        using var fresh = Client();
        var freshIntent = Intent(await Post(fresh, message));
        var (client, conversation) = await Start("ترازنامه وبملت");
        var followUp = await Post(client, message, conversation);
        Assert.Equal(freshIntent, Intent(followUp));
        Assert.Null(BalanceSheet(followUp));
    }

    public static IEnumerable<object[]> PreservedT02Phrases() =>
        Feature154T02Phrases.Preserved.Select(phrase => new object[] { phrase });

    /// <summary>
    /// F154-R-14: with an active Feature 154 task, every preserved T02 phrase routes exactly as with an active
    /// Feature 083 statement-table task (same capability, so Feature 120 carry-over is identical to HEAD), and never
    /// yields a Feature 154 payload it would not yield there.
    /// </summary>
    [Theory]
    [MemberData(nameof(PreservedT02Phrases))]
    public async Task PreservedT02Phrases_RouteUnchangedInsideActiveBalanceSheetTask(string message)
    {
        var (reference, referenceConversation) = await Start("همه اقلام ترازنامه وبملت");
        var referenceRoot = await Post(reference, message, referenceConversation);
        var (client, conversation) = await Start("ترازنامه وبملت");
        var followUp = await Post(client, message, conversation);
        Assert.Equal(Intent(referenceRoot), Intent(followUp));
        Assert.Equal(BalanceSheet(referenceRoot) is null, BalanceSheet(followUp) is null);
    }

    private static string TableSignature(JsonElement root)
    {
        if (!root.TryGetProperty("financialStatementTableResult", out var table) || table.ValueKind != JsonValueKind.Object) return "none";
        if (Quarterly(root) is not null) return "feature144";
        if (BalanceSheet(root) is not null) return "feature154";
        return table.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.Object
            ? $"feature083:{source.GetProperty("statementType").GetString()}"
            : "table-without-source";
    }

    /// <summary>
    /// Deliberate change (spec §4.1, ImplementationReport §6b): inside an active Feature 083 balance-sheet task, an
    /// explicit statement alias now overrides the carried statement type. At HEAD these two messages returned a
    /// Feature 083 balance-sheet table; now they route exactly as in a fresh conversation.
    /// </summary>
    [Theory]
    [InlineData("گزارش فصلی وبملت", "feature144")]
    [InlineData("صورت جریان وجوه نقد وبملت", null)]
    public async Task ExplicitAlias_InsideActiveFeature083BalanceSheetTask_FollowsItsOwnRoute(string message, string? expected)
    {
        using var fresh = Client();
        var freshRoot = await Post(fresh, message);
        var (client, conversation) = await Start("همه اقلام ترازنامه وبملت");
        var followUp = await Post(client, message, conversation);
        Assert.NotEqual("feature083:BalanceSheet", TableSignature(followUp));
        Assert.Equal(TableSignature(freshRoot), TableSignature(followUp));
        Assert.Equal(Intent(freshRoot), Intent(followUp));
        if (expected is not null) Assert.Equal(expected, TableSignature(followUp));
    }

    [Fact] // F154-R-08 (PD-20): one date plus a baseline cue anchors on the requested date.
    public async Task OneDatePlusBaselineCue_AnchorsOnRequestedDate()
    {
        using var client = Client();
        var report = BalanceSheet(await Post(client, "ترازنامه وبملت در ۱۴۰۵/۰۳/۳۱ را با پایان سال مالی قبل مقایسه کن"))!.Value;
        var anchorId = report.GetProperty("anchorStatementId").GetString()!;
        Assert.Equal("403733", report.GetProperty("snapshots").GetProperty(anchorId).GetProperty("statement").GetProperty("externalStatementId").GetString());
        Assert.Equal("fiscal_year_end", report.GetProperty("comparison").GetProperty("selectedBaselineKind").GetString());
        Assert.DoesNotContain(report.GetProperty("comparison").GetProperty("candidates").EnumerateArray(),
            candidate => candidate.GetProperty("kind").GetString() == "explicit");
    }

    [Fact] // F154-R-13: «آخرین ترازنامه وبملت» is Feature 154; a variant request stays Feature 083.
    public async Task LatestRequest_IsFeature154_VariantRequest_IsFeature083()
    {
        using var client = Client();
        Assert.NotNull(BalanceSheet(await Post(client, "آخرین ترازنامه وبملت")));
        var variant = await Post(client, "ترازنامه تلفیقی وبملت");
        Assert.Null(BalanceSheet(variant));
        var table = variant.GetProperty("financialStatementTableResult");
        Assert.Equal("BalanceSheet", table.GetProperty("source").GetProperty("statementType").GetString());
    }

    [Fact] // F154-R-13 / AC-X02: existing authorization applies to Feature 154.
    public async Task Unauthenticated_IsRejected()
    {
        using var anonymous = _factory.CreateClient();
        using var response = await anonymous.PostAsJsonAsync("/api/ai/v1/query", new { message = "ترازنامه وبملت" }, CancellationToken.None);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

/// <summary>The preserved T02 phrases (tests/FinancialCopilot.UnitTests/Feature154RoutingBaselineTests.cs).</summary>
internal static class Feature154T02Phrases
{
    public static readonly string[] Preserved =
    [
        "عملکرد وبملت", "وضعیت وبملت", "صورت سود و زیان وبملت", "تحلیل صورت سود و زیان وبملت", "صورت عملکرد وبملت",
        "صورت درآمد وبملت", "همه اقلام ترازنامه وبملت", "جدول کامل ترازنامه شگل", "ترازنامه تلفیقی وبملت",
        "ترازنامه حسابرسی‌شده فاذر", "ترازنامه تجدید ارائه‌شده شگل", "ترازنامه ۱۲ ماهه شگل", "تحلیل ترازنامه وبملت",
        "تحلیل شغدیر", "سهم شغدیر را بررسی کن", "جمع دارایی‌های شگل چقدر است؟", "سرمایه فاذر چند است؟",
        "P/E فولاد چقدر است؟", "صورت جریان وجوه نقد فولاد", "صورت سود و زیان فولاد", "گزارش سود و زیان شگل",
        "صورت درآمد و هزینه فاذر", "گزارشات فصلی دتولید", "عملکرد ۵ فصل اخیر فولاد", "گزارش فصلی فولاد",
        "اطلاعیه‌های کدال فولاد", "شرکتی که سود خالص ۱۲۳۴۵۶ داشته کدام است؟", "فروش ماهانه شغدیر را نشان بده"
    ];
}
