using FinancialCopilot.Domain.Financial.FundPortfolio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialCopilot.Infrastructure.Financial.Providers.Persistence;

public sealed class FundEquityPositionSnapshotRow
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public Guid FundId { get; set; }
    public FundWorkbookPeriodContext PeriodContext { get; set; }
    public DateOnly? PeriodEndDate { get; set; }
    public FundPositionState PositionState { get; set; }
    public FundEquitySecurityType SecurityType { get; set; }
    public string? ExternalCompanyId { get; set; }
    public Guid? TradingInstrumentId { get; set; }
    public string RawSecurityName { get; set; } = string.Empty;
    public string NormalizedSecurityName { get; set; } = string.Empty;
    public decimal? Quantity { get; set; }
    public decimal? UnitMarketPrice { get; set; }
    public decimal? CostAmount { get; set; }
    public decimal? MarketOrNetSaleValue { get; set; }
    public decimal? WeightOfTotalAssetsPercentage { get; set; }
    public FundSecurityResolutionStatus ResolutionStatus { get; set; }
    public int SourceLogicalRow { get; set; }
    public Guid SourceSheetId { get; set; }
    public string? SourceAddress { get; set; }
    public int SourceRevision { get; set; }
    public DateTimeOffset ImportedAtUtc { get; set; }
    public string ParserProfileVersion { get; set; } = string.Empty;
    public string MonetaryUnit { get; set; } = "IRR";
    public string PercentageScale { get; set; } = "percentage_points";
    public string SourceEvidenceJson { get; set; } = "{}";
}

public sealed class FundEquityPeriodActivityRow
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public Guid FundId { get; set; }
    public FundWorkbookPeriodContext PeriodContext { get; set; }
    public DateOnly? PeriodEndDate { get; set; }
    public FundEquitySecurityType SecurityType { get; set; }
    public string? ExternalCompanyId { get; set; }
    public Guid? TradingInstrumentId { get; set; }
    public string RawSecurityName { get; set; } = string.Empty;
    public string NormalizedSecurityName { get; set; } = string.Empty;
    public decimal? PurchasedQuantity { get; set; }
    public decimal? PurchaseCostAmount { get; set; }
    public decimal? SoldQuantity { get; set; }
    public decimal? SaleProceedsAmount { get; set; }
    public FundEquityActivityClassification ActivityClassification { get; set; }
    public decimal? QuantityReconciliationDifference { get; set; }
    public FundEquityReconciliationStatus ReconciliationStatus { get; set; }
    public decimal? KnownCorporateActionAdjustment { get; set; }
    public int SourceLogicalRow { get; set; }
    public Guid SourceSheetId { get; set; }
    public string? SourceAddress { get; set; }
    public int SourceRevision { get; set; }
    public DateTimeOffset ImportedAtUtc { get; set; }
    public string ParserProfileVersion { get; set; } = string.Empty;
    public string MonetaryUnit { get; set; } = "IRR";
    public string SourceEvidenceJson { get; set; } = "{}";
}

public sealed class FundEquitySectionTotalRow
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public Guid FundId { get; set; }
    public Guid SourceSheetId { get; set; }
    public FundWorkbookPeriodContext PeriodContext { get; set; }
    public int SourceLogicalRow { get; set; }
    public string RawLabel { get; set; } = string.Empty;
    public decimal? Quantity { get; set; }
    public decimal? CostAmount { get; set; }
    public decimal? MarketOrNetSaleValue { get; set; }
    public decimal? WeightOfTotalAssetsPercentage { get; set; }
    public string SourceEvidenceJson { get; set; } = "{}";
}

public sealed class FundEquityPositionSnapshotRowConfiguration : IEntityTypeConfiguration<FundEquityPositionSnapshotRow>
{
    public void Configure(EntityTypeBuilder<FundEquityPositionSnapshotRow> builder)
    {
        builder.ToTable("FundEquityPositionSnapshots");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RawSecurityName).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.NormalizedSecurityName).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.ExternalCompanyId).HasMaxLength(256);
        builder.Property(x => x.ParserProfileVersion).HasMaxLength(128).IsRequired();
        builder.Property(x => x.MonetaryUnit).HasMaxLength(32).IsRequired();
        builder.Property(x => x.PercentageScale).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SourceEvidenceJson).HasMaxLength(20000).IsRequired();
        builder.HasIndex(x => new { x.FundId, x.PeriodEndDate, x.PositionState });
        builder.HasIndex(x => new { x.ExternalCompanyId, x.PeriodEndDate });
        builder.HasIndex(x => x.TradingInstrumentId);
        builder.HasIndex(x => new { x.ResolutionStatus, x.SecurityType });
        builder.HasIndex(x => new { x.ReportId, x.PeriodContext, x.PositionState, x.SourceLogicalRow, x.NormalizedSecurityName }).IsUnique();
    }
}

public sealed class FundEquityPeriodActivityRowConfiguration : IEntityTypeConfiguration<FundEquityPeriodActivityRow>
{
    public void Configure(EntityTypeBuilder<FundEquityPeriodActivityRow> builder)
    {
        builder.ToTable("FundEquityPeriodActivities");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RawSecurityName).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.NormalizedSecurityName).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.ExternalCompanyId).HasMaxLength(256);
        builder.Property(x => x.ParserProfileVersion).HasMaxLength(128).IsRequired();
        builder.Property(x => x.MonetaryUnit).HasMaxLength(32).IsRequired();
        builder.Property(x => x.SourceEvidenceJson).HasMaxLength(20000).IsRequired();
        builder.HasIndex(x => new { x.FundId, x.PeriodEndDate, x.ActivityClassification });
        builder.HasIndex(x => new { x.ExternalCompanyId, x.PeriodEndDate });
        builder.HasIndex(x => x.TradingInstrumentId);
        builder.HasIndex(x => new { x.ReconciliationStatus, x.SecurityType });
        builder.HasIndex(x => new { x.ReportId, x.PeriodContext, x.SourceLogicalRow, x.NormalizedSecurityName }).IsUnique();
    }
}

public sealed class FundEquitySectionTotalRowConfiguration : IEntityTypeConfiguration<FundEquitySectionTotalRow>
{
    public void Configure(EntityTypeBuilder<FundEquitySectionTotalRow> builder)
    {
        builder.ToTable("FundEquitySectionTotals");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RawLabel).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.SourceEvidenceJson).HasMaxLength(20000).IsRequired();
        builder.HasIndex(x => new { x.ReportId, x.PeriodContext, x.SourceLogicalRow }).IsUnique();
    }
}
