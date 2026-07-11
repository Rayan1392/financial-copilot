using System.Text;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.FinancialData.FollowedSymbols;
using FinancialCopilot.Domain.Financial.Insights;
using static FinancialCopilot.Application.FinancialData.Insights.PersonalizedInsightUseCaseMapping;

namespace FinancialCopilot.Application.FinancialData.Insights;

public sealed class GetMyFollowedSymbolInsightsUseCase(
    IFollowedSymbolRepository followedSymbols,
    IFollowedSymbolInsightFeedRepository insightFeed) : IGetMyFollowedSymbolInsightsUseCase
{
    public async Task<FollowedSymbolInsightFeedResponse> ExecuteAsync(
        GetMyFollowedSymbolInsightsQuery query,
        CancellationToken cancellationToken = default)
    {
        var actor = ToInsightActor(query.Actor);
        var followed = await followedSymbols.GetAsync(ToFollowedActor(query.Actor), cancellationToken);
        var followedIds = followed
            .Select(item => item.ExternalCompanyId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (followedIds.Length == 0)
        {
            return Empty(
                "NoFollowedSymbols",
                "You are not following any symbols yet. Follow symbols from a company page or an AI answer card to receive a personalized intelligence feed.",
                [new InsightActionDto("OpenFollowedSymbols", "Manage followed symbols", "/followed-symbols")]);
        }

        var response = await insightFeed.QueryAsync(
            new FollowedSymbolInsightFeedQuery(
                actor,
                followedIds,
                query.InsightType,
                query.Severity,
                query.DateFrom,
                query.DateTo,
                query.IncludeExpired,
                query.IncludeDismissed,
                Math.Max(0, query.Skip),
                Math.Clamp(query.Take <= 0 ? 20 : query.Take, 1, 100)),
            cancellationToken);

        if (response.Items.Count == 0)
        {
            return response with
            {
                EmptyState = new FollowedSymbolInsightEmptyState(
                    "NoCurrentInsights",
                    "No current insights were found for your followed symbols.",
                    [
                        new InsightActionDto("OpenMarketInsights", "View market insights", "/insights"),
                        new InsightActionDto("ManageFollowedSymbols", "Manage followed symbols", "/followed-symbols")
                    ])
            };
        }

        return response;
    }

    private static FollowedSymbolInsightFeedResponse Empty(
        string reason,
        string message,
        IReadOnlyList<InsightActionDto> actions) =>
        new(0, DateTimeOffset.UtcNow, [], new FollowedSymbolInsightEmptyState(reason, message, actions));

    internal static InsightUserActor ToInsightActor(CurrentActor actor) =>
        new(actor.TenantId, actor.ActorId, actor.ActorType.ToString());

    internal static FinancialCopilot.Domain.Financial.FollowedSymbols.FollowedSymbolActor ToFollowedActor(CurrentActor actor) =>
        new(actor.TenantId, actor.ActorId, actor.ActorType.ToString());
}

public sealed class MarkUserInsightSeenUseCase(
    IUserInsightStateRepository states,
    IInsightEventRepository insights,
    IFollowedSymbolRepository followedSymbols,
    TimeProvider timeProvider) : IMarkUserInsightSeenUseCase
{
    public async Task<UserInsightStateDto> ExecuteAsync(
        MarkUserInsightSeenCommand command,
        CancellationToken cancellationToken = default)
    {
        await EnsureInsightBelongsToActorFeedAsync(
            command.Actor, command.InsightEventId, insights, followedSymbols, cancellationToken);
        var state = await states.MarkSeenAsync(
            GetMyFollowedSymbolInsightsUseCase.ToInsightActor(command.Actor),
            command.InsightEventId,
            timeProvider.GetUtcNow(),
            cancellationToken);
        return ToDto(state);
    }
}

public sealed class DismissUserInsightUseCase(
    IUserInsightStateRepository states,
    IInsightEventRepository insights,
    IFollowedSymbolRepository followedSymbols,
    TimeProvider timeProvider) : IDismissUserInsightUseCase
{
    public async Task<UserInsightStateDto> ExecuteAsync(
        DismissUserInsightCommand command,
        CancellationToken cancellationToken = default)
    {
        await EnsureInsightBelongsToActorFeedAsync(
            command.Actor, command.InsightEventId, insights, followedSymbols, cancellationToken);
        var state = await states.DismissAsync(
            GetMyFollowedSymbolInsightsUseCase.ToInsightActor(command.Actor),
            command.InsightEventId,
            timeProvider.GetUtcNow(),
            cancellationToken);
        return ToDto(state);
    }
}

public sealed class ExplainInsightUseCase(
    IInsightEventRepository insights,
    IFollowedSymbolRepository followedSymbols) : IExplainInsightUseCase
{
    public async Task<string> ExecuteAsync(
        ExplainInsightQuery query,
        CancellationToken cancellationToken = default)
    {
        var insight = await EnsureInsightBelongsToActorFeedAsync(
            query.Actor, query.InsightEventId, insights, followedSymbols, cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine($"Insight for {insight.Symbol}: {insight.Title}");
        sb.AppendLine();
        sb.AppendLine(insight.Summary);
        sb.AppendLine();
        sb.AppendLine($"Reason: {insight.Reason}");
        sb.AppendLine($"Severity: {insight.Severity}; importance: {insight.ImportanceScore:0.##}; confidence: {insight.ConfidenceScore:0.##}.");
        sb.AppendLine($"Source: {insight.SourceProviderName} / {insight.SourceEntityType}" +
                      (string.IsNullOrWhiteSpace(insight.SourcePeriod) ? "." : $" / period {insight.SourcePeriod}."));

        if (insight.Evidence.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Evidence:");
            foreach (var item in insight.Evidence)
            {
                var period = string.IsNullOrWhiteSpace(item.SourcePeriod) ? "" : $" | period: {item.SourcePeriod}";
                var synced = item.LastSyncedAtUtc.HasValue ? $" | synced: {item.LastSyncedAtUtc:yyyy-MM-dd HH:mm:ss} UTC" : "";
                sb.AppendLine($"- {item.Label}: {item.Value} | source: {item.SourceProvider}{period}{synced}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Suggested follow-up questions:");
        foreach (var question in SuggestedQuestions(insight))
        {
            sb.AppendLine($"- {question}");
        }

        sb.AppendLine();
        sb.AppendLine("This is an evidence-backed event for attention, not a buy or sell recommendation.");
        return sb.ToString().TrimEnd();
    }

    private static IReadOnlyList<string> SuggestedQuestions(InsightFeedItem insight) =>
        insight.InsightType switch
        {
            InsightType.MonthlySalesAnomaly =>
            [
                $"Show monthly sales trend for {insight.Symbol}.",
                $"Compare this event with the latest monthly report for {insight.Symbol}."
            ],
            InsightType.PriceMovement =>
            [
                $"Show live metrics for {insight.Symbol}.",
                $"What evidence caused this price movement insight for {insight.Symbol}?"
            ],
            InsightType.ComprehensiveAnalysisPublished =>
            [
                $"Show the latest comprehensive analysis for {insight.Symbol}.",
                $"What source evidence is attached to this insight?"
            ],
            InsightType.FinancialStatementPublished =>
            [
                $"Show the latest financial statement table for {insight.Symbol}.",
                $"Analyze the latest financial statement for {insight.Symbol}."
            ],
            _ =>
            [
                $"Show current metrics for {insight.Symbol}.",
                $"What evidence supports this insight for {insight.Symbol}?"
            ]
        };
}

public sealed class InsightNotInFollowedFeedException(Guid insightEventId)
    : InvalidOperationException($"Insight '{insightEventId}' was not found in the actor's followed-symbol feed.");

internal static class PersonalizedInsightUseCaseMapping
{
    public static UserInsightStateDto ToDto(UserInsightState state) =>
        new(
            state.InsightEventId,
            state.Seen,
            state.Dismissed,
            state.SeenAtUtc,
            state.DismissedAtUtc);

    public static async Task<InsightFeedItem> EnsureInsightBelongsToActorFeedAsync(
        CurrentActor actor,
        Guid insightEventId,
        IInsightEventRepository insights,
        IFollowedSymbolRepository followedSymbols,
        CancellationToken cancellationToken)
    {
        var insight = await insights.FindAsync(insightEventId, cancellationToken)
            ?? throw new InsightNotInFollowedFeedException(insightEventId);
        var followed = await followedSymbols.GetAsync(
            GetMyFollowedSymbolInsightsUseCase.ToFollowedActor(actor),
            cancellationToken);
        if (!followed.Any(item => string.Equals(item.ExternalCompanyId, insight.ExternalCompanyId, StringComparison.Ordinal)))
        {
            throw new InsightNotInFollowedFeedException(insightEventId);
        }

        return insight;
    }
}
