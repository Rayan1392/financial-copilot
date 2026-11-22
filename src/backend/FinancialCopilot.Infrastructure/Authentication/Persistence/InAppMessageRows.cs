using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Authentication.Persistence;

internal sealed class InAppConversationRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid RecipientUserId { get; set; }
    public required string Subject { get; set; }
    public bool RepliesEnabled { get; set; }
    public long Revision { get; set; }
    public DateTimeOffset LatestActivityUtc { get; set; }
    public DateTimeOffset? PendingSinceUtc { get; set; }
    public List<InAppMessageRow> Messages { get; set; } = [];
}

internal sealed class InAppMessageRow
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid TenantId { get; set; }
    public Guid AuthorUserId { get; set; }
    public Guid IdempotencyKey { get; set; }
    public long Sequence { get; set; }
    public bool FromStaff { get; set; }
    public required string Body { get; set; }
    public DateTimeOffset SentAtUtc { get; set; }
    public DateTimeOffset? ReadAtUtc { get; set; }
}

internal static class InAppMessageMapping
{
    internal const string IdempotencyIndex = "UX_auth_in_app_messages_idempotency";

    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<InAppConversationRow>(entity =>
        {
            entity.ToTable("auth_in_app_conversations");
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.Id, row.TenantId });
            entity.Property(row => row.Subject).HasMaxLength(120);
            entity.HasOne<UserTenantRow>().WithMany()
                .HasForeignKey(row => new { UserId = row.RecipientUserId, row.TenantId })
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(row => new { row.TenantId, row.RecipientUserId, row.LatestActivityUtc, row.Id })
                .IsDescending(false, false, true, true);
            entity.HasIndex(row => new { row.TenantId, row.PendingSinceUtc, row.Id })
                .HasFilter("\"PendingSinceUtc\" IS NOT NULL");
            entity.HasIndex(row => new { row.TenantId, row.LatestActivityUtc, row.Id })
                .IsDescending(false, true, true).HasFilter("\"RepliesEnabled\" = TRUE");
            entity.HasMany(row => row.Messages).WithOne()
                .HasForeignKey(row => new { row.ConversationId, row.TenantId })
                .HasPrincipalKey(row => new { row.Id, row.TenantId })
                .OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<InAppMessageRow>(entity =>
        {
            entity.ToTable("auth_in_app_messages");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Body).HasMaxLength(2000);
            entity.HasIndex(row => new { row.ConversationId, row.Sequence }).IsUnique();
            entity.HasIndex(row => new { row.TenantId, row.AuthorUserId, row.IdempotencyKey })
                .IsUnique().HasDatabaseName(IdempotencyIndex);
            entity.HasIndex(row => row.ConversationId)
                .HasFilter("\"FromStaff\" = TRUE AND \"ReadAtUtc\" IS NULL");
        });
    }
}
