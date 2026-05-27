using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Scanner;

namespace FinancialCopilot.Application.AI.Evaluation;

// Exact-match scorer for AI intent routing.
// Checks that the detected intent matches the golden expectation and that all
// expected scanner conditions are present in the plan with the correct metric code,
// operator, and threshold. AllowInferred = false requires the condition to be Explicit
// or Clarified (not silently defaulted by the parser).
public sealed class InterpretationScorer : IInterpretationScorer
{
    public EvaluationScore Score(GoldenQuestion question, AiQueryResponse? response, Guid runId)
    {
        if (response is null)
            return Fail(question, runId, "No response produced");

        if (response.Intent != question.ExpectedIntent)
            return Fail(question, runId,
                $"Intent: expected {question.ExpectedIntent}, got {response.Intent}");

        if (!question.ExpectedConditions.Any())
            return Pass(question, runId);

        if (response.ScannerPlan is null)
            return Fail(question, runId, "Expected scanner plan but none was produced");

        var missing = question.ExpectedConditions
            .Where(expected => !response.ScannerPlan.Conditions.Any(actual =>
                actual.MetricReference.MetricCode.Value == expected.MetricCode &&
                actual.Operator == expected.Operator &&
                actual.Threshold == expected.Threshold &&
                (expected.AllowInferred || actual.Origin != FilterOrigin.InferredDefault)))
            .Select(e => $"{e.MetricCode} {e.Operator} {e.Threshold}")
            .ToList();

        return missing.Count == 0
            ? Pass(question, runId)
            : Fail(question, runId, $"Missing expected conditions: {string.Join(", ", missing)}");
    }

    private static EvaluationScore Pass(GoldenQuestion q, Guid runId) =>
        new(Guid.NewGuid(), runId, q.QuestionId, q.Category, EvaluationScoreType.Exact, 1.0, true);

    private static EvaluationScore Fail(GoldenQuestion q, Guid runId, string details) =>
        new(Guid.NewGuid(), runId, q.QuestionId, q.Category, EvaluationScoreType.Exact, 0.0, false, details);
}

// Exact-match scorer for clarification routing.
// Passes if the response's ClarificationRequired flag matches the golden expectation.
public sealed class ClarificationScorer : IClarificationScorer
{
    public EvaluationScore Score(GoldenQuestion question, AiQueryResponse? response, Guid runId)
    {
        if (response is null)
            return new(Guid.NewGuid(), runId, question.QuestionId, question.Category,
                EvaluationScoreType.Exact, 0.0, false, "No response produced");

        var passed = response.ClarificationRequired == question.ExpectedClarification;
        return new(Guid.NewGuid(), runId, question.QuestionId, question.Category,
            EvaluationScoreType.Exact, passed ? 1.0 : 0.0, passed,
            passed ? null
                   : $"Clarification: expected {question.ExpectedClarification}, got {response.ClarificationRequired}");
    }
}

// Deterministic scorer for backend-calculated evidence completeness.
// Checks that each required metric appears in the explainable answer's MetricEvidence,
// with a non-null actual value and the required policy version when specified.
public sealed class EvidenceCompletenessScorer : IEvidenceCompletenessScorer
{
    public EvaluationScore Score(GoldenQuestion question, AiQueryResponse? response, Guid runId)
    {
        if (!question.EvidenceRequirements.Any())
            return new(Guid.NewGuid(), runId, question.QuestionId, question.Category,
                EvaluationScoreType.Deterministic, 1.0, true);

        if (response?.ExplainableAnswer is null)
            return new(Guid.NewGuid(), runId, question.QuestionId, question.Category,
                EvaluationScoreType.Deterministic, 0.0, false, "No explainable answer present");

        var evidence = response.ExplainableAnswer.MetricEvidence;
        var failures = new List<string>();

        foreach (var req in question.EvidenceRequirements)
        {
            var match = evidence.FirstOrDefault(e => e.MetricCode == req.MetricCode);
            if (match is null) { failures.Add($"Missing evidence for {req.MetricCode}"); continue; }
            if (req.RequireNonNullValue && match.ActualValue is null)
                failures.Add($"Null value for evidence {req.MetricCode}");
            if (req.RequiredPolicyVersion is not null &&
                match.CalculationPolicyVersion != req.RequiredPolicyVersion)
                failures.Add($"Policy version mismatch for {req.MetricCode}: " +
                             $"expected {req.RequiredPolicyVersion}, got {match.CalculationPolicyVersion}");
        }

        return failures.Count == 0
            ? new(Guid.NewGuid(), runId, question.QuestionId, question.Category,
                EvaluationScoreType.Deterministic, 1.0, true)
            : new(Guid.NewGuid(), runId, question.QuestionId, question.Category,
                EvaluationScoreType.Deterministic, 0.0, false, string.Join("; ", failures));
    }
}

// Deterministic scorer that verifies the confidence score was computed by the backend.
// The LLM must never produce or overwrite the confidence score; this scorer detects violations
// by asserting the score is in [0, 1] and the policy version is the backend-assigned "v1".
public sealed class ConfidenceProtectionScorer : IConfidenceProtectionScorer
{
    private const string BackendPolicyVersion = "v1";

    public EvaluationScore Score(GoldenQuestion question, AiQueryResponse? response, Guid runId)
    {
        if (response?.ExplainableAnswer is null)
            return new(Guid.NewGuid(), runId, question.QuestionId, question.Category,
                EvaluationScoreType.Deterministic, 0.0, false, "No confidence result present");

        var confidence = response.ExplainableAnswer.Confidence;
        var failures = new List<string>();

        if (confidence.Score < 0.0 || confidence.Score > 1.0)
            failures.Add($"Confidence score out of range: {confidence.Score}");
        if (confidence.PolicyVersion != BackendPolicyVersion)
            failures.Add($"Unexpected confidence policy version: '{confidence.PolicyVersion}'");

        return failures.Count == 0
            ? new(Guid.NewGuid(), runId, question.QuestionId, question.Category,
                EvaluationScoreType.Deterministic, 1.0, true)
            : new(Guid.NewGuid(), runId, question.QuestionId, question.Category,
                EvaluationScoreType.Deterministic, 0.0, false, string.Join("; ", failures));
    }
}

// Computes a regression report by comparing per-question scores between two runs.
// Severity is based on the magnitude of the score drop:
//   Minor  : 0 < drop < 0.10
//   Major  : 0.10 <= drop < 0.30
//   Critical: drop >= 0.30 (or any binary 1 → 0 regression with exact scorers)
public sealed class RegressionReporter : IRegressionReporter
{
    private const double MajorThreshold = 0.10;
    private const double CriticalThreshold = 0.30;

    public RegressionReport Compare(EvaluationRunResult baseline, EvaluationRunResult current)
    {
        var baselineByQuestion = baseline.Scores.ToDictionary(s => s.QuestionId);
        var regressions = new List<RegressionResult>();

        foreach (var currentScore in current.Scores)
        {
            if (!baselineByQuestion.TryGetValue(currentScore.QuestionId, out var baselineScore))
                continue;

            var drop = baselineScore.Score - currentScore.Score;
            if (drop <= 0) continue;

            var severity = drop >= CriticalThreshold ? RegressionSeverity.Critical
                         : drop >= MajorThreshold    ? RegressionSeverity.Major
                                                     : RegressionSeverity.Minor;

            regressions.Add(new RegressionResult(
                baseline.Metadata.RunId,
                current.Metadata.RunId,
                currentScore.QuestionId,
                currentScore.Category,
                severity,
                baselineScore.Score,
                currentScore.Score));
        }

        return new RegressionReport(
            baseline.Metadata.RunId,
            current.Metadata.RunId,
            regressions,
            regressions.Count(r => r.Severity == RegressionSeverity.Critical),
            regressions.Count(r => r.Severity == RegressionSeverity.Major),
            regressions.Count(r => r.Severity == RegressionSeverity.Minor),
            regressions.Count > 0);
    }
}

// Runs a golden evaluation dataset through the AI orchestration pipeline and scores results.
// Uses synthetic evaluation tenant/actor IDs that do not correspond to production accounts.
// Must be wired with a deterministic fake AI provider in CI.
// Live-provider runs are an explicit opt-in step; see EvaluationPolicy comments.
public sealed class AiEvaluationRunner(
    IAiQueryOrchestrationService orchestrationService,
    IInterpretationScorer interpretationScorer,
    IClarificationScorer clarificationScorer,
    IEvidenceCompletenessScorer evidenceScorer,
    IConfidenceProtectionScorer confidenceScorer,
    TimeProvider timeProvider) : IAiEvaluationRunner
{
    // Synthetic, non-billable IDs used only during evaluation runs.
    private static readonly Guid EvaluationTenantId =
        new("e7a10000-0000-0000-0000-000000000001");
    private static readonly Guid EvaluationActorId =
        new("e7a10000-0000-0000-0000-000000000002");

    public async Task<EvaluationRunResult> RunAsync(
        EvaluationDataset dataset,
        EvaluationRunMetadata metadata,
        CancellationToken cancellationToken)
    {
        var scores = new List<EvaluationScore>();
        var passedQuestions = 0;

        foreach (var question in dataset.Questions)
        {
            AiQueryResponse? response = null;
            string? errorDetail = null;

            try
            {
                var request = new AiQueryRequest(
                    Message: question.Query,
                    TenantId: EvaluationTenantId,
                    ActorId: EvaluationActorId,
                    CorrelationId: $"eval-{metadata.RunId:N}-{question.QuestionId:N}");

                response = await orchestrationService.ExecuteAsync(request, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errorDetail = ex.Message;
            }

            var questionScores = new List<EvaluationScore>
            {
                interpretationScorer.Score(question, response, metadata.RunId),
                clarificationScorer.Score(question, response, metadata.RunId)
            };

            if (question.EvidenceRequirements.Any())
                questionScores.Add(evidenceScorer.Score(question, response, metadata.RunId));

            if (question.Category == EvaluationCategory.ConfidenceProtection)
                questionScores.Add(confidenceScorer.Score(question, response, metadata.RunId));

            if (errorDetail is not null && response is null)
                questionScores.Add(new EvaluationScore(
                    Guid.NewGuid(), metadata.RunId, question.QuestionId,
                    question.Category, EvaluationScoreType.Exact, 0.0, false,
                    $"Execution exception: {errorDetail}"));

            scores.AddRange(questionScores);
            if (questionScores.All(s => s.Passed))
                passedQuestions++;
        }

        var totalQuestions = dataset.Questions.Count;
        var overall = totalQuestions > 0
            ? Math.Round((double)passedQuestions / totalQuestions, 4)
            : 0.0;

        var completedMetadata = metadata with
        {
            CompletedAt = timeProvider.GetUtcNow(),
            Status = EvaluationRunStatus.Completed
        };

        return new EvaluationRunResult(completedMetadata, scores, overall, totalQuestions, passedQuestions);
    }
}

// In-memory run repository used in tests and CI evaluation.
// Replace with a durable implementation at the composition root for production regression history.
public sealed class InMemoryEvaluationRunRepository : IEvaluationRunRepository
{
    private readonly Dictionary<Guid, EvaluationRunResult> _runs = [];

    public Task SaveRunAsync(EvaluationRunResult run, CancellationToken cancellationToken)
    {
        _runs[run.Metadata.RunId] = run;
        return Task.CompletedTask;
    }

    public Task<EvaluationRunResult?> GetRunAsync(Guid runId, CancellationToken cancellationToken) =>
        Task.FromResult(_runs.TryGetValue(runId, out var run) ? run : null);

    public Task<IReadOnlyCollection<EvaluationRunMetadata>> ListRunsAsync(
        Guid datasetId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<EvaluationRunMetadata>>(
            _runs.Values
                .Where(r => r.Metadata.DatasetId == datasetId)
                .Select(r => r.Metadata)
                .OrderByDescending(m => m.StartedAt)
                .ToList());
}

// Phase 1 stub: no prompt versions are recorded yet.
// Replace when prompt template management is implemented.
public sealed class NoOpPromptVersionRegistry : IPromptVersionRegistry
{
    public IReadOnlyCollection<PromptVersion> GetAll() => [];
    public PromptVersion? GetLatest(AiWorkloadKind workload) => null;
    public PromptVersion? GetById(Guid promptVersionId) => null;
}
