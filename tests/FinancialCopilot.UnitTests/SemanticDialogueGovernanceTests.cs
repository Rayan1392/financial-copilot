using FinancialCopilot.Application.AI.Evaluation;
using FinancialCopilot.Application.AI.Orchestration;

namespace FinancialCopilot.UnitTests;

public sealed class SemanticDialogueGovernanceTests
{
    [Fact]
    public void SemanticEvents_AreBoundedDeduplicatedAndAggregatedByCapabilityVersionAndChannel()
    {
        var sink = new BoundedSemanticDialogueEventSink(TimeProvider.System);
        var semanticEvent = new SemanticDialogueEvent(SemanticEventName.ProviderOrToolFailure,
            "correlation-1", "monthly_activity_trend", 1, "provider_or_tool_failure",
            "web-ai", DateTimeOffset.UtcNow, "Failed");

        sink.Record(semanticEvent);
        sink.Record(semanticEvent);
        sink.Record(semanticEvent with { CorrelationId = new string('x', 201) });

        var metric = Assert.Single(new SemanticDialogueMetricsQuery(sink).GetSnapshot());
        Assert.Single(sink.Snapshot());
        Assert.Equal("monthly_activity_trend", metric.CapabilityCode);
        Assert.Equal(1, metric.Failures);
        Assert.Contains(new SemanticDialogueMetricsQuery(sink).GetAlerts(), alert => alert.AlertCode == "failure_rate");
    }

    [Fact]
    public void OfflineRunner_IsDeterministicAndReportsSemanticDifferences()
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var runner = new SemanticOfflineRegressionRunner(new DeterministicCapabilityInterpreter(registry));
        var evaluation = new SemanticEvaluationCase("trend-en", 1, "monthly sales chart FOLD", "en",
            "monthly_activity_trend", new Dictionary<QuerySlotType, string>(), DialogueOutcome.Answered,
            DialogueOutcomeReasonCodes.None, ["monthly_activity_trend"], ["symbol_metric_lookup"], 1);
        Assert.True(runner.Run(evaluation).Passed);
    }

    [Fact]
    public void VersionedDataset_CoversEveryExecutableRouteAndPassesOfflineRegression()
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var runner = new SemanticOfflineRegressionRunner(new DeterministicCapabilityInterpreter(registry), registry);
        var cases = SemanticEvaluationDatasetCatalog.Create();

        var results = cases.Select(runner.Run).ToArray();

        Assert.All(results, result => Assert.True(result.Passed, string.Join("; ", result.Failures)));
        var covered = cases.SelectMany(item => item.RequiredExecutors).Distinct(StringComparer.Ordinal).ToArray();
        Assert.All(registry.GetEnabled(),
            definition => Assert.Contains(definition.Code, covered));
        Assert.Contains(cases, item => item.Channel == "telegram");
        Assert.Contains(cases, item => item.SecurityCase);
        Assert.Contains(cases, item => item.InputSlotProvenance?.Values.Contains(QueryValueProvenance.ConversationInferred) == true);
        Assert.Contains(cases, item => item.ExpectedPayloadInvariants is { Count: > 0 });
        Assert.Contains(cases, item => item.ExpectedBillingReservations == 0);
        Assert.Contains(cases, item => item.ExpectedBillingReservations == 1);
        Assert.Contains(cases, item => item.ExpectedOutcome == DialogueOutcome.NoData);
        Assert.Contains(cases, item => item.ExpectedOutcome == DialogueOutcome.DisambiguationNeeded);
        Assert.Contains(cases, item => item.ExpectedOutcome == DialogueOutcome.TemporarilyUnavailable);
        Assert.Contains(cases, item => item.ExpectedOutcome == DialogueOutcome.Failed);
    }

    [Fact]
    public void CandidatePromotion_RequiresSupportDistinctActorsAndHumanApproval()
    {
        var policy = new SemanticPhraseCandidatePolicy();
        var now = DateTimeOffset.UtcNow;
        var evidence = new[]
        {
            new SemanticPhraseEvidence("a", "sales curve", "monthly_activity_trend", now),
            new SemanticPhraseEvidence("b", "sales curve", "monthly_activity_trend", now),
            new SemanticPhraseEvidence("a", "sales curve", "monthly_activity_trend", now)
        };
        var candidate = policy.Propose(PhraseCandidateType.CapabilityAlias, "sales curve", "monthly_activity_trend", evidence, 2);
        var approved = policy.Approve(candidate, "reviewer", "validated by regression suite",
            InitialConversationalCapabilityCatalog.Create().ToArray(),
            new SemanticPhrasePromotionEvidence("ci-123", true, "all semantic regression cases passed"));
        Assert.Equal(PhraseCandidateStatus.Approved, approved.Status);
        Assert.True(approved.RollbackAvailable);
        Assert.Equal("ci-123", approved.ApprovalCiRunId);
        Assert.NotNull(approved.ApprovedAt);

        var active = policy.Activate(approved, 10, now);
        Assert.Equal(PhraseCandidateStatus.Active, active.Status);
        Assert.Equal(10, active.CanaryPercentage);
        Assert.Equal(PhraseCandidateStatus.RolledBack, policy.Rollback(active).Status);
    }

    [Fact]
    public void CollisionGate_RejectsAliasOwnedByAnotherCapability()
    {
        var policy = new SemanticPhraseCandidatePolicy();
        var candidate = new SemanticPhraseCandidate(Guid.NewGuid(), PhraseCandidateType.CapabilityAlias,
            "stock analysis", "monthly_activity_trend", 3, 2, PhraseCandidateStatus.Proposed, null, null, 2, true);
        Assert.Throws<InvalidOperationException>(() => policy.Approve(candidate, "reviewer", "reason",
            InitialConversationalCapabilityCatalog.Create().ToArray(),
            new SemanticPhrasePromotionEvidence("ci-123", true, "passed")));
    }

    [Fact]
    public void CollisionGate_RejectsMetricAndEntityVocabulary()
    {
        var policy = new SemanticPhraseCandidatePolicy();
        var candidate = new SemanticPhraseCandidate(Guid.NewGuid(), PhraseCandidateType.CapabilityAlias,
            "FOLD", "monthly_activity_trend", 3, 2, PhraseCandidateStatus.Proposed, null, null, 2, true);
        var evidence = new SemanticPhrasePromotionEvidence("ci-1", true, "regression passed",
            MetricVocabulary: ["P/E"], EntityVocabulary: ["FOLD"]);

        Assert.Throws<InvalidOperationException>(() => policy.Approve(candidate, "reviewer", "reason",
            InitialConversationalCapabilityCatalog.Create().ToArray(), evidence));
    }

    [Fact]
    public void ApprovalGate_RejectsMissingOrFailedRegressionEvidence()
    {
        var candidate = new SemanticPhraseCandidate(Guid.NewGuid(), PhraseCandidateType.CapabilityAlias,
            "sales curve", "monthly_activity_trend", 3, 2, PhraseCandidateStatus.Proposed, null, null, 2, true);
        var policy = new SemanticPhraseCandidatePolicy();
        var capabilities = InitialConversationalCapabilityCatalog.Create().ToArray();

        Assert.Throws<InvalidOperationException>(() => policy.Approve(candidate, "reviewer", "reason", capabilities,
            new SemanticPhrasePromotionEvidence("ci-failed", false, "one route failed")));
        Assert.Throws<InvalidOperationException>(() => policy.Approve(candidate, "reviewer", "reason", capabilities,
            new SemanticPhrasePromotionEvidence(string.Empty, true, "passed")));
    }

    [Fact]
    public void CompletionEvidence_RejectsShortCanaryOrUnsafeRates()
    {
        Assert.Throws<InvalidOperationException>(() => SemanticCompletionEvidencePolicy.Validate(
            new("ci-1", "dashboard-query", TimeSpan.FromHours(2), 0, 0, 0)));
        SemanticCompletionEvidencePolicy.Validate(
            new("ci-2", "dashboard-query", TimeSpan.FromHours(24), .01m, .01m, .001m));
    }
}
