using System.Text.Json;
using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Domain.Financial.FundPortfolio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialCopilot.Infrastructure.Financial.Providers.Persistence;

public sealed class FundPortfolioAnalyticsSnapshotRow
{
    public Guid Id { get; set; }
    public Guid FundId { get; set; }
    public Guid ReportId { get; set; }
    public DateOnly PeriodEndDate { get; set; }
    public Guid? PreviousComparableReportId { get; set; }
    public decimal? EquityWeight { get; set; }
    public decimal? DepositWeight { get; set; }
    public decimal? CommodityWeight { get; set; }
    public decimal? DerivativeWeight { get; set; }
    public decimal? Top5Concentration { get; set; }
    public decimal? Top10Concentration { get; set; }
    public decimal? HerfindahlIndex { get; set; }
    public decimal? PurchaseAmount { get; set; }
    public decimal? SaleAmount { get; set; }
    public decimal? NetEquityDeploymentAmount { get; set; }
    public decimal? TurnoverRatio { get; set; }
    public int NewPositionCount { get; set; }
    public int FullExitCount { get; set; }
    public FundPortfolioRiskPosture RiskPosture { get; set; }
    public FundPortfolioLiquidityRiskStatus LiquidityRiskStatus { get; set; }
    public FundPortfolioValuationQualityStatus ValuationQualityStatus { get; set; }
    public string InputCompletenessJson { get; set; } = "{}";
    public decimal ConfidenceScore { get; set; }
    public string CalculationVersion { get; set; } = string.Empty;
    public string EvidenceJson { get; set; } = "{}";
}

public sealed class FundPortfolioSignalRow
{
    public Guid Id { get; set; }
    public Guid SnapshotId { get; set; }
    public FundPortfolioSignalType SignalType { get; set; }
    public string? ExternalCompanyId { get; set; }
    public string? IndustryCode { get; set; }
    public decimal? Magnitude { get; set; }
    public decimal ImportanceScore { get; set; }
    public decimal ConfidenceScore { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string EvidenceJson { get; set; } = "{}";
    public string DeduplicationKey { get; set; } = string.Empty;
}

public sealed class FundPortfolioAnalyticsSnapshotRowConfiguration : IEntityTypeConfiguration<FundPortfolioAnalyticsSnapshotRow>
{
    public void Configure(EntityTypeBuilder<FundPortfolioAnalyticsSnapshotRow> builder)
    {
        builder.ToTable("FundPortfolioAnalyticsSnapshots");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.CalculationVersion).HasMaxLength(128).IsRequired();
        builder.Property(row => row.InputCompletenessJson).HasMaxLength(4000).IsRequired();
        builder.Property(row => row.EvidenceJson).HasMaxLength(50000).IsRequired();
        builder.HasIndex(row => new { row.FundId, row.PeriodEndDate, row.CalculationVersion }).IsUnique();
        builder.HasIndex(row => row.ReportId);
    }
}

public sealed class FundPortfolioSignalRowConfiguration : IEntityTypeConfiguration<FundPortfolioSignalRow>
{
    public void Configure(EntityTypeBuilder<FundPortfolioSignalRow> builder)
    {
        builder.ToTable("FundPortfolioSignals");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Title).HasMaxLength(512).IsRequired();
        builder.Property(row => row.Reason).HasMaxLength(4000).IsRequired();
        builder.Property(row => row.ExternalCompanyId).HasMaxLength(256);
        builder.Property(row => row.IndustryCode).HasMaxLength(128);
        builder.Property(row => row.EvidenceJson).HasMaxLength(20000).IsRequired();
        builder.Property(row => row.DeduplicationKey).HasMaxLength(512).IsRequired();
        builder.HasIndex(row => row.DeduplicationKey).IsUnique();
        builder.HasIndex(row => new { row.SnapshotId, row.SignalType });
    }
}

public sealed class EfCoreFundPortfolioAnalyticsRepository(
    FinancialProviderDbContext dbContext) : IFundPortfolioAnalyticsRepository
{
    public async Task StoreAsync(
        FundPortfolioAnalyticsSnapshot snapshot,
        IReadOnlyCollection<FundPortfolioSignal> signals,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.FundPortfolioAnalyticsSnapshots.SingleOrDefaultAsync(
            candidate => candidate.FundId == snapshot.FundId &&
                candidate.PeriodEndDate == snapshot.PeriodEndDate &&
                candidate.CalculationVersion == snapshot.CalculationVersion,
            cancellationToken);

        if (row is null)
        {
            row = new FundPortfolioAnalyticsSnapshotRow { Id = snapshot.Id };
            dbContext.FundPortfolioAnalyticsSnapshots.Add(row);
        }

        row.FundId = snapshot.FundId;
        row.ReportId = snapshot.ReportId;
        row.PeriodEndDate = snapshot.PeriodEndDate;
        row.PreviousComparableReportId = snapshot.PreviousComparableReportId;
        row.EquityWeight = snapshot.EquityWeight;
        row.DepositWeight = snapshot.DepositWeight;
        row.CommodityWeight = snapshot.CommodityWeight;
        row.DerivativeWeight = snapshot.DerivativeWeight;
        row.Top5Concentration = snapshot.Top5Concentration;
        row.Top10Concentration = snapshot.Top10Concentration;
        row.HerfindahlIndex = snapshot.HerfindahlIndex;
        row.PurchaseAmount = snapshot.PurchaseAmount;
        row.SaleAmount = snapshot.SaleAmount;
        row.NetEquityDeploymentAmount = snapshot.NetEquityDeploymentAmount;
        row.TurnoverRatio = snapshot.TurnoverRatio;
        row.NewPositionCount = snapshot.NewPositionCount;
        row.FullExitCount = snapshot.FullExitCount;
        row.RiskPosture = snapshot.RiskPosture;
        row.LiquidityRiskStatus = snapshot.LiquidityRiskStatus;
        row.ValuationQualityStatus = snapshot.ValuationQualityStatus;
        row.InputCompletenessJson = JsonSerializer.Serialize(snapshot.InputCompleteness);
        row.ConfidenceScore = snapshot.ConfidenceScore;
        row.CalculationVersion = snapshot.CalculationVersion;
        row.EvidenceJson = snapshot.EvidenceJson;

        var oldSignals = await dbContext.FundPortfolioSignals
            .Where(signal => signal.SnapshotId == row.Id)
            .ToListAsync(cancellationToken);
        dbContext.FundPortfolioSignals.RemoveRange(oldSignals);
        dbContext.FundPortfolioSignals.AddRange(signals.Select(signal => new FundPortfolioSignalRow
        {
            Id = signal.Id,
            SnapshotId = row.Id,
            SignalType = signal.SignalType,
            ExternalCompanyId = signal.ExternalCompanyId,
            IndustryCode = signal.IndustryCode,
            Magnitude = signal.Magnitude,
            ImportanceScore = signal.ImportanceScore,
            ConfidenceScore = signal.ConfidenceScore,
            Title = signal.Title,
            Reason = signal.Reason,
            EvidenceJson = signal.EvidenceJson,
            DeduplicationKey = signal.DeduplicationKey
        }));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<FundPortfolioAnalyticsResult?> GetAsync(
        FundPortfolioAnalyticsQuery query,
        CancellationToken cancellationToken)
    {
        var snapshotQuery = dbContext.FundPortfolioAnalyticsSnapshots.AsNoTracking()
            .Where(row => row.FundId == query.FundId);
        if (query.PeriodEndDate is { } periodEndDate)
        {
            snapshotQuery = snapshotQuery.Where(row => row.PeriodEndDate == periodEndDate);
        }

        var row = await snapshotQuery
            .OrderByDescending(candidate => candidate.PeriodEndDate)
            .ThenByDescending(candidate => candidate.CalculationVersion)
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null) return null;

        var signals = await dbContext.FundPortfolioSignals.AsNoTracking()
            .Where(signal => signal.SnapshotId == row.Id)
            .OrderBy(signal => signal.SignalType)
            .ThenBy(signal => signal.ExternalCompanyId)
            .ThenBy(signal => signal.DeduplicationKey)
            .Select(signal => new FundPortfolioSignal(
                signal.Id, signal.SnapshotId, signal.SignalType, signal.ExternalCompanyId,
                signal.IndustryCode, signal.Magnitude, signal.ImportanceScore,
                signal.ConfidenceScore, signal.Title, signal.Reason, signal.EvidenceJson,
                signal.DeduplicationKey))
            .ToArrayAsync(cancellationToken);

        return new FundPortfolioAnalyticsResult(
            new FundPortfolioAnalyticsSnapshot(
                row.Id, row.FundId, row.ReportId, row.PeriodEndDate, row.PreviousComparableReportId,
                row.EquityWeight, row.DepositWeight, row.CommodityWeight, row.DerivativeWeight,
                row.Top5Concentration, row.Top10Concentration, row.HerfindahlIndex,
                row.PurchaseAmount, row.SaleAmount, row.NetEquityDeploymentAmount, row.TurnoverRatio,
                row.NewPositionCount, row.FullExitCount, row.RiskPosture, row.LiquidityRiskStatus,
                row.ValuationQualityStatus,
                JsonSerializer.Deserialize<FundPortfolioInputCompleteness>(row.InputCompletenessJson) ??
                    new FundPortfolioInputCompleteness(false, false, false, false, false, false),
                row.ConfidenceScore, row.CalculationVersion, row.EvidenceJson),
            signals);
    }
}
