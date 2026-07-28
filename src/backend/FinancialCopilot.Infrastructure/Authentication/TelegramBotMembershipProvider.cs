using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Domain.Identity.Telegram;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Authentication;

public sealed class TelegramBotMembershipProvider(
    HttpClient httpClient,
    IOptions<TelegramMembershipOptions> options,
    TimeProvider timeProvider,
    ILogger<TelegramBotMembershipProvider> logger) : ITelegramChannelMembershipProvider
{
    public async Task<TelegramProviderMembershipObservation> GetMembershipAsync(
        long telegramUserId,
        string channelId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var token = Environment.GetEnvironmentVariable(settings.BotTokenEnvironmentVariable);
        var observedAt = timeProvider.GetUtcNow();

        if (string.IsNullOrWhiteSpace(token))
        {
            return Failure(TelegramMembershipFailureCategory.BotTokenMissing, observedAt);
        }

        if (string.IsNullOrWhiteSpace(channelId))
        {
            return Failure(TelegramMembershipFailureCategory.ChannelNotConfigured, observedAt);
        }

        try
        {
            var uri = $"bot{Uri.EscapeDataString(token)}/getChatMember?chat_id={Uri.EscapeDataString(channelId)}&user_id={telegramUserId}";
            var response = await httpClient.GetFromJsonAsync<TelegramGetChatMemberResponse>(uri, cancellationToken);
            if (response?.Ok != true || response.Result is null)
            {
                return Failure(TelegramMembershipFailureCategory.UnexpectedResponse, observedAt);
            }

            return new TelegramProviderMembershipObservation(
                MapStatus(response.Result.Status, response.Result.IsMember),
                observedAt);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Telegram membership provider failed for correlation {CorrelationId}.", correlationId);
            return Failure(TelegramMembershipFailureCategory.ProviderUnavailable, observedAt);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Telegram membership provider timed out for correlation {CorrelationId}.", correlationId);
            return Failure(TelegramMembershipFailureCategory.ProviderUnavailable, observedAt);
        }
    }

    private static TelegramProviderMembershipObservation Failure(
        TelegramMembershipFailureCategory category,
        DateTimeOffset observedAt) =>
        new(TelegramChannelMembershipStatus.UnknownProviderFailure, observedAt, category);

    private static TelegramChannelMembershipStatus MapStatus(string status, bool? isMember) =>
        status switch
        {
            "creator" => TelegramChannelMembershipStatus.Creator,
            "administrator" => TelegramChannelMembershipStatus.Administrator,
            "member" => TelegramChannelMembershipStatus.Member,
            "restricted" when isMember == true => TelegramChannelMembershipStatus.RestrictedMember,
            "left" => TelegramChannelMembershipStatus.Left,
            "kicked" => TelegramChannelMembershipStatus.Kicked,
            _ => TelegramChannelMembershipStatus.NotFound
        };

    private sealed record TelegramGetChatMemberResponse(
        [property: JsonPropertyName("ok")] bool Ok,
        [property: JsonPropertyName("result")] TelegramChatMember? Result);

    private sealed record TelegramChatMember(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("is_member")] bool? IsMember);
}
