using System.Security.Cryptography;
using System.Text;
using FinancialCopilot.Application.Authentication.TahlilApp;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Authentication.TahlilApp;

/// <summary>
/// Local replay guard backed by <c>auth_tahlilapp_launch_attempts</c> (Design §8). Stores only the SHA-256
/// digest of the code. The unique index makes the first insert the only winner, even under concurrency.
/// </summary>
public sealed class TahlilAppLaunchAttemptStore(AuthDbContext dbContext, TimeProvider timeProvider) : ITahlilAppLaunchAttemptStore
{
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromHours(24);

    public async Task<bool> TryBeginAttemptAsync(string code, string correlationId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var codeHash = Hash(code);

        // Bounded, low-volume purge (24 h retention). Load-then-remove keeps it portable across providers.
        var retentionCutoff = now.UtcDateTime - RetentionPeriod;
        var expired = await dbContext.TahlilAppLaunchAttempts
            .Where(row => row.CreatedAtUtc < retentionCutoff)
            .ToArrayAsync(cancellationToken);
        if (expired.Length > 0)
        {
            dbContext.TahlilAppLaunchAttempts.RemoveRange(expired);
        }

        dbContext.TahlilAppLaunchAttempts.Add(new TahlilAppLaunchAttemptRow
        {
            Id = Guid.NewGuid(),
            CodeHash = codeHash,
            CorrelationId = correlationId,
            Outcome = "started",
            CreatedAtUtc = now.UtcDateTime
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            // Distinguish a duplicate code from an unrelated database failure, which must not be treated as replay.
            if (await dbContext.TahlilAppLaunchAttempts.AnyAsync(row => row.CodeHash == codeHash, cancellationToken))
            {
                return false;
            }

            throw;
        }
    }

    public async Task CompleteAttemptAsync(string code, string outcome, CancellationToken cancellationToken)
    {
        var codeHash = Hash(code);
        var row = await dbContext.TahlilAppLaunchAttempts.SingleOrDefaultAsync(candidate => candidate.CodeHash == codeHash, cancellationToken);
        if (row is null)
        {
            return;
        }

        row.Outcome = outcome;
        row.CompletedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string Hash(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();
}
