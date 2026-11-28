using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class OptionContractRowConfiguration : IEntityTypeConfiguration<OptionContractRow>
{
    public void Configure(EntityTypeBuilder<OptionContractRow> builder)
    {
        builder.ToTable("OptionContracts", table =>
        {
            table.HasCheckConstraint("CK_OptionContracts_Side", "\"Side\" IN ('Call', 'Put')");
            table.HasCheckConstraint("CK_OptionContracts_Terms",
                "\"StrikePrice\" > 0 AND \"ContractSize\" > 0 AND \"ExpiryDate\" >= \"BeginDate\"");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Side).HasMaxLength(8).IsRequired();
        builder.HasIndex(row => row.TradingInstrumentId).IsUnique();
        builder.HasIndex(row => new { row.UnderlyingInstrumentId, row.ExpiryDate, row.StrikePrice });
        builder.HasOne<TradingInstrumentRow>()
            .WithMany()
            .HasForeignKey(row => row.TradingInstrumentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TradingInstrumentRow>()
            .WithMany()
            .HasForeignKey(row => row.UnderlyingInstrumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class OptionOrderBookLevelRowConfiguration : IEntityTypeConfiguration<OptionOrderBookLevelRow>
{
    public void Configure(EntityTypeBuilder<OptionOrderBookLevelRow> builder)
    {
        builder.ToTable("OptionOrderBookLevels", table =>
            table.HasCheckConstraint("CK_OptionOrderBookLevels_Level", "\"Level\" BETWEEN 1 AND 5"));
        builder.HasKey(row => new { row.TradingInstrumentId, row.Level });
        builder.HasOne<TradingInstrumentRow>()
            .WithMany()
            .HasForeignKey(row => row.TradingInstrumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
