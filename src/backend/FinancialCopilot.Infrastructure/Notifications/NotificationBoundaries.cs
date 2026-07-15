using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.Notifications;
using FinancialCopilot.Billing.Contracts;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Notifications;

public sealed class NotificationEntitlementPolicy(
    IBillableAccountResolver accountResolver,
    IPlanCapabilityService planCapabilities) : INotificationEntitlementPolicy
{
    public const string CapabilityCode = "Notifications.Telegram";

    public async Task ValidateManageAsync(CurrentActor actor, CancellationToken cancellationToken)
    {
        var account = await accountResolver.ResolveAsync(
            new BillableActorContext(actor.ActorId, actor.TenantId, actor.UserId, actor.ApiClientId, null),
            cancellationToken);
        await planCapabilities.ValidateCanExecuteAsync(account, CapabilityCode, cancellationToken);
    }

    public async Task<bool> CanDeliverAsync(NotificationActor actor, CancellationToken cancellationToken)
    {
        try
        {
            var isUser = actor.ActorType.Equals(ActorType.User.ToString(), StringComparison.OrdinalIgnoreCase);
            var account = await accountResolver.ResolveAsync(new BillableActorContext(
                actor.ActorId, actor.TenantId, isUser ? actor.ActorId : null,
                isUser ? null : actor.ActorId, null), cancellationToken);
            await planCapabilities.ValidateCanExecuteAsync(account, CapabilityCode, cancellationToken);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}

public sealed class NotificationRecipientResolver(
    ITelegramIdentityLinkReader linkReader) : INotificationRecipientResolver
{
    public async Task<TelegramNotificationRecipient?> ResolveTelegramAsync(
        NotificationActor actor,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ActorType>(actor.ActorType, true, out var actorType) || actorType != ActorType.User)
            return null;
        var current = new CurrentActor(actorType, actor.ActorId, actor.TenantId,
            AuthenticationMode.WebAppUser, UserId: actor.ActorId);
        var link = await linkReader.GetCurrentAsync(current, cancellationToken);
        return link is null ? null : new TelegramNotificationRecipient(link.TelegramChatId);
    }
}

public sealed class NotificationDispatcherOptions
{
    public const string SectionName = "Notifications:Dispatcher";
    public bool Enabled { get; set; }
    public int IntervalSeconds { get; set; } = 10;
    public int BatchSize { get; set; } = 100;
    public int LeaseSeconds { get; set; } = 90;
    public int MaximumAttempts { get; set; } = 5;
    public int InitialBackoffSeconds { get; set; } = 10;
    public int MaximumBackoffSeconds { get; set; } = 900;
    public int DigestMaximumItems { get; set; } = 25;
    public int MessagePartLength { get; set; } = 3800;
    public int TransportErrorRetentionDays { get; set; } = 30;
    public int DeliveryAuditRetentionDays { get; set; } = 730;
}

public sealed class TelegramNotificationOptions
{
    public const string SectionName = "Telegram:Notifications";
    public string BotToken { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.telegram.org";
    public int RequestTimeoutSeconds { get; set; } = 30;
}
