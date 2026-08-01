using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

public sealed class EfCoreFundPortfolioMappingReviewRepository(FinancialProviderDbContext dbContext) : IFundPortfolioMappingReviewRepository
{
    public async Task<IReadOnlyList<FundPortfolioMappingReviewView>> ListAsync(FundPortfolioMappingReviewStatus? status, CancellationToken cancellationToken)
    {
        var query = dbContext.FundPortfolioMappingReviews.AsNoTracking();
        if (status is not null) query = query.Where(x => x.Status == status);
        return await query.OrderBy(x => x.Status).ThenBy(x => x.Id).Select(x => new FundPortfolioMappingReviewView(x.Id, x.ReportId, x.MappingType, x.RawValue, x.NormalizedValue, x.CandidateJson, x.Status, x.ResolutionJson, x.ResolvedByActorId, x.ResolvedAtUtc, x.Version)).ToListAsync(cancellationToken);
    }

    public async Task<FundPortfolioMappingReviewPage> ListPageAsync(FundPortfolioMappingReviewStatus? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var query = dbContext.FundPortfolioMappingReviews.AsNoTracking();
        if (status is not null) query = query.Where(x => x.Status == status);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.Status).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new FundPortfolioMappingReviewView(x.Id, x.ReportId, x.MappingType, x.RawValue, x.NormalizedValue, x.CandidateJson, x.Status, x.ResolutionJson, x.ResolvedByActorId, x.ResolvedAtUtc, x.Version)).ToListAsync(cancellationToken);
        return new(items, page, pageSize, total);
    }

    public async Task<int> CreateFromReportIssuesAsync(Guid reportId, CancellationToken cancellationToken)
    {
        var issues = await dbContext.FundPortfolioExtractionIssues.AsNoTracking().Where(x => x.ReportId == reportId).ToListAsync(cancellationToken);
        var existing = await dbContext.FundPortfolioMappingReviews.Where(x => x.ReportId == reportId).Select(x => new { x.MappingType, x.RawValue }).ToListAsync(cancellationToken);
        var rows = issues.Select(issue => (issue, type: Map(issue.IssueCode))).Where(x => x.type is not null && !existing.Any(old => old.MappingType == x.type && old.RawValue == (x.issue.RawValue ?? string.Empty))).Select(x => new FundPortfolioMappingReviewRow
        {
            Id = Guid.NewGuid(), ReportId = reportId, MappingType = x.type!.Value, RawValue = x.issue.RawValue ?? string.Empty, NormalizedValue = x.issue.RawValue ?? string.Empty,
            CandidateJson = "[]", Status = FundPortfolioMappingReviewStatus.Pending, Version = 0
        }).ToArray();
        if (rows.Length == 0) return 0;
        dbContext.FundPortfolioMappingReviews.AddRange(rows); await dbContext.SaveChangesAsync(cancellationToken); return rows.Length;
    }

    public async Task<FundPortfolioMappingResolutionResult> ResolveAsync(ResolveFundPortfolioMappingReviewRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var review = await dbContext.FundPortfolioMappingReviews.SingleOrDefaultAsync(x => x.Id == request.ReviewId, cancellationToken);
        if (review is null || review.Version != request.ExpectedVersion || review.Status != FundPortfolioMappingReviewStatus.Pending) return new(false, 0, review?.ResolutionJson);
        var affected = await dbContext.FundPortfolioMappingReviews.Where(x => x.MappingType == review.MappingType && x.RawValue == review.RawValue).Select(x => x.ReportId).Distinct().CountAsync(cancellationToken);
        var previous = review.ResolutionJson;
        review.Status = request.Approve ? FundPortfolioMappingReviewStatus.Approved : FundPortfolioMappingReviewStatus.Rejected; review.ResolutionJson = request.ResolutionJson; review.ResolvedByActorId = request.ResolvedByActorId; review.ResolvedAtUtc = DateTimeOffset.UtcNow; review.Version++;
        var governed = await dbContext.FundPortfolioGovernedMappings.SingleOrDefaultAsync(x => x.MappingType == review.MappingType && x.RawValue == review.RawValue, cancellationToken);
        if (governed is null) dbContext.FundPortfolioGovernedMappings.Add(new() { Id = Guid.NewGuid(), MappingType = review.MappingType, RawValue = review.RawValue, NormalizedValue = review.NormalizedValue, ResolutionJson = request.ResolutionJson, IsApproved = request.Approve, ResolvedByActorId = request.ResolvedByActorId, ResolvedAtUtc = DateTimeOffset.UtcNow });
        else { governed.NormalizedValue = review.NormalizedValue; governed.ResolutionJson = request.ResolutionJson; governed.IsApproved = request.Approve; governed.ResolvedByActorId = request.ResolvedByActorId; governed.ResolvedAtUtc = DateTimeOffset.UtcNow; }
        await dbContext.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        return new(true, affected, previous);
    }

    private static FundPortfolioMappingReviewType? Map(string issueCode) => issueCode switch
    {
        "EXCEL_ERROR_VALUE" => FundPortfolioMappingReviewType.HeaderLayoutMismatch,
        "INVALID_JALALI_DATE" => FundPortfolioMappingReviewType.InvalidDate,
        "DUPLICATE_LOGICAL_SHEET_TYPE" => FundPortfolioMappingReviewType.UnknownSheet,
        "NO_SHEETS" => FundPortfolioMappingReviewType.HeaderLayoutMismatch,
        _ => null
    };
}
