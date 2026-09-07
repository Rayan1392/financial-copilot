using System.Globalization;
using System.Text.Json;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.Telegram;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Authentication;

/// <summary>Owns the bounded Feature 133 gates and keeps channel execution separate from linked users.</summary>
public sealed class TelegramChannelMonthlyReportHandler(
    AuthDbContext auth,
    FinancialIngestionDbContext financial,
    ICompanyResolverService companies,
    ISingleCompanyMonthlyIngestionService ingestion,
    ICompanyMonthlyActivityTrendSnapshotRepository snapshots,
    IAiQueryOrchestrationService orchestration,
    IConversationRepository conversations,
    ITelegramAssistantResponseRenderer renderer,
    ITelegramMonthlyReportRecognizer recognizer,
    IOptions<TelegramChannelMonthlyReportOptions> configured,
    TimeProvider clock,
    ILogger<TelegramChannelMonthlyReportHandler> logger) : ITelegramChannelMonthlyReportHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<TelegramAssistantResult> HandleAsync(TelegramAssistantUpdate update, CurrentActor actor, CancellationToken cancellationToken)
    {
        if (update.Kind != TelegramAssistantUpdateKind.ChannelPost || update.TelegramChatId == 0 || update.TelegramMessageId is not > 0 ||
            !string.Equals(update.ChatType, "channel", StringComparison.OrdinalIgnoreCase))
            return Empty(TelegramAssistantResultStatus.Unsupported, update);

        var options = configured.Value;
        if (options.MaximumTextLength is <= 0 or > 4096 || string.IsNullOrWhiteSpace(update.Text) || update.Text.Length > options.MaximumTextLength)
            return Empty(TelegramAssistantResultStatus.Unsupported, update);
        if (options.AllowedChannelIds.Length > 0 && !options.AllowedChannelIds.Contains(update.TelegramChatId.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal))
            return Empty(TelegramAssistantResultStatus.Unsupported, update);
        if (!MatchesActor(options, actor)) return Empty(TelegramAssistantResultStatus.Unsupported, update);

        var recognition = recognizer.Recognize(update.Text);
        if (recognition is null) return Empty(TelegramAssistantResultStatus.Unsupported, update);

        var key = $"channel-monthly:{actor.ApiClientId ?? actor.ActorId}:{update.TelegramChatId}:{update.TelegramMessageId.Value}";
        var existing = await auth.TelegramProcessedUpdates.AsNoTracking().SingleOrDefaultAsync(x => x.IdempotencyKey == key, cancellationToken);
        if (existing is not null && !string.IsNullOrWhiteSpace(existing.ResponseJson))
        {
            try
            {
                var replay = JsonSerializer.Deserialize<TelegramAssistantResult>(existing.ResponseJson, JsonOptions);
                if (replay is not null) return replay with { Status = TelegramAssistantResultStatus.Replayed };
            }
            catch (JsonException) { logger.LogWarning("Feature 133 stored result could not be replayed for {Key}.", key); }
            return Empty(TelegramAssistantResultStatus.Replayed, update);
        }

        var claim = new TelegramProcessedUpdateRow
        {
            Id = Guid.NewGuid(), IdempotencyKey = key, TelegramUpdateId = update.TelegramUpdateId,
            TelegramUserId = 0, TelegramChatId = update.TelegramChatId, Status = "Feature133:Claimed",
            TenantId = actor.TenantId, ResponseJson = "{}", ProcessedAtUtc = clock.GetUtcNow(),
            ExpiresAtUtc = clock.GetUtcNow().AddDays(90), CorrelationId = update.CorrelationId
        };
        auth.TelegramProcessedUpdates.Add(claim);
        try { await auth.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            var winner = await auth.TelegramProcessedUpdates.AsNoTracking().SingleOrDefaultAsync(x => x.IdempotencyKey == key, cancellationToken);
            if (winner is not null && winner.ResponseJson != "{}")
            {
                var replay = JsonSerializer.Deserialize<TelegramAssistantResult>(winner.ResponseJson, JsonOptions);
                if (replay is not null) return replay with { Status = TelegramAssistantResultStatus.Replayed };
            }
            return Empty(TelegramAssistantResultStatus.Replayed, update);
        }

        if (!options.AutoBackfillEnabled)
            return await CompleteAsync(claim, Empty(TelegramAssistantResultStatus.Accepted, update), "Feature133:AutomationSkipped", "AutoBackfillDisabled", cancellationToken);
        if (options.AutoPublishTrendEnabled is false)
            logger.LogInformation("Feature 133 post {ChatId}/{MessageId} will run refresh/readiness only.", update.TelegramChatId, update.TelegramMessageId);
        if (!int.TryParse(recognition.Symbol, out _) && !string.IsNullOrWhiteSpace(recognition.Symbol)) { }

        var resolved = await companies.ResolveBySymbolAsync(recognition.Symbol, cancellationToken);
        if (resolved is null || !int.TryParse(resolved.ExternalCompanyId, out var externalCompanyId))
            return await CompleteAsync(claim, Empty(TelegramAssistantResultStatus.Accepted, update), "Feature133:Failed", "SymbolUnresolved", cancellationToken);
        var eligible = await financial.NoavaranEligibleCompanies.AsNoTracking().AnyAsync(x => x.ExternalCompanyId == resolved.ExternalCompanyId, cancellationToken);
        if (!eligible)
            return await CompleteAsync(claim, Empty(TelegramAssistantResultStatus.Accepted, update), "Feature133:Failed", "SymbolUnresolved", cancellationToken);

        var refreshStarted = clock.GetUtcNow();
        claim.Status = "Feature133:Refreshing";
        await auth.SaveChangesAsync(cancellationToken);
        var refresh = await ingestion.ExecuteDirectAsync(new SingleCompanyMonthlyDirectIngestionRequest(externalCompanyId, recognition.ShamsiYear, recognition.ShamsiMonth), cancellationToken);
        if (refresh.Run.Status != DataSyncRunStatus.Completed || refresh.Run.ErrorCount != 0 || refresh.Run.CompletedAt is null)
            return await CompleteAsync(claim, Empty(TelegramAssistantResultStatus.Accepted, update), "Feature133:Failed", "RefreshFailed", cancellationToken);

        var fresh = (await snapshots.GetCompanyTrendAsync(resolved.ExternalCompanyId, recognition.ShamsiYear, recognition.ShamsiMonth, recognition.ShamsiYear, recognition.ShamsiMonth, cancellationToken))
            .FirstOrDefault(x => x.ExternalCompanyId == resolved.ExternalCompanyId && x.ReportYear == recognition.ShamsiYear && x.ReportMonth == recognition.ShamsiMonth && x.CalculatedAtUtc >= refreshStarted);
        if (fresh is null)
            return await CompleteAsync(claim, Empty(TelegramAssistantResultStatus.Accepted, update), "Feature133:Failed", "RefreshNotReady", cancellationToken);
        claim.Status = "Feature133:Refreshed";
        await auth.SaveChangesAsync(cancellationToken);

        if (!options.AutoPublishTrendEnabled)
            return await CompleteAsync(claim, Empty(TelegramAssistantResultStatus.Accepted, update), "Feature133:BackfillOnlyCompleted", "AutoPublishTrendDisabled", cancellationToken);

        claim.Status = "Feature133:Querying";
        await auth.SaveChangesAsync(cancellationToken);
        var conversationId = await conversations.CreateEmptyAsync(actor.TenantId, actor.ActorId, clock.GetUtcNow(), cancellationToken);
        var response = await orchestration.ExecuteAsync(new AiQueryRequest(
            $"روند تولید و فروش {resolved.TseSymbol ?? resolved.Ticker ?? recognition.Symbol}", actor.TenantId, actor.ActorId,
            update.CorrelationId, conversationId, UserId: null, ApiClientId: actor.ApiClientId,
            ExternalUserId: $"telegram-channel:{update.TelegramChatId}", ActorType: actor.ActorType,
            AuthenticationMode: actor.AuthenticationMode), cancellationToken);
        var trend = response.MonthlyActivityTrendResult;
        if (response.ClarificationRequired || trend is null || !string.Equals(trend.CompanySymbol, resolved.TseSymbol ?? recognition.Symbol, StringComparison.OrdinalIgnoreCase) || trend.LatestReportYear != recognition.ShamsiYear || trend.LatestReportMonth != recognition.ShamsiMonth)
            return await CompleteAsync(claim, Empty(TelegramAssistantResultStatus.Accepted, update), "Feature133:Failed", "PeriodMismatch", cancellationToken);

        var result = new TelegramAssistantResult(TelegramAssistantResultStatus.Accepted, actor.ActorId, actor.TenantId, response.ConversationId, renderer.Render(response, update.Locale), update.CorrelationId, response, renderer.Version);
        return await CompleteAsync(claim, result, "Feature133:Ready", "PublishedReady", cancellationToken);
    }

    private async Task<TelegramAssistantResult> CompleteAsync(TelegramProcessedUpdateRow row, TelegramAssistantResult result, string status, string reason, CancellationToken ct)
    {
        row.Status = status;
        row.ResponseJson = JsonSerializer.Serialize(result, JsonOptions);
        await auth.SaveChangesAsync(ct);
        logger.LogInformation("Feature 133 completed stage {Status} with reason {Reason} for {CorrelationId}.", status, reason, row.CorrelationId);
        return result;
    }

    private static bool MatchesActor(TelegramChannelMonthlyReportOptions options, CurrentActor actor) =>
        actor.ActorType == ActorType.ApiClient && actor.AuthenticationMode == AuthenticationMode.ApiClient &&
        (!Guid.TryParse(options.AuthorizedApiClientId, out var client) || client == actor.ApiClientId) &&
        (!Guid.TryParse(options.AuthorizedTenantId, out var tenant) || tenant == actor.TenantId);

    private static TelegramAssistantResult Empty(TelegramAssistantResultStatus status, TelegramAssistantUpdate update) =>
        new(status, null, null, null, [], update.CorrelationId);
}
