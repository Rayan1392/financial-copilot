using FinancialCopilot.Application.FinancialData.Ingestion;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Tsetmc;

public sealed class ConfiguredMarketQuoteSourcePriority(
    IOptions<MarketQuoteSourcePriorityOptions> options) : IMarketQuoteSourcePriority
{
    public string PrimarySourceName => options.Value.PrimarySourceName;
}
