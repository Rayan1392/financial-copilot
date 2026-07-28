using FinancialCopilot.Infrastructure.Authentication.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics.Metrics;

namespace FinancialCopilot.Infrastructure.Authentication;

public sealed class TelegramMembershipRevalidationProcessor(
    IServiceScopeFactory scopeFactory,
    IOptions<TelegramMembershipRevalidationOptions> options,
    TimeProvider timeProvider,
    ILogger<TelegramMembershipRevalidationProcessor> logger)
{
    private static readonly Meter Meter = new("FinancialCopilot.TelegramMembership");
    private static readonly Counter<long> RevalidationCounter = Meter.CreateCounter<long>("telegram.membership.revalidations");
    private static readonly Counter<long> DeadLetterCounter = Meter.CreateCounter<long>("telegram.membership.revalidation_dead_letters");

    public async Task<int> ProcessDueAsync(string leaseOwner, CancellationToken cancellationToken)
    {
        var candidates = await AcquireDueAsync(leaseOwner, cancellationToken);
        if (candidates.Count == 0)
        {
            return 0;
        }

        await Parallel.ForEachAsync(
            candidates,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Math.Max(1, options.Value.MaxConcurrency)
            },
            async (candidate, token) => await ProcessCandidateAsync(candidate, leaseOwner, token));

        return candidates.Count;
    }

    private async Task<List<RevalidationCandidate>> AcquireDueAsync(string leaseOwner, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var authDbContext = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var settings = options.Value;
        var now = timeProvider.GetUtcNow();
        var leaseExpiresAt = now.AddSeconds(Math.Max(30, settings.LeaseSeconds));

        var rows = await authDbContext.TelegramMembershipRevalidations
            .Where(row =>
                row.DeadLetteredAtUtc == null &&
                row.NextDueAtUtc <= now &&
                (row.LeaseExpiresAtUtc == null || row.LeaseExpiresAtUtc <= now))
            .OrderBy(row => row.NextDueAtUtc)
            .Take(Math.Max(1, settings.BatchSize))
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        foreach (var row in rows)
        {
            row.LeaseOwner = leaseOwner;
            row.LeaseExpiresAtUtc = leaseExpiresAt;
            row.LastAttemptedAtUtc = now;
        }

        await authDbContext.SaveChangesAsync(cancellationToken);
        return rows.Select(row => new RevalidationCandidate(
            row.Id,
            row.ActorId,
            row.TenantId,
            row.ChannelId,
            row.AttemptCount)).ToList();
    }

    private async Task ProcessCandidateAsync(
        RevalidationCandidate candidate,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var authDbContext = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var membershipService = scope.ServiceProvider.GetRequiredService<TelegramMembershipService>();
        var settings = options.Value;
        var now = timeProvider.GetUtcNow();
        var row = await authDbContext.TelegramMembershipRevalidations.SingleOrDefaultAsync(
            current => current.Id == candidate.Id,
            cancellationToken);
        if (row is null || row.LeaseOwner != leaseOwner)
        {
            return;
        }

        try
        {
            var correlationId = $"telegram-revalidation:{candidate.Id:N}:{Guid.NewGuid():N}";
            var result = await membershipService.RevalidateLatestMembershipAsync(
                candidate.ActorId,
                candidate.TenantId,
                correlationId,
                cancellationToken);

            if (result.Status == Domain.Identity.Telegram.TelegramChannelMembershipStatus.UnknownProviderFailure)
            {
                var attempts = candidate.AttemptCount + 1;
                var deadLetter = attempts >= Math.Max(1, settings.RetryCount);
                row.AttemptCount = attempts;
                row.LastFailureCategory = result.FailureCategory.ToString();
                row.LastError = $"Provider failure: {result.FailureCategory}";
                row.NextDueAtUtc = now.AddSeconds(ComputeBackoffSeconds(attempts));
                row.DeadLetteredAtUtc = deadLetter ? now : null;
                if (deadLetter)
                {
                    DeadLetterCounter.Add(1, new KeyValuePair<string, object?>("failureCategory", result.FailureCategory.ToString()));
                }
            }
            else
            {
                row.TelegramUserId = row.TelegramUserId;
                row.AttemptCount = 0;
                row.LastFailureCategory = null;
                row.LastError = null;
                row.DeadLetteredAtUtc = null;
                row.NextDueAtUtc = result.ValidUntilUtc;
            }

            row.CorrelationId = correlationId;
            row.LeaseOwner = null;
            row.LeaseExpiresAtUtc = null;
            row.LastAttemptedAtUtc = now;
            await authDbContext.SaveChangesAsync(cancellationToken);
            RevalidationCounter.Add(
                1,
                new KeyValuePair<string, object?>("status", result.Status.ToString()),
                new KeyValuePair<string, object?>("eligible", result.IsEligible));
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
        {
            var attempts = candidate.AttemptCount + 1;
            var deadLetter = attempts >= Math.Max(1, settings.RetryCount);
            row.AttemptCount = attempts;
            row.LastFailureCategory = "ProcessorFailure";
            row.LastError = exception.Message;
            row.NextDueAtUtc = now.AddSeconds(ComputeBackoffSeconds(attempts));
            row.DeadLetteredAtUtc = deadLetter ? now : null;
            row.LeaseOwner = null;
            row.LeaseExpiresAtUtc = null;
            row.LastAttemptedAtUtc = now;
            await authDbContext.SaveChangesAsync(cancellationToken);
            if (deadLetter)
            {
                DeadLetterCounter.Add(1, new KeyValuePair<string, object?>("failureCategory", "ProcessorFailure"));
            }

            logger.LogWarning(
                exception,
                "Telegram membership revalidation failed for actor {ActorId} tenant {TenantId}.",
                candidate.ActorId,
                candidate.TenantId);
        }
    }

    private int ComputeBackoffSeconds(int attempts)
    {
        var settings = options.Value;
        var initial = Math.Max(1, settings.InitialBackoffSeconds);
        var max = Math.Max(initial, settings.MaxBackoffSeconds);
        var exponent = Math.Max(0, attempts - 1);
        var seconds = initial * Math.Pow(2, exponent);
        return (int)Math.Min(max, seconds);
    }

    private sealed record RevalidationCandidate(
        Guid Id,
        Guid ActorId,
        Guid TenantId,
        string ChannelId,
        int AttemptCount);
}
