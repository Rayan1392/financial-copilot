using FinancialCopilot.Application.FinancialData;
using Microsoft.Extensions.Configuration;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

internal sealed class ConfiguredFinancialStatementValueSearchProvider(IConfiguration configuration)
    : IFinancialStatementValueSearchProvider
{
    public string ProviderName =>
        configuration.GetSection("FinancialSourcePriority:DatasetPriority:FinancialStatements").Get<string[]>()?.FirstOrDefault()
        ?? "NoavaranCurrentApi";
}
