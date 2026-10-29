using FinancialCopilot.Application.FinancialData.Semantics.Intent;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Live extraction sometimes echoes the requested measure as an output field. That field carries no
/// semantics, so the canonicalizer removes it; genuinely different requested fields are preserved so
/// the planner handoff still rejects them explicitly.
/// </summary>
public sealed class CanonicalIntentRequestedFieldNormalizationTests
{
    private static CanonicalSemanticIntent Intent(params string[] requestedFields) => new(2,
        [new RequestedMeasure("سود عملیاتی هر سهم", null, MeasureCanonicalizationStatus.Unrecognized, [],
            MeasureConceptKind.FinancialStatementItem, BusinessMeasureDimension.Unknown, new(), SemanticUnit.Unspecified,
            null, EffectiveUnitEvidence.Unresolved)],
        new(AnalyticalOperationKind.Retrieve), new(ComparisonMode.None), new(ReportingPeriodKind.Unspecified),
        new(EntityScopeKind.NamedCompanies, ["کگهر"]), [], [], null, null, null,
        new(RequestedOutputKind.Scalar, requestedFields), [], new(false, [], []));

    [Theory]
    [InlineData("سود عملیاتی هر سهم")]
    [InlineData("operatingProfitPerShare")]
    [InlineData("  سود عملیاتی هر سهم ")]
    public void RequestedFieldRestatingTheMeasureIsRemoved(string field)
    {
        var actual = new CanonicalSemanticIntentCanonicalizer(CanonicalBusinessMeasureConceptRegistry.Default).Canonicalize(Intent(field));
        Assert.Equal("operatingProfitPerShare", actual.Measures[0].ConceptId);
        Assert.Empty(actual.Output.RequestedFields);
        Assert.Equal(SemanticUnit.IRRPerShare, actual.Measures[0].RequestedUnit);
    }

    [Fact]
    public void CompanyTotalGrainOnAStatementItemIsNormalizedAway()
    {
        var intent = Intent();
        var withGrain = intent with { Measures = [intent.Measures[0] with { UserPhrase = "سود خالص", Qualifiers = new(ValueGrain: MeasureValueGrain.CompanyTotal) }] };
        var actual = new CanonicalSemanticIntentCanonicalizer(CanonicalBusinessMeasureConceptRegistry.Default).Canonicalize(withGrain);
        Assert.Equal("netProfit", actual.Measures[0].ConceptId);
        Assert.Equal(MeasureValueGrain.Unspecified, actual.Measures[0].Qualifiers.ValueGrain);
        var productLevel = intent with { Measures = [intent.Measures[0] with { UserPhrase = "سود خالص", Qualifiers = new(ValueGrain: MeasureValueGrain.ProductLevel) }] };
        Assert.Equal(MeasureValueGrain.ProductLevel,
            new CanonicalSemanticIntentCanonicalizer(CanonicalBusinessMeasureConceptRegistry.Default).Canonicalize(productLevel).Measures[0].Qualifiers.ValueGrain);
    }

    [Theory]
    [InlineData("value")]
    [InlineData("Value")]
    [InlineData("مقدار")]
    public void GenericValuePlaceholderFieldIsRemoved(string field)
    {
        var actual = new CanonicalSemanticIntentCanonicalizer(CanonicalBusinessMeasureConceptRegistry.Default).Canonicalize(Intent(field));
        Assert.Empty(actual.Output.RequestedFields);
    }

    [Fact]
    public void DifferentRequestedFieldIsPreserved()
    {
        var actual = new CanonicalSemanticIntentCanonicalizer(CanonicalBusinessMeasureConceptRegistry.Default)
            .Canonicalize(Intent("سود عملیاتی هر سهم", "تاریخ انتشار"));
        Assert.Equal(["تاریخ انتشار"], actual.Output.RequestedFields);
    }
}
