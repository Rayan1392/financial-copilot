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

        // Keep the full persisted composition; rendering decides whether to emphasize dominant rows.
        return result;
    }
}
