using FinancialCopilot.Domain.Financial.FundPortfolio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialCopilot.Infrastructure.Financial.Providers.Persistence;

public sealed class FundInvestmentIncomeSummaryRow
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public Guid FundId { get; set; }
    public FundWorkbookPeriodContext PeriodContext { get; set; }
    public FundIncomeCategory IncomeCategory { get; set; }
    public string RawCategory { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public decimal? SourcePercentageOfTotalIncome { get; set; }
    public decimal? CalculatedPercentageOfTotalIncome { get; set; }
    public decimal? PercentageOfTotalAssets { get; set; }
    public decimal? CumulativeAmount { get; set; }
    public bool HasSourceFormulaError { get; set; }
    public bool IsSourceTotal { get; set; }
    public FundIncomeReconciliationStatus ReconciliationStatus { get; set; }
    public int SourceLogicalRow { get; set; }
    public Guid SourceSheetId { get; set; }
    public string? SourceAddress { get; set; }
    public int SourceRevision { get; set; }
    public DateTimeOffset ImportedAtUtc { get; set; }
    public string ParserProfileVersion { get; set; } = string.Empty;
    public string CalculationVersion { get; set; } = string.Empty;
    public string SourceEvidenceJson { get; set; } = "{}";
}

public sealed class FundSecurityIncomeAttributionRow
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public Guid FundId { get; set; }
    public FundWorkbookPeriodContext PeriodContext { get; set; }
    public string RawSecurityName { get; set; } = string.Empty;
    public string? ExternalCompanyId { get; set; }
    public Guid? TradingInstrumentId { get; set; }
    public decimal? DividendIncome { get; set; }
    public decimal? UnrealizedPriceChangeIncome { get; set; }
    public decimal? RealizedSaleIncome { get; set; }
    public decimal? TotalIncome { get; set; }
    public FundIncomeResolutionStatus ResolutionStatus { get; set; }
    public FundIncomeReconciliationStatus ReconciliationStatus { get; set; }
    public int SourceLogicalRow { get; set; }
    public Guid SourceSheetId { get; set; }
    public string? SourceAddress { get; set; }
    public int SourceRevision { get; set; }
    public DateTimeOffset ImportedAtUtc { get; set; }
    public string ParserProfileVersion { get; set; } = string.Empty;
    public string SourceEvidenceJson { get; set; } = "{}";
}

public sealed class FundDividendIncomeDetailRow
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public Guid FundId { get; set; }
    public FundWorkbookPeriodContext PeriodContext { get; set; }
    public string RawSecurityName { get; set; } = string.Empty;
    public string? ExternalCompanyId { get; set; }
    public string? MeetingDateJalali { get; set; }
    public DateOnly? MeetingDate { get; set; }
    public decimal? EntitledQuantity { get; set; }
    public decimal? DividendPerShare { get; set; }
    public decimal? GrossDividendIncome { get; set; }
    public decimal? DiscountCost { get; set; }
    public decimal? NetDividendIncome { get; set; }
    public FundIncomeResolutionStatus ResolutionStatus { get; set; }
    public int SourceLogicalRow { get; set; }
    public Guid SourceSheetId { get; set; }
    public string? SourceAddress { get; set; }
    public int SourceRevision { get; set; }
    public DateTimeOffset ImportedAtUtc { get; set; }
    public string ParserProfileVersion { get; set; } = string.Empty;
    public string SourceEvidenceJson { get; set; } = "{}";
}

public sealed class FundCommodityIncomeDetailRow
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public Guid FundId { get; set; }
    public FundWorkbookPeriodContext PeriodContext { get; set; }
    public string RawInstrumentName { get; set; } = string.Empty;
    public decimal? UnrealizedIncome { get; set; }
    public decimal? RealizedIncome { get; set; }
    public decimal? TotalIncome { get; set; }
    public FundIncomeResolutionStatus ResolutionStatus { get; set; }
    public int SourceLogicalRow { get; set; }
    public Guid SourceSheetId { get; set; }
    public string? SourceAddress { get; set; }
    public int SourceRevision { get; set; }
    public DateTimeOffset ImportedAtUtc { get; set; }
    public string ParserProfileVersion { get; set; } = string.Empty;
    public string SourceEvidenceJson { get; set; } = "{}";
}

public sealed class FundDepositIncomeDetailRow
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public Guid FundId { get; set; }
    public FundWorkbookPeriodContext PeriodContext { get; set; }
    public string RawBankName { get; set; } = string.Empty;
    public decimal? GrossIncome { get; set; }
    public decimal? DiscountCost { get; set; }
    public decimal? NetIncome { get; set; }
    public FundIncomeResolutionStatus ResolutionStatus { get; set; }
    public int SourceLogicalRow { get; set; }
    public Guid SourceSheetId { get; set; }
    public string? SourceAddress { get; set; }
    public int SourceRevision { get; set; }
    public DateTimeOffset ImportedAtUtc { get; set; }
    public string ParserProfileVersion { get; set; } = string.Empty;
    public string SourceEvidenceJson { get; set; } = "{}";
}

public sealed class FundValuationAdjustmentRow
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public Guid FundId { get; set; }
    public FundWorkbookPeriodContext PeriodContext { get; set; }
    public string RawSecurityName { get; set; } = string.Empty;
    public string? ExternalCompanyId { get; set; }
    public Guid? TradingInstrumentId { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? ClosingPrice { get; set; }
    public decimal? AdjustedPrice { get; set; }
    public decimal? SourceAdjustmentPercentage { get; set; }
    public decimal? CalculatedAdjustmentPercentage { get; set; }
    public decimal? AdjustedValue { get; set; }
    public string? Reason { get; set; }
    public FundIncomeResolutionStatus ResolutionStatus { get; set; }
    public bool IsMaterial { get; set; }
    public int SourceLogicalRow { get; set; }
    public Guid SourceSheetId { get; set; }
    public string? SourceAddress { get; set; }
    public int SourceRevision { get; set; }
    public DateTimeOffset ImportedAtUtc { get; set; }
    public string ParserProfileVersion { get; set; } = string.Empty;
    public string SourceEvidenceJson { get; set; } = "{}";
}

public sealed class FundPortfolioValuationQualitySnapshotRow
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public Guid FundId { get; set; }
    public int AdjustedSecurityCount { get; set; }
    public decimal? AdjustedValueAmount { get; set; }
    public decimal? AdjustedValueExposurePercentage { get; set; }
    public int MaterialReconciliationIssueCount { get; set; }
    public FundPortfolioValuationQualityStatus QualityStatus { get; set; }
    public decimal? QualityScore { get; set; }
    public string CalculationVersion { get; set; } = string.Empty;
    public string EvidenceJson { get; set; } = "{}";
    public int SourceRevision { get; set; }
    public DateTimeOffset ImportedAtUtc { get; set; }
}

internal static class FundIncomeQualityPersistenceConfiguration
{
    public static void ConfigureCommon<T>(EntityTypeBuilder<T> builder) where T : class
    {
        builder.Property("ParserProfileVersion").HasMaxLength(128).IsRequired();
        builder.Property("SourceEvidenceJson").HasMaxLength(20000).IsRequired();
    }
}

public sealed class FundInvestmentIncomeSummaryRowConfiguration : IEntityTypeConfiguration<FundInvestmentIncomeSummaryRow>
{
    public void Configure(EntityTypeBuilder<FundInvestmentIncomeSummaryRow> builder)
    {
        builder.ToTable("FundInvestmentIncomeSummaries"); builder.HasKey(x => x.Id); builder.Property(x => x.RawCategory).HasMaxLength(1000).IsRequired();
        FundIncomeQualityPersistenceConfiguration.ConfigureCommon(builder); builder.HasIndex(x => new { x.FundId, x.PeriodContext }); builder.HasIndex(x => x.IncomeCategory);
        builder.HasIndex(x => new { x.ReportId, x.PeriodContext, x.SourceLogicalRow, x.IncomeCategory }).IsUnique();
    }
}

public sealed class FundSecurityIncomeAttributionRowConfiguration : IEntityTypeConfiguration<FundSecurityIncomeAttributionRow>
{
    public void Configure(EntityTypeBuilder<FundSecurityIncomeAttributionRow> builder)
    {
        builder.ToTable("FundSecurityIncomeAttributions"); builder.HasKey(x => x.Id); builder.Property(x => x.RawSecurityName).HasMaxLength(1000).IsRequired();
        FundIncomeQualityPersistenceConfiguration.ConfigureCommon(builder); builder.HasIndex(x => new { x.FundId, x.PeriodContext }); builder.HasIndex(x => x.ExternalCompanyId); builder.HasIndex(x => x.ReconciliationStatus);
        builder.HasIndex(x => new { x.ReportId, x.PeriodContext, x.SourceLogicalRow }).IsUnique();
    }
}

public sealed class FundDividendIncomeDetailRowConfiguration : IEntityTypeConfiguration<FundDividendIncomeDetailRow>
{
    public void Configure(EntityTypeBuilder<FundDividendIncomeDetailRow> builder)
    {
        builder.ToTable("FundDividendIncomeDetails"); builder.HasKey(x => x.Id); builder.Property(x => x.RawSecurityName).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.MeetingDateJalali).HasMaxLength(32); FundIncomeQualityPersistenceConfiguration.ConfigureCommon(builder); builder.HasIndex(x => new { x.FundId, x.PeriodContext }); builder.HasIndex(x => x.ExternalCompanyId);
        builder.HasIndex(x => new { x.ReportId, x.PeriodContext, x.SourceLogicalRow }).IsUnique();
    }
}

public sealed class FundCommodityIncomeDetailRowConfiguration : IEntityTypeConfiguration<FundCommodityIncomeDetailRow>
{
    public void Configure(EntityTypeBuilder<FundCommodityIncomeDetailRow> builder)
    {
        builder.ToTable("FundCommodityIncomeDetails"); builder.HasKey(x => x.Id); builder.Property(x => x.RawInstrumentName).HasMaxLength(1000).IsRequired(); FundIncomeQualityPersistenceConfiguration.ConfigureCommon(builder); builder.HasIndex(x => new { x.FundId, x.PeriodContext }); builder.HasIndex(x => x.ResolutionStatus);
        builder.HasIndex(x => new { x.ReportId, x.PeriodContext, x.SourceLogicalRow }).IsUnique();
    }
}

public sealed class FundDepositIncomeDetailRowConfiguration : IEntityTypeConfiguration<FundDepositIncomeDetailRow>
{
    public void Configure(EntityTypeBuilder<FundDepositIncomeDetailRow> builder)
    {
        builder.ToTable("FundDepositIncomeDetails"); builder.HasKey(x => x.Id); builder.Property(x => x.RawBankName).HasMaxLength(1000).IsRequired(); FundIncomeQualityPersistenceConfiguration.ConfigureCommon(builder); builder.HasIndex(x => new { x.FundId, x.PeriodContext }); builder.HasIndex(x => x.ResolutionStatus);
        builder.HasIndex(x => new { x.ReportId, x.PeriodContext, x.SourceLogicalRow }).IsUnique();
    }
}

public sealed class FundValuationAdjustmentRowConfiguration : IEntityTypeConfiguration<FundValuationAdjustmentRow>
{
    public void Configure(EntityTypeBuilder<FundValuationAdjustmentRow> builder)
    {
        builder.ToTable("FundValuationAdjustments"); builder.HasKey(x => x.Id); builder.Property(x => x.RawSecurityName).HasMaxLength(1000).IsRequired(); builder.Property(x => x.ExternalCompanyId).HasMaxLength(256); builder.Property(x => x.Reason).HasMaxLength(2000); FundIncomeQualityPersistenceConfiguration.ConfigureCommon(builder);
        builder.HasIndex(x => new { x.FundId, x.PeriodContext }); builder.HasIndex(x => x.ExternalCompanyId); builder.HasIndex(x => x.IsMaterial); builder.HasIndex(x => x.ResolutionStatus); builder.HasIndex(x => new { x.ReportId, x.PeriodContext, x.SourceLogicalRow }).IsUnique();
    }
}

public sealed class FundPortfolioValuationQualitySnapshotRowConfiguration : IEntityTypeConfiguration<FundPortfolioValuationQualitySnapshotRow>
{
    public void Configure(EntityTypeBuilder<FundPortfolioValuationQualitySnapshotRow> builder)
    {
        builder.ToTable("FundPortfolioValuationQualitySnapshots"); builder.HasKey(x => x.Id); builder.Property(x => x.CalculationVersion).HasMaxLength(128).IsRequired(); builder.Property(x => x.EvidenceJson).HasMaxLength(20000).IsRequired(); builder.HasIndex(x => x.ReportId).IsUnique(); builder.HasIndex(x => x.QualityStatus);
    }
}
