using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;

public sealed class CyclicalWavesSymbolNormalizer(
    FinancialIngestionDbContext dbContext) : IFinancialPayloadNormalizer
{
    public string ProviderName => "CyclicalWaves";

    public ProviderDataset Dataset => ProviderDataset.Symbols;

    public async Task<int> NormalizeAsync(ProviderRawPayload payload, CancellationToken cancellationToken)
    {
        var tickers = JsonSerializer.Deserialize<string[]>(payload.Payload, JsonOptions) ??
            throw new FinancialProviderException(
                FinancialProviderErrorCode.InvalidResponse,
                "CyclicalWaves ticker list payload is invalid.");

        var uniqueTickers = tickers
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existingCompanies = await dbContext.Companies
            .Where(c => c.ProviderName == ProviderName)
            .ToDictionaryAsync(c => c.ExternalCompanyId, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var existingSymbols = await dbContext.Symbols
            .Where(s => s.ProviderName == ProviderName)
            .ToDictionaryAsync(s => s.ExternalSymbolId, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var ticker in uniqueTickers)
        {
            if (!existingCompanies.TryGetValue(ticker, out var company))
            {
                company = new NormalizedCompanyRow
                {
                    Id = Guid.NewGuid(),
                    ProviderName = ProviderName,
                    ExternalCompanyId = ticker
                };
                dbContext.Companies.Add(company);
                existingCompanies[ticker] = company;
            }

            company.Name = ticker;
            company.LastSynchronizedAt = payload.ReceivedAt;

            if (!existingSymbols.TryGetValue(ticker, out var symbol))
            {
                symbol = new NormalizedSymbolRow
                {
                    Id = Guid.NewGuid(),
                    CompanyId = company.Id,
                    ProviderName = ProviderName,
                    ExternalSymbolId = ticker
                };
                dbContext.Symbols.Add(symbol);
                existingSymbols[ticker] = symbol;
            }

            symbol.SymbolCode = ticker;
            symbol.LastSynchronizedAt = payload.ReceivedAt;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return uniqueTickers.Count;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
