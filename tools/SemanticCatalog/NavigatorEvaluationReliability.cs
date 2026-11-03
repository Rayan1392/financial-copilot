using System.Diagnostics;

internal enum EvaluationDeadlineState
{
    Completed,
    TimedOut,
    Canceled,
    Faulted
}

internal sealed record EvaluationDeadlineResult<T>(
    EvaluationDeadlineState State,
    T? Value,
    Exception? Exception,
    TimeSpan Elapsed);

internal static class NavigatorEvaluationReliability
{
    public static async Task<EvaluationDeadlineResult<T>> ExecuteWithHardDeadlineAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stopwatch = Stopwatch.StartNew();

        // Isolate synchronous provider setup as well as the returned async operation. The outer
        // deadline deliberately does not depend on a provider observing cancellation.
        Task<T> operationTask = Task.Run(
            async () => await operation(linked.Token).ConfigureAwait(false),
            CancellationToken.None);
        var timeoutTask = Task.Delay(timeout, CancellationToken.None);
        var cancellationSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource)state!).TrySetResult(), cancellationSignal);

        var winner = await Task.WhenAny(operationTask, timeoutTask, cancellationSignal.Task).ConfigureAwait(false);
        if (winner == operationTask)
        {
            try
            {
                var value = await operationTask.ConfigureAwait(false);
                return new(EvaluationDeadlineState.Completed, value, null, stopwatch.Elapsed);
            }
            catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
            {
                return new(EvaluationDeadlineState.Canceled, default, exception, stopwatch.Elapsed);
            }
            catch (OperationCanceledException exception)
            {
                return new(EvaluationDeadlineState.TimedOut, default, exception, stopwatch.Elapsed);
            }
            catch (Exception exception)
            {
                return new(EvaluationDeadlineState.Faulted, default, exception, stopwatch.Elapsed);
            }
        }

        linked.Cancel();
        ObserveLateFault(operationTask);
        return winner == cancellationSignal.Task
            ? new(EvaluationDeadlineState.Canceled, default, null, stopwatch.Elapsed)
            : new(EvaluationDeadlineState.TimedOut, default, null, stopwatch.Elapsed);
    }

    private static void ObserveLateFault<T>(Task<T> task) => _ = task.ContinueWith(
        static completed => _ = completed.Exception,
        CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
        TaskScheduler.Default);
}
