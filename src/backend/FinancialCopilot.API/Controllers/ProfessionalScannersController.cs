using FinancialCopilot.API.Contracts;
using FinancialCopilot.API.Security;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.FinancialData.ProfessionalScanners;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/scanners")]
[Authorize(Policy = AuthorizationPolicies.AiFacade)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class ProfessionalScannersController(
    ICurrentActorContext actorContext,
    IProfessionalScannerUseCases useCases) : ControllerBase
{
    [HttpGet("catalog")]
    public ActionResult<ProfessionalCatalogPage> ListCatalog(
        [FromQuery] ProfessionalFilterCategory? category = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20) =>
        Ok(useCases.ListCatalog(new ProfessionalCatalogQuery(category, search, page, pageSize)));

    [HttpGet("catalog/{code}")]
    public ActionResult<ProfessionalFilterDefinition> GetFilter(string code, [FromQuery] string? version = null) =>
        Try<ProfessionalFilterDefinition>(() => Ok(useCases.GetFilter(code, version)));

    [HttpGet("resolve")]
    public ActionResult<ProfessionalAliasResolution> ResolveAlias([FromQuery] string text) =>
        Ok(useCases.ResolveAlias(text));

    [HttpPost("catalog/{code}/execute")]
    public async Task<ActionResult<ProfessionalScannerExecutionResult>> Execute(
        string code, ExecuteProfessionalFilterRequest request, CancellationToken cancellationToken) =>
        await TryAsync<ProfessionalScannerExecutionResult>(async () => Ok(await useCases.ExecuteAsync(new ProfessionalExecuteCommand(
            actorContext.Actor, code, request.FilterVersion, request.Parameters, request.FromDate, request.ToDate,
            Scope(request.Scope), request.Page, request.PageSize, CorrelationId()), cancellationToken)));

    [HttpGet("saved")]
    public async Task<ActionResult<IReadOnlyCollection<SavedFilterDto>>> ListSaved(
        CancellationToken cancellationToken, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        Ok(await useCases.ListSavedAsync(actorContext.Actor, page, pageSize, cancellationToken));

    [HttpPost("saved")]
    public async Task<ActionResult<SavedFilterDto>> Save(
        SaveProfessionalFilterRequest request, CancellationToken cancellationToken) =>
        await TryAsync<SavedFilterDto>(async () => Ok(await useCases.SaveAsync(new SaveProfessionalFilterCommand(
            actorContext.Actor, request.Name, request.FilterCodeOrAlias, request.FilterVersion, request.Parameters),
            cancellationToken)));

    [HttpPut("saved/{id:guid}")]
    public async Task<ActionResult<SavedFilterDto>> Update(
        Guid id, UpdateProfessionalFilterRequest request, CancellationToken cancellationToken) =>
        await TryAsync<SavedFilterDto>(async () => Ok(await useCases.UpdateAsync(new UpdateProfessionalFilterCommand(
            actorContext.Actor, id, request.ExpectedVersion, request.Name, request.FilterCodeOrAlias,
            request.FilterVersion, request.Parameters), cancellationToken)));

    [HttpDelete("saved/{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id, [FromQuery] int expectedVersion, CancellationToken cancellationToken) =>
        await DeleteCoreAsync(id, expectedVersion, cancellationToken);

    [HttpPost("saved/{id:guid}/run")]
    public async Task<ActionResult<ProfessionalScannerExecutionResult>> RunSaved(
        Guid id, RunSavedProfessionalFilterRequest request, CancellationToken cancellationToken) =>
        await TryAsync<ProfessionalScannerExecutionResult>(async () => Ok(await useCases.RunSavedAsync(new RunSavedProfessionalFilterCommand(
            actorContext.Actor, id, request.FromDate, request.ToDate, Scope(request.Scope),
            request.Page, request.PageSize, CorrelationId()), cancellationToken)));

    private string CorrelationId() => Request.Headers.TryGetValue("X-Correlation-ID", out var value) &&
        !string.IsNullOrWhiteSpace(value) ? value.ToString() : HttpContext.TraceIdentifier;

    private static ProfessionalScannerScope Scope(ProfessionalScannerScopeRequest? request) =>
        new(request?.IndustryCode, request?.InstrumentClass);

    private ActionResult<T> Try<T>(Func<ActionResult<T>> action)
    {
        try { return action(); }
        catch (ProfessionalScannerValidationException exception)
        { ModelState.AddModelError("scanner", exception.Message); return ValidationProblem(ModelState); }
    }

    private async Task<ActionResult<T>> TryAsync<T>(Func<Task<ActionResult<T>>> action)
    {
        try { return await action(); }
        catch (ProfessionalScannerValidationException exception)
        { ModelState.AddModelError("scanner", exception.Message); return ValidationProblem(ModelState); }
    }

    private async Task<IActionResult> DeleteCoreAsync(Guid id, int expectedVersion, CancellationToken cancellationToken)
    {
        try
        {
            await useCases.DeleteAsync(new DeleteProfessionalFilterCommand(actorContext.Actor, id, expectedVersion), cancellationToken);
            return NoContent();
        }
        catch (ProfessionalScannerValidationException exception)
        { ModelState.AddModelError("scanner", exception.Message); return ValidationProblem(ModelState); }
    }
}
