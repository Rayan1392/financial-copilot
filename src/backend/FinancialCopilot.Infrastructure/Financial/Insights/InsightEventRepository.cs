using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Insights;
using FinancialCopilot.Domain.Financial.Insights;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Insights;

internal sealed class InsightEventRepository(
    FinancialIngestionDbContext dbContext,
    TimeProvider timeProvider) : IInsightEventRepository, IFollowedSymbolInsightFeedRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<int> UpsertAsync(
        IReadOnlyCollection<InsightEvent> events,
        CancellationToken cancellationToken = default)
    {
        if (events.Count == 0) return 0;

        var keys = events.Select(e => e.DeduplicationKey).ToArray();
        var existing = await dbContext.InsightEvents
            .Where(row => keys.Contains(row.DeduplicationKey))
            .ToDictionaryAsync(row => row.DeduplicationKey, cancellationToken);

        foreach (var insight in events)
        {
            if (existing.TryGetValue(insight.DeduplicationKey, out var row))
            {
                Apply(row, insight, preserveId: true);
            }
            else
            {
                dbContext.InsightEvents.Add(ToRow(insight));
            }
        }

        return await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<InsightFeedResponse> QueryAsync(
        InsightFeedQuery query,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(query.Take <= 0 ? 20 : query.Take, 1, 100);
        var skip = Math.Max(0, query.Skip);
        var now = timeProvider.GetUtcNow();

        var rowsQuery = dbContext.InsightEvents.AsNoTracking();

        if (!query.IncludeExpired)
            rowsQuery = rowsQuery.Where(row => row.ExpiresAtUtc == null || row.ExpiresAtUtc > now);

        if (!string.IsNullOrWhiteSpace(query.Symbol))
        {
            var symbol = query.Symbol.Trim();
            rowsQuery = rowsQuery.Where(row => row.Symbol == symbol);
        }

        if (!string.IsNullOrWhiteSpace(query.IndustryCode))
        {
            var industryCode = query.IndustryCode.Trim();
            rowsQuery = rowsQuery.Where(row => row.IndustryCode == industryCode);
        }

        if (query.InsightType.HasValue)
        {
            var type = query.InsightType.Value.ToString();
            rowsQuery = rowsQuery.Where(row => row.InsightType == type);
        }

        if (query.Severity.HasValue)
        {
            var severity = query.Severity.Value.ToString();
            rowsQuery = rowsQuery.Where(row => row.Severity == severity);
        }

        if (query.DateFrom.HasValue)
            rowsQuery = rowsQuery.Where(row => row.DetectedAtUtc >= query.DateFrom.Value);

        if (query.DateTo.HasValue)
            rowsQuery = rowsQuery.Where(row => row.DetectedAtUtc <= query.DateTo.Value);

        var total = await rowsQuery.CountAsync(cancellationToken);
        var rows = await rowsQuery
            .OrderByDescending(row => row.ImportanceScore)
            .ThenByDescending(row => row.DetectedAtUtc)
            .ThenByDescending(row => row.ConfidenceScore)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return new InsightFeedResponse(
            total,
            now,
            rows.Select(ToItem).ToList());
    }

    public async Task<InsightFeedItem?> FindAsync(
        Guid insightEventId,
        CancellationToken cancellationToken = default)
    {
        var row = await dbContext.InsightEvents
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == insightEventId, cancellationToken);
        return row is null ? null : ToItem(row);
    }

    public async Task<FollowedSymbolInsightFeedResponse> QueryAsync(
        FollowedSymbolInsightFeedQuery query,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(query.Take <= 0 ? 20 : query.Take, 1, 100);
        var skip = Math.Max(0, query.Skip);
        var now = timeProvider.GetUtcNow();
        var followedIds = query.ExternalCompanyIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (followedIds.Length == 0)
        {
            return new FollowedSymbolInsightFeedResponse(0, now, [], null);
        }

        var rowsQuery = dbContext.InsightEvents
            .AsNoTracking()
            .Where(row => followedIds.Contains(row.ExternalCompanyId));

        if (!query.IncludeExpired)
            rowsQuery = rowsQuery.Where(row => row.ExpiresAtUtc == null || row.ExpiresAtUtc > now);

        if (query.InsightType.HasValue)
        {
            var type = query.InsightType.Value.ToString();
            rowsQuery = rowsQuery.Where(row => row.InsightType == type);
        }

        if (query.Severity.HasValue)
        {
            var severity = query.Severity.Value.ToString();
            rowsQuery = rowsQuery.Where(row => row.Severity == severity);
        }

        if (query.DateFrom.HasValue)
            rowsQuery = rowsQuery.Where(row => row.DetectedAtUtc >= query.DateFrom.Value);

        if (query.DateTo.HasValue)
            rowsQuery = rowsQuery.Where(row => row.DetectedAtUtc <= query.DateTo.Value);

        if (!query.IncludeDismissed)
        {
            rowsQuery = rowsQuery.Where(row => !dbContext.UserInsightStates.Any(state =>
                state.TenantId == query.Actor.TenantId &&
                state.ActorId == query.Actor.ActorId &&
                state.ActorType == query.Actor.ActorType &&
                state.InsightEventId == row.Id &&
                state.DismissedAtUtc != null));
        }

        var total = await rowsQuery.CountAsync(cancellationToken);
        var rows = await rowsQuery
            .OrderByDescending(row =>
                row.Severity == nameof(InsightSeverity.Critical) ? 4 :
                row.Severity == nameof(InsightSeverity.Important) ? 3 :
                row.Severity == nameof(InsightSeverity.Notice) ? 2 : 1)
            .ThenByDescending(row => row.ImportanceScore)
            .ThenByDescending(row => row.DetectedAtUtc)
            .ThenByDescending(row => row.ConfidenceScore)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        var ids = rows.Select(row => row.Id).ToArray();
        var states = await dbContext.UserInsightStates
            .AsNoTracking()
            .Where(state =>
                state.TenantId == query.Actor.TenantId &&
                state.ActorId == query.Actor.ActorId &&
                state.ActorType == query.Actor.ActorType &&
                ids.Contains(state.InsightEventId))
            .ToDictionaryAsync(state => state.InsightEventId, cancellationToken);

        var items = rows
            .Select(row =>
            {
                states.TryGetValue(row.Id, out var state);
                var insight = ToItem(row);
                return new FollowedSymbolInsightFeedItem(
                    insight,
                    state?.SeenAtUtc is not null,
                    state?.DismissedAtUtc is not null,
                    state?.SeenAtUtc,
                    state?.DismissedAtUtc,
                    BuildActions(insight));
            })
            .ToArray();

        return new FollowedSymbolInsightFeedResponse(total, now, items, null);
    }

    private static InsightEventRow ToRow(InsightEvent insight)
    {
        var row = new InsightEventRow();
        Apply(row, insight, preserveId: false);
        return row;
    }

    private static void Apply(InsightEventRow row, InsightEvent insight, bool preserveId)
    {
        if (!preserveId)
            row.Id = insight.Id;
        row.ExternalCompanyId = insight.ExternalCompanyId;
        row.Symbol = insight.Symbol;
        row.IndustryCode = insight.IndustryCode;
        row.InsightType = insight.InsightType.ToString();
        row.Severity = insight.Severity.ToString();
        row.ImportanceScore = insight.ImportanceScore;
        row.ConfidenceScore = insight.ConfidenceScore;
        row.Title = insight.Title;
        row.Summary = insight.Summary;
        row.Reason = insight.Reason;
        row.EvidenceJson = JsonSerializer.Serialize(insight.Evidence, JsonOptions);
        row.SourceProviderName = insight.SourceProviderName;
        row.SourceEntityType = insight.SourceEntityType.ToString();
        row.SourceEntityId = insight.SourceEntityId;
        row.SourcePeriod = insight.SourcePeriod;
        row.DetectedAtUtc = insight.DetectedAtUtc;
        row.ExpiresAtUtc = insight.ExpiresAtUtc;
        row.DeduplicationKey = insight.DeduplicationKey;
        row.SuggestedActionsJson = JsonSerializer.Serialize(insight.SuggestedActions, JsonOptions);
    }

    private static InsightFeedItem ToItem(InsightEventRow row) => new(
        row.Id,
        row.ExternalCompanyId,
        row.Symbol,
        row.IndustryCode,
        Enum.Parse<InsightType>(row.InsightType),
        Enum.Parse<InsightSeverity>(row.Severity),
        row.ImportanceScore,
        row.ConfidenceScore,
        row.Title,
        row.Summary,
        row.Reason,
        Deserialize<IReadOnlyList<InsightEvidenceItem>>(row.EvidenceJson) ?? [],
        row.SourceProviderName,
        Enum.Parse<InsightSourceEntityType>(row.SourceEntityType),
        row.SourceEntityId,
        row.SourcePeriod,
        row.DetectedAtUtc,
        row.ExpiresAtUtc,
        row.DeduplicationKey,
        Deserialize<IReadOnlyList<InsightAction>>(row.SuggestedActionsJson) ?? []);

    private static IReadOnlyList<InsightActionDto> BuildActions(InsightFeedItem insight)
    {
        var actions = insight.SuggestedActions
            .Select(action => action switch
            {
                InsightAction.OpenSymbol => new InsightActionDto("OpenSymbol", "Open symbol", insight.ExternalCompanyId),
                InsightAction.AskAiAboutThis => new InsightActionDto("AskAiAboutThis", "Ask AI", insight.Id.ToString()),
                InsightAction.OpenSourceReport => new InsightActionDto("OpenSourceReport", "Open source report", insight.SourceEntityId),
                _ => new InsightActionDto(action.ToString(), action.ToString(), null)
            })
            .ToList();

        actions.Add(new InsightActionDto("Dismiss", "Dismiss", insight.Id.ToString()));
        return actions;
    }

    private static T? Deserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}

internal sealed class UserInsightStateRepository(
    FinancialIngestionDbContext dbContext) : IUserInsightStateRepository
{
    public async Task<UserInsightState?> FindAsync(
        InsightUserActor actor,
        Guid insightEventId,
        CancellationToken cancellationToken = default)
    {
        var row = await dbContext.UserInsightStates
            .AsNoTracking()
            .Where(state =>
                state.TenantId == actor.TenantId &&
                state.ActorId == actor.ActorId &&
                state.ActorType == actor.ActorType &&
                state.InsightEventId == insightEventId)
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : ToDomain(row);
    }

    public Task<UserInsightState> MarkSeenAsync(
        InsightUserActor actor,
        Guid insightEventId,
        DateTimeOffset seenAtUtc,
        CancellationToken cancellationToken = default) =>
        UpsertAsync(actor, insightEventId, seenAtUtc, state => state.MarkSeen(seenAtUtc), cancellationToken);

    public Task<UserInsightState> DismissAsync(
        InsightUserActor actor,
        Guid insightEventId,
        DateTimeOffset dismissedAtUtc,
        CancellationToken cancellationToken = default) =>
        UpsertAsync(actor, insightEventId, dismissedAtUtc, state => state.Dismiss(dismissedAtUtc), cancellationToken);

    private async Task<UserInsightState> UpsertAsync(
        InsightUserActor actor,
        Guid insightEventId,
        DateTimeOffset now,
        Action<UserInsightState> mutate,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.UserInsightStates
            .Where(state =>
                state.TenantId == actor.TenantId &&
                state.ActorId == actor.ActorId &&
                state.ActorType == actor.ActorType &&
                state.InsightEventId == insightEventId)
            .FirstOrDefaultAsync(cancellationToken);

        var state = row is null
            ? UserInsightState.Create(actor, insightEventId, now)
            : ToDomain(row);
        mutate(state);

        if (row is null)
        {
            dbContext.UserInsightStates.Add(ToRow(state));
        }
        else
        {
            Apply(row, state);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return state;
    }

    private static UserInsightState ToDomain(UserInsightStateRow row) =>
        UserInsightState.Rehydrate(
            row.Id,
            new InsightUserActor(row.TenantId, row.ActorId, row.ActorType),
            row.InsightEventId,
            row.SeenAtUtc,
            row.DismissedAtUtc,
            row.CreatedAtUtc,
            row.UpdatedAtUtc);

    private static UserInsightStateRow ToRow(UserInsightState state)
    {
        var row = new UserInsightStateRow { Id = state.Id };
        Apply(row, state);
        return row;
    }

    private static void Apply(UserInsightStateRow row, UserInsightState state)
    {
        row.TenantId = state.Actor.TenantId;
        row.ActorId = state.Actor.ActorId;
        row.ActorType = state.Actor.ActorType;
        row.InsightEventId = state.InsightEventId;
        row.SeenAtUtc = state.SeenAtUtc;
        row.DismissedAtUtc = state.DismissedAtUtc;
        row.CreatedAtUtc = state.CreatedAtUtc;
        row.UpdatedAtUtc = state.UpdatedAtUtc;
    }
}
