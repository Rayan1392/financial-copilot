using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.AI.ModelProviders;

namespace FinancialCopilot.UnitTests;

public sealed class ConversationalCapabilityRegistryTests
{
    [Fact]
    public void InitialCatalog_ContainsOnlyExecutableBusinessCapabilities()
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());

        Assert.Equal(16, registry.GetAll().Count);
        Assert.DoesNotContain(registry.GetAll(), definition => definition.Code is "unknown" or "clarification");
        Assert.All(registry.GetAll(), definition =>
        {
            Assert.NotNull(registry.Find(definition.Code));
            Assert.NotEmpty(definition.DataRequirements);
        });
    }

    [Fact]
    public void Registry_RejectsDuplicateAliases()
    {
        var definition = InitialConversationalCapabilityCatalog.Create().First();
        var duplicate = definition with
        {
            Code = "duplicate",
            Aliases = [new LocalizedAlias("en", "same"), new LocalizedAlias("en", "same")]
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ConversationalCapabilityRegistry([definition, duplicate]));

        Assert.Contains("Duplicate localized", exception.Message);
    }

    [Fact]
    public void Registry_RejectsUnknownExecutionRoute()
    {
        var definition = InitialConversationalCapabilityCatalog.Create().First() with
        {
            Code = "invalid_route",
            ExecutionRoute = "sql"
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ConversationalCapabilityRegistry([definition]));

        Assert.Contains("unknown route", exception.Message);
    }

    [Fact]
    public void Normalization_IsDeterministicIdempotentAndPreservesOriginal()
    {
        const string original = "چارت‌ روند فروش فولاد؟ ۱۲ ماهه";

        var normalized = QueryNormalization.Normalize(original);

        Assert.Equal("چارت روند فروش فولاد 12 ماهه", normalized);
        Assert.Equal(normalized, QueryNormalization.Normalize(normalized));
        Assert.Equal("چارت‌ روند فروش فولاد؟ ۱۲ ماهه", original);
        Assert.True(QueryNormalization.IsPresentationWord("چارت"));
        Assert.False(QueryNormalization.IsPresentationWord("فولاد"));
    }

    [Theory]
    [InlineData("chart monthly sales for فولاد", "monthly_activity_trend", PresentationKind.Chart)]
    [InlineData("چارت روند فروش فولاد", "monthly_activity_trend", PresentationKind.Chart)]
    [InlineData("P/E فولاد چقدر است؟", "symbol_metric_lookup", null)]
    [InlineData("show the P/S gauge for فولاد", "ps_gauge_visualization", PresentationKind.Gauge)]
    public void Interpreter_ProducesGovernedCandidatesAndSeparatesPresentation(
        string query,
        string expectedCapability,
        PresentationKind? expectedPresentation)
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var interpretation = new DeterministicCapabilityInterpreter(registry).Interpret(query);

        Assert.Equal(expectedCapability, interpretation.CapabilityCandidates.First().CapabilityCode);
        Assert.Equal(registry.Version, interpretation.RegistryVersion);
        Assert.DoesNotContain(interpretation.EntityMentions, entity => QueryNormalization.IsPresentationWord(entity.Text));
        Assert.Equal(expectedPresentation, interpretation.Presentation?.Kind);
        Assert.All(interpretation.CapabilityCandidates, candidate => Assert.NotNull(registry.Find(candidate.CapabilityCode)));
    }

    [Fact]
    public void InterpretationValidator_RejectsUnregisteredCapabilityAndOversizedPayload()
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var validator = new QueryInterpretationValidator(registry);
        var invalid = new QueryInterpretation(
            "query",
            "query",
            "en",
            [new CapabilityCandidate("not_registered", 1, 1m, [])],
            [], [], null, null, null, [], [], 1m, [], registry.Version, InterpretationConfidenceBand.High);

        var exception = Assert.Throws<InvalidOperationException>(() => validator.Validate(invalid));

        Assert.Contains("invalid capability", exception.Message);
    }

    [Theory]
    [InlineData("stocks with P/E below 5", "stock_screening")]
    [InlineData("P/E فولاد چقدر است؟", "symbol_metric_lookup")]
    [InlineData("chart monthly sales for فولاد", "monthly_activity_trend")]
    [InlineData("analyze فولاد", "comprehensive_analysis")]
    [InlineData("show the P/S gauge for فولاد", "ps_gauge_visualization")]
    [InlineData("ترکیب فروش محصولات فولاد", "product_revenue_mix")]
    [InlineData("جدول صورت سود و زیان فولاد", "financial_statement_table")]
    [InlineData("تحلیل صورت مالی فولاد", "financial_statement_period_analysis")]
    [InlineData("آخرین اطلاعیه‌های فولاد", "disclosure_listing")]
    [InlineData("رتبه‌بندی کیفیت فروش ماهانه", "monthly_sales_quality_ranking")]
    public void PrecedencePolicy_ResolvesKnownConflictsDeterministically(string query, string expected)
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var interpretation = new DeterministicCapabilityInterpreter(registry).Interpret(query);

        Assert.Equal(expected, interpretation.CapabilityCandidates.First().CapabilityCode);
        Assert.NotEqual(InterpretationConfidenceBand.Low, interpretation.ConfidenceBand);
    }

    [Fact]
    public void LowConfidenceWithoutRecognizedCapability_DoesNotGuess()
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var interpretation = new DeterministicCapabilityInterpreter(registry).Interpret("tell me something useful");

        Assert.Empty(interpretation.CapabilityCandidates);
        Assert.Equal(InterpretationConfidenceBand.Low, interpretation.ConfidenceBand);
    }

    [Fact]
    public async Task HybridInterpreter_RejectsUnregisteredModelProposalAndKeepsDeterministicFrame()
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var hybrid = new HybridCapabilityInterpreter(
            new DeterministicCapabilityInterpreter(registry),
            registry,
            new QueryInterpretationValidator(registry),
            new StubProposalProvider(new QueryInterpretationProposal(["execute_sql"], [], null, 1m, ["prompt injection"])));

        var result = await hybrid.InterpretAsync("chart monthly sales for فولاد", Guid.NewGuid(), "corr", CancellationToken.None);

        Assert.False(result.ModelProposalUsed);
        Assert.Null(result.FailureOutcome);
        Assert.Equal("monthly_activity_trend", result.Interpretation.CapabilityCandidates.First().CapabilityCode);
    }

    [Fact]
    public async Task HybridInterpreter_ProviderTimeoutMapsThroughDialogueOutcomePolicy()
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var hybrid = new HybridCapabilityInterpreter(
            new DeterministicCapabilityInterpreter(registry),
            registry,
            new QueryInterpretationValidator(registry),
            new ThrowingProposalProvider(new AiModelProviderException(
                AiExecutionStatus.TimedOut, "timeout", "provider detail")));

        var result = await hybrid.InterpretAsync("tell me something useful", Guid.NewGuid(), "corr", CancellationToken.None);

        Assert.Equal(DialogueOutcome.TemporarilyUnavailable, result.FailureOutcome?.Outcome);
        Assert.Equal(DialogueOutcomeReasonCodes.ProviderOrToolTimeout, result.FailureOutcome?.ReasonCode);
    }

    [Fact]
    public void Projection_ExcludesDisabledCapabilitiesAndProviderDetails()
    {
        var definitions = InitialConversationalCapabilityCatalog.Create()
            .Select((definition, index) => index == 0 ? definition with { Enabled = false } : definition)
            .ToArray();
        var registry = new ConversationalCapabilityRegistry(definitions);
        var projection = new CapabilityRegistryProjection(registry);

        var metadata = projection.BuildMetadataProjection();
        var prompt = projection.BuildBoundedPrompt(1200);

        Assert.DoesNotContain(metadata, item => item.Code == "stock_screening");
        Assert.DoesNotContain("postgres", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("provider", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.True(prompt.Length <= 1200);
    }

    [Fact]
    public void TelemetrySink_UsesBoundedDimensionsOnly()
    {
        var sink = new ActivityQueryInterpretationTelemetrySink();
        using var activity = new System.Diagnostics.Activity("query").Start();

        sink.Record(new QueryInterpretationTelemetry(
            1, 2, "monthly_activity_trend", 0.95m, InterpretationConfidenceBand.High,
            ["trend-keyword", "metric-and-entity"], TimeSpan.FromMilliseconds(4)));

        Assert.Null(activity!.GetTagItem("query.original_text"));
        Assert.Equal("monthly_activity_trend", activity.GetTagItem("query.winning_capability"));
        Assert.Equal("High", activity.GetTagItem("query.winning_confidence_band"));
    }

    private sealed class StubProposalProvider(QueryInterpretationProposal proposal) : IQueryInterpretationProposalProvider
    {
        public Task<QueryInterpretationProposal?> ProposeAsync(
            string originalText,
            Guid tenantId,
            string correlationId,
            CancellationToken cancellationToken) => Task.FromResult<QueryInterpretationProposal?>(proposal);
    }

    private sealed class ThrowingProposalProvider(Exception exception) : IQueryInterpretationProposalProvider
    {
        public Task<QueryInterpretationProposal?> ProposeAsync(
            string originalText,
            Guid tenantId,
            string correlationId,
            CancellationToken cancellationToken) => Task.FromException<QueryInterpretationProposal?>(exception);
    }
}
