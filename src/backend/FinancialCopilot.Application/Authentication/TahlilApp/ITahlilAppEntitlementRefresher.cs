namespace FinancialCopilot.Application.Authentication.TahlilApp;

/// <summary>
/// Refreshes TahlilApp-sourced entitlement state for one local user when that state is due
/// (Design §11 refresh trigger 2: direct Sapino login after the status TTL).
/// Implementations must never block or fail the caller on external unavailability:
/// they keep the last known state within the stale-grace rule and return normally.
/// </summary>
public interface ITahlilAppEntitlementRefresher
{
    Task RefreshIfDueAsync(Guid tenantId, Guid localUserId, CancellationToken cancellationToken);
}
