using FinancialCopilot.Domain.Identity.Telegram;

namespace FinancialCopilot.Application.Authentication;

public sealed record TelegramMembershipVerificationResult(
    TelegramChannelMembershipStatus Status,
    bool IsEligible,
    DateTimeOffset VerifiedAtUtc,
    DateTimeOffset ValidUntilUtc,
    string ChannelId,
    string CorrelationId,
    TelegramMembershipFailureCategory FailureCategory = TelegramMembershipFailureCategory.None,
    IReadOnlyList<TelegramInlineAction>? Actions = null);

public sealed record TelegramInlineAction(
    string Kind,
    string Label,
    string? Url = null,
    string? CallbackData = null,
    bool IsPrimary = false);

public sealed record TelegramDailyFreeAllowanceView(
    string AllowanceDateKey,
    string PolicyVersion,
    decimal TotalCredits,
    decimal UsedCredits,
    decimal RemainingCredits,
    DateTimeOffset ExpiresAtUtc);

public sealed record TelegramEntitlementView(
    TelegramLinkView? Link,
    TelegramMembershipVerificationResult? Membership,
    TelegramDailyFreeAllowanceView FreeDailyAllowance,
    decimal PaidAvailableSpendingCapacity,
    string ConsumptionOrder,
    string NextAction,
    IReadOnlyList<TelegramInlineAction> Actions,
    DateTimeOffset GeneratedAtUtc);

public sealed record TelegramProviderMembershipObservation(
    TelegramChannelMembershipStatus Status,
    DateTimeOffset ObservedAtUtc,
    TelegramMembershipFailureCategory FailureCategory = TelegramMembershipFailureCategory.None);

public interface ITelegramChannelMembershipProvider
{
    Task<TelegramProviderMembershipObservation> GetMembershipAsync(
        long telegramUserId,
        string channelId,
        string correlationId,
        CancellationToken cancellationToken);
}

public interface ITelegramMembershipService
{
    Task<TelegramMembershipVerificationResult> VerifyRequiredChannelMembershipAsync(
        CurrentActor actor,
        string correlationId,
        CancellationToken cancellationToken);

    Task<TelegramEntitlementView> GetMyTelegramEntitlementAsync(
        CurrentActor actor,
        string correlationId,
        CancellationToken cancellationToken);
}
