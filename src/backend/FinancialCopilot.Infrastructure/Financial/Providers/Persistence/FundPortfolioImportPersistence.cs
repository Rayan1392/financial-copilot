using FinancialCopilot.Application.FinancialData.FundPortfolio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialCopilot.Infrastructure.Financial.Providers.Persistence;

public sealed class FundPortfolioImportRunRow
{
    public Guid Id { get; set; }
    public FundPortfolioImportTriggerType TriggerType { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string? RequestedByActorId { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public FundPortfolioImportRunStatus Status { get; set; }
    public int DiscoveredCount { get; set; }
    public int ImportedCount { get; set; }
    public int DuplicateCount { get; set; }
    public int PartialCount { get; set; }
    public int FailedCount { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
}

public sealed class FundPortfolioImportItemRow
{
    public Guid Id { get; set; }
    public Guid ImportRunId { get; set; }
    public string? SourceObjectId { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string? ObservedFundName { get; set; }
    public DateOnly? ObservedPeriodEnd { get; set; }
    public string DownloadToken { get; set; } = string.Empty;
    public string? FileSha256 { get; set; }
    public Guid? ReportId { get; set; }
    public FundPortfolioImportItemStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorSummary { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset? NextAttemptAtUtc { get; set; }
    public DateTimeOffset? LeaseUntilUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset QueuedAtUtc { get; set; }
}

public sealed class FundPortfolioMappingReviewRow
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public FundPortfolioMappingReviewType MappingType { get; set; }
    public string RawValue { get; set; } = string.Empty;
    public string NormalizedValue { get; set; } = string.Empty;
    public string CandidateJson { get; set; } = "[]";
    public FundPortfolioMappingReviewStatus Status { get; set; }
    public string? ResolutionJson { get; set; }
    public string? ResolvedByActorId { get; set; }
    public DateTimeOffset? ResolvedAtUtc { get; set; }
    public int Version { get; set; }
}

public sealed class FundPortfolioGovernedMappingRow
{
    public Guid Id { get; set; }
    public FundPortfolioMappingReviewType MappingType { get; set; }
    public string RawValue { get; set; } = string.Empty;
    public string NormalizedValue { get; set; } = string.Empty;
    public string ResolutionJson { get; set; } = string.Empty;
    public bool IsApproved { get; set; }
    public string ResolvedByActorId { get; set; } = string.Empty;
    public DateTimeOffset ResolvedAtUtc { get; set; }
}

public sealed class FundPortfolioImportRunRowConfiguration : IEntityTypeConfiguration<FundPortfolioImportRunRow>
{
    public void Configure(EntityTypeBuilder<FundPortfolioImportRunRow> builder)
    {
        builder.ToTable("FundPortfolioImportRuns"); builder.HasKey(x => x.Id);
        builder.Property(x => x.ProviderName).HasMaxLength(128).IsRequired(); builder.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => new { x.Status, x.StartedAtUtc }); builder.HasIndex(x => x.ProviderName);
    }
}

public sealed class FundPortfolioImportItemRowConfiguration : IEntityTypeConfiguration<FundPortfolioImportItemRow>
{
    public void Configure(EntityTypeBuilder<FundPortfolioImportItemRow> builder)
    {
        builder.ToTable("FundPortfolioImportItems"); builder.HasKey(x => x.Id);
        builder.Property(x => x.ProviderName).HasMaxLength(128).IsRequired(); builder.Property(x => x.OriginalFileName).HasMaxLength(512).IsRequired();
        builder.Property(x => x.SourceObjectId).HasMaxLength(512); builder.Property(x => x.DownloadToken).HasMaxLength(2048).IsRequired();
        builder.Property(x => x.LastErrorCode).HasMaxLength(128); builder.Property(x => x.LastErrorSummary).HasMaxLength(1000);
        builder.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => x.QueuedAtUtc);
        builder.HasIndex(x => new { x.ProviderName, x.SourceObjectId }).IsUnique().HasFilter("\"SourceObjectId\" IS NOT NULL");
        builder.HasIndex(x => new { x.ProviderName, x.FileSha256 }).IsUnique().HasFilter("\"FileSha256\" IS NOT NULL");
        builder.HasIndex(x => new { x.Status, x.AttemptCount, x.StartedAtUtc }); builder.HasIndex(x => x.ImportRunId);
        builder.HasIndex(x => new { x.Status, x.NextAttemptAtUtc, x.LeaseUntilUtc });
    }
}

public sealed class FundPortfolioMappingReviewRowConfiguration : IEntityTypeConfiguration<FundPortfolioMappingReviewRow>
{
    public void Configure(EntityTypeBuilder<FundPortfolioMappingReviewRow> builder)
    {
        builder.ToTable("FundPortfolioMappingReviews"); builder.HasKey(x => x.Id);
        builder.Property(x => x.RawValue).HasMaxLength(1000).IsRequired(); builder.Property(x => x.NormalizedValue).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.CandidateJson).HasMaxLength(10000).IsRequired(); builder.Property(x => x.ResolutionJson).HasMaxLength(10000);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.Status, x.MappingType }); builder.HasIndex(x => x.ReportId);
    }
}

public sealed class FundPortfolioGovernedMappingRowConfiguration : IEntityTypeConfiguration<FundPortfolioGovernedMappingRow>
{
    public void Configure(EntityTypeBuilder<FundPortfolioGovernedMappingRow> builder)
    {
        builder.ToTable("FundPortfolioGovernedMappings"); builder.HasKey(x => x.Id);
        builder.Property(x => x.RawValue).HasMaxLength(1000).IsRequired(); builder.Property(x => x.NormalizedValue).HasMaxLength(1000).IsRequired(); builder.Property(x => x.ResolutionJson).HasMaxLength(10000).IsRequired(); builder.Property(x => x.ResolvedByActorId).HasMaxLength(256).IsRequired();
        builder.HasIndex(x => new { x.MappingType, x.RawValue }).IsUnique();
    }
}

public sealed class FundPortfolioSourceWatermarkRowConfiguration : IEntityTypeConfiguration<FinancialCopilot.Infrastructure.Financial.FundPortfolio.FundPortfolioSourceWatermarkRow>
{
    public void Configure(EntityTypeBuilder<FinancialCopilot.Infrastructure.Financial.FundPortfolio.FundPortfolioSourceWatermarkRow> builder)
    {
        builder.ToTable("FundPortfolioSourceWatermarks"); builder.HasKey(x => x.ProviderName);
        builder.Property(x => x.ProviderName).HasMaxLength(128); builder.Property(x => x.LastSourceObjectId).HasMaxLength(512);
        builder.HasIndex(x => x.LeaseUntilUtc);
    }
}
