using System.Text.Json;
using System.Text.Json.Serialization;
using FinancialCopilot.Application.FinancialData.CodalAlerts;
using FinancialCopilot.Domain.Financial.CodalAlerts;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.CodalAlerts;

public sealed class EfCoreCodalAlertSubscriptionRepository(
    FinancialIngestionDbContext dbContext) : ICodalAlertSubscriptionRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<IReadOnlyCollection<CodalAlertSubscription>> GetAsync(
        CodalAlertActor actor,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.CodalAlertSubscriptions
            .AsNoTracking()
            .Where(row =>
                row.TenantId == actor.TenantId &&
                row.ActorId == actor.ActorId &&
                row.ActorType == actor.ActorType)
            .OrderBy(row => row.ExternalCompanyId)
            .ThenByDescending(row => row.UpdatedAtUtc)
            .ToArrayAsync(cancellationToken);
        return rows.Select(ToDomain).ToArray();
    }

    public async Task<CodalAlertSubscription?> FindAsync(
        CodalAlertActor actor,
        Guid subscriptionId,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.CodalAlertSubscriptions
            .AsNoTracking()
            .Where(item =>
                item.Id == subscriptionId &&
                item.TenantId == actor.TenantId &&
                item.ActorId == actor.ActorId &&
                item.ActorType == actor.ActorType)
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : ToDomain(row);
    }

    public async Task<CodalAlertSubscription?> FindForCompanyAsync(
        CodalAlertActor actor,
        string externalCompanyId,
        CancellationToken cancellationToken)
    {
        var companyId = externalCompanyId.Trim();
        var row = await dbContext.CodalAlertSubscriptions
            .AsNoTracking()
            .Where(item =>
                item.TenantId == actor.TenantId &&
                item.ActorId == actor.ActorId &&
                item.ActorType == actor.ActorType &&
                item.ExternalCompanyId == companyId)
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyCollection<CodalAlertSubscription>> GetActiveForCompaniesAsync(
        IReadOnlyCollection<string> externalCompanyIds,
        CancellationToken cancellationToken)
    {
        if (externalCompanyIds.Count == 0) return [];

        var ids = externalCompanyIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var active = CodalAlertSubscriptionState.Active.ToString();
        var rows = await dbContext.CodalAlertSubscriptions
            .AsNoTracking()
            .Where(row => ids.Contains(row.ExternalCompanyId) && row.State == active)
            .ToArrayAsync(cancellationToken);
        return rows.Select(ToDomain).ToArray();
    }

    public async Task SaveAsync(CodalAlertSubscription subscription, CancellationToken cancellationToken)
    {
        var row = await dbContext.CodalAlertSubscriptions
            .Where(item => item.Id == subscription.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            dbContext.CodalAlertSubscriptions.Add(ToRow(subscription));
        }
        else
        {
            Apply(row, subscription);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(
        CodalAlertActor actor,
        Guid subscriptionId,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.CodalAlertSubscriptions
            .Where(row =>
                row.Id == subscriptionId &&
                row.TenantId == actor.TenantId &&
                row.ActorId == actor.ActorId &&
                row.ActorType == actor.ActorType)
            .ToArrayAsync(cancellationToken);
        dbContext.CodalAlertSubscriptions.RemoveRange(rows);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static CodalAlertSubscription ToDomain(CodalAlertSubscriptionRow row) =>
        CodalAlertSubscription.Rehydrate(
            row.Id,
            new CodalAlertActor(row.TenantId, row.ActorId, row.ActorType),
            row.ExternalCompanyId,
            Deserialize(row.AnnouncementTypesJson),
            Enum.Parse<CodalAnnouncementImportance>(row.MinimumImportance),
            row.RawAlertEnabled,
            row.AiSummaryEnabled,
            Enum.Parse<CodalAlertSubscriptionState>(row.State),
            row.CreatedAtUtc,
            row.UpdatedAtUtc);

    private static CodalAlertSubscriptionRow ToRow(CodalAlertSubscription subscription)
    {
        var row = new CodalAlertSubscriptionRow { Id = subscription.Id };
        Apply(row, subscription);
        return row;
    }

    private static void Apply(CodalAlertSubscriptionRow row, CodalAlertSubscription subscription)
    {
        row.TenantId = subscription.Actor.TenantId;
        row.ActorId = subscription.Actor.ActorId;
        row.ActorType = subscription.Actor.ActorType;
        row.ExternalCompanyId = subscription.ExternalCompanyId;
        row.AnnouncementTypesJson = JsonSerializer.Serialize(subscription.AnnouncementTypes, JsonOptions);
        row.MinimumImportance = subscription.MinimumImportance.ToString();
        row.RawAlertEnabled = subscription.RawAlertEnabled;
        row.AiSummaryEnabled = subscription.AiSummaryEnabled;
        row.State = subscription.State.ToString();
        row.CreatedAtUtc = subscription.CreatedAtUtc;
        row.UpdatedAtUtc = subscription.UpdatedAtUtc;
    }

    private static IReadOnlyCollection<CodalAnnouncementType> Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<IReadOnlyCollection<CodalAnnouncementType>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
