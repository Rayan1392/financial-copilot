namespace FinancialCopilot.Billing.Accounts;

public sealed record PartnerUsageAttribution(string? ExternalUserId)
{
    public string? NormalizedExternalUserId { get; } =
        string.IsNullOrWhiteSpace(ExternalUserId) ? null : ExternalUserId.Trim();
}
