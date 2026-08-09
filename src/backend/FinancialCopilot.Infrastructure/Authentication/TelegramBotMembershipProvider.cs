using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.Telegram;
using FinancialCopilot.Domain.Identity.Telegram;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Authentication;

public sealed class TelegramBotMembershipProvider(
    ITelegramGatewayClient gatewayClient,
    TimeProvider timeProvider,
    ILogger<TelegramBotMembershipProvider> logger) : ITelegramChannelMembershipProvider
{
    public async Task<TelegramProviderMembershipObservation> GetMembershipAsync(
        long telegramUserId,
        string channelId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var observedAt = timeProvider.GetUtcNow();

        if (string.IsNullOrWhiteSpace(channelId))
        {
            return Failure(TelegramMembershipFailureCategory.ChannelNotConfigured, observedAt);
        }

        try
        {
            var response = await gatewayClient.GetChatMemberAsync(telegramUserId, channelId, correlationId, cancellationToken);
            if (!response.Succeeded || response.Status is null)
                return Failure(response.ErrorCode is "GatewayUnavailable" or "Timeout" ? TelegramMembershipFailureCategory.ProviderUnavailable : TelegramMembershipFailureCategory.UnexpectedResponse, observedAt);

            return new TelegramProviderMembershipObservation(
                MapStatus(response.Status, response.IsMember),
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

}
