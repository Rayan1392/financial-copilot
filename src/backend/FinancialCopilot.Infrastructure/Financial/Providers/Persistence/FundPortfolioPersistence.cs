using FinancialCopilot.Domain.Financial.FundPortfolio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialCopilot.Infrastructure.Financial.Providers.Persistence;

public sealed class InvestmentFundRow
{
    public Guid Id { get; set; }
    public string? ExternalFundId { get; set; }
    public string FundName { get; set; } = string.Empty;
    public string NormalizedFundName { get; set; } = string.Empty;
    public string? FundSymbol { get; set; }
    public string? RegistrationNumber { get; set; }
    public string? ManagerName { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class FundPortfolioReportRow
{
    public Guid Id { get; set; }
    public Guid FundId { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string? ExternalReportId { get; set; }
    public FundPortfolioReportType ReportType { get; set; }
    public string? PeriodStartJalali { get; set; }
    public string? PeriodEndJalali { get; set; }
    public DateOnly? PeriodStartDate { get; set; }
    public DateOnly? PeriodEndDate { get; set; }
    public string? FiscalYearStartJalali { get; set; }
    public string? FiscalYearEndJalali { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string FileSha256 { get; set; } = string.Empty;
    public string RawStorageKey { get; set; } = string.Empty;
    public long RawFileSizeBytes { get; set; }
    public string RawMimeType { get; set; } = string.Empty;
    public string ParserProfileVersion { get; set; } = string.Empty;
    public FundPortfolioParseStatus ParseStatus { get; set; }
    public int SourceRevision { get; set; }
    public DateTimeOffset ImportedAtUtc { get; set; }
    public string? CorrelationId { get; set; }
    public string? SourceObjectId { get; set; }
    public Guid? SupersedesReportId { get; set; }
}

public sealed class FundPortfolioReportSheetRow
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public string OriginalSheetName { get; set; } = string.Empty;
    public string NormalizedSheetName { get; set; } = string.Empty;
    public FundWorkbookLogicalSheetType LogicalSheetType { get; set; }
    public int SheetIndex { get; set; }
    public string? UsedRange { get; set; }
    public decimal ClassificationConfidence { get; set; }
    public string? HeaderFingerprint { get; set; }
    public string ParserProfileVersion { get; set; } = string.Empty;
}

public sealed class FundPortfolioExtractionIssueRow
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public Guid? SheetId { get; set; }
    public FundExtractionIssueSeverity Severity { get; set; }
    public string IssueCode { get; set; } = string.Empty;
    public string? SourceAddress { get; set; }
    public string? RawValue { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ParserProfileVersion { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class FundPortfolioReportStatusHistoryRow
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public FundPortfolioParseStatus Status { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? CorrelationId { get; set; }
    public string? Details { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class FundPortfolioSourceTraceRow
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public string SourceObjectId { get; set; } = string.Empty;
    public int SourceRevision { get; set; }
    public int NormalizedRowCount { get; set; }
    public int SignalCount { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class InvestmentFundRowConfiguration : IEntityTypeConfiguration<InvestmentFundRow>
{
    public void Configure(EntityTypeBuilder<InvestmentFundRow> builder)
    {
        builder.ToTable("InvestmentFunds"); builder.HasKey(x => x.Id);
        builder.Property(x => x.FundName).HasMaxLength(512).IsRequired();
        builder.Property(x => x.NormalizedFundName).HasMaxLength(512).IsRequired();
        builder.Property(x => x.ProviderName).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => new { x.ProviderName, x.ExternalFundId }).IsUnique().HasFilter("\"ExternalFundId\" IS NOT NULL");
        builder.HasIndex(x => new { x.ProviderName, x.NormalizedFundName });
    }
}

public sealed class FundPortfolioReportRowConfiguration : IEntityTypeConfiguration<FundPortfolioReportRow>
{
    public void Configure(EntityTypeBuilder<FundPortfolioReportRow> builder)
    {
        builder.ToTable("FundPortfolioReports"); builder.HasKey(x => x.Id);
        builder.Property(x => x.ProviderName).HasMaxLength(128).IsRequired();
        builder.Property(x => x.FileSha256).HasMaxLength(64).IsRequired();
        builder.Property(x => x.OriginalFileName).HasMaxLength(512).IsRequired();
        builder.Property(x => x.RawStorageKey).HasMaxLength(1024).IsRequired();
        builder.Property(x => x.RawMimeType).HasMaxLength(256).IsRequired();
        builder.Property(x => x.ParserProfileVersion).HasMaxLength(128).IsRequired();
        builder.Property(x => x.CorrelationId).HasMaxLength(128);
        builder.Property(x => x.SourceObjectId).HasMaxLength(512);
        builder.HasIndex(x => x.SourceObjectId);
        builder.HasIndex(x => new { x.FundId, x.ProviderName, x.PeriodEndDate, x.ReportType, x.SourceRevision }).IsUnique();
        builder.HasIndex(x => new { x.ProviderName, x.FileSha256 }).IsUnique();
        builder.HasIndex(x => new { x.FundId, x.PeriodEndDate });
        builder.HasIndex(x => x.ParseStatus); builder.HasIndex(x => x.ProviderName);
    }
}

public sealed class FundPortfolioReportSheetRowConfiguration : IEntityTypeConfiguration<FundPortfolioReportSheetRow>
{
    public void Configure(EntityTypeBuilder<FundPortfolioReportSheetRow> builder)
    {
        builder.ToTable("FundPortfolioReportSheets"); builder.HasKey(x => x.Id);
        builder.Property(x => x.OriginalSheetName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.NormalizedSheetName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.ParserProfileVersion).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => new { x.ReportId, x.SheetIndex }).IsUnique();
        builder.HasIndex(x => x.LogicalSheetType);
    }
}

public sealed class FundPortfolioExtractionIssueRowConfiguration : IEntityTypeConfiguration<FundPortfolioExtractionIssueRow>
{
    public void Configure(EntityTypeBuilder<FundPortfolioExtractionIssueRow> builder)
    {
        builder.ToTable("FundPortfolioExtractionIssues"); builder.HasKey(x => x.Id);
        builder.Property(x => x.IssueCode).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Message).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.ParserProfileVersion).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => new { x.ReportId, x.Severity }); builder.HasIndex(x => x.IssueCode);
        builder.HasIndex(x => new { x.Severity, x.IssueCode });
    }
}

public sealed class FundPortfolioReportStatusHistoryRowConfiguration : IEntityTypeConfiguration<FundPortfolioReportStatusHistoryRow>
{
    public void Configure(EntityTypeBuilder<FundPortfolioReportStatusHistoryRow> builder)
    {
        builder.ToTable("FundPortfolioReportStatusHistory"); builder.HasKey(x => x.Id);
        builder.Property(x => x.EventType).HasMaxLength(64).IsRequired(); builder.Property(x => x.CorrelationId).HasMaxLength(128); builder.Property(x => x.Details).HasMaxLength(1000);
        builder.HasIndex(x => new { x.ReportId, x.CreatedAtUtc });
    }
}

public sealed class FundPortfolioSourceTraceRowConfiguration : IEntityTypeConfiguration<FundPortfolioSourceTraceRow>
{
    public void Configure(EntityTypeBuilder<FundPortfolioSourceTraceRow> builder)
    {
        builder.ToTable("FundPortfolioSourceTraces"); builder.HasKey(x => x.Id); builder.Property(x => x.SourceObjectId).HasMaxLength(512).IsRequired(); builder.HasIndex(x => new { x.SourceObjectId, x.SourceRevision }).IsUnique(); builder.HasIndex(x => x.ReportId);
    }
}
