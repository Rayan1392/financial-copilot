namespace FinancialCopilot.Application.Memory;

public sealed class ConsentAwareMemoryProtectionPolicy : IMemoryProtectionPolicy
{
    public MemoryPolicyDecision Authorize(
        MemoryContextRequest request,
        OptionalMemoryRecord memory,
        MemoryConsentPolicy? consent,
        DateTimeOffset evaluatedAt)
    {
        if (request.Subject != memory.Owner)
        {
            return Reject("Memory owner does not match the requesting tenant and subject.");
        }

        if (request.Purpose != memory.Purpose)
        {
            return Reject("Memory purpose does not match the requested orchestration purpose.");
        }

        var requiresConsent = memory.Type != MemoryType.ShortTermConversationMemory ||
            memory.Sensitivity != MemorySensitivity.General;
        if (requiresConsent &&
            (consent is null ||
             consent.TenantId != memory.Owner.TenantId ||
             consent.SubjectId != memory.Owner.SubjectId ||
             consent.MemoryType != memory.Type ||
             consent.Purpose != memory.Purpose ||
             !consent.AllowsUse(evaluatedAt)))
        {
            return Reject("Explicit active consent is required for this optional memory.");
        }

        if (memory.Retention.ExpiresAt is { } expiresAt && expiresAt <= evaluatedAt)
        {
            return Reject("Memory retention period has expired.");
        }

        var providerAllowed = request.PermitProviderPromptContext &&
            memory.Sensitivity is MemorySensitivity.General or MemorySensitivity.PersonalPreference;
        var telemetryAllowed = memory.Sensitivity == MemorySensitivity.General;
        var disclosure = new MemoryUseDisclosure(
            memory.Type,
            memory.Purpose,
            $"Optional {memory.Type} influenced this answer for {memory.Purpose}.");

        return new MemoryPolicyDecision(
            Authorized: true,
            MayBeIncludedInProviderPrompt: providerAllowed,
            MayBeExportedToTelemetry: telemetryAllowed,
            Reason: "Optional memory is authorized under the requested scope.",
            Disclosure: disclosure);
    }

    private static MemoryPolicyDecision Reject(string reason) =>
        new(false, false, false, reason, null);
}

// Phase 1 has Conversation/Message history only. This implementation prevents
// optional personalized memory from silently entering AI orchestration.
public sealed class DisabledMemoryContextProvider : IMemoryContextProvider
{
    public Task<AuthorizedMemoryContext> GetAuthorizedContextAsync(
        MemoryContextRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new AuthorizedMemoryContext([], [], OptionalMemoryEnabled: false));
}

public sealed class DisabledMemoryConsentService : IMemoryConsentService
{
    public Task<MemoryConsentPolicy?> GetConsentAsync(
        MemorySubject subject,
        MemoryType type,
        MemoryPurpose purpose,
        CancellationToken cancellationToken) =>
        Task.FromResult<MemoryConsentPolicy?>(null);

    public Task<MemoryConsentPolicy> GrantAsync(
        MemoryConsentPolicy policy,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Optional durable memory is not enabled in Phase 1.");

    public Task RevokeAsync(
        MemorySubject subject,
        MemoryType type,
        MemoryPurpose purpose,
        string policyVersion,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Optional durable memory is not enabled in Phase 1.");

}

public sealed class DisabledMemoryAuditService : IMemoryAuditService
{
    public Task RecordAsync(MemoryAuditEvent auditEvent, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Optional durable memory auditing is not enabled in Phase 1.");
}

public sealed class DisabledMemoryControlService : IMemoryControlService
{
    public Task<IReadOnlyCollection<OptionalMemoryRecord>> InspectAsync(
        MemorySubject subject,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<OptionalMemoryRecord>>([]);

    public Task<Guid> WriteAsync(
        MemorySubject owner, MemoryType type, MemoryPurpose purpose, MemorySensitivity sensitivity,
        string summary, MemoryProvenance provenance, MemoryRetentionPolicy? retention,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Optional durable memory is not enabled in Phase 1.");

    public Task DeleteAsync(MemoryDeletionRequest request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Optional durable memory is not enabled in Phase 1.");

    public Task DeleteAllAsync(MemorySubject subject, string correlationId, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Optional durable memory is not enabled in Phase 1.");
}
