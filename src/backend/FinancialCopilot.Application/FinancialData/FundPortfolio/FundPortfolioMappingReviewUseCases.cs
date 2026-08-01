namespace FinancialCopilot.Application.FinancialData.FundPortfolio;

public sealed class ResolveFundPortfolioMappingReviewUseCase(IFundPortfolioMappingReviewRepository repository) : IResolveFundPortfolioMappingReviewUseCase
{
    public async Task<FundPortfolioMappingResolutionResult> ExecuteAsync(ResolveFundPortfolioMappingReviewRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ResolutionJson) || request.ResolutionJson.Length > 10000) throw new ArgumentException("A bounded resolution payload is required.", nameof(request));
        return await repository.ResolveAsync(request, cancellationToken);
    }
}
