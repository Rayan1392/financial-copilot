namespace FinancialCopilot.Billing;

public sealed class BillingIdempotencyConflictException(string message) : InvalidOperationException(message)
{
}
