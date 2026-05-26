namespace FinancialCopilot.Billing.Accounts;

public sealed class CreditLine
{
    public CreditLine(decimal approvedLimit, decimal warningThreshold)
    {
        if (approvedLimit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(approvedLimit), "Approved limit cannot be negative.");
        }

        if (warningThreshold < 0 || warningThreshold > approvedLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(warningThreshold),
                "Warning threshold must be between zero and the approved limit.");
        }

        ApprovedLimit = approvedLimit;
        WarningThreshold = warningThreshold;
    }

    public decimal ApprovedLimit { get; }

    public decimal WarningThreshold { get; }
}
