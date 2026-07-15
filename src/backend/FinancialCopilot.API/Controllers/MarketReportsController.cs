using FinancialCopilot.Application.FinancialData.MarketReports;
using FinancialCopilot.Domain.Financial.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/market-reports")]
[AllowAnonymous]
public sealed class MarketReportsController(IMarketReportService reports) : ControllerBase
{
    [HttpGet("latest")]
    public async Task<ActionResult<MarketReportView>> GetLatest(CancellationToken cancellationToken)
    {
        var report = await reports.GetLatestPublicAsync(cancellationToken);
        return report is null ? NotFound() : Ok(report);
    }

    [HttpGet("history")]
    public async Task<ActionResult<MarketReportHistoryPage>> GetHistory(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] MarketReportStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await reports.GetPublicHistoryAsync(
                new MarketReportHistoryQuery(from, to, status, page, pageSize), cancellationToken));
        }
        catch (MarketReportValidationException exception)
        {
            ModelState.AddModelError("history", exception.Message);
            return ValidationProblem(ModelState);
        }
    }

    [HttpGet("{reportId:guid}")]
    public async Task<ActionResult<MarketReportView>> GetVersion(
        Guid reportId,
        CancellationToken cancellationToken)
    {
        var report = await reports.GetPublicVersionAsync(reportId, cancellationToken);
        return report is null ? NotFound() : Ok(report);
    }
}
