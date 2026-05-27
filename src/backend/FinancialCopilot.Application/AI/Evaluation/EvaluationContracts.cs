using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Scanner;

namespace FinancialCopilot.Application.AI.Evaluation;

public enum EvaluationCategory
{
    BilingualParsing,
    SemanticMetricResolution,
    AmbiguityClarification,
    TableSchema,
    EvidenceCompleteness,
    ConfidenceProtection,
    BillingMetadataProtection
}

// Distinguishes deterministic/exact matches from rubric-assisted prose scoring.
public enum EvaluationScoreType
{
    Exact,
    Deterministic,
    RubricAssisted
}

public enum EvaluationRunStatus
{
    Pending,
    Running,
    Completed,
    Failed
}

public enum RegressionSeverity
{
    None,
    Minor,
    Major,
    Critical
}

// Structural expectation for one scanner plan condition.
public sealed record ExpectedCondition(
    string MetricCode,
    ConditionOperator Operator,
    decimal Threshold,
    bool AllowInferred = false);

// Asserts a specific metric appears in the explainable evidence with the given constraints.
public sealed record EvidenceRequirement(
    string MetricCode,
    bool RequireNonNullValue = true,
    string? RequiredPolicyVersion = null);

// Rubric criterion for prose quality assessment. Exact scoring is preferred where possible.
public sealed record ProseRequirement(
    string Criterion,
    bool MustBeSatisfied = true);

// A curated question with its complete expected outcome structure for deterministic scoring.
public sealed record GoldenQuestion(
    Guid QuestionId,
    Guid DatasetId,
    string Query,
    string Language,
    EvaluationCategory Category,
    DetectedIntent ExpectedIntent,
    bool ExpectedClarification,
    IReadOnlyCollection<ExpectedCondition> ExpectedConditions,
    IReadOnlyCollection<EvidenceRequirement> EvidenceRequirements,
    IReadOnlyCollection<ProseRequirement> ProseRequirements,
    string? Notes = null);

// Versioned golden dataset. DatasetVersion identifies the revision for regression comparisons.
public sealed record EvaluationDataset(
    Guid DatasetId,
    string Name,
    string Version,
    IReadOnlyCollection<GoldenQuestion> Questions,
    DateTimeOffset CreatedAt);

// Recorded prompt template version associated with one workload kind.
// Stored per run so model/prompt drift is attributable to version changes.
public sealed record PromptVersion(
    Guid PromptVersionId,
    AiWorkloadKind WorkloadKind,
    string SemanticVersion,
    string PromptTemplate,
    DateTimeOffset CreatedAt);

// Run-level metadata. Captures exact provider, model, prompt, semantic, and policy versions
// so two runs can be reproduced or compared without ambiguity.
public sealed record EvaluationRunMetadata(
    Guid RunId,
    Guid DatasetId,
    string DatasetVersion,
    string ProviderKey,
    string ModelKey,
    Guid? PromptVersionId,
    string SemanticDefinitionVersion,
    string CalculationPolicyVersion,
    bool UsedFakeProvider,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    EvaluationRunStatus Status);

// Score for one golden question within one run.
// ScoreType records whether the score is exact, deterministic, or rubric-assisted.
public sealed record EvaluationScore(
    Guid ScoreId,
    Guid RunId,
    Guid QuestionId,
    EvaluationCategory Category,
    EvaluationScoreType ScoreType,
    double Score,
    bool Passed,
    string? Details = null);

// Regression detected between a baseline and a current run for one question.
public sealed record RegressionResult(
    Guid BaselineRunId,
    Guid CurrentRunId,
    Guid QuestionId,
    EvaluationCategory Category,
    RegressionSeverity Severity,
    double BaselineScore,
    double CurrentScore,
    string? Notes = null);

// Complete result for one evaluation run including aggregated score.
public sealed record EvaluationRunResult(
    EvaluationRunMetadata Metadata,
    IReadOnlyCollection<EvaluationScore> Scores,
    double OverallScore,
    int TotalQuestions,
    int PassedQuestions);

// Report comparing a current run to a stored baseline.
public sealed record RegressionReport(
    Guid BaselineRunId,
    Guid CurrentRunId,
    IReadOnlyCollection<RegressionResult> Regressions,
    int CriticalCount,
    int MajorCount,
    int MinorCount,
    bool HasAnyRegression);

// Provides access to versioned golden evaluation datasets.
public interface IEvaluationDatasetRepository
{
    IReadOnlyCollection<EvaluationDataset> GetAll();
    EvaluationDataset? GetById(Guid datasetId);
    EvaluationDataset? GetByName(string name);
}

// Provides access to recorded prompt template versions by workload.
public interface IPromptVersionRegistry
{
    IReadOnlyCollection<PromptVersion> GetAll();
    PromptVersion? GetLatest(AiWorkloadKind workload);
    PromptVersion? GetById(Guid promptVersionId);
}

// Persists evaluation run results so regressions can be tracked over time.
public interface IEvaluationRunRepository
{
    Task SaveRunAsync(EvaluationRunResult run, CancellationToken cancellationToken);
    Task<EvaluationRunResult?> GetRunAsync(Guid runId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<EvaluationRunMetadata>> ListRunsAsync(
        Guid datasetId,
        CancellationToken cancellationToken);
}

// Scores AI intent routing accuracy against the expected intent in a golden question.
public interface IInterpretationScorer
{
    EvaluationScore Score(GoldenQuestion question, AiQueryResponse? response, Guid runId);
}

// Scores whether clarification was correctly triggered or suppressed.
public interface IClarificationScorer
{
    EvaluationScore Score(GoldenQuestion question, AiQueryResponse? response, Guid runId);
}

// Scores presence and completeness of deterministic backend evidence in the explainable answer.
public interface IEvidenceCompletenessScorer
{
    EvaluationScore Score(GoldenQuestion question, AiQueryResponse? response, Guid runId);
}

// Verifies the confidence score carries the backend policy version and is within [0, 1].
// The LLM must never produce or alter the confidence score; this scorer detects violations.
public interface IConfidenceProtectionScorer
{
    EvaluationScore Score(GoldenQuestion question, AiQueryResponse? response, Guid runId);
}

// Computes a regression report by comparing a baseline run to a current run.
public interface IRegressionReporter
{
    RegressionReport Compare(EvaluationRunResult baseline, EvaluationRunResult current);
}

// Runs a golden dataset through the AI orchestration pipeline and scores all questions.
// Must be wired with deterministic fake AI providers in CI to avoid live model calls.
public interface IAiEvaluationRunner
{
    Task<EvaluationRunResult> RunAsync(
        EvaluationDataset dataset,
        EvaluationRunMetadata metadata,
        CancellationToken cancellationToken);
}
