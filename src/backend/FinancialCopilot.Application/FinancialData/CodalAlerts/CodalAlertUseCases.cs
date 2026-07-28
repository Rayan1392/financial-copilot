using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.FinancialData.FollowedSymbols;
using FinancialCopilot.Domain.Financial.CodalAlerts;
using FinancialCopilot.Domain.Financial.FollowedSymbols;

namespace FinancialCopilot.Application.FinancialData.CodalAlerts;

public sealed class GetMyCodalAlertSubscriptionsUseCase(
    ICodalAlertSubscriptionRepository subscriptions,
    IFollowedCompanyResolver companyResolver) : IGetMyCodalAlertSubscriptionsUseCase
{
    public async Task<IReadOnlyCollection<CodalAlertSubscriptionDto>> ExecuteAsync(
        GetMyCodalAlertSubscriptionsQuery query,
        CancellationToken cancellationToken)
    {
        var actor = ToCodalActor(query.Actor);
        var rows = await subscriptions.GetAsync(actor, cancellationToken);
        var companies = await companyResolver.ResolveManyAsync(
            rows.Select(row => row.ExternalCompanyId).ToArray(),
            cancellationToken);

        return rows.Select(row => Map(row, companies)).ToArray();
    }

    internal static CodalAlertActor ToCodalActor(CurrentActor actor) =>
        new(actor.TenantId, actor.ActorId, actor.ActorType.ToString());

    internal static FollowedSymbolActor ToFollowedActor(CurrentActor actor) =>
        new(actor.TenantId, actor.ActorId, actor.ActorType.ToString());

    internal static CodalAlertSubscriptionDto Map(
        CodalAlertSubscription subscription,
        IReadOnlyDictionary<string, CanonicalFollowedCompany> companies)
    {
        companies.TryGetValue(subscription.ExternalCompanyId, out var company);
        return new CodalAlertSubscriptionDto(
            subscription.Id,
            subscription.ExternalCompanyId,
            company?.Symbol ?? subscription.ExternalCompanyId,
            company?.CompanyName ?? subscription.ExternalCompanyId,
            subscription.AnnouncementTypes.ToArray(),
            subscription.MinimumImportance,
            subscription.RawAlertEnabled,
            subscription.AiSummaryEnabled,
            subscription.State,
            subscription.CreatedAtUtc,
            subscription.UpdatedAtUtc);
    }
}

public sealed class CreateCodalAlertSubscriptionUseCase(
    ICodalAlertSubscriptionRepository subscriptions,
    IFollowedSymbolRepository followedSymbols,
    IFollowedCompanyResolver companyResolver,
    TimeProvider timeProvider) : ICreateCodalAlertSubscriptionUseCase
{
    public async Task<CodalAlertSubscriptionDto> ExecuteAsync(
        CreateCodalAlertSubscriptionCommand command,
        CancellationToken cancellationToken)
    {
        var actor = GetMyCodalAlertSubscriptionsUseCase.ToCodalActor(command.Actor);
        var followedActor = GetMyCodalAlertSubscriptionsUseCase.ToFollowedActor(command.Actor);
        var company = await companyResolver.ResolveAsync(command.ExternalCompanyId.Trim(), cancellationToken)
            ?? throw new CodalAlertSubscriptionValidationException("Unknown canonical company id.");
        var followed = await followedSymbols.FindAsync(followedActor, company.ExternalCompanyId, cancellationToken);
        if (followed is null)
            throw new CodalAlertSubscriptionValidationException("Codal alerts can be enabled only for followed symbols.");

        var now = timeProvider.GetUtcNow();
        var existing = await subscriptions.FindForCompanyAsync(actor, company.ExternalCompanyId, cancellationToken);
        var subscription = existing ?? CodalAlertSubscription.Create(
            actor,
            company.ExternalCompanyId,
            command.AnnouncementTypes,
            command.MinimumImportance,
            command.RawAlertEnabled,
            command.AiSummaryEnabled,
            now);

        if (existing is not null)
        {
            subscription.Update(
                command.AnnouncementTypes,
                command.MinimumImportance,
                command.RawAlertEnabled,
                command.AiSummaryEnabled,
                CodalAlertSubscriptionState.Active,
                now);
        }

        await subscriptions.SaveAsync(subscription, cancellationToken);
        return GetMyCodalAlertSubscriptionsUseCase.Map(
            subscription,
            new Dictionary<string, CanonicalFollowedCompany>(StringComparer.Ordinal)
            {
                [company.ExternalCompanyId] = company
            });
    }
}

public sealed class UpdateCodalAlertSubscriptionUseCase(
    ICodalAlertSubscriptionRepository subscriptions,
    IFollowedCompanyResolver companyResolver,
    TimeProvider timeProvider) : IUpdateCodalAlertSubscriptionUseCase
{
    public async Task<CodalAlertSubscriptionDto> ExecuteAsync(
        UpdateCodalAlertSubscriptionCommand command,
        CancellationToken cancellationToken)
    {
        var actor = GetMyCodalAlertSubscriptionsUseCase.ToCodalActor(command.Actor);
        var subscription = await subscriptions.FindAsync(actor, command.SubscriptionId, cancellationToken)
            ?? throw new CodalAlertSubscriptionValidationException("Codal alert subscription was not found.");

        subscription.Update(
            command.AnnouncementTypes,
            command.MinimumImportance,
            command.RawAlertEnabled,
            command.AiSummaryEnabled,
            command.State,
            timeProvider.GetUtcNow());

        await subscriptions.SaveAsync(subscription, cancellationToken);
        var companies = await companyResolver.ResolveManyAsync([subscription.ExternalCompanyId], cancellationToken);
        return GetMyCodalAlertSubscriptionsUseCase.Map(subscription, companies);
    }
}

public sealed class DeleteCodalAlertSubscriptionUseCase(
    ICodalAlertSubscriptionRepository subscriptions) : IDeleteCodalAlertSubscriptionUseCase
{
    public Task ExecuteAsync(
        DeleteCodalAlertSubscriptionCommand command,
        CancellationToken cancellationToken) =>
        subscriptions.RemoveAsync(
            GetMyCodalAlertSubscriptionsUseCase.ToCodalActor(command.Actor),
            command.SubscriptionId,
            cancellationToken);
}
