using FinancialCopilot.Application.FinancialData.Providers;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Providers.StockMarketDb;

public sealed class StockMarketDbConnectionFactory(IOptions<StockMarketDbProviderOptions> options)
{
    private readonly StockMarketDbProviderOptions _options = options.Value;

    public int CommandTimeoutSeconds => _options.CommandTimeoutSeconds;

    public async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new FinancialProviderException(
                FinancialProviderErrorCode.ConfigurationMissing,
                "StockMarketDb connection string is not configured.");
        }

        var connection = new SqlConnection(new SqlConnectionStringBuilder(_options.ConnectionString)
        {
            ApplicationIntent = ApplicationIntent.ReadOnly
        }.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}

