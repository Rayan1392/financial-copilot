using FinancialCopilot.Application.AI.Evaluation;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Scanner;

namespace FinancialCopilot.UnitTests;

// ── InterpretationScorer ──────────────────────────────────────────────────────

public sealed class InterpretationScorerTests
{
    private readonly InterpretationScorer _sut = new();

    [Fact]
    public void Score_NullResponse_Fails()
    {
        var score = _sut.Score(E.Question(DetectedIntent.Scanner, false), null, Guid.NewGuid());
        Assert.False(score.Passed);
        Assert.Equal(0.0, score.Score);
    }

    [Fact]
    public void Score_CorrectIntentNoExpectedConditions_Passes()
    {
        var question = E.Question(DetectedIntent.Unknown, false);
        var response = E.Response(DetectedIntent.Unknown, clarification: false);

        var score = _sut.Score(question, response, Guid.NewGuid());

        Assert.True(score.Passed);
        Assert.Equal(1.0, score.Score);
    }

    [Fact]
    public void Score_WrongIntent_Fails()
    {
        var question = E.Question(DetectedIntent.Scanner, false);
        var response = E.Response(DetectedIntent.Clarification, clarification: true);

        var score = _sut.Score(question, response, Guid.NewGuid());

        Assert.False(score.Passed);
        Assert.Contains("Intent", score.Details);
    }

    [Fact]
    public void Score_MatchingExplicitCondition_Passes()
    {
        var question = E.Question(DetectedIntent.Scanner, false,
            conditions: [new ExpectedCondition("PE_TTM", ConditionOperator.LessThan, 15m)]);

        var plan = E.Plan([D.Condition("PE_TTM", ConditionOperator.LessThan, 15m, FilterOrigin.Explicit)]);
        var response = E.ResponseWithPlan(plan);

        var score = _sut.Score(question, response, Guid.NewGuid());

        Assert.True(score.Passed);
    }

    [Fact]
    public void Score_MissingExpectedCondition_Fails()
    {
        var question = E.Question(DetectedIntent.Scanner, false,
            conditions: [new ExpectedCondition("PE_TTM", ConditionOperator.LessThan, 15m)]);

        var plan = E.Plan([D.Condition("PS_TTM", ConditionOperator.LessThan, 2m, FilterOrigin.Explicit)]);
        var response = E.ResponseWithPlan(plan);

        var score = _sut.Score(question, response, Guid.NewGuid());

        Assert.False(score.Passed);
        Assert.Contains("PE_TTM", score.Details);
    }

    [Fact]
    public void Score_InferredConditionWhenAllowInferredFalse_Fails()
    {
        var question = E.Question(DetectedIntent.Scanner, false,
            conditions: [new ExpectedCondition("PE_TTM", ConditionOperator.LessThan, 15m, AllowInferred: false)]);

        var plan = E.Plan([D.Condition("PE_TTM", ConditionOperator.LessThan, 15m, FilterOrigin.InferredDefault)]);
        var response = E.ResponseWithPlan(plan);

        var score = _sut.Score(question, response, Guid.NewGuid());

        Assert.False(score.Passed);
    }

    [Fact]
    public void Score_InferredConditionWhenAllowInferredTrue_Passes()
    {
        var question = E.Question(DetectedIntent.Scanner, false,
            conditions: [new ExpectedCondition("PE_TTM", ConditionOperator.LessThan, 15m, AllowInferred: true)]);

        var plan = E.Plan([D.Condition("PE_TTM", ConditionOperator.LessThan, 15m, FilterOrigin.InferredDefault)]);
        var response = E.ResponseWithPlan(plan);

        var score = _sut.Score(question, response, Guid.NewGuid());

        Assert.True(score.Passed);
    }

    [Fact]
    public void Score_ExpectedConditionsButNoPlan_Fails()
    {
        var question = E.Question(DetectedIntent.Scanner, false,
            conditions: [new ExpectedCondition("PE_TTM", ConditionOperator.LessThan, 15m)]);

        var response = E.Response(DetectedIntent.Scanner, clarification: false);

        var score = _sut.Score(question, response, Guid.NewGuid());

        Assert.False(score.Passed);
    }
}

// ── ClarificationScorer ───────────────────────────────────────────────────────

public sealed class ClarificationScorerTests
{
    private readonly ClarificationScorer _sut = new();

    [Fact]
    public void Score_NullResponse_Fails()
    {
        var score = _sut.Score(E.Question(DetectedIntent.Clarification, true), null, Guid.NewGuid());
        Assert.False(score.Passed);
    }

    [Fact]
    public void Score_ClarificationExpectedAndPresent_Passes()
    {
        var question = E.Question(DetectedIntent.Clarification, expectedClarification: true);
        var response = E.Response(DetectedIntent.Clarification, clarification: true);

        var score = _sut.Score(question, response, Guid.NewGuid());

        Assert.True(score.Passed);
        Assert.Equal(1.0, score.Score);
    }

    [Fact]
    public void Score_NoClarificationExpectedAndAbsent_Passes()
    {
        var question = E.Question(DetectedIntent.Scanner, expectedClarification: false);
        var response = E.Response(DetectedIntent.Scanner, clarification: false);

        var score = _sut.Score(question, response, Guid.NewGuid());

        Assert.True(score.Passed);
    }

    [Fact]
    public void Score_ClarificationExpectedButAbsent_Fails()
    {
        var question = E.Question(DetectedIntent.Clarification, expectedClarification: true);
        var response = E.Response(DetectedIntent.Scanner, clarification: false);

        var score = _sut.Score(question, response, Guid.NewGuid());

        Assert.False(score.Passed);
        Assert.NotNull(score.Details);
    }
}

// ── EvidenceCompletenessScorer ────────────────────────────────────────────────

public sealed class EvidenceCompletenessScorerTests
{
    private readonly EvidenceCompletenessScorer _sut = new();

    [Fact]
    public void Score_NoRequirements_Passes()
    {
        var question = E.Question(DetectedIntent.Scanner, false);
        var score = _sut.Score(question, E.Response(DetectedIntent.Scanner, false), Guid.NewGuid());
        Assert.True(score.Passed);
        Assert.Equal(EvaluationScoreType.Deterministic, score.ScoreType);
    }

    [Fact]
    public void Score_RequiredMetricPresentWithValue_Passes()
    {
        var question = E.QuestionWithEvidence([new EvidenceRequirement("PE_TTM")]);
        var response = E.ResponseWithEvidence([E.MetricEv("PE_TTM", 12.5m, "v1", "PE_TTM_v1")]);

        var score = _sut.Score(question, response, Guid.NewGuid());

        Assert.True(score.Passed);
    }

    [Fact]
    public void Score_RequiredMetricMissing_Fails()
    {
        var question = E.QuestionWithEvidence([new EvidenceRequirement("PE_TTM")]);
        var response = E.ResponseWithEvidence([E.MetricEv("PS_TTM", 1.5m, "v1", "PS_TTM_v1")]);

        var score = _sut.Score(question, response, Guid.NewGuid());

        Assert.False(score.Passed);
        Assert.Contains("PE_TTM", score.Details);
    }

    [Fact]
    public void Score_RequiredMetricPresentButNullValue_Fails()
    {
        var question = E.QuestionWithEvidence([new EvidenceRequirement("PE_TTM", RequireNonNullValue: true)]);
        var response = E.ResponseWithEvidence([E.MetricEvNull("PE_TTM", "v1", "PE_TTM_v1")]);

        var score = _sut.Score(question, response, Guid.NewGuid());

        Assert.False(score.Passed);
    }

    [Fact]
    public void Score_NoExplainableAnswer_Fails()
    {
        var question = E.QuestionWithEvidence([new EvidenceRequirement("PE_TTM")]);
        var score = _sut.Score(question, E.Response(DetectedIntent.Scanner, false), Guid.NewGuid());
        Assert.False(score.Passed);
    }

    [Fact]
    public void Score_PolicyVersionRequired_MismatchFails()
    {
        var question = E.QuestionWithEvidence(
            [new EvidenceRequirement("PE_TTM", RequiredPolicyVersion: "PE_TTM_v1")]);
        var response = E.ResponseWithEvidence(
            [E.MetricEv("PE_TTM", 12.5m, "v1", "PE_TTM_v2")]);

        var score = _sut.Score(question, response, Guid.NewGuid());

        Assert.False(score.Passed);
        Assert.Contains("PE_TTM", score.Details);
    }
}

// ── ConfidenceProtectionScorer ────────────────────────────────────────────────

public sealed class ConfidenceProtectionScorerTests
{
    private readonly ConfidenceProtectionScorer _sut = new();

    [Fact]
    public void Score_ValidConfidenceWithV1Policy_Passes()
    {
        var question = E.Question(DetectedIntent.Scanner, false,
            category: EvaluationCategory.ConfidenceProtection);
        var response = E.ResponseWithConfidence(score: 0.85, policyVersion: "v1");

        var score = _sut.Score(question, response, Guid.NewGuid());

        Assert.True(score.Passed);
        Assert.Equal(EvaluationScoreType.Deterministic, score.ScoreType);
    }

    [Fact]
    public void Score_TopLevelConfidenceWithoutExplainableAnswer_Passes()
    {
        var question = E.Question(DetectedIntent.SymbolLookup, false,
            category: EvaluationCategory.ConfidenceProtection);
        var response = E.ResponseWithTopLevelConfidenceOnly(DetectedIntent.SymbolLookup, 0.95, "v1");

        var score = _sut.Score(question, response, Guid.NewGuid());

        Assert.True(score.Passed);
        Assert.Equal(EvaluationScoreType.Deterministic, score.ScoreType);
    }

    [Fact]
    public void Score_WrongPolicyVersion_Fails()
    {
        var question = E.Question(DetectedIntent.Scanner, false,
            category: EvaluationCategory.ConfidenceProtection);
        var response = E.ResponseWithConfidence(score: 0.85, policyVersion: "llm-generated");

        var score = _sut.Score(question, response, Guid.NewGuid());

        Assert.False(score.Passed);
        Assert.Contains("llm-generated", score.Details);
    }

    [Fact]
    public void Score_ConfidenceOutOfRange_Fails()
    {
        var question = E.Question(DetectedIntent.Scanner, false,
            category: EvaluationCategory.ConfidenceProtection);
        var response = E.ResponseWithConfidence(score: 1.5, policyVersion: "v1");

        var score = _sut.Score(question, response, Guid.NewGuid());

        Assert.False(score.Passed);
    }

    [Fact]
    public void Score_NoExplainableAnswer_Fails()
    {
        var question = E.Question(DetectedIntent.Scanner, false,
            category: EvaluationCategory.ConfidenceProtection);
        var score = _sut.Score(question, E.Response(DetectedIntent.Scanner, false), Guid.NewGuid());
        Assert.False(score.Passed);
    }
}

// ── RegressionReporter ────────────────────────────────────────────────────────

public sealed class RegressionReporterTests
{
    private readonly RegressionReporter _sut = new();

    [Fact]
    public void Compare_NoScoreDrop_ReturnsEmptyRegressions()
    {
        var questionId = Guid.NewGuid();
        var baseline = E.Run([E.Score(questionId, 1.0, passed: true)]);
        var current = E.Run([E.Score(questionId, 1.0, passed: true)]);

        var report = _sut.Compare(baseline, current);

        Assert.False(report.HasAnyRegression);
        Assert.Empty(report.Regressions);
    }

    [Fact]
    public void Compare_SmallDrop_IsMinorRegression()
    {
        var questionId = Guid.NewGuid();
        var baseline = E.Run([E.Score(questionId, 1.0, passed: true)]);
        var current = E.Run([E.Score(questionId, 0.95, passed: true)]);

        var report = _sut.Compare(baseline, current);

        Assert.Single(report.Regressions);
        Assert.Equal(RegressionSeverity.Minor, report.Regressions.Single().Severity);
        Assert.Equal(1, report.MinorCount);
    }

    [Fact]
    public void Compare_TenPercentDrop_IsMajorRegression()
    {
        var questionId = Guid.NewGuid();
        var baseline = E.Run([E.Score(questionId, 1.0, passed: true)]);
        var current = E.Run([E.Score(questionId, 0.85, passed: false)]);

        var report = _sut.Compare(baseline, current);

        Assert.Single(report.Regressions);
        Assert.Equal(RegressionSeverity.Major, report.Regressions.Single().Severity);
        Assert.Equal(1, report.MajorCount);
    }

    [Fact]
    public void Compare_ThirtyPercentDrop_IsCriticalRegression()
    {
        var questionId = Guid.NewGuid();
        var baseline = E.Run([E.Score(questionId, 1.0, passed: true)]);
        var current = E.Run([E.Score(questionId, 0.0, passed: false)]);

        var report = _sut.Compare(baseline, current);

        Assert.Equal(RegressionSeverity.Critical, report.Regressions.Single().Severity);
        Assert.Equal(1, report.CriticalCount);
    }

    [Fact]
    public void Compare_QuestionMissingFromBaseline_IsIgnored()
    {
        var baseline = E.Run([E.Score(Guid.NewGuid(), 1.0, passed: true)]);
        var current = E.Run([E.Score(Guid.NewGuid(), 0.0, passed: false)]);

        var report = _sut.Compare(baseline, current);

        Assert.False(report.HasAnyRegression);
    }

    [Fact]
    public void Compare_ScoreImproved_IsNotRegression()
    {
        var questionId = Guid.NewGuid();
        var baseline = E.Run([E.Score(questionId, 0.5, passed: false)]);
        var current = E.Run([E.Score(questionId, 1.0, passed: true)]);

        var report = _sut.Compare(baseline, current);

        Assert.False(report.HasAnyRegression);
    }
}

// ── Golden Dataset invariants ─────────────────────────────────────────────────

public sealed class GoldenDatasetsTests
{
    [Fact]
    public void Phase1Dataset_HasAtLeastTenQuestions()
    {
        Assert.True(GoldenDatasets.Phase1ScannerEvaluation.Questions.Count >= 10);
    }

    [Fact]
    public void Phase1Dataset_AllQuestionsHaveUniqueIds()
    {
        var ids = GoldenDatasets.Phase1ScannerEvaluation.Questions
            .Select(q => q.QuestionId)
            .ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Phase1Dataset_CoversBilingualParsing()
    {
        var categories = GoldenDatasets.Phase1ScannerEvaluation.Questions
            .Select(q => q.Category)
            .ToHashSet();
        Assert.Contains(EvaluationCategory.BilingualParsing, categories);
    }

    [Fact]
    public void Phase1Dataset_CoversAmbiguityClarification()
    {
        Assert.Contains(
            GoldenDatasets.Phase1ScannerEvaluation.Questions,
            q => q.Category == EvaluationCategory.AmbiguityClarification);
    }

    [Fact]
    public void Phase1Dataset_CoversConfidenceProtection()
    {
        Assert.Contains(
            GoldenDatasets.Phase1ScannerEvaluation.Questions,
            q => q.Category == EvaluationCategory.ConfidenceProtection);
    }

    [Fact]
    public void Phase1Dataset_HasVersionString()
    {
        Assert.False(string.IsNullOrWhiteSpace(
            GoldenDatasets.Phase1ScannerEvaluation.Version));
    }

    [Fact]
    public void SeedRepository_GetByName_ReturnsPhase1Dataset()
    {
        var repo = new SeedEvaluationDatasetRepository();
        var dataset = repo.GetByName("phase1-scanner-evaluation");
        Assert.NotNull(dataset);
        Assert.Equal(GoldenDatasets.Phase1ScannerEvaluation.DatasetId, dataset.DatasetId);
    }

    [Fact]
    public void SeedRepository_GetById_ReturnsPhase1Dataset()
    {
        var repo = new SeedEvaluationDatasetRepository();
        var dataset = repo.GetById(GoldenDatasets.Phase1ScannerEvaluation.DatasetId);
        Assert.NotNull(dataset);
    }
}

// ── E = evaluation test data factory ─────────────────────────────────────────

internal static class E
{
    private static readonly Guid DatasetId = new("eeeeeeee-0000-0000-0000-000000000001");

    public static GoldenQuestion Question(
        DetectedIntent intent,
        bool expectedClarification,
        IReadOnlyCollection<ExpectedCondition>? conditions = null,
        EvaluationCategory category = EvaluationCategory.BilingualParsing) =>
        new(Guid.NewGuid(), DatasetId, "test query", "en", category, intent,
            expectedClarification, conditions ?? [], [], []);

    public static GoldenQuestion QuestionWithEvidence(
        IReadOnlyCollection<EvidenceRequirement> requirements) =>
        new(Guid.NewGuid(), DatasetId, "test query", "en",
            EvaluationCategory.EvidenceCompleteness, DetectedIntent.Scanner, false,
            [], requirements, []);

    public static AiQueryResponse Response(DetectedIntent intent, bool clarification) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), intent,
            null, null, null, null, null, null, clarification, null, null);

    public static AiQueryResponse ResponseWithPlan(ScannerQueryPlan plan) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DetectedIntent.Scanner,
            plan, null, null, null, null, null, false, null, null);

    public static AiQueryResponse ResponseWithEvidence(
        IReadOnlyCollection<MetricEvidenceSummary> evidence)
    {
        var confidence = new ConfidenceScoreResult(
            0.9, new ConfidenceFactors(1.0, 1.0, 1.0, 0.0), "v1");
        var answer = new ExplainableAnswer([], evidence, [], confidence, [], null);
        return new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DetectedIntent.Scanner,
            null, null, null, answer, confidence, null, false, null, null);
    }

    public static AiQueryResponse ResponseWithConfidence(double score, string policyVersion)
    {
        var confidence = new ConfidenceScoreResult(
            score, new ConfidenceFactors(1.0, 1.0, 1.0, 0.0), policyVersion);
        var answer = new ExplainableAnswer([], [], [], confidence, [], null);
        return new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DetectedIntent.Scanner,
            null, null, null, answer, confidence, null, false, null, null);
    }

    public static AiQueryResponse ResponseWithTopLevelConfidenceOnly(
        DetectedIntent intent,
        double score,
        string policyVersion)
    {
        var confidence = new ConfidenceScoreResult(
            score, new ConfidenceFactors(1.0, 1.0, 1.0, 0.0), policyVersion);
        return new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), intent,
            null, null, null, null, confidence, null, false, null, null);
    }

    public static ScannerQueryPlan Plan(IReadOnlyCollection<ScannerCondition> conditions) =>
        new(Guid.NewGuid(), "test", "en", conditions, [], false, null, [], [], DateTimeOffset.UtcNow, "v1");

    public static MetricEvidenceSummary MetricEv(
        string code, decimal value, string metricVersion, string policyVersion) =>
        new(code, metricVersion, policyVersion, code, "x", value, value.ToString(), "TTM", null);

    public static MetricEvidenceSummary MetricEvNull(
        string code, string metricVersion, string policyVersion) =>
        new(code, metricVersion, policyVersion, code, "x", null, null, "TTM", null);

    public static EvaluationRunResult Run(IReadOnlyCollection<EvaluationScore> scores)
    {
        var runId = scores.FirstOrDefault()?.RunId ?? Guid.NewGuid();
        var metadata = new EvaluationRunMetadata(
            runId, DatasetId, "1.0.0", "fake", "fake-model",
            null, "v1", "v1", true,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            EvaluationRunStatus.Completed);
        var passed = scores.Count(s => s.Passed);
        return new EvaluationRunResult(metadata, scores, passed / (double)scores.Count, scores.Count, passed);
    }

    public static EvaluationScore Score(Guid questionId, double score, bool passed) =>
        new(Guid.NewGuid(), Guid.NewGuid(), questionId,
            EvaluationCategory.BilingualParsing, EvaluationScoreType.Exact,
            score, passed);
}
