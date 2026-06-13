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

        // Source provenance (spec 051) — filter companies by logical vendor / import mode.
        builder.Property(row => row.LogicalVendor).HasMaxLength(64);
        builder.Property(row => row.SourceMode).HasMaxLength(32);
        builder.HasIndex(row => new { row.LogicalVendor, row.SourceMode });

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
        builder.Property(row => row.LogicalVendor).HasMaxLength(64);
        builder.Property(row => row.SourceMode).HasMaxLength(32);
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
        builder.Property(row => row.LogicalVendor).HasMaxLength(64);
        builder.Property(row => row.SourceMode).HasMaxLength(32);
        builder.Property(row => row.OutputType);
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
        builder.Property(row => row.Title).HasMaxLength(512);
        builder.Property(row => row.Unit).HasMaxLength(128);
    }
}

public sealed class MonthlyActivityBackfillStateRowConfiguration :
    IEntityTypeConfiguration<MonthlyActivityBackfillStateRow>
{
    public void Configure(EntityTypeBuilder<MonthlyActivityBackfillStateRow> builder)
    {
        builder.ToTable("MonthlyActivityBackfillStates");
        builder.HasKey(row => row.SourceName);
        builder.Property(row => row.SourceName).HasMaxLength(64);
        builder.Property(row => row.RequestedBy).HasMaxLength(256);
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

        // Batch-level source provenance (spec 051 AC #7).
        builder.Property(row => row.LogicalVendor).HasMaxLength(64);
        builder.Property(row => row.PhysicalSource).HasMaxLength(64);
        builder.Property(row => row.SourceMode).HasMaxLength(32);
        builder.Property(row => row.SourceDateRangeStartJalali).HasMaxLength(16);
        builder.Property(row => row.SourceDateRangeEndJalali).HasMaxLength(16);
        builder.HasIndex(row => new { row.LogicalVendor, row.PhysicalSource });
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

public sealed class NadpcoApiSyncStateRowConfiguration : IEntityTypeConfiguration<NadpcoApiSyncStateRow>
{
    public void Configure(EntityTypeBuilder<NadpcoApiSyncStateRow> builder)
    {
        builder.ToTable("NadpcoApiSyncStates");
        builder.HasKey(row => row.Dataset);
        builder.Property(row => row.Dataset).HasMaxLength(64);
        builder.Property(row => row.LastRunMode).HasMaxLength(64);
        builder.Property(row => row.LastError).HasMaxLength(1000);
    }
}

public sealed class NadpcoScheduledSyncRunRowConfiguration : IEntityTypeConfiguration<NadpcoScheduledSyncRunRow>
{
    public void Configure(EntityTypeBuilder<NadpcoScheduledSyncRunRow> builder)
    {
        builder.ToTable("NadpcoScheduledSyncRuns");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.TriggerSource).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Status).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Diagnostics).HasMaxLength(2000);
        builder.Property(row => row.ScheduleSnapshotJson).HasMaxLength(4000).IsRequired();
        builder.Property(row => row.DatasetSelectionJson).HasMaxLength(1000).IsRequired();
        builder.Property(row => row.LockOwner).HasMaxLength(256);
        builder.Property(row => row.ManualReason).HasMaxLength(500);
        builder.HasIndex(row => row.StartedAt);
        builder.HasIndex(row => row.CompletedAt);
        builder.HasIndex(row => row.Status);
        builder.HasIndex(row => row.LockLeaseExpiresAt);
    }
}

public sealed class ArchiveImportRunRowConfiguration : IEntityTypeConfiguration<ArchiveImportRunRow>
{
    public void Configure(EntityTypeBuilder<ArchiveImportRunRow> builder)
    {
        builder.ToTable("ArchiveImportRuns");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Action).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Status).HasMaxLength(32).IsRequired();
        builder.Property(row => row.RequestedBy).HasMaxLength(256).IsRequired();
        builder.Property(row => row.DatasetSelectionJson).HasMaxLength(512).IsRequired();
        builder.Property(row => row.Reason).HasMaxLength(1000);
        builder.Property(row => row.Diagnostics).HasMaxLength(2000);
        builder.Property(row => row.LockOwner).HasMaxLength(256);
        builder.HasIndex(row => row.StartedAt);
        builder.HasIndex(row => row.Status);
        builder.HasIndex(row => row.LockLeaseExpiresAt);
    }
}

public sealed class ArchiveFreezeStateRowConfiguration : IEntityTypeConfiguration<ArchiveFreezeStateRow>
{
    public void Configure(EntityTypeBuilder<ArchiveFreezeStateRow> builder)
    {
        builder.ToTable("ArchiveFreezeStates");
        builder.HasKey(row => row.SourceName);
        builder.Property(row => row.SourceName).HasMaxLength(64);
        builder.Property(row => row.Reason).HasMaxLength(1000);
    }
}

public sealed class NadpcoFundamentalIndexObservationRowConfiguration :
    IEntityTypeConfiguration<NadpcoFundamentalIndexObservationRow>
{
    public void Configure(EntityTypeBuilder<NadpcoFundamentalIndexObservationRow> builder)
    {
        builder.ToTable("NadpcoFundamentalIndexObservations");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.ProviderName).HasMaxLength(64).IsRequired();
        builder.Property(row => row.ExternalCompanyId).HasMaxLength(64).IsRequired();
        builder.Property(row => row.CompanyTitle).HasMaxLength(256);
        builder.Property(row => row.CompanyIndexTitle).HasMaxLength(256);
        builder.Property(row => row.CompanyIndexGroupTitle).HasMaxLength(256);
        builder.Property(row => row.CompanyIndexUnit).HasMaxLength(64);
        builder.Property(row => row.JalaliFiscalYearEnd).HasMaxLength(16);
        builder.Property(row => row.JalaliPeriodEnd).HasMaxLength(16);
        builder.Property(row => row.JalaliAnnouncementDate).HasMaxLength(16);
        builder.Property(row => row.SourcePayloadChecksum).HasMaxLength(64).IsRequired();
        // Canonical observation key: one row per (provider, company, index, period type, period end).
        builder.HasIndex(row => new
        {
            row.ProviderName,
            row.ExternalCompanyId,
            row.CompanyIndexId,
            row.PeriodType,
            row.PeriodEnd
        }).IsUnique();
        builder.HasIndex(row => new { row.CompanyIndexId, row.IsGovernedCandidate });
    }
}

public sealed class FundamentalIndexCatchUpRunRowConfiguration :
    IEntityTypeConfiguration<FundamentalIndexCatchUpRunRow>
{
    public void Configure(EntityTypeBuilder<FundamentalIndexCatchUpRunRow> builder)
    {
        builder.ToTable("FundamentalIndexCatchUpRuns");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Status).HasMaxLength(32).IsRequired();
        builder.Property(row => row.RequestedBy).HasMaxLength(256).IsRequired();
        builder.Property(row => row.FailedCompanyIdsJson).HasMaxLength(4000).IsRequired();
        builder.Property(row => row.Diagnostics).HasMaxLength(2000);
        builder.Property(row => row.LockOwner).HasMaxLength(256);
        builder.HasIndex(row => row.StartedAt);
        builder.HasIndex(row => row.Status);
        builder.HasIndex(row => row.LockLeaseExpiresAt);
    }
}

public sealed class TradingInstrumentRowConfiguration : IEntityTypeConfiguration<TradingInstrumentRow>
{
    public void Configure(EntityTypeBuilder<TradingInstrumentRow> builder)
    {
        builder.ToTable("TradingInstruments");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => new { row.ProviderName, row.ExternalInstrumentId }).IsUnique();
        builder.HasIndex(row => new { row.ProviderName, row.InstrumentCode }).IsUnique();
        builder.HasIndex(row => row.NormalizedCompanyId);
        builder.HasOne<NormalizedCompanyRow>()
            .WithMany()
            .HasForeignKey(row => row.NormalizedCompanyId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class IntradayTradeSnapshotRowConfiguration : IEntityTypeConfiguration<IntradayTradeSnapshotRow>
{
    public void Configure(EntityTypeBuilder<IntradayTradeSnapshotRow> builder)
    {
        builder.ToTable("IntradayTradeSnapshots");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => new { row.ProviderName, row.ExternalSnapshotId }).IsUnique();
        builder.HasIndex(row => new { row.TradingInstrumentId, row.ReceivedAt });
        builder.HasOne<TradingInstrumentRow>()
            .WithMany()
            .HasForeignKey(row => row.TradingInstrumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class DailyInstrumentTradeRowConfiguration : IEntityTypeConfiguration<DailyInstrumentTradeRow>
{
    public void Configure(EntityTypeBuilder<DailyInstrumentTradeRow> builder)
    {
        builder.ToTable("DailyInstrumentTrades");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => new { row.ProviderName, row.ExternalTradeId }).IsUnique();
        builder.HasIndex(row => new { row.TradingInstrumentId, row.TradingDate });
        builder.HasOne<TradingInstrumentRow>()
            .WithMany()
            .HasForeignKey(row => row.TradingInstrumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class IntradayIndexSnapshotRowConfiguration : IEntityTypeConfiguration<IntradayIndexSnapshotRow>
{
    public void Configure(EntityTypeBuilder<IntradayIndexSnapshotRow> builder)
    {
        builder.ToTable("IntradayIndexSnapshots");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => new { row.ProviderName, row.ExternalSnapshotId }).IsUnique();
        builder.HasIndex(row => new { row.TradingInstrumentId, row.TradingDate });
        builder.HasOne<TradingInstrumentRow>()
            .WithMany()
            .HasForeignKey(row => row.TradingInstrumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class DailyIndexSnapshotRowConfiguration : IEntityTypeConfiguration<DailyIndexSnapshotRow>
{
    public void Configure(EntityTypeBuilder<DailyIndexSnapshotRow> builder)
    {
        builder.ToTable("DailyIndexSnapshots");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => new { row.ProviderName, row.TradingInstrumentId, row.TradingDate }).IsUnique();
        builder.HasOne<TradingInstrumentRow>()
            .WithMany()
            .HasForeignKey(row => row.TradingInstrumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class LatestMarketQuoteRowConfiguration : IEntityTypeConfiguration<LatestMarketQuoteRow>
{
    public void Configure(EntityTypeBuilder<LatestMarketQuoteRow> builder)
    {
        builder.ToTable("LatestMarketQuotes");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => new { row.ProviderName, row.TradingInstrumentId }).IsUnique();
        builder.HasOne<TradingInstrumentRow>()
            .WithMany()
            .HasForeignKey(row => row.TradingInstrumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class StockMarketSyncStateRowConfiguration : IEntityTypeConfiguration<StockMarketSyncStateRow>
{
    public void Configure(EntityTypeBuilder<StockMarketSyncStateRow> builder)
    {
        builder.ToTable("StockMarketSyncStates");
        builder.HasKey(row => row.Dataset);
        builder.Property(row => row.Dataset).HasMaxLength(64);
        builder.Property(row => row.LogicalVendor).HasMaxLength(64);
        builder.Property(row => row.PhysicalSource).HasMaxLength(64);
        builder.Property(row => row.SourceMode).HasMaxLength(32);
    }
}

public sealed class WatchlistSymbolRowConfiguration : IEntityTypeConfiguration<WatchlistSymbolRow>
{
    public void Configure(EntityTypeBuilder<WatchlistSymbolRow> builder)
    {
        builder.ToTable("WatchlistSymbols");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.ActorType).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Symbol).HasMaxLength(64).IsRequired();
        builder.HasIndex(row => new { row.TenantId, row.ActorId, row.ActorType, row.Symbol }).IsUnique();
        builder.HasIndex(row => new { row.TenantId, row.ActorId, row.ActorType, row.Position });
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
