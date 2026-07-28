using FinancialCopilot.Application.FinancialData.Providers;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Providers.CodalDb;

/// <summary>
/// Opens read-only <see cref="SqlConnection"/> instances to CodalDB from configured options. The
/// connection string is read from configuration/secrets (never hardcoded) and is augmented with
/// <see cref="ApplicationIntent.ReadOnly"/>. The provider never issues writes or DDL.
/// </summary>
public sealed class CodalDbConnectionFactory(IOptions<CodalDbProviderOptions> options)
{
    private readonly CodalDbProviderOptions _options = options.Value;

    public int CommandTimeoutSeconds => _options.CommandTimeoutSeconds;

    public string BuildConnectionString()
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new FinancialProviderException(
                FinancialProviderErrorCode.ConfigurationMissing,
                "CodalDb connection string is not configured.");
        }

        return new SqlConnectionStringBuilder(_options.ConnectionString)
        {
            ApplicationIntent = ApplicationIntent.ReadOnly
        }.ConnectionString;
    }

    public async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(BuildConnectionString());
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
