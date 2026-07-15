using FinancialCopilot.API.Contracts;
using FinancialCopilot.API.Security;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Billing.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/admin/billing")]
[Authorize(Policy = AuthorizationPolicies.BillingAdmin)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class AdminBillingReceiptReviewsController(
    ICurrentActorContext actorContext,
    IBillingPurchaseUseCases purchaseUseCases) : ControllerBase
{
    [HttpPost("receipt-reviews/{checkoutId:guid}")]
    public async Task<ActionResult<BillingCheckoutResponse>> ReviewReceipt(
        Guid checkoutId,
        [FromBody] ReviewBillingReceiptRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var actor = actorContext.Actor;
            var checkout = await purchaseUseCases.ReviewReceiptAsync(
                new ReviewBillingReceiptCommand(
                    actor.ActorId,
                    actor.TenantId,
                    checkoutId,
                    request.ExpectedVersion,
                    request.Approved,
                    request.Reason,
                    request.IdempotencyKey ?? Request.Headers["Idempotency-Key"].FirstOrDefault() ??
                    $"{checkoutId:N}:review:{DateTimeOffset.UtcNow:O}",
                    HttpContext.TraceIdentifier),
                cancellationToken);
            return Ok(Map(checkout));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { error = exception.Message });
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { error = exception.Message });
        }
    }

    [HttpGet("reconciliation")]
    public async Task<ActionResult<BillingReconciliationResponse>> Reconcile(CancellationToken cancellationToken)
    {
        var actor = actorContext.Actor;
        var result = await purchaseUseCases.ReconcileAsync(actor.TenantId, cancellationToken);
        return Ok(new BillingReconciliationResponse(
            result.AwaitingPayment,
            result.UnderReview,
            result.Fulfilled,
            result.Rejected,
            result.Expired,
            result.FulfilledAmount,
            result.RefundedAmount,
            result.GeneratedAtUtc));
    }

    private static BillingCheckoutResponse Map(BillingCheckoutView checkout) =>
        new(
            checkout.Id,
            checkout.CustomerAccountId,
            checkout.ProductType.ToString(),
            checkout.ProductCode,
            checkout.ProductVersion,
            checkout.ProductDisplayName,
            checkout.Amount,
            checkout.Currency,
            checkout.PaymentReference,
            checkout.Status.ToString(),
            checkout.CreatedAtUtc,
            checkout.ExpiresAtUtc,
            checkout.ReceiptSubmittedAtUtc,
            checkout.ReviewedAtUtc,
            checkout.FulfilledAtUtc,
            checkout.ReceiptAttachmentKind,
            checkout.ReceiptAttachmentReference,
            checkout.ReviewReason,
            checkout.Version,
            checkout.AlreadyApplied,
            checkout.Status switch
            {
                FinancialCopilot.Billing.Purchases.BillingCheckoutStatus.UnderReview => ["approve", "reject"],
                FinancialCopilot.Billing.Purchases.BillingCheckoutStatus.Fulfilled => ["status"],
                _ => ["status"]
            });
}
