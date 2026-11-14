using System.Text.Encodings.Web;
using System.Text.Json;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

/// <summary>Feature 161 T-R00: frozen pre-feature rule and query snapshots.
/// Includes isolated follow-ups with an existing Feature 083 cash-flow task; sequential conversation and full endpoint routing remain integration coverage.</summary>
public sealed class CashFlowReport161RoutingBaselineTests
{
    private static readonly string[] Phrases =
    [
        "صورت جریان وجوه نقد فلوله", "صورت جریان‌های نقدی فولاد", "گزارش جریان نقدی وبملت", "گردش وجوه نقد شگل",
        "صورت جریان نقد فملی در شش ماهه ۱۴۰۵", "Cash Flow Statement فولاد", "صورت جریان وجوه نقد فولاد را بررسی کن",
        "جدول کامل جریان وجوه نقد فولاد", "صورت جریان وجوه نقد تلفیقی فولاد", "صورت جریان وجوه نقد فولاد در ۱۴۰۵/۰۶/۳۱ تلفیقی",
        "جریان نقد عملیاتی فلوله چقدر است؟", "مانده وجه نقد وبملت چقدر است؟", "فلوله چقدر تسهیلات دریافت کرده؟",
        "خالص جریان نقد تامین مالی فولاد چقدر بوده؟", "جریان نقد آزاد کگل چقدر است؟", "وبملت چقدر سود سهام نقدی پرداخت کرده؟",
        "مخارج سرمایه‌ای فلوله", "اثر نرخ ارز بر وجه نقد وبملت", "آیا سود فلوله پشتوانه نقدی دارد؟",
        "آیا فولاد از عملیات خود نقد تولید می‌کند؟", "علت افزایش موجودی نقد وبملت چیست؟", "آیا کگل برای سرمایه‌گذاری‌هایش به تسهیلات وابسته است؟",
        "کیفیت سود شگل از نظر جریان نقدی چگونه است؟", "تحلیل جریان نقد فولاد", "تحلیل ترازنامه و جریان وجوه نقد فولاد",
        "روند جریان نقد عملیاتی فولاد در دو سال اخیر", "جریان نقد فلوله را با سال قبل مقایسه کن", "جریان نقد عملیاتی کگل و کچاد را مقایسه کن",
        "صورت جریان نقدی وبملت را فصل به فصل نشان بده", "صورت جریان نقدی فلوله در چهار فصل اخیر", "جریان وجوه نقد وبملت در شش‌ماهه ۱۴۰۵",
        "جریان نقد عملیاتی کگل نسبت به سال قبل", "جریان نقد سالانه فولاد در سه سال اخیر", "گزارش جریان نقدی فصلی فولاد",
        "جریان نقد فولاد", "جریان نقد", "موجودی نقد وبملت", "وضعیت نقدینگی وبملت", "نسبت نقدینگی وبملت", "جریان نقدینگی حقیقی فولاد",
        "سود نقدی هر سهم فولاد", "DPS فولاد", "سود خالص فلوله چقدر است؟", "نسبت جاری فولاد", "جریان نقد عملیاتی بانک‌ها",
        "آخرین ترازنامه کگل", "ترازنامه وبملت را با پایان سال مالی قبل مقایسه کن", "ترازنامه‌اش",
        "گزارش فصلی فولاد", "صورت سود و زیان فصلی فولاد", "عملکرد ۵ فصل اخیر فولاد", "عملکرد فصلی فملی را بررسی کن",
        "جدول صورت سود و زیان فولاد", "تحلیل ترازنامه شگل", "تحلیل صورت مالی فولاد", "روند فروش ماهانه فولاد",
        "تحلیل جامع فولاد", "آخرین اطلاعیه جریان وجه نقد فولاد", "اطلاعیه‌های کدال فولاد", "ترازنامه و جریان وجوه نقد فولاد",
        "صورت جریان نقدی فلوله را نشان بده", "همین گزارش را برای فولاد بده", "دوره قبلش چطور بود؟", "با سود خالصش مقایسه کن",
        "وضعیت تامین مالی آن چطور است؟", "ترازنامه‌اش را هم نشان بده", "سود و زیانش چطور؟"
    ];
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static string PathToBaseline()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "specs"))) root = root.Parent;
        Assert.NotNull(root);
        return Path.Combine(root!.FullName, "tests", "FinancialCopilot.UnitTests", "TestData", "Feature161", "routing-baseline.json");
    }
    private static string Capture() => JsonSerializer.Serialize(Phrases.Select(phrase => new
    {
        Phrase = phrase,
        V2 = Feature154RoutingBaselineTests.Capture(phrase, true),
        V1 = Feature154RoutingBaselineTests.Capture(phrase, true, v2: false),
        V2Query = FinancialStatementTableIntentRules.BuildV2Query(phrase),
        V2QuarterlyQuery = FinancialStatementTableIntentRules.BuildV2QuarterlyQuery(phrase),
        V1Query = FinancialStatementTableIntentRules.BuildQuery(phrase),
        V1QuarterlyQuery = FinancialStatementTableIntentRules.BuildQuarterlyQuery(phrase)
    }).ToArray(), Options);

    [Fact]
    public void UnchangedRoutingRulesAndRequestObjects_MatchBaseline()
    {
        var path = PathToBaseline();
        var actual = Capture();
        if (Environment.GetEnvironmentVariable("FEATURE161_WRITE_ROUTING_BASELINE") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
        }
        Assert.True(File.Exists(path), "Capture the baseline on unchanged routing code first.");
        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n"), actual.Replace("\r\n", "\n"));
    }

    private static readonly string[] FollowUps = Phrases[^6..];
    private sealed class StateRepository : IConversationTaskStateRepository
    {
        private readonly Dictionary<ConversationTaskStateScope, ConversationTaskState> _states = [];
        public Task<ConversationTaskState?> FindAsync(ConversationTaskStateScope scope, CancellationToken ct) => Task.FromResult(_states.GetValueOrDefault(scope));
        public Task<ConversationTaskStateWriteResult> TryWriteAsync(ConversationTaskState state, long? expectedVersion, CancellationToken ct)
        {
            _states[new(state.ConversationId, state.TenantId, state.ActorId)] = state;
            return Task.FromResult(new ConversationTaskStateWriteResult(true, state));
        }
        public Task DeleteAsync(ConversationTaskStateScope scope, CancellationToken ct) { _states.Remove(scope); return Task.CompletedTask; }
    }
    private sealed class EmptyMessages : IMessageRepository
    {
        public Task<Guid> AppendAsync(Guid conversationId, MessageRole role, string content, string? scannerQueryPlanJson, DateTimeOffset createdAt, CancellationToken ct) => Task.FromResult(Guid.Empty);
        public Task<IReadOnlyCollection<MessageRecord>> ListByConversationAsync(Guid conversationId, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<MessageRecord>>([]);
    }
    private sealed class EmptyMetrics : IDirectMetricRoutingRegistry
    {
        public DirectMetricRoutingMatch? TryResolve(string message, DateOnly asOf) => null;
        public IReadOnlyList<DirectMetricRoutingMatch> ResolveAll(string message, DateOnly asOf) => [];
        public bool ContainsDirectMetricTerm(string message, DateOnly asOf) => false;
        public SymbolLookupPeriodSelector? ResolvePeriodSelector(string message, MetricCode code) => null;
        public string ResolveDisplayLabel(MetricCode code, SymbolLookupPeriodSelector? selector) => code.Value;
        public string StripResolvedPhrase(string message, DirectMetricRoutingMatch match) => message;
    }

    [Fact]
    public async Task FollowUps_InAnExistingCashFlowTableTask_MatchDialogueBaseline()
    {
        var results = new List<object>();
        var companyId = Guid.Parse("66000000-0000-0000-0000-000000000007");
        await using var db = new FinancialIngestionDbContext(new DbContextOptionsBuilder<FinancialIngestionDbContext>().UseInMemoryDatabase("feature161-baseline").Options);
        db.Companies.Add(new NormalizedCompanyRow { Id = companyId, ProviderName = "NoavaranCurrentApi", Ticker = "فولاد", CompanySymbol = "فولاد", Name = "فولاد مبارکه اصفهان", GroupId = Guid.Parse("66000000-0000-0000-0000-000000000008"), IndustryId = Guid.Parse("66000000-0000-0000-0000-000000000009") });
        var originalCompanyId = Guid.Parse("66000000-0000-0000-0000-000000000010");
        db.Companies.Add(new NormalizedCompanyRow { Id = originalCompanyId, ProviderName = "NoavaranCurrentApi", Ticker = "فلوله", CompanySymbol = "فلوله", Name = "فلوله", GroupId = Guid.Parse("66000000-0000-0000-0000-000000000008"), IndustryId = Guid.Parse("66000000-0000-0000-0000-000000000009") });
        await db.SaveChangesAsync();
        foreach (var phrase in FollowUps.Append(Phrases[47]))
        {
            var previousSymbol = phrase == FollowUps[0] ? "فلوله" : "فولاد";
            var previousCompanyId = phrase == FollowUps[0] ? originalCompanyId : companyId;
            var scope = new ConversationTaskStateScope(Guid.Parse("61000000-0000-0000-0000-000000000001"), Guid.Parse("61000000-0000-0000-0000-000000000002"), Guid.Parse("61000000-0000-0000-0000-000000000003"));
            var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
            var states = new ConversationTaskStateService(new StateRepository(), TimeProvider.System, new ConversationTaskStateOptions());
            await states.RecordAnsweredAsync(scope, "financial_statement_table", [new ConversationTaskSlot(QuerySlotType.CompanyOrSymbol, previousSymbol, previousCompanyId, QueryValueProvenance.UserExplicit, 1m, null, 1), new ConversationTaskSlot(QuerySlotType.StatementType, phrase == Phrases[47] ? "BalanceSheet;report=feature154_balance_sheet_report" : "CashFlow", null, QueryValueProvenance.UserExplicit, 1m, null, 1)], Guid.Empty, "baseline", default);
            var gate = new ConversationDialogueGate(states, new DeterministicCapabilityInterpreter(registry), new CanonicalQueryEntityResolver(db, Microsoft.Extensions.Options.Options.Create(new CanonicalEntityResolutionOptions())), new CapabilitySlotValidator(registry), new EmptyMetrics(), new SemanticRoutingRolloutCoordinator(new SemanticRoutingOptions(DefaultMode: SemanticRoutingMode.SemanticPrimary), new NullSemanticRoutingTelemetrySink()), new EmptyMessages(), TimeProvider.System);
            var request = new AiQueryRequest(phrase, scope.TenantId, scope.ActorId, "baseline-followup", ConversationId: scope.ConversationId);
            var result = await gate.PrepareAsync(request, scope.ConversationId, default);
            var frame = result.Request.SemanticFrame;
            results.Add(new { Phrase = phrase, result.Request.Message, result.Request.OriginalUserMessage, result.Request.SemanticRoutingDisposition, Capability = frame?.CapabilityCode, Slots = frame?.Slots.Select(slot => new { slot.Type, slot.Value, slot.ValidationState }).ToArray() });
        }
        var path = Path.Combine(Path.GetDirectoryName(PathToBaseline())!, "followup-baseline.json");
        var actual = JsonSerializer.Serialize(results, Options);
        if (Environment.GetEnvironmentVariable("FEATURE161_WRITE_ROUTING_BASELINE") == "1") File.WriteAllText(path, actual);
        Assert.True(File.Exists(path));
        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n"), actual.Replace("\r\n", "\n"));
    }
}
