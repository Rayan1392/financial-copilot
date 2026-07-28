using FinancialCopilot.Domain.Financial.MissingAnswer;

namespace FinancialCopilot.Application.Scanner;

/// <summary>
/// Inputs the scanner already has at the end of execution. Kept as a flat record so the classifier
/// is pure / framework-free and trivial to unit-test.
/// </summary>
public sealed record MissingAnswerClassificationContext(
    string PrimaryMetricCode,
    bool MetricRegistered,
    int DerivedMetricRowCountForMetric,
    int TotalSymbolCount,
    int MatchedSymbolCount,
    double CoverageThreshold = 0.5);

public static class MissingAnswerFeedbackClassifier
{
    /// <summary>
    /// Decides which classification applies. Returns null when the answer is healthy (matched count
    /// meets the coverage threshold) — no feedback should be emitted in that case.
    /// </summary>
    public static MissingAnswerFeedbackClassification? Classify(MissingAnswerClassificationContext context)
    {
        if (!context.MetricRegistered)
        {
            return MissingAnswerFeedbackClassification.MetricGap;
        }

        if (context.DerivedMetricRowCountForMetric == 0)
        {
            return MissingAnswerFeedbackClassification.CalculationGap;
        }

        if (context.TotalSymbolCount == 0)
        {
            return MissingAnswerFeedbackClassification.UnknownGap;
        }

        var coverageRatio = (double)context.MatchedSymbolCount / context.TotalSymbolCount;

        if (context.MatchedSymbolCount == 0)
        {
            return MissingAnswerFeedbackClassification.DataCoverageGap;
        }

        if (coverageRatio < context.CoverageThreshold)
        {
            return MissingAnswerFeedbackClassification.DataCoverageGap;
        }

        return null;
    }
}
