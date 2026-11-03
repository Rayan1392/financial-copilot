using FinancialCopilot.Application.AI.Orchestration;
using Xunit;

namespace FinancialCopilot.UnitTests;

public sealed class Feature132SemanticRoutingTests
{
    private static ICapabilityInterpreter Interpreter() =>
        new DeterministicCapabilityInterpreter(new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create()));

    [Theory]
    [InlineData("Which company has revenue 3300508?", 3300508)]
    [InlineData("Which company has revenue 3,300,508?", 3300508)]
    [InlineData("Which company has revenue 3300508.25?", 3300508.25)]
    public void NumericNormalization_ProducesExactDecimal(string query, decimal expected)
    {
        Assert.True(QueryNormalization.TryParseFinancialStatementClues(query, out var clues, out var error), error);
        Assert.Single(clues);
        Assert.Equal(expected, clues.Single().Value);
    }

    [Fact]
    public void ExactValueIdentification_PrecedesKnownEntityMetricRoute()
    {
        var interpretation = Interpreter().Interpret("Which company has revenue 3300508?");

        Assert.Equal("financial_statement_value_search", interpretation.CapabilityCandidates.First().CapabilityCode);
    }

    [Fact]
    public void ThresholdQuery_RemainsScanner()
    {
        var interpretation = Interpreter().Interpret("companies with revenue above 3300508");

        Assert.Equal("stock_screening", interpretation.CapabilityCandidates.First().CapabilityCode);
        Assert.DoesNotContain(interpretation.CapabilityCandidates, candidate => candidate.CapabilityCode == "financial_statement_value_search");
    }

    [Fact]
    public void NoNumericClue_DoesNotSelectValueSearch()
    {
        var interpretation = Interpreter().Interpret("find a symbol with revenue");

        Assert.DoesNotContain(interpretation.CapabilityCandidates, candidate => candidate.CapabilityCode == "financial_statement_value_search");
    }

    [Fact]
    public void ValueSearchFrame_DoesNotRequireCompanySymbol()
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var interpretation = Interpreter().Interpret("Which company has revenue 3300508?");
        var entity = new EntityResolutionResult.Missing("CompanyOrSymbol");
        var slots = new CapabilitySlotValidator(registry).Validate("financial_statement_value_search", interpretation, entity).Slots;
        var enriched = new SemanticQueryFrameEnricher().Enrich("financial_statement_value_search", interpretation, slots, DateTimeOffset.UtcNow);

        Assert.Contains(enriched, slot => slot.Type == QuerySlotType.NumericClues && slot.ValidationState == QuerySlotValidationState.Valid);
        Assert.DoesNotContain(enriched, slot => slot.Type == QuerySlotType.CompanyOrSymbol && slot.ValidationState != QuerySlotValidationState.Valid);
    }

    [Fact]
    public void MoreThanTwentyClues_IsRejected()
    {
        var query = "find company revenue " + string.Join(' ', Enumerable.Range(1, 21));

        Assert.False(QueryNormalization.TryParseFinancialStatementClues(query, out _, out var error));
        Assert.Equal("too_many_numeric_clues", error);
    }
}
