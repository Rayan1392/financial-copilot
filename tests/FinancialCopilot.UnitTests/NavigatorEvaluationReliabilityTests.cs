using System.Diagnostics;
using System.Text.Json.Nodes;

public sealed class NavigatorEvaluationReliabilityTests
{
    [Fact]
    public async Task HardDeadline_ReturnsTimeout_WhenOperationIgnoresCancellation()
    {
        var never = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopwatch = Stopwatch.StartNew();

        var result = await NavigatorEvaluationReliability.ExecuteWithHardDeadlineAsync(
            _ => never.Task,
            TimeSpan.FromMilliseconds(40));

        Assert.Equal(EvaluationDeadlineState.TimedOut, result.State);
        Assert.Equal(0, result.Value);
        Assert.Null(result.Exception);
        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromMilliseconds(20), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task HardDeadline_ReturnsFault_WithoutLosingException()
    {
        var expected = new InvalidOperationException("deterministic failure");

        var result = await NavigatorEvaluationReliability.ExecuteWithHardDeadlineAsync<int>(
            _ => Task.FromException<int>(expected),
            TimeSpan.FromSeconds(1));

        Assert.Equal(EvaluationDeadlineState.Faulted, result.State);
        Assert.Same(expected, result.Exception);
    }

    [Fact]
    public async Task HardDeadline_DistinguishesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(40));
        var never = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var result = await NavigatorEvaluationReliability.ExecuteWithHardDeadlineAsync(
            _ => never.Task,
            TimeSpan.FromSeconds(2),
            cancellation.Token);

        Assert.Equal(EvaluationDeadlineState.Canceled, result.State);
    }

    [Fact]
    public async Task AttemptPersistence_ReconcilesInterruptedRow_AsSingleTerminalRecord()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"navigator-evaluation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "results.jsonl");
            var rows = new Dictionary<string, JsonObject>(StringComparer.Ordinal)
            {
                ["case-1"] = new JsonObject
                {
                    ["caseId"] = "case-1",
                    ["status"] = "running",
                    ["attemptId"] = "attempt-1",
                    ["attemptTimestampUtc"] = DateTimeOffset.UtcNow
                }
            };

            await NavigatorEvaluationCli.PersistEvaluationRowsAsync(path, rows);
            var reloaded = await NavigatorEvaluationCli.ReadEvaluationRowsAsync(path);
            Assert.Equal(1, NavigatorEvaluationCli.ReconcileInterruptedRows(reloaded));
            await NavigatorEvaluationCli.PersistEvaluationRowsAsync(path, reloaded);

            var lines = await File.ReadAllLinesAsync(path);
            var row = Assert.Single(lines);
            var parsed = JsonNode.Parse(row)!.AsObject();
            Assert.Equal("failed", parsed["status"]!.GetValue<string>());
            Assert.Equal("Canceled", parsed["terminalStatus"]!.GetValue<string>());
            Assert.Equal("interrupted_process", parsed["errorCategory"]!.GetValue<string>());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
