using FinancialCopilot.Application.Authentication.TahlilApp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Authentication.TahlilApp;

/// <summary>
/// Background entitlement revalidation (Design §11 trigger 3, T-21). Polls for paid identities whose due time has
/// passed, claims them under a lease, refreshes each through <see cref="ITahlilAppEntitlementRefresher"/>
/// (the same rules as login) and releases the lease. Runs only when both <c>Authentication:TahlilApp:Enabled</c>
/// and <c>Authentication:TahlilApp:Revalidation:Enabled</c> are true. Registered in the TahlilApp composition root only.
/// </summary>
public sealed class TahlilAppEntitlementRevalidationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<TahlilAppOptions> options,
    TimeProvider timeProvider,
    ILogger<TahlilAppEntitlementRevalidationWorker> logger) : BackgroundService
{
    // Unique per process and per worker instance; the lease is held only while this owner refreshes an identity.
    private readonly string _leaseOwner = $"{Environment.ProcessId}-{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled || !settings.Revalidation.Enabled)
        {
            return;
        }

        logger.LogInformation(
            "TahlilApp entitlement revalidation started. PollSeconds: {PollSeconds}; BatchSize: {BatchSize}",
            settings.Revalidation.PollSeconds, settings.Revalidation.BatchSize);

        var pollInterval = TimeSpan.FromSeconds(settings.Revalidation.PollSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Type only: the message can carry data from the failing query.
                logger.LogError("TahlilApp revalidation cycle failed. ExceptionType: {ExceptionType}", exception.GetType().Name);
            }

            try
            {
                await Task.Delay(pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>One poll cycle: claim a batch, refresh each identity, release its lease. Returns the number claimed.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var revalidation = options.Value.Revalidation;
        var now = timeProvider.GetUtcNow();

        IReadOnlyList<TahlilAppDueIdentity> due;
        await using (var claimScope = scopeFactory.CreateAsyncScope())
        {
            var store = claimScope.ServiceProvider.GetRequiredService<ITahlilAppEntitlementStateStore>();
            due = await store.ClaimDueAsync(
                _leaseOwner,
                now,
                now.AddSeconds(revalidation.LeaseSeconds),
                revalidation.BatchSize,
                cancellationToken);
        }

        foreach (var identity in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RefreshOneAsync(identity, revalidation.LeaseSeconds, cancellationToken);
        }

        return due.Count;
    }

    private async Task RefreshOneAsync(TahlilAppDueIdentity identity, int leaseSeconds, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ITahlilAppEntitlementStateStore>();
        try
        {
            // Earlier identities in the batch may have taken longer than the lease, so renew it for this one.
            await store.ExtendLeaseAsync(
                identity.LocalUserId,
                _leaseOwner,
                timeProvider.GetUtcNow().AddSeconds(leaseSeconds),
                cancellationToken);

            var refresher = scope.ServiceProvider.GetRequiredService<ITahlilAppEntitlementRefresher>();
            await refresher.RefreshIfDueAsync(identity.TenantId, identity.LocalUserId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                "TahlilApp revalidation of one identity failed. ExceptionType: {ExceptionType}; LocalUserId: {LocalUserId}",
                exception.GetType().Name, identity.LocalUserId);
        }
        finally
        {
            try
            {
                await store.ReleaseLeaseAsync(identity.LocalUserId, _leaseOwner, CancellationToken.None);
            }
            catch (Exception exception)
            {
                // The lease expires on its own; a failed release must not mask the refresh outcome.
                logger.LogWarning(
                    "TahlilApp lease release failed; the lease will expire. ExceptionType: {ExceptionType}; LocalUserId: {LocalUserId}",
                    exception.GetType().Name, identity.LocalUserId);
            }
        }
    }
}
