using FinancialCopilot.Application.FinancialData.Providers;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Providers.StockMarketDb;

public sealed class StockMarketDbSqlResilience(ILogger<StockMarketDbSqlResilience> logger)
{
    private static readonly HashSet<int> TransientErrors =
    [
        -2, 20, 64, 121, 233, 1205, 4060, 10053, 10054, 10060
    ];

    public async Task<T> ExecuteAsync<T>(
        string operation,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action(cancellationToken);
            }
            catch (SqlException exception) when (
                exception.Errors.Cast<SqlError>().Any(error => TransientErrors.Contains(error.Number)) &&
                attempt < maxAttempts)
            {
                logger.LogWarning(
                    exception,
                    "Transient StockMarketDb error during {Operation}; retrying attempt {Attempt}/{MaxAttempts}.",
                    operation, attempt + 1, maxAttempts);
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
            }
            catch (SqlException exception)
            {
                throw new FinancialProviderException(
                    exception.Number == -2 ? FinancialProviderErrorCode.Timeout : FinancialProviderErrorCode.RemoteUnavailable,
                    $"StockMarketDb {operation} failed.",
                    exception);
            }
        }
    }
}

