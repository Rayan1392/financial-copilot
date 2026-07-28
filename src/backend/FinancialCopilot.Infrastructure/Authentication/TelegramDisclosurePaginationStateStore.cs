using System.Security.Cryptography;
using FinancialCopilot.Application.Telegram;
using Microsoft.Extensions.Caching.Memory;

namespace FinancialCopilot.Infrastructure.Authentication;

/// <summary>Opaque, expiring server-side state; Telegram receives only an unguessable token.</summary>
public sealed class TelegramDisclosurePaginationStateStore(IMemoryCache cache) : ITelegramDisclosurePaginationStateStore
{
    private const string Prefix = "telegram:disclosure-pagination:";

    public string Create(TelegramDisclosurePaginationState state)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        cache.Set(Prefix + token, state, state.ExpiresAtUtc);
        return token;
    }

    public bool TryGet(string token, out TelegramDisclosurePaginationState state)
    {
        state = default!;
        return token.Length == 32 && cache.TryGetValue(Prefix + token, out state!);
    }
}
