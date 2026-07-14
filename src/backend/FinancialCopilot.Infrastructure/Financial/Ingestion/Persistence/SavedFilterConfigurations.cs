using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class SavedFilterRowConfiguration : IEntityTypeConfiguration<SavedFilterRow>
{
    public void Configure(EntityTypeBuilder<SavedFilterRow> builder)
    {
        builder.ToTable("SavedFilters");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.ActorType).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Name).HasMaxLength(100).IsRequired();
        builder.Property(row => row.NormalizedName).HasMaxLength(100).IsRequired();
        builder.Property(row => row.FilterCode).HasMaxLength(64).IsRequired();
        builder.Property(row => row.FilterVersion).HasMaxLength(32).IsRequired();
        builder.Property(row => row.ParametersJson).HasColumnType("jsonb").IsRequired();
        builder.Property(row => row.ConcurrencyToken).IsConcurrencyToken();
        builder.HasIndex(row => new { row.TenantId, row.ActorId, row.ActorType, row.NormalizedName })
            .IsUnique().HasFilter("\"RemovedAtUtc\" IS NULL")
            .HasDatabaseName("UIX_SavedFilters_Actor_Name_Active");
        builder.HasIndex(row => new { row.TenantId, row.ActorId, row.ActorType, row.RemovedAtUtc, row.UpdatedAtUtc })
            .HasDatabaseName("IX_SavedFilters_Actor_State_Updated");
        builder.HasIndex(row => new { row.FilterCode, row.FilterVersion, row.RemovedAtUtc })
            .HasDatabaseName("IX_SavedFilters_CatalogReference");
    }
}
