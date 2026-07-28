using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.Memory;
using FinancialCopilot.Infrastructure.Conversations.Persistence;
using FinancialCopilot.Infrastructure.Memory;
using FinancialCopilot.Infrastructure.Memory.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.IntegrationTests;

public sealed class MemoryImplementationTests
{
    private static readonly Guid TenantId = Guid.Parse("aabb1100-0000-0000-0000-000000000001");
    private static readonly Guid SubjectId = Guid.Parse("aabb1100-0000-0000-0000-000000000002");
    private static readonly MemorySubject Subject = new(TenantId, SubjectId);

    private static MemoryDbContext CreateMemDb() =>
        new(new DbContextOptionsBuilder<MemoryDbContext>()
            .UseInMemoryDatabase($"memory-test-{Guid.NewGuid():N}")
            .Options);

    private static ConversationDbContext CreateConvDb() =>
        new(new DbContextOptionsBuilder<ConversationDbContext>()
            .UseInMemoryDatabase($"conv-test-{Guid.NewGuid():N}")
            .Options);

    [Fact]
    public async Task ConsentService_Grant_PersistsGrantedRecord()
    {
        using var db = CreateMemDb();
        var service = new EfCoreMemoryConsentService(db);

        var result = await service.GrantAsync(MakePolicy(), CancellationToken.None);

        Assert.Equal(MemoryConsentStatus.Granted, result.Status);
        Assert.NotNull(result.GrantedAt);
        Assert.Equal(1, await db.ConsentPolicies.CountAsync());
    }

    [Fact]
    public async Task ConsentService_Revoke_SetsRevokedStatusAndTimestamp()
    {
        using var db = CreateMemDb();
        var service = new EfCoreMemoryConsentService(db);
        await service.GrantAsync(MakePolicy(), CancellationToken.None);

        await service.RevokeAsync(Subject, MemoryType.PreferenceMemory, MemoryPurpose.Personalization, "v1", CancellationToken.None);

        var row = await db.ConsentPolicies.SingleAsync();
        Assert.Equal(MemoryConsentStatus.Revoked.ToString(), row.Status);
        Assert.NotNull(row.RevokedAt);
    }

    [Fact]
    public async Task ConsentService_GetConsent_ReturnsNullWhenNoRow()
    {
        using var db = CreateMemDb();
        var service = new EfCoreMemoryConsentService(db);

        var result = await service.GetConsentAsync(Subject, MemoryType.PreferenceMemory, MemoryPurpose.Personalization, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ConsentService_GrantTwice_IsIdempotentSingleRow()
    {
        using var db = CreateMemDb();
        var service = new EfCoreMemoryConsentService(db);
        await service.GrantAsync(MakePolicy(), CancellationToken.None);
        await service.GrantAsync(MakePolicy(), CancellationToken.None);

        Assert.Equal(1, await db.ConsentPolicies.CountAsync());
        Assert.Equal(MemoryConsentStatus.Granted.ToString(), (await db.ConsentPolicies.SingleAsync()).Status);
    }

    [Fact]
    public async Task ContextProvider_NoConsentOrRecords_ReturnsEmptyDisabledContext()
    {
        using var memDb = CreateMemDb();
        using var convDb = CreateConvDb();

        var context = await BuildProvider(memDb, convDb).GetAuthorizedContextAsync(
            new MemoryContextRequest(Subject, null, MemoryPurpose.Personalization, "c1", true),
            CancellationToken.None);

        Assert.False(context.OptionalMemoryEnabled);
        Assert.Empty(context.Items);
    }

    [Fact]
    public async Task ContextProvider_WithGrantedConsentAndRecord_ReturnsAuthorizedItem()
    {
        using var memDb = CreateMemDb();
        using var convDb = CreateConvDb();

        await new EfCoreMemoryConsentService(memDb).GrantAsync(MakePolicy(), CancellationToken.None);
        await new EfCoreMemoryRecordRepository(memDb).WriteAsync(
            Subject, MemoryType.PreferenceMemory, MemoryPurpose.Personalization,
            MemorySensitivity.PersonalPreference, "Prefers technology sector",
            new MemoryProvenance("UserExplicit", null, DateTimeOffset.UtcNow), null, CancellationToken.None);

        var context = await BuildProvider(memDb, convDb).GetAuthorizedContextAsync(
            new MemoryContextRequest(Subject, null, MemoryPurpose.Personalization, "c2", true),
            CancellationToken.None);

        Assert.True(context.OptionalMemoryEnabled);
        Assert.Single(context.Items);
        Assert.Equal(MemoryType.PreferenceMemory, context.Items.First().Type);
    }

    [Fact]
    public async Task ContextProvider_SensitiveFinancialMemory_AuthorizedButExcludedFromProviderPrompt()
    {
        using var memDb = CreateMemDb();
        using var convDb = CreateConvDb();

        memDb.ConsentPolicies.Add(new MemoryConsentPolicyRow
        {
            Id = Guid.NewGuid(), TenantId = TenantId, SubjectId = SubjectId,
            MemoryType = MemoryType.PortfolioAwareMemory.ToString(),
            Purpose = MemoryPurpose.PortfolioInsight.ToString(),
            Status = MemoryConsentStatus.Granted.ToString(),
            GrantedAt = DateTimeOffset.UtcNow, PolicyVersion = "v1"
        });
        await memDb.SaveChangesAsync();
        await new EfCoreMemoryRecordRepository(memDb).WriteAsync(
            Subject, MemoryType.PortfolioAwareMemory, MemoryPurpose.PortfolioInsight,
            MemorySensitivity.SensitiveFinancial, "Holds AAPL 500 shares",
            new MemoryProvenance("UserExplicit", null, DateTimeOffset.UtcNow), null, CancellationToken.None);

        var context = await BuildProvider(memDb, convDb).GetAuthorizedContextAsync(
            new MemoryContextRequest(Subject, null, MemoryPurpose.PortfolioInsight, "c3", true),
            CancellationToken.None);

        Assert.True(context.OptionalMemoryEnabled);
        var item = Assert.Single(context.Items);
        // Verify policy: SensitiveFinancial must not flow to provider prompt
        var decision = new ConsentAwareMemoryProtectionPolicy().Authorize(
            new MemoryContextRequest(Subject, null, MemoryPurpose.PortfolioInsight, "c3", true),
            item,
            new MemoryConsentPolicy(TenantId, SubjectId, MemoryType.PortfolioAwareMemory,
                MemoryPurpose.PortfolioInsight, MemoryConsentStatus.Granted,
                DateTimeOffset.UtcNow, null, null, "v1"),
            DateTimeOffset.UtcNow);
        Assert.False(decision.MayBeIncludedInProviderPrompt);
    }

    [Fact]
    public async Task ContextProvider_ShortTermConversationMemory_DerivedFromConversationMessages()
    {
        using var memDb = CreateMemDb();
        using var convDb = CreateConvDb();

        var convId = Guid.NewGuid();
        convDb.Conversations.Add(new ConversationRow
        {
            Id = convId, TenantId = TenantId, ActorId = SubjectId,
            StartedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, MessageCount = 2
        });
        convDb.Messages.AddRange(
            new MessageRow { Id = Guid.NewGuid(), ConversationId = convId, Role = "User", Content = "Show tech stocks", CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2) },
            new MessageRow { Id = Guid.NewGuid(), ConversationId = convId, Role = "Assistant", Content = "Here are some tech stocks", CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1) });
        await convDb.SaveChangesAsync();

        var context = await BuildProvider(memDb, convDb).GetAuthorizedContextAsync(
            new MemoryContextRequest(Subject, convId, MemoryPurpose.CurrentConversationContinuity, "c4", true),
            CancellationToken.None);

        Assert.True(context.OptionalMemoryEnabled);
        var item = Assert.Single(context.Items);
        Assert.Equal(MemoryType.ShortTermConversationMemory, item.Type);
        Assert.Contains("tech stocks", item.Summary);
    }

    [Fact]
    public async Task AuditService_RecordEvent_PersistsAuditRow()
    {
        using var db = CreateMemDb();
        var service = new EfCoreMemoryAuditService(db, NullLogger<EfCoreMemoryAuditService>.Instance);

        await service.RecordAsync(new MemoryAuditEvent(
            Guid.NewGuid(), Subject, null, MemoryAuditAction.Inspected,
            MemoryPurpose.Personalization, "c5", DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Equal(1, await db.AuditEvents.CountAsync());
        Assert.Equal(MemoryAuditAction.Inspected.ToString(), (await db.AuditEvents.SingleAsync()).Action);
    }

    [Fact]
    public async Task ControlService_Write_PersistsRecordAndReturnsNonEmptyId()
    {
        using var db = CreateMemDb();
        var (_, control) = BuildControlServices(db);

        var id = await control.WriteAsync(Subject, MemoryType.WatchlistMemory, MemoryPurpose.WatchlistContext,
            MemorySensitivity.General, "Watching NVDA",
            new MemoryProvenance("UserExplicit", null, DateTimeOffset.UtcNow), null, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, id);
        var records = await control.InspectAsync(Subject, CancellationToken.None);
        Assert.Equal("Watching NVDA", Assert.Single(records).Summary);
    }

    [Fact]
    public async Task ControlService_DeleteAll_SoftDeletesAllRecordsAndLeavesTombstones()
    {
        using var db = CreateMemDb();
        var (repo, control) = BuildControlServices(db);

        await repo.WriteAsync(Subject, MemoryType.PreferenceMemory, MemoryPurpose.Personalization,
            MemorySensitivity.General, "Prefers tech", new MemoryProvenance("T", null, DateTimeOffset.UtcNow), null, CancellationToken.None);
        await repo.WriteAsync(Subject, MemoryType.ResearchMemory, MemoryPurpose.ResearchContinuation,
            MemorySensitivity.General, "MSFT research", new MemoryProvenance("T", null, DateTimeOffset.UtcNow), null, CancellationToken.None);

        await control.DeleteAllAsync(Subject, "c6", CancellationToken.None);

        Assert.Empty(await control.InspectAsync(Subject, CancellationToken.None));
        Assert.Equal(2, await db.MemoryRecords.Where(r => r.IsDeleted).CountAsync());
    }

    private static EfCoreMemoryContextProvider BuildProvider(MemoryDbContext memDb, ConversationDbContext convDb) =>
        new(new EfCoreMemoryRecordRepository(memDb),
            new EfCoreMemoryConsentService(memDb),
            new ConsentAwareMemoryProtectionPolicy(),
            new MessageRepository(convDb),
            TimeProvider.System);

    private static (EfCoreMemoryRecordRepository repo, EfCoreMemoryControlService control) BuildControlServices(MemoryDbContext db)
    {
        var repo = new EfCoreMemoryRecordRepository(db);
        var audit = new EfCoreMemoryAuditService(db, NullLogger<EfCoreMemoryAuditService>.Instance);
        var control = new EfCoreMemoryControlService(repo, audit, TimeProvider.System);
        return (repo, control);
    }

    private static MemoryConsentPolicy MakePolicy() =>
        new(TenantId, SubjectId, MemoryType.PreferenceMemory, MemoryPurpose.Personalization,
            MemoryConsentStatus.Granted, DateTimeOffset.UtcNow, null, null, "v1");
}
