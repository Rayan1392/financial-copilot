using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class UserInsightStateRowConfiguration : IEntityTypeConfiguration<UserInsightStateRow>
{
    public void Configure(EntityTypeBuilder<UserInsightStateRow> builder)
    {
        builder.ToTable("UserInsightStates");
        builder.HasKey(row => row.Id);

        builder.Property(row => row.ActorType).HasMaxLength(32).IsRequired();

        builder.HasIndex(row => new { row.TenantId, row.ActorId, row.ActorType, row.InsightEventId })
            .IsUnique()
            .HasDatabaseName("UIX_UserInsightStates_Actor_InsightEventId");

        builder.HasIndex(row => row.InsightEventId)
            .HasDatabaseName("IX_UserInsightStates_InsightEventId");

        builder.HasIndex(row => new { row.TenantId, row.ActorId, row.ActorType, row.DismissedAtUtc })
            .HasDatabaseName("IX_UserInsightStates_Actor_DismissedAtUtc");
    }
}
