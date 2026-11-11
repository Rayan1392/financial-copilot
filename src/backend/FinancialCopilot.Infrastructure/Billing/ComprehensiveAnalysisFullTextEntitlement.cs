using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Billing.Contracts;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Billing;

/// <summary>
/// Feature 162 change «max-plan-full-analysis-text»: the whole analysis text is granted only to a user (never an API
/// client) whose individual account's effective plan has an enabled <see cref="ComprehensiveAnalysisFullText.CapabilityCode"/>
/// row (Max). Fails closed: no account, another tenant, no plan or any error → not granted. Logs no user id or text.
/// </summary>
public sealed class ComprehensiveAnalysisFullTextEntitlement(
    ICustomerAccountRepository accounts,
    IPlanCapabilityService planCapabilities,
    ILogger<ComprehensiveAnalysisFullTextEntitlement> logger) : IComprehensiveAnalysisFullTextEntitlement
{
    public async Task<bool> IsGrantedAsync(ComprehensiveAnalysisReader reader, CancellationToken cancellationToken)
    {
        if (reader.ApiClientId is not null || reader.UserId is not { } userId)
        {
            return false;
        }

        try
        {
            var account = await accounts.FindIndividualByUserAsync(reader.TenantId, userId, cancellationToken);
            if (account is null || account.TenantId != reader.TenantId)
            {
                return false;
            }

            return await planCapabilities.IsEnabledAsync(account, ComprehensiveAnalysisFullText.CapabilityCode, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Comprehensive analysis full-text entitlement check failed; the preview is shown. Category: {Category}",
                exception.GetType().Name);
            return false;
        }
    }
}
