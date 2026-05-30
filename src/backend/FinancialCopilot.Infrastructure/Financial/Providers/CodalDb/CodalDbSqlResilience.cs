using FinancialCopilot.Application.FinancialData.Providers;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Providers.CodalDb;

/// <summary>
/// Small explicit resilience wrapper for CodalDB SQL command execution: bounded transient-error
/// retry with linear backoff, and terminal-failure mapping to <see cref="FinancialProviderException"/>
/// so a raw <see cref="SqlException"/> never surfaces to the Application layer. Per-command timeout
/// is applied on the <see cref="SqlCommand"/> itself by the caller.
/// </summary>
public sealed class CodalDbSqlResilience(
    IOptions<CodalDbProviderOptions> options,
    ILogger<CodalDbSqlResilience> logger)
{
    private readonly CodalDbProviderOptions _options = options.Value;

    // SQL Server transient error numbers (timeouts, deadlocks, transport, throttling).
    private static readonly HashSet<int> TransientErrorNumbers =
    [
        -2, 20, 64, 121, 233, 1205, 4060, 10053, 10054, 10060, 10928, 10929,
        40197, 40501, 40613, 49918, 49919, 49920
    ];

    public async Task<T> ExecuteAsync<T>(
        string operation,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(0, _options.RetryCount) + 1;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action(cancellationToken);
            }
            catch (SqlException exception) when (IsTransient(exception) && attempt < maxAttempts)
            {
                var delay = TimeSpan.FromMilliseconds(_options.RetryBaseDelayMilliseconds * attempt);
                logger.LogWarning(
                    exception,
                    "Transient CodalDb error during {Operation} (attempt {Attempt}/{MaxAttempts}); retrying in {DelayMs}ms.",
                    operation,
                    attempt,
                    maxAttempts,
                    delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }
            catch (SqlException exception)
            {
                throw new FinancialProviderException(MapError(exception), $"CodalDb {operation} failed.", exception);
            }
            catch (Exception exception)
                when (exception is not OperationCanceledException and not FinancialProviderException)
            {
                throw new FinancialProviderException(
                    FinancialProviderErrorCode.RemoteUnavailable,
                    $"CodalDb {operation} failed.",
                    exception);
            }
        }
    }

    private static bool IsTransient(SqlException exception) =>
        exception.Errors.Cast<SqlError>().Any(error => TransientErrorNumbers.Contains(error.Number));

    private static FinancialProviderErrorCode MapError(SqlException exception) =>
        exception.Number switch
        {
            -2 => FinancialProviderErrorCode.Timeout,
            18456 or 18452 or 4060 => FinancialProviderErrorCode.Unauthorized,
            _ => FinancialProviderErrorCode.RemoteUnavailable
        };
}
