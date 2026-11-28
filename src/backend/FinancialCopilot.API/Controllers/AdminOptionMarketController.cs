using FinancialCopilot.API.Security;
using FinancialCopilot.Application.FinancialData.Options;
using FinancialCopilot.Application.FinancialData.Providers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinancialCopilot.API.Controllers;

/// <summary>
/// Feature 167: operator-triggered option market syncs and the instrument stub symbol backfill.
/// Shares the route space of the other TSETMC admin operations.
/// </summary>
[ApiController]
[Route("api/v1/admin/tsetmc")]
[Authorize(Policy = AuthorizationPolicies.DataAdmin)]
public sealed class AdminOptionMarketController(
    IOptionMarketSyncService optionMarketSync,
    IInstrumentStubSymbolBackfill stubBackfill,
    ILogger<AdminOptionMarketController> logger) : ControllerBase
{
    [HttpPost("optioncontracts/sync")]
    public Task<ActionResult<AdminOptionMarketSyncResponse>> SyncOptionContracts(CancellationToken cancellationToken) =>
        RunAsync(service => service.SynchronizeContractsAsync(cancellationToken));

    [HttpPost("optionorderbook/sync")]
    public Task<ActionResult<AdminOptionMarketSyncResponse>> SyncOptionOrderBook(CancellationToken cancellationToken) =>
        RunAsync(service => service.SynchronizeOrderBookAsync(cancellationToken));

    /// <summary>Renames instruments still stored under their numeric code, from stored daily trade payloads.</summary>
    [HttpPost("instrument-stubs/backfill")]
    public async Task<ActionResult<InstrumentStubBackfillResult>> BackfillInstrumentStubSymbols(
        CancellationToken cancellationToken)
    {
        var result = await stubBackfill.BackfillAsync(cancellationToken);
        logger.LogInformation(
            "Admin {Actor} ran the TSETMC instrument stub backfill: {Renamed} of {Found} renamed.",
            User.Identity?.Name ?? "unknown", result.StubsRenamed, result.StubsFound);
        return Ok(result);
    }

    private async Task<ActionResult<AdminOptionMarketSyncResponse>> RunAsync(
        Func<IOptionMarketSyncService, Task<OptionMarketSyncResult>> sync)
    {
        if (!optionMarketSync.IsOperational)
            return Conflict(new { error = "TsetmcWebService is not operational. Enable it and configure credentials." });

        try
        {
            var result = await sync(optionMarketSync);
            return Ok(new AdminOptionMarketSyncResponse(
                result.Dataset, result.RowsFetched, result.RowsPersisted, result.RowsUnchanged,
                result.RowsRejected, result.RowsUnresolved, result.RowsRemoved, result.RowsIgnored,
                result.Duration.ToString("g")));
        }
        catch (FinancialProviderException exception)
        {
            // Stored data is untouched; the caller can retry.
            logger.LogWarning(exception, "Admin-triggered option market sync failed at the provider.");
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: exception.Message);
        }
    }
}

public sealed record AdminOptionMarketSyncResponse(
    string Dataset,
    int RowsFetched,
    int RowsPersisted,
    int RowsUnchanged,
    int RowsRejected,
    int RowsUnresolved,
    int RowsRemoved,
    int RowsIgnored,
    string Duration);
