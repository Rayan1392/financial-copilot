using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class NormalizedCompanyRowConfiguration : IEntityTypeConfiguration<NormalizedCompanyRow>
{
    public void Configure(EntityTypeBuilder<NormalizedCompanyRow> builder)
    {
        builder.ToTable("Companies");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => new { row.ProviderName, row.ExternalCompanyId }).IsUnique();

        // Classification FKs are optional dimension references; no cascade. Indexed for
        // scanner filtering/segmentation by industry, group, and market.
        builder.HasOne<NormalizedIndustryRow>()
            .WithMany()
            .HasForeignKey(row => row.IndustryId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<NormalizedIndustryGroupRow>()
            .WithMany()
            .HasForeignKey(row => row.GroupId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<NormalizedMarketRow>()
            .WithMany()
            .HasForeignKey(row => row.MarketId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(row => row.IndustryId);
        builder.HasIndex(row => row.GroupId);
        builder.HasIndex(row => row.MarketId);
    }
}

public sealed class NormalizedIndustryRowConfiguration : IEntityTypeConfiguration<NormalizedIndustryRow>
{
    public void Configure(EntityTypeBuilder<NormalizedIndustryRow> builder)
    {
        builder.ToTable("Industries");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => new { row.ProviderName, row.ExternalId }).IsUnique();
        builder.HasOne<NormalizedIndustryRow>()
            .WithMany()
            .HasForeignKey(row => row.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class NormalizedIndustryGroupRowConfiguration : IEntityTypeConfiguration<NormalizedIndustryGroupRow>
{
    public void Configure(EntityTypeBuilder<NormalizedIndustryGroupRow> builder)
    {
        builder.ToTable("IndustryGroups");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => new { row.ProviderName, row.ExternalId }).IsUnique();
    }
}

public sealed class NormalizedMarketRowConfiguration : IEntityTypeConfiguration<NormalizedMarketRow>
{
    public void Configure(EntityTypeBuilder<NormalizedMarketRow> builder)
    {
        builder.ToTable("Markets");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => new { row.ProviderName, row.ExternalId }).IsUnique();
    }
}

public sealed class NormalizedSymbolRowConfiguration : IEntityTypeConfiguration<NormalizedSymbolRow>
{
    public void Configure(EntityTypeBuilder<NormalizedSymbolRow> builder)
    {
        builder.ToTable("Symbols");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => new { row.ProviderName, row.ExternalSymbolId }).IsUnique();
        builder.HasIndex(row => row.SymbolCode);
    }
}

public sealed class NormalizedFinancialStatementRowConfiguration :
    IEntityTypeConfiguration<NormalizedFinancialStatementRow>
{
    public void Configure(EntityTypeBuilder<NormalizedFinancialStatementRow> builder)
    {
        builder.ToTable("FinancialStatements");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.StatementType).HasMaxLength(32).IsRequired();
        // Spec 029: the natural key is now (Provider, ExternalStatementId, StatementType) so the
        // same CodalDB statement can keep its native id while income and balance share it.
        builder.HasIndex(row => new
        {
            row.ProviderName,
            row.ExternalStatementId,
            row.StatementType
        }).IsUnique();
        // Support index for "all balance sheets from provider X" filtering.
        builder.HasIndex(row => new { row.ProviderName, row.StatementType });
    }
}

public sealed class NormalizedFinancialStatementLineItemRowConfiguration :
    IEntityTypeConfiguration<NormalizedFinancialStatementLineItemRow>
{
    public void Configure(EntityTypeBuilder<NormalizedFinancialStatementLineItemRow> builder)
    {
        builder.ToTable("FinancialStatementLineItems");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => new { row.FinancialStatementId, row.MetricCode }).IsUnique();
    }
}

public sealed class NormalizedMonthlyReportRowConfiguration :
    IEntityTypeConfiguration<NormalizedMonthlyReportRow>
{
    public void Configure(EntityTypeBuilder<NormalizedMonthlyReportRow> builder)
    {
        builder.ToTable("MonthlyReports");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => new { row.ProviderName, row.ExternalReportId }).IsUnique();
    }
}

public sealed class NormalizedMonthlyReportLineItemRowConfiguration :
    IEntityTypeConfiguration<NormalizedMonthlyReportLineItemRow>
{
    public void Configure(EntityTypeBuilder<NormalizedMonthlyReportLineItemRow> builder)
    {
        builder.ToTable("MonthlyReportLineItems");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => new { row.MonthlyReportId, row.ProductCode }).IsUnique();
    }
}

public sealed class DataSyncRunRowConfiguration : IEntityTypeConfiguration<DataSyncRunRow>
{
    public void Configure(EntityTypeBuilder<DataSyncRunRow> builder)
    {
        builder.ToTable("ProviderSyncRuns");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => row.IdempotencyKey).IsUnique();
        builder.Property(row => row.ErrorMessage).HasMaxLength(1000);
    }
}

public sealed class MetricRecalculationRequestRowConfiguration :
    IEntityTypeConfiguration<MetricRecalculationRequestRow>
{
    public void Configure(EntityTypeBuilder<MetricRecalculationRequestRow> builder)
    {
        builder.ToTable("MetricRecalculationRequests");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => new { row.SourceDataset, row.SourcePayloadChecksum }).IsUnique();
        builder.HasIndex(row => row.ProcessedAt);
        builder.Property(row => row.LastError).HasMaxLength(1000);
    }
}

public sealed class CodalDbSyncStateRowConfiguration : IEntityTypeConfiguration<CodalDbSyncStateRow>
{
    public void Configure(EntityTypeBuilder<CodalDbSyncStateRow> builder)
    {
        builder.ToTable("CodalDbSyncStates");
        builder.HasKey(row => row.Dataset);
        builder.Property(row => row.Dataset).HasMaxLength(64);
    }
}

public sealed class MissingAnswerFeedbackRowConfiguration : IEntityTypeConfiguration<MissingAnswerFeedbackRow>
{
    public void Configure(EntityTypeBuilder<MissingAnswerFeedbackRow> builder)
    {
        builder.ToTable("MissingAnswerFeedbacks");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.ActorId).HasMaxLength(128).IsRequired();
        builder.Property(row => row.QueryText).HasMaxLength(500).IsRequired();
        builder.Property(row => row.QueryHashSha256).HasMaxLength(64).IsRequired();
        builder.Property(row => row.Classification).HasMaxLength(32).IsRequired();
        builder.Property(row => row.RequestedMetricCode).HasMaxLength(128);
        builder.Property(row => row.AffectedDataCodeOrName).HasMaxLength(256);
        builder.Property(row => row.Context).HasMaxLength(2000);

        builder.HasIndex(row => row.ActorId);
        builder.HasIndex(row => row.Classification);
        builder.HasIndex(row => row.RequestedMetricCode);
        builder.HasIndex(row => row.SubmittedAt);
        builder.HasIndex(row => row.DateBucket);
        // Coalesce key — duplicate (actor, query, classification) within the same day-bucket increments count.
        builder.HasIndex(row => new
        {
            row.ActorId,
            row.QueryHashSha256,
            row.Classification,
            row.DateBucket
        }).IsUnique();
    }
}

public sealed class DerivedMetricRowConfiguration : IEntityTypeConfiguration<DerivedMetricRow>
{
    public void Configure(EntityTypeBuilder<DerivedMetricRow> builder)
    {
        builder.ToTable("DerivedMetrics");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => new
        {
            row.SymbolId,
            row.MetricCode,
            row.MetricVersion,
            row.CalculationPolicyVersion,
            row.PeriodEnd
        }).IsUnique();
        builder.Property(row => row.MetricCode).HasMaxLength(128);
        builder.Property(row => row.MetricVersion).HasMaxLength(64);
        builder.Property(row => row.CalculationPolicyVersion).HasMaxLength(64);
        builder.Property(row => row.PeriodType).HasMaxLength(32);
        builder.Property(row => row.Unit).HasMaxLength(32);
    }
}

public sealed class FeatureDefinitionRowConfiguration : IEntityTypeConfiguration<FeatureDefinitionRow>
{
    public void Configure(EntityTypeBuilder<FeatureDefinitionRow> builder)
    {
        builder.ToTable("FeatureDefinitions");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => new { row.FeatureCode, row.FeatureVersion }).IsUnique();
        builder.Property(row => row.FeatureCode).HasMaxLength(128);
        builder.Property(row => row.FeatureVersion).HasMaxLength(64);
        builder.Property(row => row.PolicyVersion).HasMaxLength(64);
        builder.Property(row => row.Unit).HasMaxLength(32);
        builder.Property(row => row.StrategyKey).HasMaxLength(128);
        builder.Property(row => row.AlgorithmVersion).HasMaxLength(64);
        builder.Property(row => row.InputSchemaVersion).HasMaxLength(64);
    }
}

public sealed class FeatureSnapshotRowConfiguration : IEntityTypeConfiguration<FeatureSnapshotRow>
{
    public void Configure(EntityTypeBuilder<FeatureSnapshotRow> builder)
    {
        builder.ToTable("FeatureSnapshots");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => new
        {
            row.SymbolId,
            row.FeatureCode,
            row.FeatureVersion,
            row.PolicyVersion,
            row.PeriodEnd,
            row.InputFingerprint
        }).IsUnique();
        builder.Property(row => row.FeatureCode).HasMaxLength(128);
        builder.Property(row => row.FeatureVersion).HasMaxLength(64);
        builder.Property(row => row.PolicyVersion).HasMaxLength(64);
        builder.Property(row => row.PeriodType).HasMaxLength(32);
        builder.Property(row => row.Unit).HasMaxLength(32);
        builder.Property(row => row.InputFingerprint).HasMaxLength(128);
    }
}

public sealed class FeatureComputationJobRowConfiguration : IEntityTypeConfiguration<FeatureComputationJobRow>
{
    public void Configure(EntityTypeBuilder<FeatureComputationJobRow> builder)
    {
        builder.ToTable("FeatureComputationJobs");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => row.IdempotencyKey).IsUnique();
        builder.Property(row => row.FeatureCode).HasMaxLength(128);
        builder.Property(row => row.FeatureVersion).HasMaxLength(64);
        builder.Property(row => row.PeriodType).HasMaxLength(32);
        builder.Property(row => row.Status).HasMaxLength(32);
        builder.Property(row => row.IdempotencyKey).HasMaxLength(256);
        builder.Property(row => row.ErrorMessage).HasMaxLength(1000);
    }
}
