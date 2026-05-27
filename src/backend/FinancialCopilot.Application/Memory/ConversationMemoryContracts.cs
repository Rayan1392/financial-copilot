namespace FinancialCopilot.Application.Memory;

public enum MemoryType
{
    ShortTermConversationMemory,
    LongTermUserMemory,
    PortfolioAwareMemory,
    PreferenceMemory,
    ResearchMemory,
    WatchlistMemory
}

public enum MemoryPurpose
{
    CurrentConversationContinuity,
    Personalization,
    PortfolioInsight,
    ResearchContinuation,
    WatchlistContext
}

public enum MemorySensitivity
{
    General,
    PersonalPreference,
    SensitiveFinancial,
    RestrictedSecret
}

public enum MemoryConsentStatus
{
    NotRequired,
    Granted,
    Denied,
    Revoked,
    Expired,
    NotFound
}

public enum MemoryAuditAction
{
    Retrieved,
    UsedInAnswer,
    Inspected,
    ConsentGranted,
    ConsentRevoked,
    Deleted,
    RejectedByPolicy
}

public sealed record MemorySubject(
    Guid TenantId,
    Guid SubjectId);

public sealed record MemoryRetentionPolicy(
    DateTimeOffset? ExpiresAt,
    bool InspectableBySubject,
    bool DeletableBySubject);

public sealed record MemoryProvenance(
    string SourceType,
    string? AuthoritativeRecordReference,
    DateTimeOffset CapturedAt);

public sealed record MemoryConsentPolicy(
    Guid TenantId,
    Guid SubjectId,
    MemoryType MemoryType,
    MemoryPurpose Purpose,
    MemoryConsentStatus Status,
    DateTimeOffset? GrantedAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? ExpiresAt,
    string PolicyVersion)
{
    public bool AllowsUse(DateTimeOffset at) =>
        Status == MemoryConsentStatus.Granted &&
        RevokedAt is null &&
        (ExpiresAt is null || ExpiresAt > at);
}

public sealed record OptionalMemoryRecord(
    Guid MemoryId,
    MemorySubject Owner,
    MemoryType Type,
    MemoryPurpose Purpose,
    MemorySensitivity Sensitivity,
    string MemoryVersion,
    string PolicyVersion,
    string Summary,
    MemoryProvenance Provenance,
    MemoryRetentionPolicy Retention);

public sealed record MemoryContextRequest(
    MemorySubject Subject,
    Guid? ConversationId,
    MemoryPurpose Purpose,
    string CorrelationId,
    bool PermitProviderPromptContext = false);

public sealed record AuthorizedMemoryContext(
    IReadOnlyCollection<OptionalMemoryRecord> Items,
    IReadOnlyCollection<MemoryUseDisclosure> Disclosures,
    bool OptionalMemoryEnabled);

public sealed record MemoryUseDisclosure(
    MemoryType Type,
    MemoryPurpose Purpose,
    string Explanation);

public sealed record MemoryAuditEvent(
    Guid EventId,
    MemorySubject Subject,
    Guid? MemoryId,
    MemoryAuditAction Action,
    MemoryPurpose Purpose,
    string CorrelationId,
    DateTimeOffset OccurredAt,
    string? Reason = null);

public sealed record MemoryDeletionRequest(
    MemorySubject Subject,
    Guid MemoryId,
    string CorrelationId);

public interface IMemoryContextProvider
{
    Task<AuthorizedMemoryContext> GetAuthorizedContextAsync(
        MemoryContextRequest request,
        CancellationToken cancellationToken);
}

public interface IMemoryConsentService
{
    Task<MemoryConsentPolicy?> GetConsentAsync(
        MemorySubject subject,
        MemoryType type,
        MemoryPurpose purpose,
        CancellationToken cancellationToken);

    Task<MemoryConsentPolicy> GrantAsync(
        MemoryConsentPolicy policy,
        CancellationToken cancellationToken);

    Task RevokeAsync(
        MemorySubject subject,
        MemoryType type,
        MemoryPurpose purpose,
        string policyVersion,
        CancellationToken cancellationToken);
}

public interface IMemoryAuditService
{
    Task RecordAsync(MemoryAuditEvent auditEvent, CancellationToken cancellationToken);
}

public interface IMemoryControlService
{
    Task<IReadOnlyCollection<OptionalMemoryRecord>> InspectAsync(
        MemorySubject subject,
        CancellationToken cancellationToken);

    Task DeleteAsync(MemoryDeletionRequest request, CancellationToken cancellationToken);
}

public interface IMemoryProtectionPolicy
{
    MemoryPolicyDecision Authorize(
        MemoryContextRequest request,
        OptionalMemoryRecord memory,
        MemoryConsentPolicy? consent,
        DateTimeOffset evaluatedAt);
}

public sealed record MemoryPolicyDecision(
    bool Authorized,
    bool MayBeIncludedInProviderPrompt,
    bool MayBeExportedToTelemetry,
    string Reason,
    MemoryUseDisclosure? Disclosure);
