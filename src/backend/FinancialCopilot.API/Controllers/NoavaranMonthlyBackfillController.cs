using FinancialCopilot.API.Contracts;
using FinancialCopilot.API.Security;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/admin/noavaran-current/monthly-backfill")]
[Authorize(Policy = AuthorizationPolicies.NoavaranMonthlyBackfill)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class NoavaranMonthlyBackfillController(
    ISingleCompanyMonthlyIngestionService singleCompanyIngestion,
    FinancialIngestionDbContext dbContext) : ControllerBase
{
    [HttpPost("single-company-month")]
    public async Task<ActionResult<AdminMonthlyActivitySingleCompanyMonthDirectResponse>> RunSingleCompanyMonth(
        [FromBody] AdminMonthlyActivitySingleCompanyMonthDirectRequest request,
        CancellationToken cancellationToken)
    {
        var companyId = request.CompanyId;

        if (!string.IsNullOrWhiteSpace(request.Symbol))
        {
            var externalCompanyId = await dbContext.NoavaranEligibleCompanies
                .AsNoTracking()
                .Where(company => company.TseSymbol == request.Symbol.Trim())
                .Select(company => company.ExternalCompanyId)
                .FirstOrDefaultAsync(cancellationToken);

            if (externalCompanyId is null)
                return NotFound(new { message = $"No eligible company was found for symbol '{request.Symbol}'." });

            if (!int.TryParse(externalCompanyId, out companyId))
                return Problem("The resolved ExternalCompanyId is not a valid integer.");
        }

        if (companyId <= 0)
            ModelState.AddModelError(nameof(request.CompanyId), "CompanyId must be a positive integer.");
        if (request.ShamsiYear is < 1404 or > 1500)
            ModelState.AddModelError(nameof(request.ShamsiYear), "ShamsiYear must be between 1404 and 1500 for NADPCO monthly activity.");
        if (request.ShamsiMonth is < 1 or > 12)
            ModelState.AddModelError(nameof(request.ShamsiMonth), "ShamsiMonth must be between 1 and 12.");
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var result = await singleCompanyIngestion.ExecuteDirectAsync(
            new SingleCompanyMonthlyDirectIngestionRequest(companyId, request.ShamsiYear, request.ShamsiMonth),
            cancellationToken);

        return Ok(new AdminMonthlyActivitySingleCompanyMonthDirectResponse(
            result.Run.Id, companyId, request.ShamsiYear, request.ShamsiMonth,
            result.Run.Status.ToString(), result.AlreadyProcessed, result.Run.ProcessedRecords,
            result.Run.ErrorCount, result.Run.ErrorMessage, result.Run.CompletedAt));
    }
}
