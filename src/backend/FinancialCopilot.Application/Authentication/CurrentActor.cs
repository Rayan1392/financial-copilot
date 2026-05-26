namespace FinancialCopilot.Application.Authentication;

public sealed record CurrentActor(
    Guid TenantId,
    AuthenticationMode AuthenticationMode,
    Guid? UserId = null,
    Guid? ApiClientId = null);
