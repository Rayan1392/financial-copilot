namespace FinancialCopilot.Application.Authentication;

public sealed record CurrentActor(
    ActorType ActorType,
    Guid ActorId,
    Guid TenantId,
    AuthenticationMode AuthenticationMode,
    Guid? UserId = null,
    Guid? ApiClientId = null);
