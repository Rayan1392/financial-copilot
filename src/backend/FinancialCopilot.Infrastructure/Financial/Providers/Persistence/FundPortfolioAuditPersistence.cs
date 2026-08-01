using FinancialCopilot.Application.FinancialData.FundPortfolio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialCopilot.Infrastructure.Financial.Providers.Persistence;

public sealed class FundPortfolioOperationAuditRow
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? ActorId { get; set; }
    public Guid? RunId { get; set; }
    public Guid? ReportId { get; set; }
    public Guid? ReviewId { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class FundPortfolioOperationAuditRowConfiguration : IEntityTypeConfiguration<FundPortfolioOperationAuditRow>
{
    public void Configure(EntityTypeBuilder<FundPortfolioOperationAuditRow> builder)
    {
        builder.ToTable("FundPortfolioOperationAudits"); builder.HasKey(x => x.Id);
        builder.Property(x => x.EventType).HasMaxLength(64).IsRequired(); builder.Property(x => x.ActorId).HasMaxLength(256);
        builder.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired(); builder.Property(x => x.Summary).HasMaxLength(1000);
        builder.HasIndex(x => new { x.EventType, x.CreatedAtUtc }); builder.HasIndex(x => x.RunId); builder.HasIndex(x => x.ReportId); builder.HasIndex(x => x.ReviewId);
    }
}

public sealed class EfCoreFundPortfolioAuditSink(FinancialProviderDbContext dbContext) : IFundPortfolioAuditSink
{
    public async Task WriteAsync(FundPortfolioAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        dbContext.Set<FundPortfolioOperationAuditRow>().Add(new FundPortfolioOperationAuditRow
        {
            Id = Guid.NewGuid(), EventType = auditEvent.EventType, ActorId = auditEvent.ActorId, RunId = auditEvent.RunId, ReportId = auditEvent.ReportId,
            ReviewId = auditEvent.ReviewId, CorrelationId = auditEvent.CorrelationId, Summary = auditEvent.Summary, CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
