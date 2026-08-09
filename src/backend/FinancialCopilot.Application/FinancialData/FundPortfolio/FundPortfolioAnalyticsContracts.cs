using FinancialCopilot.Domain.Financial.FundPortfolio;

namespace FinancialCopilot.Application.FinancialData.FundPortfolio;

public sealed record FundPortfolioAnalyticsQuery(Guid FundId, DateOnly? PeriodEndDate = null);

public sealed record FundPortfolioAnalyticsResult(
    FundPortfolioAnalyticsSnapshot Snapshot,
    IReadOnlyCollection<FundPortfolioSignal> Signals);

public interface IFundPortfolioAnalyticsRepository
{
    Task StoreAsync(
        FundPortfolioAnalyticsSnapshot snapshot,
        IReadOnlyCollection<FundPortfolioSignal> signals,
        CancellationToken cancellationToken);

    Task<FundPortfolioAnalyticsResult?> GetAsync(
        FundPortfolioAnalyticsQuery query,
        CancellationToken cancellationToken);
}

public interface IFundPortfolioAnalyticsCalculator
{
    string CalculationVersion { get; }

    Task<FundPortfolioAnalyticsResult> CalculateAsync(
        FundPortfolioAnalyticsCalculationContext context,
        CancellationToken cancellationToken);
}

public sealed record FundPortfolioAnalyticsCalculationContext(
    FundPortfolioAnalyticsSnapshot Snapshot,
    IReadOnlyCollection<FundPortfolioSignal> Signals);

public interface IFundPortfolioAnalyticsCalculationRegistry
{
    IFundPortfolioAnalyticsCalculator Resolve(string calculationVersion);
}

public sealed class FundPortfolioAnalyticsCalculationRegistry(
    IEnumerable<IFundPortfolioAnalyticsCalculator> calculators) : IFundPortfolioAnalyticsCalculationRegistry
{
    private readonly IReadOnlyDictionary<string, IFundPortfolioAnalyticsCalculator> _calculators =
        calculators.ToDictionary(calculator => calculator.CalculationVersion, StringComparer.Ordinal);

    public IFundPortfolioAnalyticsCalculator Resolve(string calculationVersion) =>
        _calculators.TryGetValue(calculationVersion, out var calculator)
            ? calculator
            : throw new KeyNotFoundException($"Fund portfolio analytics calculation '{calculationVersion}' is not registered.");
}
