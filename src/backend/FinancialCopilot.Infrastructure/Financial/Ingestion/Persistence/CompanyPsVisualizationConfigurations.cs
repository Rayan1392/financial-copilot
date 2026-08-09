using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class CompanyPsGaugeSnapshotRowConfiguration : IEntityTypeConfiguration<CompanyPsGaugeSnapshotRow>
{
    public void Configure(EntityTypeBuilder<CompanyPsGaugeSnapshotRow> b)
    {
        b.ToTable("CompanyPsGaugeSnapshots"); b.HasKey(x => x.Id);
        b.Property(x => x.ProviderName).HasMaxLength(64); b.Property(x => x.SourceCompanyIsin).HasMaxLength(32);
        b.Property(x => x.ProviderSymbol).HasMaxLength(128); b.Property(x => x.CompletenessStatus).HasMaxLength(32);
        b.Property(x => x.GaugeRenderabilityStatus).HasMaxLength(32); b.Property(x => x.QualityStatus).HasMaxLength(32);
        b.Property(x => x.QualityWarningsJson).HasMaxLength(4096); b.Property(x => x.GaugePayloadHash).HasMaxLength(64);
        b.Property(x => x.CurrentValuesPayloadHash).HasMaxLength(64); b.Property(x => x.NormalizedSnapshotHash).HasMaxLength(64);
        foreach (var property in new[] { nameof(CompanyPsGaugeSnapshotRow.TtmPsRatio), nameof(CompanyPsGaugeSnapshotRow.ForwardPsRatio), nameof(CompanyPsGaugeSnapshotRow.GaugeClose), nameof(CompanyPsGaugeSnapshotRow.BoundaryStart), nameof(CompanyPsGaugeSnapshotRow.BoundaryMin), nameof(CompanyPsGaugeSnapshotRow.BoundaryAverage), nameof(CompanyPsGaugeSnapshotRow.BoundaryMax), nameof(CompanyPsGaugeSnapshotRow.BoundaryEnd) }) b.Property(property).HasPrecision(28, 14);
        b.HasIndex(x => new { x.ProviderName, x.CompanyId, x.ObservationDate }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.GaugeRenderabilityStatus, x.ObservationDate });
        b.HasOne<NormalizedCompanyRow>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CompanyPsHistoryPointRowConfiguration : IEntityTypeConfiguration<CompanyPsHistoryPointRow>
{
    public void Configure(EntityTypeBuilder<CompanyPsHistoryPointRow> b)
    {
        b.ToTable("CompanyPsHistoryPoints"); b.HasKey(x => x.Id);
        b.Property(x => x.ProviderName).HasMaxLength(64); b.Property(x => x.SourceCompanyIsin).HasMaxLength(32);
        b.Property(x => x.ProviderPointId).HasMaxLength(128); b.Property(x => x.PsRatio).HasPrecision(28, 14); b.Property(x => x.SourcePayloadHash).HasMaxLength(64);
        b.HasIndex(x => new { x.ProviderName, x.CompanyId, x.ProviderPointId }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.IsActiveInLatestSuccessfulSeries, x.ObservationDate, x.ProviderPointId });
        b.HasOne<NormalizedCompanyRow>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CompanyPsSeriesSyncStateRowConfiguration : IEntityTypeConfiguration<CompanyPsSeriesSyncStateRow>
{
    public void Configure(EntityTypeBuilder<CompanyPsSeriesSyncStateRow> b)
    {
        b.ToTable("CompanyPsSeriesSyncStates"); b.HasKey(x => x.Id);
        b.Property(x => x.ProviderName).HasMaxLength(64); b.Property(x => x.SourceCompanyIsin).HasMaxLength(32);
        b.Property(x => x.NormalizedLatestSuccessfulSeriesHash).HasMaxLength(64); b.Property(x => x.LastWarningCodesJson).HasMaxLength(4096);
        b.Property(x => x.LastErrorCode).HasMaxLength(64); b.Property(x => x.LastSuccessfulCorrelationId).HasMaxLength(128);
        b.HasIndex(x => new { x.ProviderName, x.CompanyId }).IsUnique();
        b.HasOne<NormalizedCompanyRow>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CompanyPsVisualizationLeaseRowConfiguration : IEntityTypeConfiguration<CompanyPsVisualizationLeaseRow>
{
    public void Configure(EntityTypeBuilder<CompanyPsVisualizationLeaseRow> b)
    {
        b.ToTable("CompanyPsVisualizationLeases"); b.HasKey(x => x.LeaseName);
        b.Property(x => x.LeaseName).HasMaxLength(128); b.Property(x => x.Owner).HasMaxLength(128);
    }
}
