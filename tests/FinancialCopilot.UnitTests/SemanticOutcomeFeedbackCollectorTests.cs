using FinancialCopilot.Application.AI.Evaluation;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Scanner;

namespace FinancialCopilot.UnitTests;

public sealed class SemanticOutcomeFeedbackCollectorTests
{
    [Theory]
    [InlineData(CapabilityExecutionStatus.NoData)]
    [InlineData(CapabilityExecutionStatus.Partial)]
    [InlineData(CapabilityExecutionStatus.ClarificationRequired)]
    [InlineData(CapabilityExecutionStatus.DisambiguationRequired)]
    [InlineData(CapabilityExecutionStatus.Unsupported)]
    [InlineData(CapabilityExecutionStatus.TemporarilyUnavailable)]
    [InlineData(CapabilityExecutionStatus.Failed)]
    public async Task EveryNonSuccessSemanticOutcome_IsCollectedWithoutChangingTheResponse(
        CapabilityExecutionStatus status)
    {
        var inner = new RecordingCollector();
        var collector = new SemanticOutcomeFeedbackCollector(inner);

        await collector.TryCollectAsync(Request(), Frame(),
            new CapabilityExecutionResult("monthly_activity_trend", 1, status, "reason"),
            DateTimeOffset.UtcNow, default);

        Assert.Single(inner.Requests);
        Assert.StartsWith("semantic:monthly_activity_trend:", inner.Requests[0].Context);
    }

    [Fact]
    public async Task FeedbackFailure_IsIsolatedAndExecutedOutcomeIsSkipped()
    {
        var throwing = new RecordingCollector(throwOnCollect: true);
        var collector = new SemanticOutcomeFeedbackCollector(throwing);

        await collector.TryCollectAsync(Request(), Frame(),
            new CapabilityExecutionResult("monthly_activity_trend", 1, CapabilityExecutionStatus.Failed, "failure"),
            DateTimeOffset.UtcNow, default);
        await collector.TryCollectAsync(Request(), Frame(),
            new CapabilityExecutionResult("monthly_activity_trend", 1, CapabilityExecutionStatus.Executed, "none"),
            DateTimeOffset.UtcNow, default);

        Assert.Equal(1, throwing.CallCount);
    }

    private static AiQueryRequest Request() => new("trend", Guid.NewGuid(), Guid.NewGuid(), "feedback-test");
    private static ValidatedQueryFrame Frame() => new("monthly_activity_trend", 1,
        [new(QuerySlotType.CompanyOrSymbol, "FOLD", QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid)],
        new("trend", "trend", "en", [], [], [], null, null, null, [], [], 1m, [], 1));

    private sealed class RecordingCollector(bool throwOnCollect = false) : IMissingAnswerFeedbackCollector
    {
        public List<MissingAnswerFeedbackRequest> Requests { get; } = [];
        public int CallCount { get; private set; }
        public Task CollectAsync(MissingAnswerFeedbackRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (throwOnCollect) throw new InvalidOperationException("feedback unavailable");
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }
}
