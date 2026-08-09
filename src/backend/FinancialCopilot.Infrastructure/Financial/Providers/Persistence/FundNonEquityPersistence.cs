using FinancialCopilot.Domain.Financial.FundPortfolio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialCopilot.Infrastructure.Financial.Providers.Persistence;

public sealed class FundAssetAllocationSnapshotRow
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public Guid FundId { get; set; }
    public FundWorkbookPeriodContext PeriodContext { get; set; }
    public DateOnly? PeriodEndDate { get; set; }
    public FundAssetClass AssetClass { get; set; }
    public string RawAssetClassLabel { get; set; } = string.Empty;
    public string NormalizedAssetClassCode { get; set; } = string.Empty;
    public decimal? CostAmount { get; set; }
    public decimal? MarketOrNetSaleValue { get; set; }
    public decimal? WeightOfTotalAssetsPercentage { get; set; }
    public bool IsSectionTotal { get; set; }
    public bool HasSourceFormulaError { get; set; }
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

public sealed class FundCommodityCertificatePositionRow
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public Guid FundId { get; set; }
    public FundWorkbookPeriodContext PeriodContext { get; set; }
    public DateOnly? PeriodEndDate { get; set; }
    public FundCommodityType CommodityType { get; set; }
    public string? CommodityCode { get; set; }
    public string? ExtractedInstrumentSymbol { get; set; }
    public Guid? TradingInstrumentId { get; set; }
    public string RawInstrumentName { get; set; } = string.Empty;
    public string NormalizedInstrumentName { get; set; } = string.Empty;
    public decimal? BeginningQuantity { get; set; }
    public decimal? BeginningCostAmount { get; set; }
    public decimal? BeginningMarketValue { get; set; }
    public decimal? PurchasedQuantity { get; set; }
    public decimal? PurchaseCostAmount { get; set; }
    public decimal? SoldQuantity { get; set; }
    public decimal? SaleProceedsAmount { get; set; }
    public decimal? EndingQuantity { get; set; }
    public decimal? EndingUnitPrice { get; set; }
    public decimal? EndingCostAmount { get; set; }
    public decimal? EndingMarketValue { get; set; }
    public decimal? WeightOfTotalAssetsPercentage { get; set; }
    public decimal? QuantityReconciliationDifference { get; set; }
    public FundNonEquityReconciliationStatus ReconciliationStatus { get; set; }
    public FundNonEquityResolutionStatus ResolutionStatus { get; set; }
    public bool IsSectionTotal { get; set; }
    public int SourceLogicalRow { get; set; }
    public Guid SourceSheetId { get; set; }
    public string? SourceAddress { get; set; }
    public int SourceRevision { get; set; }
    public DateTimeOffset ImportedAtUtc { get; set; }
    public string ParserProfileVersion { get; set; } = string.Empty;
    public string SourceEvidenceJson { get; set; } = "{}";
}

public sealed class FundBankDepositPositionRow
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public Guid FundId { get; set; }
    public FundWorkbookPeriodContext PeriodContext { get; set; }
    public DateOnly? PeriodEndDate { get; set; }
    public string? BankCode { get; set; }
    public string RawBankName { get; set; } = string.Empty;
    public string NormalizedBankName { get; set; } = string.Empty;
    public decimal? BeginningBalance { get; set; }
    public decimal? IncreaseAmount { get; set; }
    public decimal? DecreaseAmount { get; set; }
    public decimal? EndingBalance { get; set; }
    public decimal? WeightOfTotalAssetsPercentage { get; set; }
    public decimal? BalanceReconciliationDifference { get; set; }
    public FundNonEquityReconciliationStatus ReconciliationStatus { get; set; }
    public FundNonEquityResolutionStatus ResolutionStatus { get; set; }
    public bool IsSectionTotal { get; set; }
    public int SourceLogicalRow { get; set; }
    public Guid SourceSheetId { get; set; }
    public string? SourceAddress { get; set; }
    public int SourceRevision { get; set; }
    public DateTimeOffset ImportedAtUtc { get; set; }
    public string ParserProfileVersion { get; set; } = string.Empty;
    public string SourceEvidenceJson { get; set; } = "{}";
}

public sealed class FundDerivativePositionRow
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public Guid FundId { get; set; }
    public FundWorkbookPeriodContext PeriodContext { get; set; }
    public DateOnly? PeriodEndDate { get; set; }
    public FundDerivativeType DerivativeType { get; set; }
    public FundOptionType OptionType { get; set; }
    public FundPositionSide PositionSide { get; set; }
    public Guid? TradingInstrumentId { get; set; }
    public string? UnderlyingExternalCompanyId { get; set; }
    public Guid? UnderlyingTradingInstrumentId { get; set; }
    public string RawInstrumentName { get; set; } = string.Empty;
    public string NormalizedInstrumentName { get; set; } = string.Empty;
    public string? RawUnderlyingName { get; set; }
    public decimal? ContractQuantity { get; set; }
    public decimal? ContractMultiplier { get; set; }
    public decimal? UnderlyingCoverageQuantity { get; set; }
    public decimal? StrikePrice { get; set; }
    public string? ExpiryOrExerciseJalali { get; set; }
    public DateOnly? ExpiryOrExerciseDate { get; set; }
    public decimal? EffectiveReturnPercentage { get; set; }
    public decimal? CostAmount { get; set; }
    public decimal? MarketValue { get; set; }
    public decimal? WeightOfTotalAssetsPercentage { get; set; }
    public FundNonEquityResolutionStatus ResolutionStatus { get; set; }
    public FundHedgeCoverageStatus HedgeCoverageStatus { get; set; }
    public string? HedgeCoverageCalculationVersion { get; set; }
    public string? HedgeCoverageEvidenceJson { get; set; }
    public int SourceLogicalRow { get; set; }
    public Guid SourceSheetId { get; set; }
    public string? SourceAddress { get; set; }
    public int SourceRevision { get; set; }
    public DateTimeOffset ImportedAtUtc { get; set; }
    public string ParserProfileVersion { get; set; } = string.Empty;
    public string SourceEvidenceJson { get; set; } = "{}";
}

internal static class FundNonEquityPersistenceConfiguration
{
    public static void ConfigureEvidence<T>(EntityTypeBuilder<T> builder) where T : class
    {
        builder.Property("ParserProfileVersion").HasMaxLength(128).IsRequired();
        builder.Property("SourceEvidenceJson").HasMaxLength(20000).IsRequired();
    }
}

public sealed class FundAssetAllocationSnapshotRowConfiguration : IEntityTypeConfiguration<FundAssetAllocationSnapshotRow>
{
    public void Configure(EntityTypeBuilder<FundAssetAllocationSnapshotRow> builder)
    {
        builder.ToTable("FundAssetAllocationSnapshots"); builder.HasKey(x => x.Id);
        builder.Property(x => x.RawAssetClassLabel).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.NormalizedAssetClassCode).HasMaxLength(128).IsRequired();
        builder.Property(x => x.MonetaryUnit).HasMaxLength(32).IsRequired();
        builder.Property(x => x.PercentageScale).HasMaxLength(64).IsRequired();
        FundNonEquityPersistenceConfiguration.ConfigureEvidence(builder);
        builder.HasIndex(x => new { x.FundId, x.PeriodEndDate, x.PeriodContext });
        builder.HasIndex(x => x.AssetClass);
        builder.HasIndex(x => new { x.ReportId, x.PeriodContext, x.SourceLogicalRow, x.NormalizedAssetClassCode }).IsUnique();
    }
}

public sealed class FundCommodityCertificatePositionRowConfiguration : IEntityTypeConfiguration<FundCommodityCertificatePositionRow>
{
    public void Configure(EntityTypeBuilder<FundCommodityCertificatePositionRow> builder)
    {
        builder.ToTable("FundCommodityCertificatePositions"); builder.HasKey(x => x.Id);
        builder.Property(x => x.CommodityCode).HasMaxLength(128);
        builder.Property(x => x.ExtractedInstrumentSymbol).HasMaxLength(256);
        builder.Property(x => x.RawInstrumentName).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.NormalizedInstrumentName).HasMaxLength(1000).IsRequired();
        FundNonEquityPersistenceConfiguration.ConfigureEvidence(builder);
        builder.HasIndex(x => new { x.FundId, x.PeriodEndDate, x.PeriodContext });
        builder.HasIndex(x => x.CommodityCode);
        builder.HasIndex(x => x.TradingInstrumentId);
        builder.HasIndex(x => x.ResolutionStatus);
        builder.HasIndex(x => new { x.ReportId, x.PeriodContext, x.SourceLogicalRow, x.NormalizedInstrumentName }).IsUnique();
    }
}

public sealed class FundBankDepositPositionRowConfiguration : IEntityTypeConfiguration<FundBankDepositPositionRow>
{
    public void Configure(EntityTypeBuilder<FundBankDepositPositionRow> builder)
    {
        builder.ToTable("FundBankDepositPositions"); builder.HasKey(x => x.Id);
        builder.Property(x => x.BankCode).HasMaxLength(128);
        builder.Property(x => x.RawBankName).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.NormalizedBankName).HasMaxLength(1000).IsRequired();
        FundNonEquityPersistenceConfiguration.ConfigureEvidence(builder);
        builder.HasIndex(x => new { x.FundId, x.PeriodEndDate, x.PeriodContext });
        builder.HasIndex(x => x.BankCode);
        builder.HasIndex(x => x.ResolutionStatus);
        builder.HasIndex(x => new { x.ReportId, x.PeriodContext, x.SourceLogicalRow, x.NormalizedBankName }).IsUnique();
    }
}

public sealed class FundDerivativePositionRowConfiguration : IEntityTypeConfiguration<FundDerivativePositionRow>
{
    public void Configure(EntityTypeBuilder<FundDerivativePositionRow> builder)
    {
        builder.ToTable("FundDerivativePositions"); builder.HasKey(x => x.Id);
        builder.Property(x => x.UnderlyingExternalCompanyId).HasMaxLength(256);
        builder.Property(x => x.RawInstrumentName).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.NormalizedInstrumentName).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.RawUnderlyingName).HasMaxLength(1000);
        builder.Property(x => x.ExpiryOrExerciseJalali).HasMaxLength(32);
        builder.Property(x => x.HedgeCoverageCalculationVersion).HasMaxLength(128);
        builder.Property(x => x.HedgeCoverageEvidenceJson).HasMaxLength(20000);
        FundNonEquityPersistenceConfiguration.ConfigureEvidence(builder);
        builder.HasIndex(x => new { x.FundId, x.PeriodEndDate, x.PeriodContext });
        builder.HasIndex(x => x.DerivativeType);
        builder.HasIndex(x => x.TradingInstrumentId);
        builder.HasIndex(x => x.UnderlyingExternalCompanyId);
        builder.HasIndex(x => x.ExpiryOrExerciseDate);
        builder.HasIndex(x => x.ResolutionStatus);
        builder.HasIndex(x => new { x.ReportId, x.PeriodContext, x.SourceLogicalRow, x.NormalizedInstrumentName }).IsUnique();
    }
}
