using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class CodalAlertSubscriptionRowConfiguration : IEntityTypeConfiguration<CodalAlertSubscriptionRow>
{
    public void Configure(EntityTypeBuilder<CodalAlertSubscriptionRow> builder)
    {
        builder.ToTable("CodalAlertSubscriptions");
        builder.HasKey(row => row.Id);

        builder.Property(row => row.ActorType).HasMaxLength(32).IsRequired();
        builder.Property(row => row.ExternalCompanyId).HasMaxLength(64).IsRequired();
        builder.Property(row => row.AnnouncementTypesJson).HasColumnType("jsonb").IsRequired();
        builder.Property(row => row.MinimumImportance).HasMaxLength(32).IsRequired();
        builder.Property(row => row.State).HasMaxLength(32).IsRequired();

        builder.HasIndex(row => new { row.TenantId, row.ActorId, row.ActorType })
            .HasDatabaseName("IX_CodalAlertSubscriptions_Actor");

        builder.HasIndex(row => new { row.TenantId, row.ActorId, row.ActorType, row.ExternalCompanyId })
            .IsUnique()
            .HasDatabaseName("UIX_CodalAlertSubscriptions_Actor_Company");

        builder.HasIndex(row => new { row.ExternalCompanyId, row.State })
            .HasDatabaseName("IX_CodalAlertSubscriptions_Company_State");
    }
}
