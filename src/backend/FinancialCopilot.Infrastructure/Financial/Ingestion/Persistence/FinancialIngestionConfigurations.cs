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
        builder.HasIndex(row => new { row.ProviderName, row.ExternalStatementId }).IsUnique();
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
