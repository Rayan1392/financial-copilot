using FinancialCopilot.API.Security;
using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Infrastructure.Financial.FundPortfolio;
using FinancialCopilot.Domain.Financial.FundPortfolio;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/admin/fund-portfolio-reports")]
[Authorize(Policy = AuthorizationPolicies.DataAdmin)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class FundPortfolioAdminController(
    ManualUploadFundPortfolioReportSource manualSource,
    IStartFundPortfolioImportRunUseCase startRun,
    IFundPortfolioReportSourceRegistry sourceRegistry,
    IFundPortfolioImportRunRepository runs,
    IGetFundPortfolioReportStatusUseCase reportStatus,
    IGetFundPortfolioReportIssuesUseCase reportIssues,
    IQueryFundPortfolioReportsUseCase reportQueries,
    IReprocessFundPortfolioReportUseCase reprocess,
    IFundPortfolioAuditSink audit,
    ICurrentActorContext actor,
    IFundPortfolioOperationalTelemetry telemetry) : ControllerBase
{
    [HttpPost("uploads")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<ActionResult<FundPortfolioImportRunResult>> Upload(
        [FromForm] IFormFile? file,
        [FromForm] string? fundName,
        [FromForm] string? providerName,
        CancellationToken cancellationToken)
    {
        if (file is null) return BadRequest("A workbook file is required.");
        FundPortfolioImportRunResult result;
        try { result = await CreateManualRunAsync([file], fundName, providerName, cancellationToken); }
        catch (InvalidDataException exception) { return BadRequest(exception.Message); }
        await audit.WriteAsync(new("upload", actor.Actor.ActorId.ToString("N"), result.RunId, null, null, result.CorrelationId, "Manual fund portfolio workbook upload queued."), cancellationToken);
        return AcceptedAtAction(nameof(GetRun), new { runId = result.RunId }, result);
    }

    [HttpPost("bulk-import")]
    [RequestSizeLimit(250 * 1024 * 1024)]
    public async Task<ActionResult<FundPortfolioImportRunResult>> BulkImport(
        [FromForm] List<IFormFile>? files,
        [FromForm] string? fundName,
        [FromForm] string? providerName,
        CancellationToken cancellationToken)
    {
        if (files is null || files.Count == 0) return BadRequest("At least one workbook file is required.");
        if (files.Count > 50) return BadRequest("A bulk import cannot contain more than 50 files.");
        FundPortfolioImportRunResult result;
        try { result = await CreateManualRunAsync(files, fundName, providerName, cancellationToken); }
        catch (InvalidDataException exception) { return BadRequest(exception.Message); }
        await audit.WriteAsync(new("bulk-upload", actor.Actor.ActorId.ToString("N"), result.RunId, null, null, result.CorrelationId, $"{result.ItemCount} fund portfolio workbooks queued."), cancellationToken);
        return AcceptedAtAction(nameof(GetRun), new { runId = result.RunId }, result);
    }

    [HttpPost("discover")]
    public async Task<ActionResult<FundPortfolioImportRunResult>> Discover(
        [FromBody] FundPortfolioDiscoverRequest request,
        CancellationToken cancellationToken)
    {
        var source = sourceRegistry.Get(request.ProviderName);
        if (!source.IsAvailable) return Conflict(new { code = "SOURCE_UNAVAILABLE", message = source.UnavailableReason });
        var page = await source.DiscoverAsync(new(request.ProviderName, request.ModifiedAfterUtc, Math.Clamp(request.MaximumItems, 1, 500)), cancellationToken);
        if (page.Items.Count == 0) return Accepted(new FundPortfolioImportRunResult(Guid.Empty, 0, FundPortfolioImportRunStatus.Completed, Guid.NewGuid().ToString("N")));
        var result = await startRun.ExecuteAsync(new(FundPortfolioImportTriggerType.BulkBackfill, request.ProviderName, null, page.Items), cancellationToken);
        await audit.WriteAsync(new("discover", actor.Actor.ActorId.ToString("N"), result.RunId, null, null, result.CorrelationId, $"{result.ItemCount} source objects discovered."), cancellationToken);
        return AcceptedAtAction(nameof(GetRun), new { runId = result.RunId }, result);
    }

    [HttpGet("source-status/{providerName}")]
    public ActionResult<object> SourceStatus(string providerName)
    {
        var source = sourceRegistry.Get(providerName);
        return Ok(new { providerName = source.ProviderName, available = source.IsAvailable, unavailableReason = source.UnavailableReason });
    }

    [HttpGet("health")]
    public async Task<ActionResult<object>> Health(CancellationToken cancellationToken)
    {
        var runPage = await runs.ListRunsAsync(new(Page: 1, PageSize: 1), cancellationToken);
        var queued = await runs.ListItemsAsync(new(null, 1, 1, FundPortfolioImportItemStatus.Queued), cancellationToken);
        var retryable = await runs.ListItemsAsync(new(null, 1, 1, FundPortfolioImportItemStatus.RetryableFailure), cancellationToken);
        var pendingReviews = await HttpContext.RequestServices.GetRequiredService<IFundPortfolioMappingReviewRepository>().ListPageAsync(FundPortfolioMappingReviewStatus.Pending, 1, 1, cancellationToken);
        var source = sourceRegistry.Get("ConfiguredLocalStorage");
        return Ok(new { sourceAvailable = source.IsAvailable, sourceReason = source.UnavailableReason, totalRuns = runPage.TotalCount, queuedItems = queued.TotalCount, retryableItems = retryable.TotalCount, pendingReviews = pendingReviews.TotalCount, lastRunAtUtc = runPage.Items.FirstOrDefault()?.StartedAtUtc });
    }

    [HttpGet("runs/{runId:guid}")]
    public async Task<ActionResult<FundPortfolioImportRunView>> GetRun(Guid runId, CancellationToken cancellationToken)
    {
        var result = await runs.GetRunAsync(runId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("runs")]
    public async Task<ActionResult<FundPortfolioImportRunPage>> ListRuns([FromQuery] FundPortfolioImportRunQuery query, CancellationToken cancellationToken) => Ok(await runs.ListRunsAsync(query, cancellationToken));

    [HttpGet("items")]
    public async Task<ActionResult<FundPortfolioImportItemPage>> ListItems([FromQuery] FundPortfolioImportItemQuery query, CancellationToken cancellationToken) => Ok(await runs.ListItemsAsync(query, cancellationToken));

    [HttpGet]
    public async Task<ActionResult<FundPortfolioReportPage>> ListReports([FromQuery] FundPortfolioReportQuery query, CancellationToken cancellationToken) => Ok(await reportQueries.ListAsync(query, cancellationToken));

    [HttpGet("{reportId:guid}")]
    public async Task<ActionResult<FundPortfolioReportStatusResult>> GetReport(Guid reportId, CancellationToken cancellationToken)
    {
        var result = await reportStatus.ExecuteAsync(reportId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("{reportId:guid}/detail")]
    public async Task<ActionResult<FundPortfolioReportDetail>> GetReportDetail(Guid reportId, CancellationToken cancellationToken)
    {
        var result = await reportQueries.GetDetailAsync(reportId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("{reportId:guid}/issues")]
    public async Task<ActionResult<FundPortfolioReportIssuePage>> GetIssues(Guid reportId, [FromQuery] int page = 1, [FromQuery] int pageSize = 100, [FromQuery] FundExtractionIssueSeverity? severity = null, [FromQuery] string? issueCode = null, CancellationToken cancellationToken = default)
    {
        var result = await reportStatus.ExecuteAsync(reportId, cancellationToken);
        if (result is null) return NotFound();
        return Ok(await reportIssues.ExecuteAsync(reportId, page, pageSize, severity, issueCode, cancellationToken));
    }

    [HttpPost("{reportId:guid}/reprocess")]
    public async Task<IActionResult> Reprocess(Guid reportId, [FromBody] FundPortfolioReprocessRequest request, CancellationToken cancellationToken)
    {
        if (!request.Confirm) return BadRequest(new { code = "CONFIRMATION_REQUIRED", message = "Explicit confirmation is required before reprocessing." });
        var status = await reprocess.ExecuteAsync(new(reportId, string.IsNullOrWhiteSpace(request.ParserProfileVersion) ? "iran-fund-portfolio-workbook-v1" : request.ParserProfileVersion), cancellationToken);
        if (status is not null) await audit.WriteAsync(new("reprocess", actor.Actor.ActorId.ToString("N"), null, reportId, null, Guid.NewGuid().ToString("N"), "Fund portfolio report reprocessed."), cancellationToken);
        return status is null ? NotFound() : Accepted(new { reportId, status });
    }

    [HttpPost("reprocess-bulk")]
    public async Task<IActionResult> ReprocessBulk([FromBody] FundPortfolioBulkReprocessRequest request, CancellationToken cancellationToken)
    {
        if (!request.Confirm) return BadRequest(new { code = "CONFIRMATION_REQUIRED", message = "Explicit confirmation is required before bulk reprocessing." });
        if (request.ReportIds.Count is 0 or > 50) return BadRequest("Bulk reprocess must contain between 1 and 50 reports.");
        var results = new List<object>();
        foreach (var reportId in request.ReportIds.Distinct())
        {
            var status = await reprocess.ExecuteAsync(new(reportId, "iran-fund-portfolio-workbook-v1"), cancellationToken);
            if (status is not null) { results.Add(new { reportId, status }); await audit.WriteAsync(new("bulk-reprocess", actor.Actor.ActorId.ToString("N"), null, reportId, null, Guid.NewGuid().ToString("N"), "Fund portfolio report reprocessed in confirmed bulk operation."), cancellationToken); }
        }
        return Accepted(results);
    }

    [HttpPost("runs/{runId:guid}/cancel")]
    public async Task<IActionResult> CancelRun(Guid runId, [FromBody] FundPortfolioCancellationRequest request, CancellationToken cancellationToken)
    {
        if (!request.Confirm) return BadRequest(new { code = "CONFIRMATION_REQUIRED", message = "Explicit confirmation is required before cancellation." });
        var changed = await runs.CancelRunAsync(runId, cancellationToken);
        if (changed == 0 && await runs.GetRunAsync(runId, cancellationToken) is null) return NotFound();
        await audit.WriteAsync(new("cancel", actor.Actor.ActorId.ToString("N"), runId, null, null, Guid.NewGuid().ToString("N"), $"Cancelled {changed} queued or running import items."), cancellationToken);
        return Accepted(new { runId, cancelledItems = changed });
    }

    private async Task<FundPortfolioImportRunResult> CreateManualRunAsync(IReadOnlyList<IFormFile> files, string? fundName, string? providerName, CancellationToken cancellationToken)
    {
        var descriptors = new List<FundPortfolioReportSourceDescriptor>(files.Count);
        foreach (var file in files)
        {
            if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Only .xlsx workbooks are accepted.");
            if (file.Length <= 0 || file.Length > 50 * 1024 * 1024) throw new InvalidDataException("Workbook size is outside the allowed range.");
            if (!string.Equals(file.ContentType, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(file.ContentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unsupported workbook MIME type.");
            await using var input = file.OpenReadStream();
            await using var memory = new MemoryStream();
            await input.CopyToAsync(memory, cancellationToken);
            telemetry.RecordUpload(memory.Length);
            descriptors.Add(manualSource.Register(new ManualFundPortfolioUpload(file.FileName, file.ContentType, memory.ToArray(), fundName)));
        }
        if (!string.IsNullOrWhiteSpace(providerName) && !string.Equals(providerName, "ManualUpload", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Manual uploads must use the ManualUpload provider.");
        return await startRun.ExecuteAsync(new(FundPortfolioImportTriggerType.ManualUpload, "ManualUpload", null, descriptors), cancellationToken);
    }
}

public sealed record FundPortfolioDiscoverRequest(string ProviderName, DateTimeOffset? ModifiedAfterUtc = null, int MaximumItems = 100);
public sealed record FundPortfolioReprocessRequest(string? ParserProfileVersion, bool Confirm = false);
public sealed record FundPortfolioCancellationRequest(bool Confirm);
