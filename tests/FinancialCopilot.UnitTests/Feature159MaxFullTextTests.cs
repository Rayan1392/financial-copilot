using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Infrastructure.Billing;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.UnitTests;

/// <summary>Feature 159 change «max-plan-full-analysis-text»: only Max gets the whole analysis text; any doubt → preview.</summary>
public sealed class Feature159MaxFullTextTests
{
    private const string Capability = ComprehensiveAnalysisFullText.CapabilityCode;
    private static readonly string Url = ComprehensiveAnalysisPresentationOptions.DefaultTahlilAppAnalysesUrl;

    // --- T1: plan rule (fail closed) ---

    [Theory]
    [InlineData("Max", 10, true, true)]
    [InlineData("Max", -1, true, false)]   // expired window → Free
    [InlineData("Pro", 10, true, false)]   // Pro has no row
    [InlineData("Max", 10, false, false)]  // Max row disabled (kill switch)
    public async Task IsEnabled_OnlyForAnActiveWindowWithAnEnabledRow(string plan, int expiresInDays, bool rowEnabled, bool expected)
    {
        await using var db = BillingDb();
        var account = await SeedExternalAccountAsync(db, Guid.NewGuid(), Guid.NewGuid(), plan, expiresInDays, rowEnabled);

        Assert.Equal(expected, await new PlanCapabilityService(db).IsEnabledAsync(account, Capability, default));
    }

    [Fact]
    public async Task IsEnabled_AccountWithoutAPlan_IsFalse_ThoughValidateCanExecuteAllowsIt()
    {
        await using var db = BillingDb();
        var accountId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        db.CustomerAccounts.Add(new CustomerAccountRow { Id = accountId, TenantId = tenantId, AccountType = "Organization", BillingMode = "Prepaid" });
        await db.SaveChangesAsync();
        var account = new CustomerAccount(accountId, tenantId, CustomerAccountType.Organization, BillingMode.Prepaid);
        var service = new PlanCapabilityService(db);

        await service.ValidateCanExecuteAsync(account, Capability, default); // the old rule: no plan passes
        Assert.False(await service.IsEnabledAsync(account, Capability, default));
    }

    // --- T2: entitlement adapter ---

    [Fact]
    public async Task Entitlement_MaxUser_IsGranted_OthersAreNot()
    {
        await using var db = BillingDb();
        var tenantId = Guid.NewGuid();
        var maxUser = Guid.NewGuid();
        var proUser = Guid.NewGuid();
        await SeedExternalAccountAsync(db, tenantId, maxUser, "Max", 10, rowEnabled: true);
        await SeedExternalAccountAsync(db, tenantId, proUser, "Pro", 10, rowEnabled: true, seedPlanRows: false);
        var entitlement = Entitlement(db);

        Assert.True(await entitlement.IsGrantedAsync(new(tenantId, maxUser, maxUser, null), default));
        Assert.False(await entitlement.IsGrantedAsync(new(tenantId, proUser, proUser, null), default));
        Assert.False(await entitlement.IsGrantedAsync(new(tenantId, maxUser, maxUser, ApiClientId: Guid.NewGuid()), default));
        Assert.False(await entitlement.IsGrantedAsync(new(tenantId, maxUser, UserId: null, null), default));
        Assert.False(await entitlement.IsGrantedAsync(new(tenantId, Guid.NewGuid(), Guid.NewGuid(), null), default)); // no account
        Assert.False(await entitlement.IsGrantedAsync(new(Guid.NewGuid(), maxUser, maxUser, null), default));         // other tenant
    }

    [Fact]
    public async Task Entitlement_BillingError_IsNotGranted()
    {
        await using var db = BillingDb();
        var tenantId = Guid.NewGuid();
        var user = Guid.NewGuid();
        await SeedExternalAccountAsync(db, tenantId, user, "Max", 10, rowEnabled: true);
        var entitlement = new ComprehensiveAnalysisFullTextEntitlement(
            new CustomerAccountRepository(db), new ThrowingPlanCapabilities(), NullLogger<ComprehensiveAnalysisFullTextEntitlement>.Instance);

        Assert.False(await entitlement.IsGrantedAsync(new(tenantId, user, user, null), default));
    }

    // --- T3: executor ---

    [Fact]
    public async Task Executor_Granted_AsksForTheWholeText_InBothQueries()
    {
        // A technical question with no technical analysis runs the second (latest-analysis) query too (BUG-020).
        var analysis = new RecordingAnalysis(firstHasRows: false);
        var executor = new ComprehensiveAnalysisCapabilityExecutor(analysis, new EmptyLookup(), TimeProvider.System, new FixedEntitlement(true));

        await executor.ExecuteAsync(Frame(ComprehensiveAnalysisTopics.TechnicalAnalysis), UserContext(), default);

        Assert.Equal(2, analysis.Requests.Count);
        Assert.All(analysis.Requests, request => Assert.True(request.IncludeFullText));
    }

    [Fact]
    public async Task Executor_NoUserOrAnApiClient_NeverAsksThePort()
    {
        var port = new FixedEntitlement(true);
        var analysis = new RecordingAnalysis(firstHasRows: true);
        var executor = new ComprehensiveAnalysisCapabilityExecutor(analysis, new EmptyLookup(), TimeProvider.System, port);

        await executor.ExecuteAsync(Frame(), new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "t", "fa"), default);
        await executor.ExecuteAsync(Frame(), UserContext() with { ApiClientId = Guid.NewGuid() }, default);

        Assert.Equal(0, port.Calls);
        Assert.All(analysis.Requests, request => Assert.False(request.IncludeFullText));
    }

    [Fact]
    public async Task Executor_PortThrows_IsThePreview_AndTheAnswerSucceeds()
    {
        var analysis = new RecordingAnalysis(firstHasRows: true);
        var executor = new ComprehensiveAnalysisCapabilityExecutor(analysis, new EmptyLookup(), TimeProvider.System, new ThrowingEntitlement());

        var result = await executor.ExecuteAsync(Frame(), UserContext(), default);

        Assert.False(Assert.Single(analysis.Requests).IncludeFullText);
        Assert.IsType<SemanticComprehensiveAnalysisPayload>(result.Payload);
    }

    [Fact]
    public async Task Executor_PortSlowerThanTheBudget_IsThePreview()
    {
        var analysis = new RecordingAnalysis(firstHasRows: true);
        var executor = new ComprehensiveAnalysisCapabilityExecutor(analysis, new EmptyLookup(), TimeProvider.System, new HangingEntitlement());
        var started = DateTimeOffset.UtcNow;

        await executor.ExecuteAsync(Frame(), UserContext(), default);

        Assert.False(Assert.Single(analysis.Requests).IncludeFullText);
        Assert.InRange(DateTimeOffset.UtcNow - started, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(10));
    }

    // --- T4: card ---

    private const string WholeText = "بند اول تحلیل با عدد 0.88 واحد.\nبند دوم تحلیل.\nبند سوم تحلیل.";

    [Fact]
    public void FullTextCard_Web_ShowsEveryParagraph_WithoutTheReadMoreLine()
    {
        var card = ComprehensiveAnalysisCardComposer.ComposeMarkdown([Item(WholeText)], ["فولاد"], Url, fullText: true);

        Assert.Contains("بند اول تحلیل با عدد 0.88 واحد.\n\nبند دوم تحلیل.\n\nبند سوم تحلیل.", card);
        Assert.DoesNotContain(ComprehensiveAnalysisCardComposer.ReadMoreFa, card);
        Assert.EndsWith($"[{ComprehensiveAnalysisCardComposer.AllAnalysesLinkTextFa}]({Url})", card);
    }

    [Fact]
    public void PreviewCard_IsUnchanged()
    {
        var card = ComprehensiveAnalysisCardComposer.ComposeMarkdown([Item("پیش‌نمایش …")], ["فولاد"], Url);

        Assert.Contains(ComprehensiveAnalysisCardComposer.ReadMoreFa, card);
        Assert.EndsWith($"[{ComprehensiveAnalysisCardComposer.LinkTextFa}]({Url})", card);
    }

    [Fact]
    public void FullTextCard_Telegram_IsCutToTheLimit_AndKeepsTheLink()
    {
        var longText = string.Concat(Enumerable.Repeat("این یک جملهٔ نمونه از تحلیل است. ", 300));

        var card = ComprehensiveAnalysisCardComposer.ComposePlainText(
            [Item(longText)], ["فولاد"], Url, ["فولاد"], fullText: true, maxLength: 3900);

        Assert.InRange(card.Length, 3000, 3900);
        Assert.Contains(ComprehensiveAnalysisTeaserPolicy.Ellipsis, card);
        Assert.StartsWith(ComprehensiveAnalysisCardComposer.TechnicalAnalysisNotFoundSentence(["فولاد"]), card);
        Assert.EndsWith($"{ComprehensiveAnalysisCardComposer.AllAnalysesLinkTextFa}: {Url}", card);
        Assert.DoesNotContain(ComprehensiveAnalysisCardComposer.ReadMoreFa, card);
    }

    [Fact]
    public void FullTextCard_Telegram_ShortText_IsNotCut()
    {
        var card = ComprehensiveAnalysisCardComposer.ComposePlainText([Item(WholeText)], ["فولاد"], Url, fullText: true, maxLength: 3900);

        Assert.Contains(WholeText, card);
        Assert.DoesNotContain(ComprehensiveAnalysisTeaserPolicy.Ellipsis, card);
    }

    [Fact]
    public void FullText_DropsARepeatedTitle_AndFitToCutsAtASentenceEnd()
    {
        Assert.Equal("متن تحلیل.", ComprehensiveAnalysisTeaserPolicy.FullText("فولاد- گزارش", "فولاد- گزارش - متن تحلیل."));
        Assert.Equal("جملهٔ اول. …", ComprehensiveAnalysisTeaserPolicy.FitTo("جملهٔ اول. جملهٔ دوم طولانی‌تر است.", 15));
        Assert.Equal("کوتاه", ComprehensiveAnalysisTeaserPolicy.FitTo("کوتاه", 15));
    }

    // --- helpers ---

    private static ComprehensiveAnalysisSummaryItem Item(string text) =>
        new(1, "فولاد- گزارش", "1405/08/18", "", text, [], DateTimeOffset.UtcNow);

    private static ValidatedQueryFrame Frame(string? topic = null) => new(
        "comprehensive_analysis", 1,
        topic is null
            ? [new(QuerySlotType.CompanyOrSymbol, "فولاد", QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid)]
            : [new(QuerySlotType.CompanyOrSymbol, "فولاد", QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid),
               new(QuerySlotType.AnalysisTopic, topic, QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid)],
        new("تحلیل جامع فولاد", "تحلیل جامع فولاد", "fa", [], [], [], null, null, null, [], [], 1m, [], 1));

    private static QueryExecutionContext UserContext()
    {
        var user = Guid.NewGuid();
        return new(Guid.NewGuid(), user, Guid.NewGuid(), "t", "fa", UserId: user);
    }

    private static BillingDbContext BillingDb() =>
        new(new DbContextOptionsBuilder<BillingDbContext>().UseInMemoryDatabase($"f162-fulltext-{Guid.NewGuid():N}").Options);

    private static ComprehensiveAnalysisFullTextEntitlement Entitlement(BillingDbContext db) =>
        new(new CustomerAccountRepository(db), new PlanCapabilityService(db), NullLogger<ComprehensiveAnalysisFullTextEntitlement>.Instance);

    private static async Task<CustomerAccount> SeedExternalAccountAsync(
        BillingDbContext db, Guid tenantId, Guid userId, string plan, int expiresInDays, bool rowEnabled, bool seedPlanRows = true)
    {
        var accountId = Guid.NewGuid();
        db.CustomerAccounts.Add(new CustomerAccountRow
        {
            Id = accountId, TenantId = tenantId, UserId = userId, AccountType = "Individual", BillingMode = "Prepaid",
            SubscriptionPlanCode = plan, ExternalSubscriptionManaged = true
        });
        if (seedPlanRows && !await db.SubscriptionPlans.AnyAsync(row => row.Code == "Max"))
        {
            db.SubscriptionPlans.Add(new SubscriptionPlanRow { Code = "Max", Name = "Max", IncludedCredits = 0, QuotaMode = "Unlimited", PricingPolicyVersion = "v1" });
            db.PlanCapabilities.Add(new PlanCapabilityRow { PlanCode = "Max", CapabilityCode = Capability, PolicyVersion = "v1", IsEnabled = rowEnabled });
        }

        var now = DateTimeOffset.UtcNow;
        db.ExternalPlanSubscriptions.Add(new ExternalPlanSubscriptionRow
        {
            Id = Guid.NewGuid(), CustomerAccountId = accountId, IdentityKey = $"{plan}:{accountId:N}", PlanCode = plan,
            StartsAtUtc = now.AddDays(-30), ExpiresAtUtc = now.AddDays(expiresInDays)
        });
        await db.SaveChangesAsync();
        return new CustomerAccount(accountId, tenantId, CustomerAccountType.Individual, BillingMode.Prepaid);
    }

    private sealed class RecordingAnalysis(bool firstHasRows) : IComprehensiveAnalysisQueryUseCase
    {
        public List<ComprehensiveAnalysisQueryRequest> Requests { get; } = [];

        public Task<ComprehensiveAnalysisQueryResponse> ExecuteAsync(ComprehensiveAnalysisQueryRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var hasRows = firstHasRows || Requests.Count > 1;
            IReadOnlyList<ComprehensiveAnalysisSummaryItem> items = hasRows ? [Item("متن")] : [];
            return Task.FromResult(new ComprehensiveAnalysisQueryResponse(items, [], FullTextIncluded: request.IncludeFullText && hasRows));
        }
    }

    private sealed class EmptyLookup : ISymbolMetricLookupService
    {
        public Task<SymbolLookupTableResult> LookupAsync(SymbolLookupRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new SymbolLookupTableResult(Guid.NewGuid(), [], [], new(DateTimeOffset.UtcNow, TimeSpan.Zero, 0, 0, false), [], []));
    }

    private sealed class FixedEntitlement(bool granted) : IComprehensiveAnalysisFullTextEntitlement
    {
        public int Calls { get; private set; }

        public Task<bool> IsGrantedAsync(ComprehensiveAnalysisReader reader, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(granted);
        }
    }

    private sealed class ThrowingEntitlement : IComprehensiveAnalysisFullTextEntitlement
    {
        public Task<bool> IsGrantedAsync(ComprehensiveAnalysisReader reader, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("billing down");
    }

    private sealed class HangingEntitlement : IComprehensiveAnalysisFullTextEntitlement
    {
        // Ignores the token on purpose: the executor's own bound must still end the wait.
        public Task<bool> IsGrantedAsync(ComprehensiveAnalysisReader reader, CancellationToken cancellationToken) =>
            new TaskCompletionSource<bool>().Task;
    }

    private sealed class ThrowingPlanCapabilities : IPlanCapabilityService
    {
        public Task ValidateCanExecuteAsync(CustomerAccount account, string operationCode, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("billing down");

        public Task<decimal?> GetLimitAsync(CustomerAccount account, string capabilityCode, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("billing down");

        public Task<bool> IsEnabledAsync(CustomerAccount account, string capabilityCode, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("billing down");
    }
}
