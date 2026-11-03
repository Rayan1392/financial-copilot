using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Semantics.Navigation;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Workflow;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Temporary chat billing policy for Feature 141: one credit only for an answer with verified rows; NoData, Ambiguous,
/// Unsupported, refused and failed results settle with a zero-charge completion status.
/// </summary>
public sealed class Feature141ChatBillingClassificationTests
{
    private static readonly IReadOnlyDictionary<string, string> Row = new Dictionary<string, string> { ["Value"] = "1" };

    private static PersianStructuredQueryResponse Response(StructuredAnswerStatus status, int rows) =>
        new(status, "متن", null, null, [], [], Enumerable.Repeat(Row, rows).ToArray(), []);

    [Theory]
    [InlineData(StructuredAnswerStatus.Answered, 1, true)]
    // A disclosed partial ranking or discovery list carries rows and is an answer.
    [InlineData(StructuredAnswerStatus.Incomplete, 2, true)]
    [InlineData(StructuredAnswerStatus.Incomplete, 0, false)]
    [InlineData(StructuredAnswerStatus.NoData, 0, false)]
    [InlineData(StructuredAnswerStatus.Ambiguous, 0, false)]
    [InlineData(StructuredAnswerStatus.Unsupported, 0, false)]
    [InlineData(StructuredAnswerStatus.Failed, 0, false)]
    public void OnlyAResultWithRowsIsChargedAsAnAnswer(StructuredAnswerStatus status, int rows, bool charged)
    {
        var answer = Feature141ChatQueryService.Definitive(Response(status, rows));

        Assert.True(answer.Definitive);
        Assert.Equal(charged, answer.Answered);
        Assert.Equal(charged ? "Completed" : "ValidationFailed", answer.CompletionStatus);
        Assert.Equal(DialogueOutcome.Answered, answer.Outcome);
    }

    [Fact]
    public void RefusalsAndPendingResultsAreNeverCharged()
    {
        var refused = new Feature141ChatAnswer("پشتیبانی نمی‌شود", Definitive: true, Refused: true);
        var pending = new Feature141ChatAnswer("لطفاً دوره را مشخص کنید", Definitive: false);

        Assert.Equal("ValidationFailed", refused.CompletionStatus);
        Assert.Equal(DialogueOutcome.Unsupported, refused.Outcome);
        Assert.Equal("ClarificationRequired", pending.CompletionStatus);
    }
}
