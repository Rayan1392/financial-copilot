using FinancialCopilot.Application.Authentication.TahlilApp;
using FinancialCopilot.Infrastructure.Authentication;

namespace FinancialCopilot.Infrastructure.Authentication.TahlilApp;

/// <summary>Adapter: issues the Sapino session through the shared owned-identity session path (Design §12).</summary>
public sealed class TahlilAppSessionIssuer(IOwnedIdentityService identityService) : ITahlilAppSessionIssuer
{
    public async Task<AuthenticatedSessionTokens> IssueAsync(
        Guid localUserId,
        Guid tenantId,
        string? presentedRefreshToken,
        CancellationToken cancellationToken)
    {
        var session = await identityService.CreateSessionForLinkedUserAsync(
            localUserId,
            tenantId,
            OwnedIdentityService.TahlilAppAuthenticationMethod,
            presentedRefreshToken,
            cancellationToken);

        return new AuthenticatedSessionTokens(
            session.AccessToken,
            session.AccessTokenExpiresAt,
            session.RefreshToken,
            session.RefreshTokenExpiresAt,
            session.Profile.UserId,
            session.Profile.TenantId,
            session.Profile.Email,
            session.Profile.Roles,
            session.Profile.Permissions);
    }
}
