using System.Collections.Concurrent;

namespace FinancialCopilot.TelegramGateway;

public sealed class GatewayReplayNonceStore
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> nonces = new(StringComparer.Ordinal);

    public bool TryAccept(string serviceId, string nonce, DateTimeOffset now, TimeSpan lifetime)
    {
        var key = $"{serviceId}:{nonce}";
        foreach (var entry in nonces)
            if (entry.Value < now - lifetime)
                nonces.TryRemove(entry.Key, out _);

        return nonces.TryAdd(key, now);
    }
}
