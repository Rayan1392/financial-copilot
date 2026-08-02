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
    IFundPortfolioIngestionTelemetrySink telemetry,
    IFundPortfolioAuditSink audit,
    IFundPortfolioAnalyticsRecalculationCoordinator? recalculationCoordinator = null) : IIngestFundPortfolioWorkbookUseCase
{
    public async Task<IngestFundPortfolioWorkbookResult> ExecuteAsync(IngestFundPortfolioWorkbookRequest request, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var correlationId = Guid.TryParse(request.CorrelationId, out var suppliedCorrelationId) ? suppliedCorrelationId : Guid.NewGuid();
        Guid? reportId = null;
        try
        {
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
            await WriteAuditBestEffortAsync(FundPortfolioAuditPolicy.Duplicate(duplicate.Value.ReportId, correlationId, duplicate.Value.SourceRevision), cancellationToken);
            return new(duplicate.Value.ReportId, FundResolutionStatus.Resolved, FundPortfolioParseStatus.Parsed, true, duplicate.Value.SourceRevision, hash, "Duplicate workbook import.");
        }

        var fund = await fundResolver.ExecuteAsync(new(request.ProviderName, request.FundName, request.ExternalFundId, request.FundSymbol), cancellationToken);
        if (fund.Fund is null) throw new InvalidOperationException(fund.Message ?? "Fund identity requires review.");
        reportId = Guid.NewGuid();
        copy.Position = 0;
        var stored = await rawStore.StoreAsync(reportId.Value, request.OriginalFileName, request.ContentType, copy, hash, cancellationToken);
        var periodEnd = request.KnownPeriod?.PeriodEndDate;
        var revision = await reports.GetNextRevisionAsync(fund.Fund.Id, request.ProviderName, periodEnd, cancellationToken);
        copy.Position = 0;
        var envelope = (await parser.ParseAsync(new(reportId.Value, fund.Fund.Id, request.ProviderName, request.OriginalFileName, hash, "iran-fund-portfolio-workbook-v1", copy, request.KnownPeriod), cancellationToken)) with { CorrelationId = correlationId.ToString("N") };
        var superseded = revision > 1 ? await reports.FindLatestReportIdAsync(fund.Fund.Id, request.ProviderName, periodEnd, cancellationToken) : null;
        var saved = await reports.SaveParsedReportAsync(fund.Fund, request, stored, envelope, revision, superseded, cancellationToken);
        if (!saved)
        {
            var concurrentDuplicate = await reports.FindByHashAsync(request.ProviderName, hash, cancellationToken)
                ?? throw new InvalidOperationException("Workbook import conflicted but the winning report could not be located.");
            await WriteAuditBestEffortAsync(FundPortfolioAuditPolicy.Duplicate(concurrentDuplicate.ReportId, correlationId, concurrentDuplicate.SourceRevision), cancellationToken);
            return new(concurrentDuplicate.ReportId, fund.Status, FundPortfolioParseStatus.Parsed, true, concurrentDuplicate.SourceRevision, hash, "Duplicate workbook import.");
        }
        foreach (var normalizer in sectionNormalizers)
            await normalizer.NormalizeAsync(envelope, cancellationToken);
        if (recalculationCoordinator is not null && envelope.Period.PeriodEndDate is { } scheduledPeriodEnd &&
            envelope.Status is FundPortfolioParseStatus.Parsed or FundPortfolioParseStatus.PartiallyParsed)
        {
            try
            {
                await recalculationCoordinator.RequestAsync(
                    new(fund.Fund.Id, reportId.Value, scheduledPeriodEnd,
                        FundPortfolioAnalyticsRecalculationReason.NormalizedSectionsCompleted,
                        $"{hash}:revision:{revision}", FundPortfolioAnalyticsCalculationPolicy.CalculationVersion),
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Recalculation is failure-isolated. Normalized source data remains committed.
            }
        }
        telemetry.Record(new(correlationId, fund.Status, hash[..12], envelope.ParserProfileVersion, envelope.Sheets.Count,
            envelope.Sheets.Count(sheet => sheet.LogicalSheetType == FundWorkbookLogicalSheetType.Unclassified), envelope.Issues.Count,
            envelope.Issues.Count(issue => issue.Severity is FundExtractionIssueSeverity.Error or FundExtractionIssueSeverity.Fatal), envelope.Status,
            DateTimeOffset.UtcNow - startedAt,
            envelope.Issues.Count(issue => issue.IssueCode == "EXCEL_ERROR_VALUE"),
            envelope.Issues.Count(issue => issue.IssueCode == "INVALID_JALALI_DATE"),
            envelope.Status == FundPortfolioParseStatus.PartiallyParsed ? 1 : 0));
        await WriteAuditBestEffortAsync(FundPortfolioAuditPolicy.Ingested(reportId.Value, correlationId, revision, envelope.Status), cancellationToken);
        if (revision > 1 && superseded is not null)
        {
            await WriteAuditBestEffortAsync(FundPortfolioAuditPolicy.CorrectedRevision(reportId.Value, correlationId, revision, superseded), cancellationToken);
            await WriteAuditBestEffortAsync(FundPortfolioAuditPolicy.Superseded(reportId.Value, correlationId, revision, superseded.Value), cancellationToken);
        }
        return new(reportId.Value, fund.Status, envelope.Status, false, revision, hash);
        }
        catch (Exception exception)
        {
            await WriteAuditBestEffortAsync(FundPortfolioAuditPolicy.Failure(reportId, correlationId, exception.GetType().Name), cancellationToken);
            throw;
        }
    }

    private async Task WriteAuditBestEffortAsync(FundPortfolioAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        try { await audit.WriteAsync(auditEvent, cancellationToken); }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { }
    }
}

public sealed class GetFundPortfolioReportStatusUseCase(IFundPortfolioReportRepository reports) : IGetFundPortfolioReportStatusUseCase
{
    public Task<FundPortfolioReportStatusResult?> ExecuteAsync(Guid reportId, CancellationToken cancellationToken) => reports.FindStatusAsync(reportId, cancellationToken);
}

public sealed class GetFundPortfolioReportIssuesUseCase(IFundPortfolioReportRepository reports) : IGetFundPortfolioReportIssuesUseCase
{
    public Task<FundPortfolioReportIssuePage> ExecuteAsync(Guid reportId, int page = 1, int pageSize = 100, FundExtractionIssueSeverity? severity = null, string? issueCode = null, CancellationToken cancellationToken = default) => reports.FindIssuesAsync(reportId, page, pageSize, severity, issueCode, cancellationToken);
}
