using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.AI.Evaluation;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters;

namespace FinancialCopilot.UnitTests;

public sealed class Feature128SemanticRoutingTests
{
    [Fact]
    public void DefaultRoutingMode_IsSafeShadowAndRollbackIsLegacyOnly()
    {
        var shadow = new SemanticRoutingRolloutCoordinator(
            new SemanticRoutingOptions(),
            new NullSemanticRoutingTelemetrySink());
        var rollback = new SemanticRoutingRolloutCoordinator(
            new SemanticRoutingOptions(DefaultMode: SemanticRoutingMode.Rollback),
            new NullSemanticRoutingTelemetrySink());

        var shadowDecision = shadow.Decide("monthly_activity_trend");
        var rollbackDecision = rollback.Decide("monthly_activity_trend");

        Assert.Equal(SemanticRoutingMode.Shadow, shadowDecision.Mode);
        Assert.False(shadowDecision.ExecuteSemanticRoute);
        Assert.True(shadowDecision.RunShadowComparison);
        Assert.False(rollbackDecision.ExecuteSemanticRoute);
        Assert.False(rollbackDecision.RunShadowComparison);
    }

    [Fact]
    public void ShadowComparison_PreservesStructuredOperationalFieldsAndCategory()
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var events = new RecordingDialogueEvents();
        var sink = new SemanticRoutingEventTelemetrySink(events, registry, TimeProvider.System);
        var coordinator = new SemanticRoutingRolloutCoordinator(
            new SemanticRoutingOptions(DefaultMode: SemanticRoutingMode.Shadow), sink);
        var frame = new ValidatedQueryFrame(
            "monthly_activity_trend",
            registry.Version,
            [new ResolvedQuerySlot(QuerySlotType.CompanyOrSymbol, "FOLD", QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid)],
            new QueryInterpretation(
                "فروش فولاد",
                "فروش فولاد",
                "fa",
                [new CapabilityCandidate("monthly_activity_trend", registry.Version, 0.91m, [])],
                [], [], null, null, null, [], [], 0.91m,
                [new InterpretationEvidence("arbitration-reason", SemanticArbitrationReasonCodes.DeterministicCandidatePreferred, QueryValueProvenance.PolicyDefaulted)],
                registry.Version,
                InterpretationConfidenceBand.High));
        var request = new AiQueryRequest("فروش فولاد", Guid.NewGuid(), Guid.NewGuid(), "operational-correlation");
        var context = SemanticRoutingTelemetryFactory.FromFrame(
            request,
            frame,
            "legacy_monthly_sales",
            "legacy_monthly_sales",
            SemanticRoutingMode.Shadow,
            "test-provider/model-v1") with
        {
            Category = SemanticRoutingDisagreementCategory.LEGACY_CORRECTS_SEMANTIC
        };

        coordinator.RecordShadowComparison(
            frame.CapabilityCode,
            "legacy_monthly_sales",
            frame.CapabilityCode,
            request.CorrelationId,
            context);

        var comparison = Assert.Single(sink.Snapshot());
        Assert.Equal(SemanticRoutingDisagreementCategory.LEGACY_CORRECTS_SEMANTIC, comparison.Category);
        Assert.Equal("test-provider/model-v1", comparison.Context?.ProviderModelVersion);
        Assert.Equal(SemanticArbitrationPolicy.Version, comparison.Context?.ArbitrationPolicyVersion);
        Assert.Equal(SemanticRoutingTelemetryFactory.QueryHash(request.Message), comparison.Context?.QueryHash);
        Assert.Equal(SemanticRoutingDisagreementCategory.LEGACY_CORRECTS_SEMANTIC.ToString(), Assert.Single(events.Items).ReasonCode);
    }

    [Fact]
    public void LatencyStatistics_ReportsRequiredPercentilesAndEmptyState()
    {
        var empty = SemanticRoutingLatencyStatistics.Calculate([]);
        var statistics = SemanticRoutingLatencyStatistics.Calculate([100d, 200d, 300d, 400d]);

        Assert.Equal(0, empty.SampleCount);
        Assert.Null(empty.P50Ms);
        Assert.Equal(4, statistics.SampleCount);
        Assert.Equal(200d, statistics.P50Ms);
        Assert.Equal(400d, statistics.P95Ms);
        Assert.Equal(400d, statistics.P99Ms);
    }

    [Fact]
    public void RoutingDiagnostic_ContainsSemanticEntitiesArbitrationAndExecution()
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var request = new AiQueryRequest(
            "\u0641\u0648\u0644\u0627\u062f \u0645\u062d\u0635\u0648\u0644\u0627\u062a \u06af\u0631\u0645\u0634 \u0686\u0642\u062f\u0631 \u0641\u0631\u0648\u062e\u062a\u0647\u061f",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "diagnostic-correlation");
        var frame = new ValidatedQueryFrame(
            "product_sales_value",
            registry.Version,
            [
                new ResolvedQuerySlot(QuerySlotType.CompanyOrSymbol, "\u0641\u0648\u0644\u0627\u062f", QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid),
                new ResolvedQuerySlot(QuerySlotType.Product, "\u0645\u062d\u0635\u0648\u0644\u0627\u062a \u06af\u0631\u0645", QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid)
            ],
            new QueryInterpretation(
                request.Message,
                request.Message,
                "fa",
                [new CapabilityCandidate("product_sales_value", registry.Version, 0.94m, [])],
                [
                    new EntityMention("\u0641\u0648\u0644\u0627\u062f", 0, 5, EntityType: "company"),
                    new EntityMention("\u0645\u062d\u0635\u0648\u0644\u0627\u062a \u06af\u0631\u0645", 6, 14, EntityType: "product", Scope: "product")
                ],
                [new MetricSelection("MONTHLY_SALES", null, QueryValueProvenance.UserExplicit)],
                new PeriodSelection("latest", QueryValueProvenance.PolicyDefaulted),
                null,
                null,
                [],
                [],
                0.94m,
                [
                    new InterpretationEvidence("arbitration-reason", "ExplicitResolvedProductScope", QueryValueProvenance.PolicyDefaulted),
                    new InterpretationEvidence("arbitration-veto", "product_revenue_mix", QueryValueProvenance.PolicyDefaulted)
                ],
                registry.Version,
                InterpretationConfidenceBand.High),
            LegacyCapability: "product_revenue_mix",
            LegacyConfidence: 0.71m);

        var diagnostic = SemanticRoutingTelemetryFactory.CreateDiagnostic(
            request,
            frame,
            SemanticRoutingMode.Canary,
            "product_sales_value",
            "Semantic",
            "test-provider",
            "test-model",
            executorFrame: frame);

        Assert.Equal("product_sales_value", diagnostic.SemanticIntent);
        Assert.Equal("product_sales_value", diagnostic.SemanticCapabilityCandidate);
        Assert.Equal("Resolved", diagnostic.CompanyResolutionState);
        Assert.Equal("Resolved", diagnostic.ProductResolutionState);
        Assert.Equal("product_sales_value", diagnostic.ArbitrationWinner);
        Assert.Equal("product_sales_value", diagnostic.ActualExecutedCapability);
        Assert.Contains("product_revenue_mix", diagnostic.ArbitrationVetoes);
        Assert.Equal("product_sales_value", diagnostic.FinalFrameCapability);
        Assert.Equal("فولاد", diagnostic.FinalFrameCompany);
        Assert.Equal("Resolved", diagnostic.FinalFrameCompanyState);
        Assert.Equal("محصولات گرم", diagnostic.FinalFrameProduct);
        Assert.Equal("Resolved", diagnostic.FinalFrameProductState);
        Assert.Equal("product_sales_value", diagnostic.ExecutorCapability);
        Assert.Equal("فولاد", diagnostic.ExecutorCompany);
        Assert.Equal("محصولات گرم", diagnostic.ExecutorProduct);
    }

    [Fact]
    public void RoutingDiagnostic_PreservesSemanticFailureStatusAndLegacyExecution()
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var request = new AiQueryRequest("\u0641\u0648\u0644\u0627\u062f", Guid.NewGuid(), Guid.NewGuid(), "diagnostic-failure");
        var interpretation = new DeterministicCapabilityInterpreter(registry).Interpret(request.Message);
        var frame = new ValidatedQueryFrame(
            "product_sales_value",
            registry.Version,
            [],
            interpretation,
            LegacyCapability: "product_revenue_mix",
            LegacyConfidence: 0.62m,
            SemanticStatus: "InvalidStructuredOutput");

        var diagnostic = SemanticRoutingTelemetryFactory.CreateDiagnostic(
            request,
            frame,
            SemanticRoutingMode.Shadow,
            "product_revenue_mix",
            "Legacy");

        Assert.Equal("InvalidStructuredOutput", diagnostic.SemanticStatus);
        Assert.Equal("product_revenue_mix", diagnostic.LegacyCapability);
        Assert.Equal("product_revenue_mix", diagnostic.ActualExecutedCapability);
    }

    [Theory]
    [InlineData("فروش محصولات گرم فولاد")]
    [InlineData("روند فروش محصولات گرم فولاد")]
    [InlineData("کدام محصول بیشترین فروش فولاد را دارد؟")]
    [InlineData("مقایسه فروش محصولات گرم و سرد فولاد")]
    [InlineData("سهام با P/E زیر ۵")]
    [InlineData("P/E فولاد")]
    [InlineData("فروش ماهانه شپدیس")]
    [InlineData("سلام")]
    [InlineData("تحلیل شغدیر")]
    [InlineData("تحلیل بده")]
    [InlineData("قیمت تعادلی فملی")]
    [InlineData("روند فروش محصولات کنسانتره‌ش فولاد")]
    [InlineData("رصد معاملات عمده کرازی")]
    public void RepresentativeShadowCorpus_RecordsOneBoundedComparisonPerQuery(string query)
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var interpretation = new DeterministicCapabilityInterpreter(registry).Interpret(query);
        var capability = interpretation.CapabilityCandidates.FirstOrDefault()?.CapabilityCode ?? "unknown";
        var frame = new ValidatedQueryFrame(capability, registry.Version, [], interpretation);
        var sink = new SemanticRoutingEventTelemetrySink(new RecordingDialogueEvents(), registry, TimeProvider.System);
        var coordinator = new SemanticRoutingRolloutCoordinator(
            new SemanticRoutingOptions(DefaultMode: SemanticRoutingMode.Shadow), sink);

        coordinator.RecordShadowComparison(capability, capability, capability, Guid.NewGuid().ToString("N"),
            SemanticRoutingTelemetryFactory.FromFrame(
                new AiQueryRequest(query, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid().ToString("N")),
                frame,
                capability,
                capability,
                SemanticRoutingMode.Shadow));

        Assert.Single(sink.Snapshot());
    }

    [Fact]
    public void ProductMentionVetoesCompanyWideMixEvenWhenModelConfidenceIsHigher()
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var deterministic = new DeterministicCapabilityInterpreter(registry).Interpret("product sales for فولاد");
        var semantic = deterministic with
        {
            CapabilityCandidates =
            [new CapabilityCandidate("product_revenue_mix", registry.Version, 0.99m, [new InterpretationEvidence("model", "product", QueryValueProvenance.ModelProposed)])],
            EntityMentions = [new EntityMention("محصول گرم", 0, 9, QueryValueProvenance.ModelProposed, "product", "product")]
        };

        var result = SemanticArbitrator.Arbitrate(deterministic, semantic, registry);

        Assert.DoesNotContain(result.Candidates, candidate => candidate.CapabilityCode == "product_revenue_mix");
        Assert.Contains("resolved_product_scope_vetoed_company_wide_product_revenue_mix", result.Vetoes);
    }

    [Fact]
    public void DeterministicCandidateWinsTieOverModelConfidence()
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var deterministic = new DeterministicCapabilityInterpreter(registry).Interpret("chart monthly sales for فولاد");
        var semantic = deterministic with
        {
            CapabilityCandidates =
            [new CapabilityCandidate("symbol_metric_lookup", registry.Version, 0.99m, [new InterpretationEvidence("model", "metric", QueryValueProvenance.ModelProposed)])]
        };

        var result = SemanticArbitrator.Arbitrate(deterministic, semantic, registry);

        Assert.Equal("monthly_activity_trend", result.Candidates.First().CapabilityCode);
        Assert.False(result.ModelConfidenceUsed);
    }

    [Fact]
    public void SemanticMetricHintsDoNotBecomeCanonicalMetricSlots()
    {
        var proposal = new QueryInterpretationProposal(
            ["symbol_metric_lookup"], [], null, 0.9m, ["metric-hint"],
            Intent: "metric_lookup", MetricHints: ["unknown_metric_alias"]);

        Assert.Equal("unknown_metric_alias", Assert.Single(proposal.MetricHints!));
        Assert.Null(proposal.Entities);
    }

    [Theory]
    [InlineData("\u0641\u0648\u0644\u0627\u062f \u0645\u062d\u0635\u0648\u0644\u0627\u062a \u06af\u0631\u0645\u0634 \u0686\u0642\u062f\u0631 \u0641\u0631\u0648\u062e\u062a\u0647\u061f", "product_sales_value", "\u0645\u062d\u0635\u0648\u0644\u0627\u062a \u06af\u0631\u0645")]
    [InlineData("\u0641\u0631\u0648\u0634 \u0645\u062d\u0635\u0648\u0644\u0627\u062a \u06af\u0631\u0645 \u0641\u0648\u0644\u0627\u062f \u0686\u0637\u0648\u0631 \u0628\u0648\u062f\u0647\u061f", "product_sales_value", "\u0645\u062d\u0635\u0648\u0644\u0627\u062a \u06af\u0631\u0645")]
    [InlineData("\u0631\u0648\u0646\u062f \u0641\u0631\u0648\u0634 \u0645\u062d\u0635\u0648\u0644\u0627\u062a \u06af\u0631\u0645 \u0641\u0648\u0644\u0627\u062f\u061f", "product_sales_trend", "\u0645\u062d\u0635\u0648\u0644\u0627\u062a \u06af\u0631\u0645")]
    [InlineData("\u0631\u0648\u0646\u062f \u0641\u0631\u0648\u0634 \u0645\u062d\u0635\u0648\u0644\u0627\u062a \u06a9\u0633\u0627\u0646\u062a\u0631\u0647\u200c\u0634 \u0641\u0648\u0644\u0627\u062f", "product_sales_trend", "\u0645\u062d\u0635\u0648\u0644\u0627\u062a \u06a9\u0633\u0627\u0646\u062a\u0631\u0647")]
    public void MandatoryProductQueriesRouteToTheScopedProductCapabilities(string query, string expectedCapability, string expectedProduct)
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var interpretation = new DeterministicCapabilityInterpreter(registry).Interpret(query);

        Assert.Equal(expectedCapability, interpretation.CapabilityCandidates.First().CapabilityCode);
        var product = Assert.Single(interpretation.EntityMentions, entity => entity.EntityType == "product");
        Assert.Equal(expectedProduct, product.Text);
    }

    [Theory]
    [InlineData("\u0641\u0645\u0644\u06cc \u06a9\u0627\u062a\u062f\u0634 \u0686\u0642\u062f\u0631 \u0641\u0631\u0648\u062e\u062a\u0647\u061f", "\u06a9\u0627\u062a\u062f")]
    [InlineData("\u06a9\u06af\u0644 \u06a9\u0646\u0633\u0627\u0646\u062a\u0631\u0647\u200c\u0634 \u0686\u0642\u062f\u0631 \u0641\u0631\u0648\u0634 \u062f\u0627\u0634\u062a\u061f", "\u06a9\u0646\u0633\u0627\u0646\u062a\u0631\u0647")]
    public void PossessiveProductMentionsAreExtractedWithoutHardcodedProductNames(string query, string expectedProduct)
    {
        Assert.Equal(expectedProduct, ProductSemanticIntentRules.ExtractProductMention(query));
    }

    [Theory]
    [InlineData("\u0641\u0631\u0648\u0634 \u0645\u0627\u0647\u0627\u0646\u0647 \u0641\u0648\u0644\u0627\u062f")]
    public void GenericCompanyQueriesDoNotInventProductScope(string query)
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var interpretation = new DeterministicCapabilityInterpreter(registry).Interpret(query);

        Assert.DoesNotContain(interpretation.CapabilityCandidates,
            candidate => candidate.CapabilityCode is "product_sales_value" or "product_sales_trend");
        Assert.DoesNotContain(interpretation.EntityMentions, entity => entity.EntityType == "product");
    }

    [Fact]
    public void PossessiveProductQueryKeepsOnlyTheCompanyAsCompanyEntity()
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var interpretation = new DeterministicCapabilityInterpreter(registry)
            .Interpret("\u06a9\u06af\u0644 \u06a9\u0646\u0633\u0627\u0646\u062a\u0631\u0647\u200c\u0634 \u0686\u0642\u062f\u0631 \u0641\u0631\u0648\u0634 \u062f\u0627\u0634\u062a");

        var companyMentions = interpretation.EntityMentions
            .Where(entity => !string.Equals(entity.EntityType, "product", StringComparison.OrdinalIgnoreCase))
            .Select(entity => entity.Text)
            .ToArray();
        Assert.Equal(["\u06a9\u06af\u0644"], companyMentions);
    }

    [Fact]
    public void LatencySinkMergesComponentSamplesByCorrelationId()
    {
        var sink = new BoundedSemanticRoutingLatencySink();
        sink.Record(new SemanticRoutingLatencySample("corr", LegacyInterpretationMs: 2, SemanticModelMs: 8));
        sink.Record(new SemanticRoutingLatencySample("corr", EntityResolutionMs: 3, TotalRequestMs: 20));

        var sample = Assert.Single(sink.Snapshot());
        Assert.Equal(2, sample.LegacyInterpretationMs);
        Assert.Equal(8, sample.SemanticModelMs);
        Assert.Equal(3, sample.EntityResolutionMs);
        Assert.Equal(20, sample.TotalRequestMs);
    }

    [Fact]
    public void CompositionQueryKeepsProductSlotNullAndUsesCompanyWideMix()
    {
        var query = "\u06a9\u062f\u0627\u0645 \u0645\u062d\u0635\u0648\u0644 \u0628\u06cc\u0634\u062a\u0631\u06cc\u0646 \u0641\u0631\u0648\u0634 \u0641\u0648\u0644\u0627\u062f \u0631\u0627 \u062f\u0627\u0631\u062f\u061f";
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var interpretation = new DeterministicCapabilityInterpreter(registry).Interpret(query);

        Assert.Equal("product_revenue_mix", interpretation.CapabilityCandidates.First().CapabilityCode);
        Assert.DoesNotContain(interpretation.EntityMentions, entity => entity.EntityType == "product");
        Assert.True(ProductSemanticIntentRules.LooksLikeProductRevenueComposition(query));
    }

    [Fact]
    public async Task ProductResolutionIsCompanyScopedAndRejectsUnknownProduct()
    {
        var companyId = Guid.NewGuid();
        var period = new JalaliPeriod(1404, 6);
        var repository = new FakeProductRepository(period, [
            Observation(companyId, "HOT", "\u0645\u062d\u0635\u0648\u0644\u0627\u062a \u06af\u0631\u0645", "KEY-HOT", 1250m),
            Observation(companyId, "COLD", "\u0645\u062d\u0635\u0648\u0644\u0627\u062a \u0633\u0631\u062f", "KEY-COLD", 800m)]);
        var resolver = new CompanyScopedProductResolver(
            new FakeCompanyResolver(new ResolvedCompany(companyId, "external-fold", "\u0641\u0648\u0644\u0627\u062f", null, null, null, null)),
            repository);

        var resolved = await resolver.ResolveAsync("\u0641\u0648\u0644\u0627\u062f", "\u06af\u0631\u0645");
        var product = Assert.IsType<ProductResolutionResult.Resolved>(resolved);
        Assert.Equal("KEY-HOT", product.Product.ProductKey);
        Assert.Equal(companyId, product.Product.CompanyId);

        var unknown = await resolver.ResolveAsync("\u0641\u0648\u0644\u0627\u062f", "\u0646\u0627\u0634\u0646\u0627\u062e\u062a\u0647");
        Assert.IsType<ProductResolutionResult.NotFound>(unknown);

        var unknownCompany = await resolver.ResolveAsync("\u0634\u0631\u06a9\u062a-\u0646\u0627\u0634\u0646\u0627\u062e\u062a\u0647", "\u06af\u0631\u0645");
        Assert.IsType<ProductResolutionResult.NotFound>(unknownCompany);
        Assert.Equal(2, repository.PeriodReadCount);
    }

    [Fact]
    public async Task ProductResolutionPreservesAmbiguityWithinOneCompany()
    {
        var companyId = Guid.NewGuid();
        var period = new JalaliPeriod(1404, 6);
        var resolver = new CompanyScopedProductResolver(
            new FakeCompanyResolver(new ResolvedCompany(companyId, "external-fold", "\u0641\u0648\u0644\u0627\u062f", null, null, null, null)),
            new FakeProductRepository(period, [
                Observation(companyId, "HOT-A", "\u0645\u062d\u0635\u0648\u0644 \u06af\u0631\u0645", "KEY-A", 100m),
                Observation(companyId, "HOT-B", "\u0645\u062d\u0635\u0648\u0644 \u06af\u0631\u0645", "KEY-B", 110m)]));

        var result = await resolver.ResolveAsync("\u0641\u0648\u0644\u0627\u062f", "\u0645\u062d\u0635\u0648\u0644 \u06af\u0631\u0645");

        var ambiguous = Assert.IsType<ProductResolutionResult.Ambiguous>(result);
        Assert.Equal(2, ambiguous.Candidates.Count);
    }

    private static ProductSalesObservation Observation(Guid companyId, string code, string title, string key, decimal sales) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "external-fold", new JalaliPeriod(1404, 6), "test", "report", new DateOnly(2025, 9, 1), new DateOnly(2025, 9, 30), null, title, "ton", null, 10m, 100m, sales, ProductKey: key, ProviderProductCode: code);

    private sealed class RecordingDialogueEvents : ISemanticDialogueEventSink
    {
        public List<SemanticDialogueEvent> Items { get; } = [];
        public void Record(SemanticDialogueEvent semanticEvent) => Items.Add(semanticEvent);
        public IReadOnlyCollection<SemanticDialogueEvent> Snapshot() => Items;
    }

    private sealed class FakeCompanyResolver(ResolvedCompany? company) : ICompanyResolverService
    {
        public Task<ResolvedCompany?> ResolveBySymbolAsync(string symbol, CancellationToken ct = default) =>
            Task.FromResult(symbol == "\u0641\u0648\u0644\u0627\u062f" ? company : null);
    }

    private sealed class FakeProductRepository(JalaliPeriod period, IReadOnlyList<ProductSalesObservation> observations) : IMonthlyProductComparisonReadRepository
    {
        public int PeriodReadCount { get; private set; }

        public Task<IReadOnlyList<JalaliPeriod>> GetAvailablePeriodsAsync(string externalCompanyId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<JalaliPeriod>>([period]);

        public Task<MonthlyProductComparisonPeriod?> GetPeriodAsync(string externalCompanyId, JalaliPeriod requestedPeriod, CancellationToken ct = default)
        {
            PeriodReadCount++;
            return Task.FromResult<MonthlyProductComparisonPeriod?>(new MonthlyProductComparisonPeriod(period, observations, []));
        }
    }
}
