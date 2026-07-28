using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class FollowedSymbolRowConfiguration : IEntityTypeConfiguration<FollowedSymbolRow>
{
    public void Configure(EntityTypeBuilder<FollowedSymbolRow> builder)
    {
        builder.ToTable("FollowedSymbols");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.ActorType).HasMaxLength(32).IsRequired();
        builder.Property(row => row.ExternalCompanyId).HasMaxLength(64).IsRequired();
        builder.Property(row => row.Symbol).HasMaxLength(64).IsRequired();
        builder.Property(row => row.CompanyName).HasMaxLength(512).IsRequired();
        builder.Property(row => row.CompanyNameEnglish).HasMaxLength(512);
        builder.Property(row => row.Source).HasMaxLength(64);
        builder.HasIndex(row => new { row.TenantId, row.ActorId, row.ActorType });
        builder.HasIndex(row => new { row.TenantId, row.ActorId, row.ActorType, row.ExternalCompanyId }).IsUnique();
        builder.HasIndex(row => row.ExternalCompanyId);
    }
}
