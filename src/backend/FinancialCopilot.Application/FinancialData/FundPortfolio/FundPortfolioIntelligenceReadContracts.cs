using FinancialCopilot.Domain.Financial.FundPortfolio;

namespace FinancialCopilot.Application.FinancialData.FundPortfolio;

public enum FundPortfolioIntelligenceSection
{
    Holdings,
    Activity,
    Allocation,
    Sectors,
    IncomeAttribution,
    Risk,
    SourceEvidence
}

public sealed record FundPortfolioIntelligenceReportMetadata(
    Guid ReportId,
    Guid FundId,
    DateOnly? PeriodEndDate,
    FundPortfolioParseStatus ParseStatus,
    int SourceRevision,
    DateTimeOffset ImportedAtUtc,
    decimal ConfidenceScore,
    FundPortfolioInputCompleteness InputCompleteness,
    string CalculationVersion);

public sealed record FundPortfolioIntelligenceResponse(
    FundPortfolioAnalyticsSnapshot Snapshot,
    IReadOnlyCollection<FundPortfolioSignal> Signals,
    FundPortfolioIntelligenceReportMetadata Report,
    IReadOnlyList<FundPortfolioIntelligenceSection> AvailableSections);

public sealed record FundPortfolioIntelligenceDetailQuery(
    Guid FundId,
    DateOnly? PeriodEndDate,
    FundPortfolioIntelligenceSection Section,
    string? Cursor = null,
    int PageSize = 50);

public sealed record FundPortfolioIntelligenceDetailItem(
    Guid Id,
    string Subject,
    string? ExternalCompanyId,
    string? Category,
    decimal? Amount,
    decimal? WeightPercentage,
    decimal? ChangePercentagePoints,
    string? ReconciliationStatus,
    string? ResolutionStatus,
    int SourceRevision,
    DateTimeOffset ImportedAtUtc,
    string SourceEvidenceJson);

public sealed record FundPortfolioIntelligenceDetailPage(
    FundPortfolioIntelligenceSection Section,
    DateOnly? PeriodEndDate,
    IReadOnlyList<FundPortfolioIntelligenceDetailItem> Items,
    string? NextCursor,
    bool HasMore);

public interface IFundPortfolioIntelligenceReadUseCase
{
    Task<FundPortfolioIntelligenceResponse?> ExecuteAsync(Guid fundId, DateOnly? periodEndDate, CancellationToken cancellationToken);
}

public interface IFundPortfolioIntelligenceDetailRepository
{
    Task<FundPortfolioIntelligenceDetailPage> QueryAsync(FundPortfolioIntelligenceDetailQuery query, CancellationToken cancellationToken);
}

public sealed class GetFundPortfolioIntelligenceUseCase(
    IFundPortfolioAnalyticsRepository analytics,
    IGetFundPortfolioReportStatusUseCase reportStatus) : IFundPortfolioIntelligenceReadUseCase
{
    public async Task<FundPortfolioIntelligenceResponse?> ExecuteAsync(Guid fundId, DateOnly? periodEndDate, CancellationToken cancellationToken)
    {
        var result = await analytics.GetAsync(new FundPortfolioAnalyticsQuery(fundId, periodEndDate), cancellationToken);
        if (result is null) return null;
        var report = await reportStatus.ExecuteAsync(result.Snapshot.ReportId, cancellationToken);
        if (report is null) return null;
        return new(
            result.Snapshot,
            result.Signals,
            new(report.ReportId, report.FundId, result.Snapshot.PeriodEndDate, report.ParseStatus, report.SourceRevision, report.ImportedAtUtc, result.Snapshot.ConfidenceScore, result.Snapshot.InputCompleteness, result.Snapshot.CalculationVersion),
            Enum.GetValues<FundPortfolioIntelligenceSection>());
    }
}
