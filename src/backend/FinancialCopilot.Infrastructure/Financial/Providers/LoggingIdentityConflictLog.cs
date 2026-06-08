using System.Collections.Concurrent;
using FinancialCopilot.Application.FinancialData.Providers;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Providers;

/// <summary>
/// Structured-logging <see cref="IIdentityConflictLog"/> for cross-source identity disagreements
/// (spec 051 AC #9). Conflicts are emitted as warnings and coalesced by (kind, value, source pair) so
/// a repeated disagreement across many rows in one process does not flood the log (Release It!:
/// bounded log output, no retry-storm spam). Recording never throws: a logging failure must not break
/// an ingestion run, so exceptions are swallowed.
/// </summary>
public sealed class LoggingIdentityConflictLog(ILogger<LoggingIdentityConflictLog> logger) : IIdentityConflictLog
{
    // Bounded de-dup set; capped so an unbounded variety of conflicts cannot grow memory without limit.
    private const int MaxTrackedKeys = 10_000;
    private readonly ConcurrentDictionary<string, byte> _seen = new();

    public Task RecordAsync(IdentityConflict conflict, CancellationToken cancellationToken)
    {
        try
        {
            var key = $"{conflict.IdentifierKind}|{conflict.IdentifierValue}|{conflict.ExistingSourceName}|{conflict.IncomingSourceName}";
            if (_seen.Count < MaxTrackedKeys && !_seen.TryAdd(key, 0))
            {
                return Task.CompletedTask;
            }

            logger.LogWarning(
                "Identity conflict on {IdentifierKind}={IdentifierValue}: existing source {ExistingSource} value '{ExistingValue}' " +
                "disagrees with incoming source {IncomingSource} value '{IncomingValue}'. {Detail}",
                conflict.IdentifierKind,
                conflict.IdentifierValue,
                conflict.ExistingSourceName,
                conflict.ExistingValue,
                conflict.IncomingSourceName,
                conflict.IncomingValue,
                conflict.Detail);
        }
        catch (Exception exception)
        {
            // Swallow — identity-conflict logging is best-effort and must never fail an ingestion run.
            try
            {
                logger.LogDebug(exception, "Failed to record identity conflict.");
            }
            catch
            {
                // ignored
            }
        }

        return Task.CompletedTask;
    }
}
