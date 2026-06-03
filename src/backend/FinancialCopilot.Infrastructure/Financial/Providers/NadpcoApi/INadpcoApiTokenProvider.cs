namespace FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;

public interface INadpcoApiTokenProvider
{
    Task<string> GetTokenAsync(bool forceRefresh, CancellationToken cancellationToken);

    void Invalidate();
}
