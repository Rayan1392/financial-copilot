namespace FinancialCopilot.Billing;

public sealed class InsufficientCreditException()
    : InvalidOperationException("Available spending capacity is insufficient.");
