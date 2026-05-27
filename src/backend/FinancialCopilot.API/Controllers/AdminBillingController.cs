using FinancialCopilot.API.Contracts;
using FinancialCopilot.API.Security;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/admin/billing/customers/{customerAccountId:guid}")]
[Authorize(Policy = AuthorizationPolicies.BillingAdmin)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class AdminBillingController(
    ICurrentActorContext actorContext,
    IBillingAdministrationService administration,
    IWalletService wallets,
    IApiUsageReportService usageReports,
    IInvoiceService invoices,
    ICreditAdjustmentService adjustments,
    IUsageRefundService refunds,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("wallet")]
    public async Task<ActionResult<AdminBillingWalletResponse>> GetWallet(
        Guid customerAccountId,
        CancellationToken cancellationToken)
    {
        var account = await GetTenantAccountAsync(customerAccountId, cancellationToken);
        var wallet = await wallets.GetSnapshotAsync(customerAccountId, cancellationToken);

        return Ok(new AdminBillingWalletResponse(
            account.Id,
            account.AccountType.ToString(),
            account.BillingMode.ToString(),
            wallet.Balance,
            wallet.ReservedAmount,
            account.CreditLine?.ApprovedLimit ?? 0,
            account.CreditLine?.WarningThreshold ?? 0,
            account.GetAvailableSpendingCapacity(wallet),
            wallet.UpdatedAt));
    }

    [HttpGet("usage")]
    public async Task<ActionResult<UsageSummaryResponse>> GetUsage(
        Guid customerAccountId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var periodTo = to ?? timeProvider.GetUtcNow();
        var periodFrom = from ?? periodTo.AddDays(-30);

        if (periodFrom > periodTo)
        {
            ModelState.AddModelError(nameof(from), "Usage period start must not be after its end.");
            return ValidationProblem(ModelState);
        }

        var account = await GetTenantAccountAsync(customerAccountId, cancellationToken);
        var wallet = await wallets.GetSnapshotAsync(customerAccountId, cancellationToken);
        var entries = await usageReports.QueryUsageAsync(
            customerAccountId,
            periodFrom,
            periodTo,
            cancellationToken);

        return Ok(new UsageSummaryResponse(
            account.AccountType.ToString(),
            account.BillingMode.ToString(),
            wallet.Balance,
            wallet.ReservedAmount,
            account.GetAvailableSpendingCapacity(wallet),
            wallet.UpdatedAt,
            periodFrom,
            periodTo,
            entries.Select(entry => new UsageEntryResponse(
                entry.OperationCode,
                entry.EntryType.ToString(),
                entry.CreditsCharged,
                entry.PricingPolicyVersion,
                entry.OccurredAt,
                entry.ExternalUserId,
                entry.CompletionStatus)).ToArray()));
    }

    [HttpGet("invoices")]
    public async Task<ActionResult<AdminInvoiceAccountResponse>> GetInvoiceAccount(
        Guid customerAccountId,
        CancellationToken cancellationToken)
    {
        await GetTenantAccountAsync(customerAccountId, cancellationToken);
        var invoice = await invoices.GetInvoiceAccountAsync(customerAccountId, cancellationToken);

        return Ok(new AdminInvoiceAccountResponse(
            invoice.CustomerAccountId,
            invoice.LegalName,
            invoice.BillingEmail,
            invoice.SettlementTerms));
    }

    [HttpPost("adjustments")]
    public async Task<ActionResult<AdminCreditAdjustmentResponse>> ApplyCreditAdjustment(
        Guid customerAccountId,
        [FromBody] AdminCreditAdjustmentRequest request,
        CancellationToken cancellationToken)
    {
        var account = await GetTenantAccountAsync(customerAccountId, cancellationToken);
        var actor = actorContext.Actor;
        var result = await adjustments.ApplyAsync(
            new CreditAdjustmentCommand(
                customerAccountId,
                actor.ActorId,
                actor.TenantId,
                request.Credits,
                request.Reason,
                request.IdempotencyKey),
            cancellationToken);

        return Ok(new AdminCreditAdjustmentResponse(
            result.LedgerEntry.Id,
            result.LedgerEntry.CreditsCharged,
            result.Wallet.Balance,
            account.GetAvailableSpendingCapacity(result.Wallet),
            result.AlreadyApplied));
    }

    [HttpPost("refunds")]
    public async Task<ActionResult<AdminUsageRefundResponse>> RefundUsage(
        Guid customerAccountId,
        [FromBody] AdminUsageRefundRequest request,
        CancellationToken cancellationToken)
    {
        var account = await GetTenantAccountAsync(customerAccountId, cancellationToken);
        var actor = actorContext.Actor;
        var result = await refunds.RefundAsync(
            new UsageRefundCommand(
                customerAccountId,
                actor.ActorId,
                actor.TenantId,
                request.OriginalChargeIdempotencyKey,
                request.Credits,
                request.Reason,
                request.IdempotencyKey),
            cancellationToken);

        return Ok(new AdminUsageRefundResponse(
            result.LedgerEntry.Id,
            result.LedgerEntry.RelatedEntryId!.Value,
            result.LedgerEntry.CreditsCharged,
            result.Wallet.Balance,
            account.GetAvailableSpendingCapacity(result.Wallet),
            result.AlreadyApplied));
    }

    private Task<CustomerAccount> GetTenantAccountAsync(
        Guid customerAccountId,
        CancellationToken cancellationToken) =>
        administration.GetTenantAccountAsync(
            actorContext.Actor.TenantId,
            customerAccountId,
            cancellationToken);
}
