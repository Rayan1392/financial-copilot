using FinancialCopilot.Application.AI.Orchestration;

namespace FinancialCopilot.UnitTests;

public sealed class ConversationalCapabilityRegistryTests
{
    [Fact]
    public void InitialCatalog_ContainsOnlyExecutableBusinessCapabilities()
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());

        Assert.Equal(11, registry.GetAll().Count);
        Assert.DoesNotContain(registry.GetAll(), definition => definition.Code is "unknown" or "clarification");
        Assert.All(registry.GetAll(), definition => Assert.NotNull(registry.Find(definition.Code)));
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
            [], [], null, null, null, [], [], 1m, [], registry.Version);

        var exception = Assert.Throws<InvalidOperationException>(() => validator.Validate(invalid));

        Assert.Contains("invalid capability", exception.Message);
    }
}
