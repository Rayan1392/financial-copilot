using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class IndustryRelativeValuationCalculationRowConfiguration : IEntityTypeConfiguration<IndustryRelativeValuationCalculationRow>
{
    public void Configure(EntityTypeBuilder<IndustryRelativeValuationCalculationRow> builder)
    {
        builder.ToTable("IndustryRelativeValuationCalculations");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.IndustryExternalId).HasMaxLength(128).IsRequired();
        builder.Property(row => row.IndustryTitleSnapshot).HasMaxLength(512).IsRequired();
        builder.Property(row => row.Status).HasMaxLength(32).IsRequired();
        builder.Property(row => row.AlgorithmVersion).HasMaxLength(64).IsRequired();
        builder.Property(row => row.MembershipHash).HasMaxLength(64).IsRequired();
        builder.Property(row => row.SourceBarrierHash).HasMaxLength(64).IsRequired();
        builder.Property(row => row.SourceBarrierEvidenceJson).HasColumnType("text").IsRequired();
        builder.HasIndex(row => new { row.CalculationDate, row.IndustryId, row.CalculationVersion }).IsUnique();
        builder.HasIndex(row => new { row.IndustryId, row.CalculationDate, row.Status });
        builder.HasIndex(row => new { row.IndustryId, row.CalculationDate, row.IsLatestEvaluation })
            .IsUnique().HasFilter("\"IsLatestEvaluation\" = TRUE");
        builder.HasIndex(row => new { row.IndustryId, row.CalculationDate, row.IsSelectedCurrent })
            .IsUnique().HasFilter("\"IsSelectedCurrent\" = TRUE");
        builder.HasOne<NormalizedIndustryRow>().WithMany().HasForeignKey(row => row.IndustryId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class IndustryRelativeValuationMetricRowConfiguration : IEntityTypeConfiguration<IndustryRelativeValuationMetricRow>
{
    public void Configure(EntityTypeBuilder<IndustryRelativeValuationMetricRow> builder)
    {
        builder.ToTable("IndustryRelativeValuationMetrics");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.MetricKind).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Readiness).HasMaxLength(64).IsRequired();
        builder.Property(row => row.Reason).HasMaxLength(256).IsRequired();
        foreach (var name in new[] { nameof(IndustryRelativeValuationMetricRow.Quartile1), nameof(IndustryRelativeValuationMetricRow.Quartile3), nameof(IndustryRelativeValuationMetricRow.LowerBound), nameof(IndustryRelativeValuationMetricRow.UpperBound), nameof(IndustryRelativeValuationMetricRow.CleanAverage) }) builder.Property(name).HasPrecision(28, 14);
        builder.HasIndex(row => new { row.CalculationId, row.MetricKind }).IsUnique();
        builder.HasOne<IndustryRelativeValuationCalculationRow>().WithMany().HasForeignKey(row => row.CalculationId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CompanyIndustryRelativeValuationRowConfiguration : IEntityTypeConfiguration<CompanyIndustryRelativeValuationRow>
{
    public void Configure(EntityTypeBuilder<CompanyIndustryRelativeValuationRow> builder)
    {
        builder.ToTable("CompanyIndustryRelativeValuations");
        builder.HasKey(row => row.Id);
        foreach (var name in new[] { nameof(CompanyIndustryRelativeValuationRow.CurrentPE), nameof(CompanyIndustryRelativeValuationRow.HistoricalAveragePE), nameof(CompanyIndustryRelativeValuationRow.CurrentPS), nameof(CompanyIndustryRelativeValuationRow.HistoricalAveragePS), nameof(CompanyIndustryRelativeValuationRow.CurrentMarketPrice), nameof(CompanyIndustryRelativeValuationRow.EquilibriumPrice), nameof(CompanyIndustryRelativeValuationRow.PEPercent), nameof(CompanyIndustryRelativeValuationRow.PSPercent), nameof(CompanyIndustryRelativeValuationRow.EquilibriumPercent) }) builder.Property(name).HasPrecision(28, 14);
        builder.Property(row => row.RankVersion).HasMaxLength(64).IsRequired();
        foreach (var name in new[] { nameof(CompanyIndustryRelativeValuationRow.PeSourceObservationId), nameof(CompanyIndustryRelativeValuationRow.PeSourceVersion), nameof(CompanyIndustryRelativeValuationRow.PsSourceObservationId), nameof(CompanyIndustryRelativeValuationRow.PsSourceVersion), nameof(CompanyIndustryRelativeValuationRow.EquilibriumSourceObservationId), nameof(CompanyIndustryRelativeValuationRow.EquilibriumSourceVersion), nameof(CompanyIndustryRelativeValuationRow.PeSourceWatermark), nameof(CompanyIndustryRelativeValuationRow.PsSourceWatermark), nameof(CompanyIndustryRelativeValuationRow.EquilibriumSourceWatermark) }) builder.Property(name).HasMaxLength(1024).IsRequired();
        foreach (var name in new[] { nameof(CompanyIndustryRelativeValuationRow.PEClassification), nameof(CompanyIndustryRelativeValuationRow.PSClassification), nameof(CompanyIndustryRelativeValuationRow.EquilibriumClassification), nameof(CompanyIndustryRelativeValuationRow.PEReason), nameof(CompanyIndustryRelativeValuationRow.PSReason), nameof(CompanyIndustryRelativeValuationRow.EquilibriumReason) }) builder.Property(name).HasMaxLength(128).IsRequired();
        builder.HasIndex(row => new { row.CalculationId, row.CompanyId }).IsUnique();
        builder.HasIndex(row => new { row.CalculationId, row.GlobalRank });
        builder.HasOne<IndustryRelativeValuationCalculationRow>().WithMany().HasForeignKey(row => row.CalculationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<NormalizedCompanyRow>().WithMany().HasForeignKey(row => row.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class IndustryWatchStateRowConfiguration : IEntityTypeConfiguration<IndustryWatchStateRow>
{
    public void Configure(EntityTypeBuilder<IndustryWatchStateRow> builder)
    {
        builder.ToTable("IndustryWatchStates");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.State).HasMaxLength(32).IsRequired();
        builder.Property(row => row.LastTransitionReason).HasMaxLength(256).IsRequired();
        builder.Property(row => row.AlgorithmVersion).HasMaxLength(64).IsRequired();
        builder.HasIndex(row => row.IndustryId).IsUnique();
        builder.HasOne<NormalizedIndustryRow>().WithMany().HasForeignKey(row => row.IndustryId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class IndustryWatchTransitionRowConfiguration : IEntityTypeConfiguration<IndustryWatchTransitionRow>
{
    public void Configure(EntityTypeBuilder<IndustryWatchTransitionRow> builder)
    {
        builder.ToTable("IndustryWatchTransitions");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.EvaluationKind).HasMaxLength(64).IsRequired();
        builder.Property(row => row.PreviousState).HasMaxLength(32).IsRequired();
        builder.Property(row => row.NextState).HasMaxLength(32).IsRequired();
        builder.Property(row => row.EvaluationOutcome).HasMaxLength(64).IsRequired();
        builder.Property(row => row.Reason).HasMaxLength(256).IsRequired();
        builder.Property(row => row.AlgorithmVersion).HasMaxLength(64).IsRequired();
        builder.Property(row => row.EventIdentity).HasMaxLength(256).IsRequired();
        builder.Property(row => row.CreatedAtUtc).IsRequired();
        builder.HasIndex(row => new { row.IndustryId, row.CalculationId, row.EvaluationKind }).IsUnique();
        builder.HasIndex(row => new { row.IndustryId, row.TransitionDate });
        builder.HasOne<NormalizedIndustryRow>().WithMany().HasForeignKey(row => row.IndustryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IndustryRelativeValuationCalculationRow>().WithMany().HasForeignKey(row => row.CalculationId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class IndustryWatchEvaluationRowConfiguration : IEntityTypeConfiguration<IndustryWatchEvaluationRow>
{
    public void Configure(EntityTypeBuilder<IndustryWatchEvaluationRow> builder)
    {
        builder.ToTable("IndustryWatchEvaluations");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.EvaluationKind).HasMaxLength(64).IsRequired();
        builder.Property(row => row.Outcome).HasMaxLength(64).IsRequired();
        builder.Property(row => row.PreviousState).HasMaxLength(32).IsRequired();
        builder.Property(row => row.NewState).HasMaxLength(32).IsRequired();
        builder.Property(row => row.TransitionReason).HasMaxLength(256).IsRequired();
        builder.Property(row => row.AlgorithmVersion).HasMaxLength(64).IsRequired();
        builder.HasIndex(row => new { row.IndustryId, row.CalculationDate, row.IsEffective });
        builder.HasIndex(row => new { row.IndustryId, row.CalculationId, row.EvaluationKind }).IsUnique();
        builder.HasOne<NormalizedIndustryRow>().WithMany().HasForeignKey(row => row.IndustryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IndustryRelativeValuationCalculationRow>().WithMany().HasForeignKey(row => row.CalculationId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class IndustryRelativeValuationOutboxRowConfiguration : IEntityTypeConfiguration<IndustryRelativeValuationOutboxRow>
{
    public void Configure(EntityTypeBuilder<IndustryRelativeValuationOutboxRow> builder)
    {
        builder.ToTable("IndustryRelativeValuationOutbox");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.EventIdentity).HasMaxLength(256).IsRequired();
        builder.Property(row => row.EventType).HasMaxLength(64).IsRequired();
        builder.Property(row => row.Payload).HasColumnType("text").IsRequired();
        builder.HasIndex(row => row.EventIdentity).IsUnique();
        builder.HasIndex(row => new { row.PublishedAtUtc, row.CreatedAtUtc });
        builder.HasOne<NormalizedIndustryRow>().WithMany().HasForeignKey(row => row.IndustryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IndustryRelativeValuationCalculationRow>().WithMany().HasForeignKey(row => row.CalculationId).OnDelete(DeleteBehavior.Restrict);
    }
}
