using FinancialCopilot.Application.AI.Orchestration;

namespace FinancialCopilot.UnitTests;

public sealed class IndustryRelativeValuationSemanticTests
{
    [Theory]
    [InlineData("compare symbol with its industry", "symbol_vs_industry_relative_valuation")]
    [InlineData("industry relative valuation ranking", "industry_relative_valuation_ranking")]
    [InlineData("industry relative valuation summary", "industry_relative_valuation_summary")]
    [InlineData("compare two symbols within their industry", "symbol_pair_within_industry")]
    public void Feature125Capabilities_RouteWithPrecedence(string query, string expected)
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var interpretation = new DeterministicCapabilityInterpreter(registry).Interpret(query);
        Assert.Equal(expected, interpretation.CapabilityCandidates.First().CapabilityCode);
        Assert.Equal("industry_relative_valuation_read", registry.Find(expected)!.ExecutionRoute);
    }

    [Theory]
    [InlineData("fa")]
    [InlineData("en")]
    public void Presentation_RendersEvidenceWithoutRecommendation(string language)
    {
        var model = Model("symbol_vs_industry_relative_valuation", language);
        var text = IndustryRelativeValuationPresentation.Explain(model, language);

        Assert.Contains(language == "fa" ? "1 از 3" : "1/3", text);
        Assert.Contains("42.5", text);
        Assert.Contains("67.5", text);
        Assert.Contains(language == "fa" ? "پرت" : "outlier", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("buy", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sell", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Presentation_ExplainsUnavailableAndInsufficientBenchmark()
    {
        var metric = new RelativeValuationMetricReadModel(null, null, "Unclassifiable", false, "InsufficientBenchmark", "InsufficientBenchmark", "PE", "Insufficient", 1, 0, "at least two clean observations required");
        var model = Model("industry_relative_valuation_summary", "en") with
        {
            Members = [new(Guid.NewGuid(), "AAA", "A", null, 3, metric, metric, metric)],
            InsufficientBenchmarkReason = "at least two clean observations required"
        };

        var text = IndustryRelativeValuationPresentation.Explain(model, "en");

        Assert.Contains("unavailable", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("at least two clean observations", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Executor_RejectsLimitAboveConfiguredMaximum()
    {
        var repository = new RecordingReadRepository();
        var executor = new IndustryRelativeValuationCapabilityExecutor(repository, new IndustryRelativeValuationReadOptions { DefaultResultLimit = 3, MaximumResultLimit = 5 }, "industry_relative_valuation_ranking");
        var frame = Frame("industry_relative_valuation_ranking", new ResolvedQuerySlot(QuerySlotType.ResultLimit, "6", QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid));

        var result = await executor.ExecuteAsync(frame, Context(), default);

        Assert.Equal(CapabilityExecutionStatus.ClarificationRequired, result.Status);
        Assert.Equal(DialogueOutcomeReasonCodes.ResultLimitExceeded, result.ReasonCode);
        Assert.Null(repository.Request);
    }

    [Fact]
    public async Task Executor_UsesDefaultLimitAndReadOnlyRepository()
    {
        var industryId = Guid.NewGuid();
        var repository = new RecordingReadRepository();
        var executor = new IndustryRelativeValuationCapabilityExecutor(repository, new IndustryRelativeValuationReadOptions { DefaultResultLimit = 3, MaximumResultLimit = 5 }, "industry_relative_valuation_ranking");
        var frame = Frame("industry_relative_valuation_ranking", new ResolvedQuerySlot(QuerySlotType.Industry, industryId.ToString("D"), QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid));

        var result = await executor.ExecuteAsync(frame, Context(), default);

        Assert.Equal(CapabilityExecutionStatus.NoData, result.Status);
        Assert.Equal(3, repository.Request!.Limit);
        Assert.Equal(industryId, repository.Request.GroupId);
    }

    [Fact]
    public void ReadOptions_ValidateDefaultAndMaximumBoundaries()
    {
        new IndustryRelativeValuationReadOptions { DefaultResultLimit = 1, MaximumResultLimit = 1 }.Validate();
        new IndustryRelativeValuationReadOptions { DefaultResultLimit = 100, MaximumResultLimit = 1000 }.Validate();
        Assert.Throws<InvalidOperationException>(() => new IndustryRelativeValuationReadOptions { DefaultResultLimit = 6, MaximumResultLimit = 5 }.Validate());
    }

    private static IndustryRelativeValuationReadModel Model(string capability, string language) =>
        new(
            capability,
            Guid.NewGuid(),
            "industry-1",
            language == "fa" ? "صنعت" : "Industry",
            new DateOnly(2026, 8, 12),
            Guid.NewGuid(),
            2,
            "Published",
            DateTimeOffset.UtcNow,
            "IQR-R7-1.5-v1",
            "rank-v1",
            3,
            2,
            [new(Guid.NewGuid(), "AAA", "A", 1, 3,
                new(42.5m, 67.5m, "Green", false, "", "Available", "PE", "Ready", 3, 1),
                new(80m, 75m, "Red", true, "ExcludedFromIndustryBenchmark", "Outlier", "PS", "Ready", 3, 1),
                new(90m, null, "Unclassifiable", false, "InsufficientBenchmark", "Insufficient", "Equilibrium", "Insufficient", 1, 0, "at least two clean observations required"))],
            [],
            "Published",
            DateTimeOffset.UtcNow,
            [],
            "Published",
            "PE:Ready",
            "");

    private static ValidatedQueryFrame Frame(string capability, params ResolvedQuerySlot[] slots) =>
        new(capability, 1, slots, new(capability, capability, "en", [], [], [], null, null, null, [], [], 1m, [], 1));

    private static QueryExecutionContext Context() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "test", "en");

    private sealed class RecordingReadRepository : IIndustryRelativeValuationReadRepository
    {
        public IndustryRelativeValuationReadRequest? Request { get; private set; }

        public Task<IndustryRelativeValuationReadModel?> ReadAsync(IndustryRelativeValuationReadRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult<IndustryRelativeValuationReadModel?>(null);
        }
    }
}
