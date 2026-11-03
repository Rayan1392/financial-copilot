namespace FinancialCopilot.Billing.Accounts;

/// <summary>Quota semantics are explicit; numeric wallet values remain finite.</summary>
public enum CreditQuotaMode
{
    Finite = 0,
    Unlimited = 1
}
