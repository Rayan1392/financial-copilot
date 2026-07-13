using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Authentication.Persistence;

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options)
    : IdentityDbContext<
        FinancialCopilotUser,
        FinancialCopilotRole,
        Guid,
        IdentityUserClaim<Guid>,
        IdentityUserRole<Guid>,
        IdentityUserLogin<Guid>,
        IdentityRoleClaim<Guid>,
        IdentityUserToken<Guid>>(options)
{
    public DbSet<PermissionRow> Permissions => Set<PermissionRow>();
    public DbSet<RolePermissionRow> RolePermissions => Set<RolePermissionRow>();
    public DbSet<TenantRow> Tenants => Set<TenantRow>();
    public DbSet<UserTenantRow> UserTenants => Set<UserTenantRow>();
    public DbSet<RefreshTokenRow> RefreshTokens => Set<RefreshTokenRow>();
    public DbSet<SecurityAdminAuditRow> SecurityAdminAudits => Set<SecurityAdminAuditRow>();
    public DbSet<TelegramAccountLinkRow> TelegramAccountLinks => Set<TelegramAccountLinkRow>();
    public DbSet<TelegramLinkTokenRow> TelegramLinkTokens => Set<TelegramLinkTokenRow>();
    public DbSet<TelegramLinkAuditRow> TelegramLinkAudits => Set<TelegramLinkAuditRow>();
    public DbSet<TelegramChannelMembershipVerificationRow> TelegramChannelMembershipVerifications => Set<TelegramChannelMembershipVerificationRow>();
    public DbSet<TelegramMembershipRevalidationRow> TelegramMembershipRevalidations => Set<TelegramMembershipRevalidationRow>();
    public DbSet<TelegramConversationBindingRow> TelegramConversationBindings => Set<TelegramConversationBindingRow>();
    public DbSet<TelegramProcessedUpdateRow> TelegramProcessedUpdates => Set<TelegramProcessedUpdateRow>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<FinancialCopilotUser>().ToTable("auth_users");
        builder.Entity<FinancialCopilotRole>().ToTable("auth_roles");
        builder.Entity<FinancialCopilotRole>().Property(role => role.IsEnabled).HasDefaultValue(true);
        builder.Entity<IdentityUserRole<Guid>>().ToTable("auth_user_roles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("auth_user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("auth_user_logins");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("auth_role_claims");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("auth_user_tokens");

        builder.Entity<PermissionRow>(entity =>
        {
            entity.ToTable("auth_permissions");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => row.Code).IsUnique();
            entity.Property(row => row.Code).HasMaxLength(160).IsRequired();
        });
        builder.Entity<RolePermissionRow>(entity =>
        {
            entity.ToTable("auth_role_permissions");
            entity.HasKey(row => new { row.RoleId, row.PermissionId });
            entity.HasOne<FinancialCopilotRole>().WithMany().HasForeignKey(row => row.RoleId);
            entity.HasOne<PermissionRow>().WithMany().HasForeignKey(row => row.PermissionId);
        });
        builder.Entity<TenantRow>(entity =>
        {
            entity.ToTable("auth_tenants");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Name).HasMaxLength(160).IsRequired();
        });
        builder.Entity<UserTenantRow>(entity =>
        {
            entity.ToTable("auth_user_tenants");
            entity.HasKey(row => new { row.UserId, row.TenantId });
            entity.HasOne<FinancialCopilotUser>().WithMany().HasForeignKey(row => row.UserId);
            entity.HasOne<TenantRow>().WithMany().HasForeignKey(row => row.TenantId);
        });
        builder.Entity<RefreshTokenRow>(entity =>
        {
            entity.ToTable("auth_refresh_tokens");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => row.TokenHash).IsUnique();
            entity.HasIndex(row => new { row.UserId, row.ExpiresAt });
            entity.Property(row => row.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.RevocationReason).HasMaxLength(250);
            entity.HasOne<FinancialCopilotUser>().WithMany().HasForeignKey(row => row.UserId);
        });
        builder.Entity<SecurityAdminAuditRow>(entity =>
        {
            entity.ToTable("auth_security_admin_audits");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.TenantId, row.OccurredAt });
            entity.Property(row => row.ActorType).HasMaxLength(32).IsRequired();
            entity.Property(row => row.PermissionCode).HasMaxLength(160).IsRequired();
            entity.Property(row => row.ActionCode).HasMaxLength(160).IsRequired();
            entity.Property(row => row.TargetType).HasMaxLength(80).IsRequired();
            entity.Property(row => row.TargetId).HasMaxLength(160).IsRequired();
            entity.Property(row => row.Reason).HasMaxLength(500);
            entity.Property(row => row.CorrelationId).HasMaxLength(160).IsRequired();
            entity.Property(row => row.Before).HasMaxLength(2000);
            entity.Property(row => row.After).HasMaxLength(2000);
            entity.Property(row => row.IdempotencyKey).HasMaxLength(160);
        });
        builder.Entity<TelegramAccountLinkRow>(entity =>
        {
            entity.ToTable("auth_telegram_account_links");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.ActorId, row.TenantId })
                .IsUnique()
                .HasFilter("\"RevokedAtUtc\" IS NULL");
            entity.HasIndex(row => row.TelegramUserId)
                .IsUnique()
                .HasFilter("\"RevokedAtUtc\" IS NULL");
            entity.Property(row => row.Username).HasMaxLength(64);
            entity.Property(row => row.Version).IsRowVersion();
            entity.HasOne<FinancialCopilotUser>().WithMany().HasForeignKey(row => row.ActorId);
            entity.HasOne<TenantRow>().WithMany().HasForeignKey(row => row.TenantId);
        });
        builder.Entity<TelegramLinkTokenRow>(entity =>
        {
            entity.ToTable("auth_telegram_link_tokens");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => row.TokenHash).IsUnique();
            entity.HasIndex(row => new { row.ActorId, row.Status, row.ExpiresAtUtc });
            entity.HasIndex(row => new { row.TelegramUserId, row.Status, row.ExpiresAtUtc });
            entity.HasIndex(row => row.TelegramUpdateId).IsUnique().HasFilter("\"TelegramUpdateId\" IS NOT NULL");
            entity.Property(row => row.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.Purpose).HasMaxLength(32).IsRequired();
            entity.Property(row => row.Status).HasMaxLength(32).IsRequired();
            entity.Property(row => row.Username).HasMaxLength(64);
            entity.Property(row => row.CorrelationId).HasMaxLength(160).IsRequired();
            entity.Property(row => row.Version).IsRowVersion();
            entity.HasOne<FinancialCopilotUser>().WithMany().HasForeignKey(row => row.ActorId);
            entity.HasOne<TenantRow>().WithMany().HasForeignKey(row => row.TenantId);
        });
        builder.Entity<TelegramLinkAuditRow>(entity =>
        {
            entity.ToTable("auth_telegram_link_audits");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.TenantId, row.OccurredAtUtc });
            entity.HasIndex(row => new { row.ActorId, row.OccurredAtUtc });
            entity.Property(row => row.Action).HasMaxLength(64).IsRequired();
            entity.Property(row => row.CorrelationId).HasMaxLength(160).IsRequired();
            entity.Property(row => row.Reason).HasMaxLength(250);
        });
        builder.Entity<TelegramChannelMembershipVerificationRow>(entity =>
        {
            entity.ToTable("auth_telegram_channel_membership_verifications");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.ActorId, row.TenantId, row.ChannelId, row.IsLatest })
                .IsUnique()
                .HasFilter("\"IsLatest\" = TRUE");
            entity.HasIndex(row => new { row.ExpiresAtUtc, row.Status });
            entity.HasIndex(row => new { row.TelegramUserId, row.ChannelId, row.VerifiedAtUtc });
            entity.Property(row => row.ChannelId).HasMaxLength(160).IsRequired();
            entity.Property(row => row.Status).HasMaxLength(64).IsRequired();
            entity.Property(row => row.FailureCategory).HasMaxLength(64).IsRequired();
            entity.Property(row => row.CorrelationId).HasMaxLength(160).IsRequired();
            entity.HasOne<FinancialCopilotUser>().WithMany().HasForeignKey(row => row.ActorId);
            entity.HasOne<TenantRow>().WithMany().HasForeignKey(row => row.TenantId);
        });
        builder.Entity<TelegramMembershipRevalidationRow>(entity =>
        {
            entity.ToTable("auth_telegram_membership_revalidations");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.ActorId, row.TenantId, row.ChannelId }).IsUnique();
            entity.HasIndex(row => new { row.NextDueAtUtc, row.DeadLetteredAtUtc, row.LeaseExpiresAtUtc });
            entity.Property(row => row.ChannelId).HasMaxLength(160).IsRequired();
            entity.Property(row => row.LeaseOwner).HasMaxLength(160);
            entity.Property(row => row.LastFailureCategory).HasMaxLength(64);
            entity.Property(row => row.LastError).HasMaxLength(500);
            entity.Property(row => row.CorrelationId).HasMaxLength(160).IsRequired();
            entity.HasOne<FinancialCopilotUser>().WithMany().HasForeignKey(row => row.ActorId);
            entity.HasOne<TenantRow>().WithMany().HasForeignKey(row => row.TenantId);
        });
        builder.Entity<TelegramConversationBindingRow>(entity =>
        {
            entity.ToTable("auth_telegram_conversation_bindings");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.ActorId, row.TenantId, row.TelegramChatId, row.MessageThreadKey, row.RevokedAtUtc });
            entity.HasIndex(row => new { row.ActorId, row.TenantId, row.TelegramChatId, row.MessageThreadKey })
                .IsUnique()
                .HasFilter("\"RevokedAtUtc\" IS NULL");
            entity.Property(row => row.CorrelationId).HasMaxLength(160).IsRequired();
            entity.HasOne<FinancialCopilotUser>().WithMany().HasForeignKey(row => row.ActorId);
            entity.HasOne<TenantRow>().WithMany().HasForeignKey(row => row.TenantId);
        });
        builder.Entity<TelegramProcessedUpdateRow>(entity =>
        {
            entity.ToTable("auth_telegram_processed_updates");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => row.IdempotencyKey).IsUnique();
            entity.HasIndex(row => row.ExpiresAtUtc);
            entity.HasIndex(row => new { row.ActorId, row.TenantId, row.TelegramChatId, row.ProcessedAtUtc });
            entity.Property(row => row.IdempotencyKey).HasMaxLength(200).IsRequired();
            entity.Property(row => row.CallbackQueryId).HasMaxLength(160);
            entity.Property(row => row.Status).HasMaxLength(64).IsRequired();
            entity.Property(row => row.ResponseJson).HasColumnType("jsonb").IsRequired();
            entity.Property(row => row.CorrelationId).HasMaxLength(160).IsRequired();
            entity.HasOne<FinancialCopilotUser>().WithMany().HasForeignKey(row => row.ActorId);
            entity.HasOne<TenantRow>().WithMany().HasForeignKey(row => row.TenantId);
        });
    }
}
