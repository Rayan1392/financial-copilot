using System.Collections.Concurrent;
using System.Text.Json;
using FinancialCopilot.Application.Telegram;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.TelegramGateway;

public sealed class GatewayIdempotencyStore(IOptions<TelegramGatewaySettings> options, ILogger<GatewayIdempotencyStore> logger)
{
    private readonly string path = Path.GetFullPath(options.Value.IdempotencyFilePath);
    private readonly ConcurrentDictionary<string, TelegramGatewayOperationResult> entries = Load(options.Value.IdempotencyFilePath, logger);
    private readonly SemaphoreSlim gate = new(1, 1);

    public bool TryGet(string key, out TelegramGatewayOperationResult result)
    {
        if (entries.TryGetValue(key, out result!) && result.Succeeded)
        {
            return true;
        }

        result = default!;
        return false;
    }

    public async Task SetAsync(string key, TelegramGatewayOperationResult result, CancellationToken cancellationToken)
    {
        if (!result.Succeeded)
        {
            throw new ArgumentException("Only confirmed Telegram sends can be persisted.", nameof(result));
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            var snapshot = entries.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
            snapshot[key] = result;
            var temporaryPath = path + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(snapshot), cancellationToken);
            File.Move(temporaryPath, path, true);
            entries[key] = result;
        }
        finally { gate.Release(); }
    }

    private static ConcurrentDictionary<string, TelegramGatewayOperationResult> Load(string configuredPath, ILogger logger)
    {
        try
        {
            var path = Path.GetFullPath(configuredPath);
            if (!File.Exists(path)) return new();
            var persisted = JsonSerializer.Deserialize<Dictionary<string, TelegramGatewayOperationResult>>(File.ReadAllText(path)) ?? [];
            return new ConcurrentDictionary<string, TelegramGatewayOperationResult>(
                persisted.Where(entry => entry.Value.Succeeded),
                StringComparer.Ordinal);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Telegram Gateway idempotency store could not be loaded; starting empty.");
            return new();
        }
    }
}
