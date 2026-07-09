using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Insights;
using FinancialCopilot.Domain.Financial.Insights;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Insights;

internal sealed class InsightEventRepository(
    FinancialIngestionDbContext dbContext,
    TimeProvider timeProvider) : IInsightEventRepository
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
