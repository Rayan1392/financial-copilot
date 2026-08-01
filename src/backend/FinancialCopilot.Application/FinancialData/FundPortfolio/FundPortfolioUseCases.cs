using FinancialCopilot.Domain.Financial.FundPortfolio;

namespace FinancialCopilot.Application.FinancialData.FundPortfolio;

public sealed class CreateOrResolveInvestmentFundUseCase(
    IInvestmentFundRepository repository) : ICreateOrResolveInvestmentFundUseCase
{
    public async Task<CreateOrResolveInvestmentFundResult> ExecuteAsync(CreateOrResolveInvestmentFundRequest request, CancellationToken cancellationToken)
    {
        var normalized = FundPortfolioFundNamePolicy.Normalize(request.FundName);
        var candidates = await repository.FindCandidatesAsync(request.ProviderName, normalized, request.ExternalFundId, cancellationToken);
        if (candidates.Count == 1) return new(FundResolutionStatus.Resolved, candidates[0], candidates);
        if (candidates.Count > 1) return new(FundResolutionStatus.Ambiguous, null, candidates, "Multiple funds match the supplied identity; review is required.");
        if (!request.AllowCreate) return new(FundResolutionStatus.NeedsReview, null, [], "No governed fund identity match exists.");
        var fund = new InvestmentFund(Guid.NewGuid(), request.FundName, normalized, request.ProviderName, request.ExternalFundId, request.FundSymbol);
        return new(FundResolutionStatus.Created, await repository.AddAsync(fund, cancellationToken), []);
    }
}

public sealed class IngestFundPortfolioWorkbookUseCase(
    ICreateOrResolveInvestmentFundUseCase fundResolver,
    IFundPortfolioRawWorkbookStore rawStore,
    IFundPortfolioWorkbookParser parser,
    IFundPortfolioReportRepository reports,
    IEnumerable<IFundPortfolioSectionNormalizer> sectionNormalizers,
    IFundPortfolioIngestionTelemetrySink telemetry) : IIngestFundPortfolioWorkbookUseCase
{
    public async Task<IngestFundPortfolioWorkbookResult> ExecuteAsync(IngestFundPortfolioWorkbookRequest request, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var correlationId = Guid.NewGuid();
        if (request.Workbook is null || !request.Workbook.CanRead) throw new ArgumentException("A readable workbook is required.", nameof(request));
        if (!string.Equals(Path.GetExtension(request.OriginalFileName), ".xlsx", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Only .xlsx workbooks are supported.");
        if (!string.Equals(request.ContentType, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(request.ContentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unsupported workbook MIME type.");

        await using var copy = new MemoryStream();
        await request.Workbook.CopyToAsync(copy, cancellationToken);
        var bytes = copy.ToArray();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        var duplicate = await reports.FindByHashAsync(request.ProviderName, hash, cancellationToken);
        if (duplicate is not null)
        {
            telemetry.Record(new(correlationId, FundResolutionStatus.Resolved, hash[..12], "iran-fund-portfolio-workbook-v1", 0, 0, 0, 0,
                FundPortfolioParseStatus.Parsed, DateTimeOffset.UtcNow - startedAt));
            return new(duplicate.Value.ReportId, FundResolutionStatus.Resolved, FundPortfolioParseStatus.Parsed, true, duplicate.Value.SourceRevision, hash, "Duplicate workbook import.");
        }

        var fund = await fundResolver.ExecuteAsync(new(request.ProviderName, request.FundName, request.ExternalFundId, request.FundSymbol), cancellationToken);
        if (fund.Fund is null) throw new InvalidOperationException(fund.Message ?? "Fund identity requires review.");
        var reportId = Guid.NewGuid();
        copy.Position = 0;
        var stored = await rawStore.StoreAsync(reportId, request.OriginalFileName, request.ContentType, copy, hash, cancellationToken);
        var periodEnd = request.KnownPeriod?.PeriodEndDate;
        var revision = await reports.GetNextRevisionAsync(fund.Fund.Id, request.ProviderName, periodEnd, cancellationToken);
        copy.Position = 0;
        var envelope = await parser.ParseAsync(new(reportId, fund.Fund.Id, request.ProviderName, request.OriginalFileName, hash, "iran-fund-portfolio-workbook-v1", copy, request.KnownPeriod), cancellationToken);
        var superseded = revision > 1 ? await reports.FindLatestReportIdAsync(fund.Fund.Id, request.ProviderName, periodEnd, cancellationToken) : null;
        var saved = await reports.SaveParsedReportAsync(fund.Fund, request, stored, envelope, revision, superseded, cancellationToken);
        if (!saved)
        {
            var concurrentDuplicate = await reports.FindByHashAsync(request.ProviderName, hash, cancellationToken)
                ?? throw new InvalidOperationException("Workbook import conflicted but the winning report could not be located.");
            return new(concurrentDuplicate.ReportId, fund.Status, FundPortfolioParseStatus.Parsed, true, concurrentDuplicate.SourceRevision, hash, "Duplicate workbook import.");
        }
        foreach (var normalizer in sectionNormalizers)
            await normalizer.NormalizeAsync(envelope, cancellationToken);
        telemetry.Record(new(correlationId, fund.Status, hash[..12], envelope.ParserProfileVersion, envelope.Sheets.Count,
            envelope.Sheets.Count(sheet => sheet.LogicalSheetType == FundWorkbookLogicalSheetType.Unclassified), envelope.Issues.Count,
            envelope.Issues.Count(issue => issue.Severity is FundExtractionIssueSeverity.Error or FundExtractionIssueSeverity.Fatal), envelope.Status,
            DateTimeOffset.UtcNow - startedAt));
        return new(reportId, fund.Status, envelope.Status, false, revision, hash);
    }
}

public sealed class GetFundPortfolioReportStatusUseCase(IFundPortfolioReportRepository reports) : IGetFundPortfolioReportStatusUseCase
{
    public Task<FundPortfolioReportStatusResult?> ExecuteAsync(Guid reportId, CancellationToken cancellationToken) => reports.FindStatusAsync(reportId, cancellationToken);
}

public sealed class GetFundPortfolioReportIssuesUseCase(IFundPortfolioReportRepository reports) : IGetFundPortfolioReportIssuesUseCase
{
    public Task<IReadOnlyList<FundPortfolioReportIssueResult>> ExecuteAsync(Guid reportId, CancellationToken cancellationToken) => reports.FindIssuesAsync(reportId, cancellationToken);
}
