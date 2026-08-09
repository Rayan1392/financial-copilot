using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Domain.Financial.FundPortfolio;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

public sealed class NoKnownFundEquityCorporateActionAdjustmentProvider : IFundEquityCorporateActionAdjustmentProvider
{
    public Task<decimal?> GetKnownQuantityAdjustmentAsync(Guid reportId, FundWorkbookPeriodContext periodContext, string normalizedSecurityName, CancellationToken cancellationToken) => Task.FromResult<decimal?>(null);
}
