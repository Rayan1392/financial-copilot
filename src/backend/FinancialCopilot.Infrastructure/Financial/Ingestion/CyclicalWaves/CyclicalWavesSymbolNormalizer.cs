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

    public async Task<NormalizationOutcome> NormalizeAsync(ProviderRawPayload payload, CancellationToken cancellationToken)
    {
        var tickers = JsonSerializer.Deserialize<string[]>(payload.Payload, JsonOptions) ??
            throw new FinancialProviderException(
                FinancialProviderErrorCode.InvalidResponse,
                "CyclicalWaves ticker list payload is invalid.");

        var uniqueTickers = tickers
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Where(ContainsPersianCharacter)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existingSymbols = await dbContext.Symbols
            .Where(s => s.ProviderName == ProviderName)
            .ToDictionaryAsync(s => s.ExternalSymbolId, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var linked = 0;
        foreach (var ticker in uniqueTickers)
        {
            var linkage = await CyclicalWavesCompanyLinkageResolver.ResolveAsync(
                dbContext,
                ticker,
                enticker: null,
                cancellationToken);
            if (linkage is null)
            {
                continue;
            }

            if (!existingSymbols.TryGetValue(ticker, out var symbol))
            {
                symbol = new NormalizedSymbolRow
                {
                    Id = Guid.NewGuid(),
                    ProviderName = ProviderName,
                    ExternalSymbolId = ticker
                };
                dbContext.Symbols.Add(symbol);
                existingSymbols[ticker] = symbol;
            }

            symbol.CompanyId = linkage.CompanyId;
            symbol.SymbolCode = ticker;
            symbol.LastSynchronizedAt = payload.ReceivedAt;
            linked++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new NormalizationOutcome(linked);
    }

    // Persian/Arabic Unicode block: U+0600-U+06FF.
    private static bool ContainsPersianCharacter(string ticker) =>
        ticker.Any(c => c is >= '\u0600' and <= '\u06FF');

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
