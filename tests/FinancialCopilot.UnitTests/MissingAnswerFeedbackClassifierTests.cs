using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.MissingAnswer;

namespace FinancialCopilot.UnitTests;

public sealed class MissingAnswerFeedbackClassifierTests
{
    [Fact]
    public void UnregisteredMetric_ProducesMetricGap()
    {
        var result = MissingAnswerFeedbackClassifier.Classify(
            new MissingAnswerClassificationContext("UNKNOWN_METRIC",
                MetricRegistered: false, DerivedMetricRowCountForMetric: 0,
                TotalSymbolCount: 100, MatchedSymbolCount: 0));
        Assert.Equal(MissingAnswerFeedbackClassification.MetricGap, result);
    }

    [Fact]
    public void RegisteredButNoDerivedRows_ProducesCalculationGap()
    {
        var result = MissingAnswerFeedbackClassifier.Classify(
            new MissingAnswerClassificationContext("REVENUE_GROWTH_YOY",
                MetricRegistered: true, DerivedMetricRowCountForMetric: 0,
                TotalSymbolCount: 100, MatchedSymbolCount: 0));
        Assert.Equal(MissingAnswerFeedbackClassification.CalculationGap, result);
    }

    [Fact]
    public void RegisteredWithRowsButZeroMatches_ProducesDataCoverageGap()
    {
        var result = MissingAnswerFeedbackClassifier.Classify(
            new MissingAnswerClassificationContext("NET_PROFIT_GROWTH_YOY",
                MetricRegistered: true, DerivedMetricRowCountForMetric: 50,
                TotalSymbolCount: 100, MatchedSymbolCount: 0));
        Assert.Equal(MissingAnswerFeedbackClassification.DataCoverageGap, result);
    }

    [Fact]
    public void SparseCoverageBelowThreshold_ProducesDataCoverageGap()
    {
        var result = MissingAnswerFeedbackClassifier.Classify(
            new MissingAnswerClassificationContext("PE_TTM",
                MetricRegistered: true, DerivedMetricRowCountForMetric: 200,
                TotalSymbolCount: 100, MatchedSymbolCount: 20)); // 20% coverage
        Assert.Equal(MissingAnswerFeedbackClassification.DataCoverageGap, result);
    }

    [Fact]
    public void HealthyCoverageAboveThreshold_ProducesNullNoFeedback()
    {
        var result = MissingAnswerFeedbackClassifier.Classify(
            new MissingAnswerClassificationContext("PE_TTM",
                MetricRegistered: true, DerivedMetricRowCountForMetric: 200,
                TotalSymbolCount: 100, MatchedSymbolCount: 60)); // 60% coverage
        Assert.Null(result);
    }

    [Fact]
    public void EmptyUniverseRegisteredMetric_ProducesUnknownGap()
    {
        var result = MissingAnswerFeedbackClassifier.Classify(
            new MissingAnswerClassificationContext("PE_TTM",
                MetricRegistered: true, DerivedMetricRowCountForMetric: 5,
                TotalSymbolCount: 0, MatchedSymbolCount: 0));
        Assert.Equal(MissingAnswerFeedbackClassification.UnknownGap, result);
    }

    [Fact]
    public void CustomThreshold_IsRespected()
    {
        var result = MissingAnswerFeedbackClassifier.Classify(
            new MissingAnswerClassificationContext("PE_TTM",
                MetricRegistered: true, DerivedMetricRowCountForMetric: 100,
                TotalSymbolCount: 100, MatchedSymbolCount: 75,
                CoverageThreshold: 0.9)); // 75% < 90%
        Assert.Equal(MissingAnswerFeedbackClassification.DataCoverageGap, result);
    }
}
