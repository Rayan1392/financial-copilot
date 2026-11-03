using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Infrastructure.Conversations.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Feature 153 T02 (decision D-A1): characterizes what <c>POST /api/ai/v1/query</c> does today when a client
/// resends the same idempotency key and payload, for example after a phone loses the connection mid-request.
/// These tests document current behavior; they do not define the desired contract. Billing is single-charge,
/// but the AI workflow runs again and persists again, so a same-key resend is not a safe retry. Proposed backend
/// task B-3 (spec §8.3) would make the replay return the original result; when it lands, invert these assertions.
/// </summary>
public sealed class AiQueryIdempotentReplayCharacterizationTests : IClassFixture<V2ScannerApiFactory>
{
    private const string Message = "P/E below 6";
    private readonly V2ScannerApiFactory _factory;

    public AiQueryIdempotentReplayCharacterizationTests(V2ScannerApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
    }

    [Fact]
    public async Task NewChatReplay_WithSameKey_ChargesOnce_ButCreatesASecondConversation()
    {
        using var client = CreateClient();
        var key = Guid.NewGuid();
        var conversationsBefore = await CountConversationsAsync();
        var usageBefore = _factory.ReadUsageEntries().Count;
        var reservationsBefore = _factory.ReadBillingReservations().Count;

        var first = await QueryAsync(client, new { message = Message, idempotencyKey = key });
        var replay = await QueryAsync(client, new { message = Message, idempotencyKey = key });

        Assert.NotEqual(first.ConversationId, replay.ConversationId);
        Assert.Equal(conversationsBefore + 2, await CountConversationsAsync());
        Assert.Equal(usageBefore + 1, _factory.ReadUsageEntries().Count);
        Assert.Equal(reservationsBefore + 1, _factory.ReadBillingReservations().Count);
    }

    [Fact]
    public async Task FollowUpReplay_WithSameKey_ChargesOnce_ButPersistsTheExchangeTwice()
    {
        using var client = CreateClient();
        var opening = await QueryAsync(client, new { message = Message, idempotencyKey = Guid.NewGuid() });
        var messagesBefore = await CountMessagesAsync(opening.ConversationId);
        var usageBefore = _factory.ReadUsageEntries().Count;
        var reservationsBefore = _factory.ReadBillingReservations().Count;
        var key = Guid.NewGuid();
        var payload = new { message = Message, conversationId = opening.ConversationId, idempotencyKey = key };

        var first = await QueryAsync(client, payload);
        var replay = await QueryAsync(client, payload);

        Assert.Equal(opening.ConversationId, first.ConversationId);
        Assert.Equal(opening.ConversationId, replay.ConversationId);
        Assert.NotEqual(first.MessageId, replay.MessageId);
        Assert.Equal(messagesBefore + 4, await CountMessagesAsync(opening.ConversationId));
        Assert.Equal(usageBefore + 1, _factory.ReadUsageEntries().Count);
        Assert.Equal(reservationsBefore + 1, _factory.ReadBillingReservations().Count);
    }

    private HttpClient CreateClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);
        return client;
    }

    private static async Task<(Guid ConversationId, Guid MessageId)> QueryAsync(HttpClient client, object payload)
    {
        using var response = await client.PostAsJsonAsync("/api/ai/v1/query", payload, CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(body);
        return (
            document.RootElement.GetProperty("conversationId").GetGuid(),
            document.RootElement.GetProperty("messageId").GetGuid());
    }

    private async Task<int> CountConversationsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ConversationDbContext>();
        return await db.Conversations.CountAsync();
    }

    private async Task<int> CountMessagesAsync(Guid conversationId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ConversationDbContext>();
        return await db.Messages.CountAsync(message => message.ConversationId == conversationId);
    }
}
