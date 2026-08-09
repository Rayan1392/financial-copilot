namespace FinancialCopilot.Application.FinancialData.FundPortfolio;

public static class FundPortfolioSourceEligibilityPolicy
{
    public static bool IsNewer(FundPortfolioReportSourceDescriptor descriptor, DateTimeOffset? watermarkUtc, string? watermarkSourceObjectId)
    {
        if (string.IsNullOrWhiteSpace(descriptor.StableSourceObjectId)) return false;
        if (watermarkUtc is null) return true;
        if (descriptor.LastModifiedUtc is null) return false;
        return descriptor.LastModifiedUtc > watermarkUtc || (descriptor.LastModifiedUtc == watermarkUtc && string.CompareOrdinal(descriptor.StableSourceObjectId, watermarkSourceObjectId) > 0);
    }
}

public static class FundPortfolioRetryPolicy
{
    public static TimeSpan DelayForAttempt(int attempt) => TimeSpan.FromSeconds(Math.Min(3600, Math.Pow(2, Math.Max(0, attempt - 1)) * 30));
    public static bool IsPoisoned(int attempt, int maximumAttempts) => attempt >= Math.Max(1, maximumAttempts);
}
