using System.Globalization;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Options;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.Tsetmc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Tsetmc;

/// <summary>
/// One-off repair (Feature 167). Instruments first seen through the intraday trade feed, which
/// has no symbol, were stored with their numeric instrument code as symbol and never renamed.
/// This reads the stored daily trade payloads, newest first, and gives each such stub the symbol
/// the provider reported. It only renames; it never creates or re-links an instrument.
/// Safe to run again: a renamed instrument is no longer a stub.
/// </summary>
public sealed class TsetmcInstrumentStubSymbolBackfill(
    FinancialIngestionDbContext ingestionDb,
    FinancialProviderDbContext providerDb,
    IOptions<TsetmcWebServiceOptions> options,
    TimeProvider timeProvider,
    ILogger<TsetmcInstrumentStubSymbolBackfill> logger) : IInstrumentStubSymbolBackfill
{
    private const int SaveBatchSize = 500;

    public async Task<InstrumentStubBackfillResult> BackfillAsync(CancellationToken cancellationToken)
    {
        var candidates = await ingestionDb.TradingInstruments
            .Where(row => row.InstrumentIsin == string.Empty)
            .ToListAsync(cancellationToken);
        var stubs = candidates
            .Where(row => TsetmcInstrumentStub.HasStubSymbol(row.Symbol, row.InstrumentCode))
            .GroupBy(row => row.InstrumentCode)
            .ToDictionary(group => group.Key, group => group.ToList());
        var stubCount = stubs.Values.Sum(rows => rows.Count);
        if (stubCount == 0)
            return new InstrumentStubBackfillResult(0, 0, 0, 0);

        var providerName = options.Value.ProviderName;
        var payloadIds = await providerDb.ProviderRawPayloads.AsNoTracking()
            .Where(row => row.ProviderName == providerName && row.Dataset == "DailyTrades")
            .OrderByDescending(row => row.ReceivedAt)
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);

        var now = timeProvider.GetUtcNow();
        int renamed = 0, payloadsRead = 0, pending = 0;
        foreach (var payloadId in payloadIds)
        {
            if (stubs.Count == 0) break;
            cancellationToken.ThrowIfCancellationRequested();

            // One payload at a time: each is a full trading day and they are not needed together.
            var payload = await providerDb.ProviderRawPayloads.AsNoTracking()
                .Where(row => row.Id == payloadId)
                .Select(row => row.Payload)
                .SingleAsync(cancellationToken);
            payloadsRead++;

            foreach (var (insCode, symbol) in ReadSymbols(payload))
            {
                if (!stubs.Remove(insCode, out var rows)) continue;
                foreach (var row in rows)
                {
                    row.Symbol = symbol;
                    row.SourceChangedAt = now;
                    renamed++;
                    pending++;
                }
            }

            if (pending >= SaveBatchSize)
            {
                await ingestionDb.SaveChangesAsync(cancellationToken);
                pending = 0;
            }
        }

        await ingestionDb.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "TSETMC instrument stub backfill: {Found} stubs, {Renamed} renamed, {Skipped} without a stored symbol, {Payloads} payloads read.",
            stubCount, renamed, stubCount - renamed, payloadsRead);
        return new InstrumentStubBackfillResult(stubCount, renamed, stubCount - renamed, payloadsRead);
    }

    private List<(long InsCode, string Symbol)> ReadSymbols(string payload)
    {
        var result = new List<(long, string)>();
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return result;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty("InsCode", out var code) || !code.TryGetInt64(out var insCode) ||
                    !item.TryGetProperty("Symbol", out var symbolElement) ||
                    symbolElement.ValueKind != JsonValueKind.String)
                    continue;

                var symbol = symbolElement.GetString()?.Trim() ?? string.Empty;
                if (symbol.Length > 0 && !TsetmcInstrumentStub.HasStubSymbol(symbol, insCode))
                    result.Add((insCode, symbol));
            }
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "TSETMC instrument stub backfill: skipped an unreadable daily trade payload.");
        }

        return result;
    }
}

/// <summary>
/// An instrument stub is a row created from a feed that carries no symbol: its symbol is the
/// instrument code written as a number.
/// </summary>
public static class TsetmcInstrumentStub
{
    public static bool HasStubSymbol(string? symbol, long instrumentCode) =>
        string.IsNullOrWhiteSpace(symbol) ||
        string.Equals(symbol.Trim(), instrumentCode.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
}
