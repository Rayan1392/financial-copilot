using FinancialCopilot.Application.Memory;

namespace FinancialCopilot.UnitTests;

public sealed class ConversationMemoryStrategyTests
{
    private static readonly Guid TenantId = Guid.Parse("69245cbd-1fe6-49e9-a726-3c4304926a9a");
    private static readonly Guid SubjectId = Guid.Parse("56dce8d6-aa67-4aa0-b5b5-eb6f5d458c98");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-27T12:00:00Z");

    [Fact]
    public async Task DisabledPhaseOneProvider_ReturnsNoOptionalPersonalizedMemory()
    {
        var provider = new DisabledMemoryContextProvider();

        var context = await provider.GetAuthorizedContextAsync(
            Request(MemoryPurpose.Personalization),
            CancellationToken.None);

        Assert.False(context.OptionalMemoryEnabled);
        Assert.Empty(context.Items);
        Assert.Empty(context.Disclosures);
    }

    [Fact]
    public async Task DisabledPhaseOneControls_RejectDurableMemoryMutationAndAudit()
    {
        var memory = Record(MemoryType.PreferenceMemory, MemoryPurpose.Personalization, MemorySensitivity.PersonalPreference);
        var consent = Granted(memory);
        var consentService = new DisabledMemoryConsentService();
        var controlService = new DisabledMemoryControlService();
        var auditService = new DisabledMemoryAuditService();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            consentService.GrantAsync(consent, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            consentService.RevokeAsync(memory.Owner, memory.Type, memory.Purpose, memory.PolicyVersion, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controlService.DeleteAsync(new MemoryDeletionRequest(memory.Owner, memory.MemoryId, "delete-test"), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            auditService.RecordAsync(
                new MemoryAuditEvent(
                    Guid.NewGuid(),
                    memory.Owner,
                    memory.MemoryId,
                    MemoryAuditAction.UsedInAnswer,
                    memory.Purpose,
                    "audit-test",
                    Now),
                CancellationToken.None));
    }

    [Fact]
    public void DurablePreferenceMemory_RequiresActiveExplicitConsent()
    {
        var policy = new ConsentAwareMemoryProtectionPolicy();
        var memory = Record(MemoryType.PreferenceMemory, MemoryPurpose.Personalization, MemorySensitivity.PersonalPreference);

        var withoutConsent = policy.Authorize(Request(MemoryPurpose.Personalization), memory, null, Now);
        var withConsent = policy.Authorize(
            Request(MemoryPurpose.Personalization, permitProviderPromptContext: true),
            memory,
            Granted(memory),
            Now);

        Assert.False(withoutConsent.Authorized);
        Assert.True(withConsent.Authorized);
        Assert.True(withConsent.MayBeIncludedInProviderPrompt);
        Assert.False(withConsent.MayBeExportedToTelemetry);
        Assert.NotNull(withConsent.Disclosure);
    }

    [Theory]
    [InlineData(MemoryConsentStatus.Revoked)]
    [InlineData(MemoryConsentStatus.Expired)]
    [InlineData(MemoryConsentStatus.Denied)]
    public void RevokedExpiredOrDeniedConsent_BlocksOptionalMemory(MemoryConsentStatus status)
    {
        var memory = Record(MemoryType.LongTermUserMemory, MemoryPurpose.Personalization, MemorySensitivity.PersonalPreference);
        var consent = Granted(memory) with
        {
            Status = status,
            RevokedAt = status == MemoryConsentStatus.Revoked ? Now.AddMinutes(-1) : null,
            ExpiresAt = status == MemoryConsentStatus.Expired ? Now.AddMinutes(-1) : null
        };

        var decision = new ConsentAwareMemoryProtectionPolicy().Authorize(
            Request(MemoryPurpose.Personalization),
            memory,
            consent,
            Now);

        Assert.False(decision.Authorized);
    }

    [Fact]
    public void DifferentTenantOrSubject_CannotRetrieveMemoryEvenWithConsent()
    {
        var memory = Record(MemoryType.WatchlistMemory, MemoryPurpose.WatchlistContext, MemorySensitivity.PersonalPreference);
        var request = new MemoryContextRequest(
            new MemorySubject(Guid.NewGuid(), SubjectId),
            null,
            MemoryPurpose.WatchlistContext,
            "cross-tenant");

        var decision = new ConsentAwareMemoryProtectionPolicy().Authorize(request, memory, Granted(memory), Now);

        Assert.False(decision.Authorized);
        Assert.Contains("owner", decision.Reason);
    }

    [Fact]
    public void SensitivePortfolioMemory_IsNeverPromptOrTelemetryExportable()
    {
        var memory = Record(MemoryType.PortfolioAwareMemory, MemoryPurpose.PortfolioInsight, MemorySensitivity.SensitiveFinancial);

        var decision = new ConsentAwareMemoryProtectionPolicy().Authorize(
            Request(MemoryPurpose.PortfolioInsight, permitProviderPromptContext: true),
            memory,
            Granted(memory),
            Now);

        Assert.True(decision.Authorized);
        Assert.False(decision.MayBeIncludedInProviderPrompt);
        Assert.False(decision.MayBeExportedToTelemetry);
    }

    [Fact]
    public void ShortTermGeneralConversationMemory_DoesNotAuthorizeCrossPurposeReuse()
    {
        var memory = Record(
            MemoryType.ShortTermConversationMemory,
            MemoryPurpose.CurrentConversationContinuity,
            MemorySensitivity.General);
        var policy = new ConsentAwareMemoryProtectionPolicy();

        var sameConversationPurpose = policy.Authorize(
            Request(MemoryPurpose.CurrentConversationContinuity),
            memory,
            consent: null,
            Now);
        var personalizationReuse = policy.Authorize(
            Request(MemoryPurpose.Personalization),
            memory,
            consent: null,
            Now);

        Assert.True(sameConversationPurpose.Authorized);
        Assert.False(personalizationReuse.Authorized);
    }

    private static MemoryContextRequest Request(
        MemoryPurpose purpose,
        bool permitProviderPromptContext = false) =>
        new(
            new MemorySubject(TenantId, SubjectId),
            Guid.NewGuid(),
            purpose,
            "memory-test",
            permitProviderPromptContext);

    private static OptionalMemoryRecord Record(
        MemoryType type,
        MemoryPurpose purpose,
        MemorySensitivity sensitivity) =>
        new(
            Guid.NewGuid(),
            new MemorySubject(TenantId, SubjectId),
            type,
            purpose,
            sensitivity,
            "memory-schema-v1",
            "v1",
            "Protected contextual summary.",
            new MemoryProvenance("UserProvided", null, Now.AddDays(-1)),
            new MemoryRetentionPolicy(Now.AddDays(30), true, true));

    private static MemoryConsentPolicy Granted(OptionalMemoryRecord memory) =>
        new(
            memory.Owner.TenantId,
            memory.Owner.SubjectId,
            memory.Type,
            memory.Purpose,
            MemoryConsentStatus.Granted,
            Now.AddDays(-1),
            null,
            Now.AddDays(30),
            memory.PolicyVersion);
}
