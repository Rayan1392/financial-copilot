using FinancialCopilot.Application.Telegram;
using FinancialCopilot.Infrastructure.Authentication;
using Microsoft.Extensions.Caching.Memory;

namespace FinancialCopilot.UnitTests;

public sealed class TelegramDisclosurePagination112Tests
{
    [Fact]
    public void Opaque_state_token_is_bounded_and_does_not_expose_query_text()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new TelegramDisclosurePaginationStateStore(cache);
        var state = new TelegramDisclosurePaginationState(Guid.NewGuid(), Guid.NewGuid(), 1, 2, null,
            Guid.NewGuid(), "فهرست اطلاعیه‌های شرکت محرمانه", 3, DateTimeOffset.UtcNow.AddMinutes(15));

        var token = store.Create(state);

        Assert.Equal(32, token.Length);
        Assert.DoesNotContain("محرمانه", token);
        Assert.True(store.TryGet(token, out var resolved));
        Assert.Equal(state, resolved);
        Assert.False(store.TryGet(token + "x", out _));
    }
}
