using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Domain.Financial.CodalAlerts;

namespace FinancialCopilot.Application.FinancialData.CodalAlerts;

public sealed record CodalAlertSubscriptionDto(
    Guid Id,
    string ExternalCompanyId,
    string Symbol,
    string CompanyName,
    IReadOnlyList<CodalAnnouncementType> AnnouncementTypes,
    CodalAnnouncementImportance MinimumImportance,
    bool RawAlertEnabled,
    bool AiSummaryEnabled,
    CodalAlertSubscriptionState State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record GetMyCodalAlertSubscriptionsQuery(CurrentActor Actor);

public sealed record CreateCodalAlertSubscriptionCommand(
    CurrentActor Actor,
    string ExternalCompanyId,
    IReadOnlyCollection<CodalAnnouncementType> AnnouncementTypes,
    CodalAnnouncementImportance MinimumImportance,
    bool RawAlertEnabled,
    bool AiSummaryEnabled);

public sealed record UpdateCodalAlertSubscriptionCommand(
    CurrentActor Actor,
    Guid SubscriptionId,
    IReadOnlyCollection<CodalAnnouncementType> AnnouncementTypes,
    CodalAnnouncementImportance MinimumImportance,
    bool RawAlertEnabled,
    bool AiSummaryEnabled,
    CodalAlertSubscriptionState State);

public sealed record DeleteCodalAlertSubscriptionCommand(CurrentActor Actor, Guid SubscriptionId);

public sealed record GenerateCodalAlertSummaryCommand(CurrentActor Actor, Guid InsightEventId, string CorrelationId);

public sealed record CodalAlertSummaryDto(
    Guid Id,
    Guid InsightEventId,
    string Status,
    string? SummaryText,
    string EvidenceHash,
    string PromptPolicyVersion,
    string? ProviderName,
    string? ModelName,
    string? FailureReason,
    DateTimeOffset UpdatedAtUtc);

public interface ICodalAlertSubscriptionRepository
{
    Task<IReadOnlyCollection<CodalAlertSubscription>> GetAsync(
        CodalAlertActor actor,
        CancellationToken cancellationToken);

    Task<CodalAlertSubscription?> FindAsync(
        CodalAlertActor actor,
        Guid subscriptionId,
        CancellationToken cancellationToken);

    Task<CodalAlertSubscription?> FindForCompanyAsync(
        CodalAlertActor actor,
        string externalCompanyId,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<CodalAlertSubscription>> GetActiveForCompaniesAsync(
        IReadOnlyCollection<string> externalCompanyIds,
        CancellationToken cancellationToken);

    Task SaveAsync(CodalAlertSubscription subscription, CancellationToken cancellationToken);

    Task RemoveAsync(CodalAlertActor actor, Guid subscriptionId, CancellationToken cancellationToken);
}

public interface IGetMyCodalAlertSubscriptionsUseCase
{
    Task<IReadOnlyCollection<CodalAlertSubscriptionDto>> ExecuteAsync(
        GetMyCodalAlertSubscriptionsQuery query,
        CancellationToken cancellationToken);
}

public interface ICreateCodalAlertSubscriptionUseCase
{
    Task<CodalAlertSubscriptionDto> ExecuteAsync(
        CreateCodalAlertSubscriptionCommand command,
        CancellationToken cancellationToken);
}

public interface IUpdateCodalAlertSubscriptionUseCase
{
    Task<CodalAlertSubscriptionDto> ExecuteAsync(
        UpdateCodalAlertSubscriptionCommand command,
        CancellationToken cancellationToken);
}

public interface IDeleteCodalAlertSubscriptionUseCase
{
    Task ExecuteAsync(
        DeleteCodalAlertSubscriptionCommand command,
        CancellationToken cancellationToken);
}

public interface IGenerateCodalAlertSummaryUseCase
{
    Task<CodalAlertSummaryDto> ExecuteAsync(
        GenerateCodalAlertSummaryCommand command,
        CancellationToken cancellationToken);
}

public sealed class CodalAlertSubscriptionValidationException(string message) : InvalidOperationException(message);
