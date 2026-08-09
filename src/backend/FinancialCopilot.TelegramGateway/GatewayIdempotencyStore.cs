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

    public bool TryGet(string key, out TelegramGatewayOperationResult result) => entries.TryGetValue(key, out result!);

    public async Task SetAsync(string key, TelegramGatewayOperationResult result, CancellationToken cancellationToken)
    {
        entries[key] = result;
        await gate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(entries), cancellationToken);
        }
        finally { gate.Release(); }
    }

    private static ConcurrentDictionary<string, TelegramGatewayOperationResult> Load(string configuredPath, ILogger logger)
    {
        try
        {
            var path = Path.GetFullPath(configuredPath);
            return File.Exists(path)
                ? new ConcurrentDictionary<string, TelegramGatewayOperationResult>(JsonSerializer.Deserialize<Dictionary<string, TelegramGatewayOperationResult>>(File.ReadAllText(path)) ?? [])
                : new();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Telegram Gateway idempotency store could not be loaded; starting empty.");
            return new();
        }
    }
}
