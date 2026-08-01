using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Domain.Financial.FundPortfolio;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

public sealed class FundPortfolioRawStorageOptions
{
    public const string SectionName = "FundPortfolio:RawStorage";
    public string RootPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "fund-portfolio-workbooks");
    public long MaximumFileBytes { get; set; } = 50 * 1024 * 1024;
}

public sealed class FileSystemFundPortfolioRawWorkbookStore(IOptions<FundPortfolioRawStorageOptions> options) : IFundPortfolioRawWorkbookStore
{
    public async Task<FundPortfolioStoredFile> StoreAsync(Guid reportId, string originalFileName, string contentType, Stream content, string sha256, CancellationToken cancellationToken)
    {
        if (content.Length > options.Value.MaximumFileBytes) throw new InvalidDataException("Workbook exceeds the configured storage limit.");
        var root = Path.GetFullPath(options.Value.RootPath);
        Directory.CreateDirectory(root);
        var safeName = $"{reportId:N}-{sha256[..16].ToLowerInvariant()}.xlsx";
        var path = Path.Combine(root, safeName);
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid storage key.");
        await using (var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            await content.CopyToAsync(output, cancellationToken);
        return new($"fund-portfolio/{safeName}", content.Length, contentType, sha256);
    }
}

public sealed class EfCoreInvestmentFundRepository(FinancialProviderDbContext dbContext) : IInvestmentFundRepository
{
    public async Task<IReadOnlyList<InvestmentFund>> FindCandidatesAsync(string providerName, string normalizedFundName, string? externalFundId, CancellationToken cancellationToken)
    {
        var query = dbContext.InvestmentFunds.AsNoTracking().Where(row => row.ProviderName == providerName);
        if (!string.IsNullOrWhiteSpace(externalFundId)) query = query.Where(row => row.ExternalFundId == externalFundId || row.NormalizedFundName == normalizedFundName);
        else query = query.Where(row => row.NormalizedFundName == normalizedFundName);
        return await query.Select(row => ToDomain(row)).ToListAsync(cancellationToken);
    }

    public async Task<InvestmentFund> AddAsync(InvestmentFund fund, CancellationToken cancellationToken)
    {
        dbContext.InvestmentFunds.Add(new InvestmentFundRow
        {
            Id = fund.Id, ExternalFundId = fund.ExternalFundId, FundName = fund.FundName, NormalizedFundName = fund.NormalizedFundName,
            FundSymbol = fund.FundSymbol, ProviderName = fund.ProviderName, IsActive = fund.IsActive, CreatedAtUtc = fund.CreatedAtUtc, UpdatedAtUtc = fund.UpdatedAtUtc
        });
        try { await dbContext.SaveChangesAsync(cancellationToken); return fund; }
        catch (DbUpdateException)
        {
            var existing = await dbContext.InvestmentFunds.AsNoTracking().SingleOrDefaultAsync(row => row.ProviderName == fund.ProviderName && row.NormalizedFundName == fund.NormalizedFundName, cancellationToken);
            if (existing is null) throw;
            return ToDomain(existing);
        }
    }

    private static InvestmentFund ToDomain(InvestmentFundRow row) => new(row.Id, row.FundName, row.NormalizedFundName, row.ProviderName, row.ExternalFundId, row.FundSymbol);
}

public sealed class EfCoreFundPortfolioReportRepository(FinancialProviderDbContext dbContext) : IFundPortfolioReportRepository
{
    public async Task<(Guid ReportId, int SourceRevision)?> FindByHashAsync(string providerName, string fileSha256, CancellationToken cancellationToken)
    {
        var row = await dbContext.FundPortfolioReports.AsNoTracking().SingleOrDefaultAsync(x => x.ProviderName == providerName && x.FileSha256 == fileSha256, cancellationToken);
        return row is null ? null : (row.Id, row.SourceRevision);
    }

    public async Task<int> GetNextRevisionAsync(Guid fundId, string providerName, DateOnly? periodEndDate, CancellationToken cancellationToken)
    {
        var query = dbContext.FundPortfolioReports.Where(x => x.FundId == fundId && x.ProviderName == providerName && x.ReportType == FundPortfolioReportType.MonthlyPortfolio);
        if (periodEndDate is null) query = query.Where(x => x.PeriodEndDate == null); else query = query.Where(x => x.PeriodEndDate == periodEndDate);
        var current = await query.Select(x => (int?)x.SourceRevision).MaxAsync(cancellationToken);
        return (current ?? 0) + 1;
    }

    public async Task<bool> SaveParsedReportAsync(InvestmentFund fund, IngestFundPortfolioWorkbookRequest request, FundPortfolioStoredFile storedFile, FundPortfolioWorkbookEnvelope envelope, int sourceRevision, Guid? supersedesReportId, CancellationToken cancellationToken)
    {
        var report = new FundPortfolioReportRow
        {
            Id = envelope.ReportId, FundId = fund.Id, ProviderName = request.ProviderName, ReportType = FundPortfolioReportType.MonthlyPortfolio,
            PeriodEndJalali = envelope.Period.PeriodEndJalali, PeriodEndDate = envelope.Period.PeriodEndDate,
            PeriodStartJalali = envelope.Period.PeriodStartJalali, PeriodStartDate = envelope.Period.PeriodStartDate,
            FiscalYearStartJalali = envelope.Period.FiscalYearStartJalali, FiscalYearEndJalali = envelope.Period.FiscalYearEndJalali,
            OriginalFileName = request.OriginalFileName, FileSha256 = storedFile.Sha256, RawStorageKey = storedFile.StorageKey,
            RawFileSizeBytes = storedFile.SizeBytes, RawMimeType = storedFile.ContentType, ParserProfileVersion = envelope.ParserProfileVersion,
            ParseStatus = envelope.Status, SourceRevision = sourceRevision, ImportedAtUtc = DateTimeOffset.UtcNow, SupersedesReportId = supersedesReportId
        };
        dbContext.FundPortfolioReports.Add(report);
        dbContext.FundPortfolioReportSheets.AddRange(envelope.Sheets.Select(sheet => new FundPortfolioReportSheetRow
        {
            Id = sheet.SheetId, ReportId = envelope.ReportId, OriginalSheetName = sheet.OriginalSheetName, NormalizedSheetName = sheet.NormalizedSheetName,
            LogicalSheetType = sheet.LogicalSheetType, SheetIndex = sheet.SheetIndex, UsedRange = sheet.UsedRange,
            ClassificationConfidence = sheet.ClassificationConfidence, HeaderFingerprint = sheet.HeaderFingerprint, ParserProfileVersion = envelope.ParserProfileVersion
        }));
        dbContext.FundPortfolioExtractionIssues.AddRange(envelope.Issues.Select(issue => new FundPortfolioExtractionIssueRow
        {
            Id = issue.Id, ReportId = envelope.ReportId, SheetId = issue.SheetId, Severity = issue.Severity, IssueCode = issue.IssueCode,
            SourceAddress = issue.SourceAddress, RawValue = issue.RawValue, Message = issue.Message, ParserProfileVersion = issue.ParserProfileVersion, CreatedAtUtc = issue.CreatedAtUtc
        }));
        if (supersedesReportId is Guid previousId)
        {
            var previous = await dbContext.FundPortfolioReports.SingleOrDefaultAsync(x => x.Id == previousId, cancellationToken);
            if (previous is not null) previous.ParseStatus = FundPortfolioParseStatus.Superseded;
        }
        try { await dbContext.SaveChangesAsync(cancellationToken); return true; }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            if (await dbContext.FundPortfolioReports.AsNoTracking().AnyAsync(x => x.ProviderName == request.ProviderName && x.FileSha256 == storedFile.Sha256, cancellationToken))
                return false;
            throw;
        }
    }

    public async Task<Guid?> FindLatestReportIdAsync(Guid fundId, string providerName, DateOnly? periodEndDate, CancellationToken cancellationToken)
    {
        var query = dbContext.FundPortfolioReports.Where(x => x.FundId == fundId && x.ProviderName == providerName && x.ReportType == FundPortfolioReportType.MonthlyPortfolio);
        query = periodEndDate is null ? query.Where(x => x.PeriodEndDate == null) : query.Where(x => x.PeriodEndDate == periodEndDate);
        return await query.OrderByDescending(x => x.SourceRevision).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<FundPortfolioReportStatusResult?> FindStatusAsync(Guid reportId, CancellationToken cancellationToken)
    {
        return await dbContext.FundPortfolioReports.AsNoTracking().Where(x => x.Id == reportId).Select(x => new FundPortfolioReportStatusResult(
            x.Id, x.FundId, x.ParseStatus, x.SourceRevision, x.ProviderName, x.FileSha256, x.ImportedAtUtc)).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FundPortfolioReportIssueResult>> FindIssuesAsync(Guid reportId, CancellationToken cancellationToken)
    {
        return await dbContext.FundPortfolioExtractionIssues.AsNoTracking().Where(x => x.ReportId == reportId).OrderByDescending(x => x.Severity).ThenBy(x => x.Id).Select(x => new FundPortfolioReportIssueResult(
            x.Id, x.ReportId, x.SheetId, x.Severity, x.IssueCode, x.SourceAddress, x.RawValue, x.Message, x.CreatedAtUtc)).ToListAsync(cancellationToken);
    }
}
