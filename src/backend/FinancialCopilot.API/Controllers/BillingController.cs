using FinancialCopilot.API.Contracts;
using FinancialCopilot.API.Security;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Billing.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/billing")]
[Authorize(Policy = AuthorizationPolicies.AiFacade)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class BillingController(
    ICurrentActorContext actorContext,
    IBillableAccountResolver accountResolver,
    IFinancialAccountingService financialAccounting,
    IBillingPurchaseUseCases purchaseUseCases,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("catalog")]
    public async Task<ActionResult<BillingCatalogResponse>> GetCatalog(CancellationToken cancellationToken)
    {
        var catalog = await purchaseUseCases.GetCatalogAsync("Telegram", cancellationToken);
        return Ok(new BillingCatalogResponse(
            catalog.Products.Select(product => new BillingCatalogProductResponse(
                product.Code,
                product.ProductType.ToString(),
                product.Version,
                product.DisplayName,
                product.Amount,
                product.Currency,
                product.Credits,
                product.PlanCode,
                product.DurationDays,
                product.Channel)).ToArray(),
            catalog.GeneratedAtUtc));
    }

    [HttpGet("checkouts")]
    public async Task<ActionResult<BillingCheckoutPageResponse>> GetCheckouts(
        [FromQuery] int offset = 0,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var page = await purchaseUseCases.GetMyCheckoutsAsync(
            ToBillableActor(),
            offset,
            pageSize,
            cancellationToken);
        return Ok(new BillingCheckoutPageResponse(
            page.Items.Select(Map).ToArray(),
            page.Offset,
            page.PageSize,
            page.HasMore));
    }

    [HttpGet("checkouts/{checkoutId:guid}")]
    public async Task<ActionResult<BillingCheckoutResponse>> GetCheckout(
        Guid checkoutId,
        CancellationToken cancellationToken)
    {
        var checkout = await purchaseUseCases.GetCheckoutAsync(
            ToBillableActor(),
            checkoutId,
            cancellationToken);
        return checkout is null ? NotFound() : Ok(Map(checkout));
    }

    [HttpPost("checkouts")]
    public async Task<ActionResult<BillingCheckoutResponse>> CreateCheckout(
        [FromBody] CreateBillingCheckoutRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var checkout = await purchaseUseCases.CreateCheckoutAsync(
                new CreateBillingCheckoutCommand(
                    ToBillableActor(),
                    request.ProductCode,
                    request.IdempotencyKey ?? Request.Headers["Idempotency-Key"].FirstOrDefault() ??
                    $"{actorContext.Actor.ActorId:N}:{request.ProductCode}:{DateTimeOffset.UtcNow:O}",
                    CorrelationId()),
                cancellationToken);
            return Ok(Map(checkout));
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { error = exception.Message });
        }
    }

    [HttpPost("checkouts/{checkoutId:guid}/receipt")]
    public async Task<ActionResult<BillingCheckoutResponse>> SubmitReceipt(
        Guid checkoutId,
        [FromBody] SubmitBillingReceiptRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var checkout = await purchaseUseCases.SubmitReceiptAsync(
                new SubmitBillingReceiptCommand(
                    ToBillableActor(),
                    checkoutId,
                    request.ExpectedVersion,
                    request.AttachmentKind,
                    request.AttachmentReference,
                    request.ProviderReference,
                    request.IdempotencyKey ?? Request.Headers["Idempotency-Key"].FirstOrDefault() ??
                    $"{checkoutId:N}:receipt:{DateTimeOffset.UtcNow:O}",
                    CorrelationId()),
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

    [HttpPost("checkouts/{checkoutId:guid}/cancel")]
    public async Task<ActionResult<BillingCheckoutResponse>> CancelCheckout(
        Guid checkoutId,
        [FromBody] CancelBillingCheckoutRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var checkout = await purchaseUseCases.CancelCheckoutAsync(
                new CancelBillingCheckoutCommand(
                    ToBillableActor(),
                    checkoutId,
                    request.ExpectedVersion,
                    request.Reason,
                    CorrelationId()),
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

    [HttpPost("payment-callback/{provider}")]
    [AllowAnonymous]
    [DisableRateLimiting]
    public async Task<ActionResult<PaymentCallbackResponse>> PaymentCallback(
        string provider,
        [FromBody] PaymentCallbackRequest request,
        CancellationToken cancellationToken)
    {
        var result = await purchaseUseCases.ProcessPaymentCallbackAsync(
            new PaymentCallbackCommand(
                provider,
                request.ProviderReference,
                request.PaymentReference,
                request.Amount,
                request.Currency,
                request.Signature,
                request.TimestampUtc,
                request.Nonce,
                CorrelationId()),
            cancellationToken);
        return Accepted(new PaymentCallbackResponse(
            result.Status,
            result.CheckoutId,
            result.Fulfilled,
            result.Reason));
    }

    [HttpGet("transactions")]
    public async Task<ActionResult<BillingTransactionsResponse>> GetTransactions(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var periodTo = to ?? timeProvider.GetUtcNow();
        var periodFrom = from ?? periodTo.AddDays(-30);

        if (periodFrom > periodTo)
        {
            ModelState.AddModelError(nameof(from), "Transaction period start must not be after its end.");
            return ValidationProblem(ModelState);
        }

        var actor = actorContext.Actor;
        var account = await accountResolver.ResolveAsync(
            new BillableActorContext(
                actor.ActorId,
                actor.TenantId,
                actor.UserId,
                actor.ApiClientId,
                ExternalUserId: null),
            cancellationToken);
        var transactions = await financialAccounting.QueryAsync(
            account.Id,
            periodFrom,
            periodTo,
            cancellationToken);

        return Ok(new BillingTransactionsResponse(
            periodFrom,
            periodTo,
            transactions
                .Select(transaction => new BillingTransactionResponse(
                    transaction.Type.ToString(),
                    transaction.Amount,
                    transaction.Currency,
                    transaction.OccurredAt))
                .ToArray()));
    }

    private BillableActorContext ToBillableActor()
    {
        var actor = actorContext.Actor;
        return new BillableActorContext(
            actor.ActorId,
            actor.TenantId,
            actor.UserId,
            actor.ApiClientId,
            ExternalUserId: null);
    }

    private string CorrelationId() =>
        HttpContext.TraceIdentifier;

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
            NextActions(checkout.Status));

    private static string[] NextActions(FinancialCopilot.Billing.Purchases.BillingCheckoutStatus status) =>
        status switch
        {
            FinancialCopilot.Billing.Purchases.BillingCheckoutStatus.AwaitingPayment =>
                ["submit_receipt", "cancel", "status"],
            FinancialCopilot.Billing.Purchases.BillingCheckoutStatus.UnderReview =>
                ["status"],
            FinancialCopilot.Billing.Purchases.BillingCheckoutStatus.Rejected =>
                ["create_new_checkout"],
            FinancialCopilot.Billing.Purchases.BillingCheckoutStatus.Fulfilled =>
                ["view_entitlement", "status"],
            _ => ["status"]
        };
}
