using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class Feature126EventStreamRowConfiguration : IEntityTypeConfiguration<Feature126EventStreamRow>
{
    public void Configure(EntityTypeBuilder<Feature126EventStreamRow> builder)
    {
        builder.ToTable("Feature126EventStreams");
        builder.HasKey(x => x.RunId);
        builder.Property(x => x.RunId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.TehranDate).HasMaxLength(16).IsRequired();
        builder.Property(x => x.OwnerId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.State).HasMaxLength(32).IsRequired();
        builder.Property(x => x.NextSequence).IsRequired();
        builder.Property(x => x.FencingToken).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();
    }
}

public sealed class Feature126EventRowConfiguration : IEntityTypeConfiguration<Feature126EventRow>
{
    public void Configure(EntityTypeBuilder<Feature126EventRow> builder)
    {
        builder.ToTable("Feature126Events");
        builder.HasKey(x => x.EventId);
        builder.Property(x => x.EventId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.RunId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.EventType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ExpectedPredecessorState).HasMaxLength(32).IsRequired();
        builder.Property(x => x.OwnerId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.TehranDate).HasMaxLength(16).IsRequired();
        builder.Property(x => x.AttemptReason).HasMaxLength(256).IsRequired();
        builder.Property(x => x.RecoveredFromRunId).HasMaxLength(128);
        builder.Property(x => x.FieldsJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(x => new { x.RunId, x.EventSequence }).IsUnique();
        builder.HasIndex(x => new { x.RunId, x.AppendedAtUtc });
    }
}
