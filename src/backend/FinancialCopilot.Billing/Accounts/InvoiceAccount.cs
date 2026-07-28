namespace FinancialCopilot.Billing.Accounts;

public sealed record InvoiceAccount(
    Guid CustomerAccountId,
    string LegalName,
    string BillingEmail,
    string SettlementTerms)
{
    public InvoiceAccount Validate()
    {
        if (CustomerAccountId == Guid.Empty ||
            string.IsNullOrWhiteSpace(LegalName) ||
            string.IsNullOrWhiteSpace(BillingEmail) ||
            string.IsNullOrWhiteSpace(SettlementTerms))
        {
            throw new ArgumentException("Complete invoice account settlement details are required.");
        }

        return this;
    }
}
