using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

internal sealed class ProductRevenueMixQueryUseCase(
    ICompanyResolverService companyResolver,
    ICompanyProductRevenueMixRepository repository)
    : IProductRevenueMixQueryUseCase
{
    public async Task<ProductRevenueMixResponse?> ExecuteAsync(
        ProductRevenueMixQuery query,
        CancellationToken ct = default)
    {
        var company = await companyResolver.ResolveBySymbolAsync(query.CompanySymbol, ct);
        if (company is null) return null;

        ProductRevenueMixResponse? result;

        if (query.Year.HasValue && query.Month.HasValue)
        {
            result = await repository.GetByPeriodAsync(
                company.ExternalCompanyId,
                query.Year.Value,
                (byte)query.Month.Value,
                ct);
        }
        else
        {
            result = await repository.GetLatestAsync(company.ExternalCompanyId, ct);
        }

        if (result is null) return null;

        // Apply dominant-product or top-N selection.
        var dominant = result.Products.Where(p => p.IsDominantProduct).ToList();
        var products = dominant.Count > 0
            ? dominant
            : result.Products.Take(query.TopN).ToList();

        return result with { Products = products };
    }
}
