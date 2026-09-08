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

    private enum ProcessingStage
    {
        Claimed, Refreshing, Refreshed, Querying, Ready, Failed,
        AutomationSkipped, BackfillOnlyCompleted
    }

    private enum CompletionReason
    {
        AutoBackfillDisabled, SymbolUnresolved, RefreshFailed, RefreshNotReady,
        AutoPublishTrendDisabled, PeriodMismatch, PublishedReady
    }

    public async Task<TelegramAssistantResult> HandleAsync(
        TelegramAssistantUpdate update, CurrentActor actor, CancellationToken cancellationToken)
    {
        var options = configured.Value;
        if (!IsSupported(update, actor, options))
            return Empty(TelegramAssistantResultStatus.Unsupported, update);

        var recognition = recognizer.Recognize(update.Text);
        if (recognition is null)
            return Empty(TelegramAssistantResultStatus.Unsupported, update);

        var (claim, replay) = await ClaimOrReplayAsync(update, actor, cancellationToken);
        if (replay is not null)
            return replay;

        return await ProcessClaimAsync(claim!, update, actor, recognition, options, cancellationToken);
    }

    private async Task<TelegramAssistantResult> ProcessClaimAsync(
        TelegramProcessedUpdateRow claim,
        TelegramAssistantUpdate update,
        CurrentActor actor,
        TelegramMonthlyReportRecognition recognition,
        TelegramChannelMonthlyReportOptions options,
        CancellationToken cancellationToken)
    {
        if (!options.AutoBackfillEnabled)
            return await CompleteWithoutMessageAsync(claim, update, ProcessingStage.AutomationSkipped,
                CompletionReason.AutoBackfillDisabled, cancellationToken);

        var company = await ResolveEligibleCompanyAsync(recognition.Symbol, cancellationToken);
        if (company is null)
            return await FailAsync(claim, update, CompletionReason.SymbolUnresolved, cancellationToken);

        var refreshStarted = clock.GetUtcNow();
        if (!await RefreshCompanyMonthAsync(claim, company, recognition, cancellationToken))
            return await FailAsync(claim, update, CompletionReason.RefreshFailed, cancellationToken);

        if (!await HasFreshSnapshotAsync(company, recognition, refreshStarted, cancellationToken))
            return await FailAsync(claim, update, CompletionReason.RefreshNotReady, cancellationToken);

        await SetStageAsync(claim, ProcessingStage.Refreshed, cancellationToken);

        if (!options.AutoPublishTrendEnabled)
            return await CompleteWithoutMessageAsync(claim, update, ProcessingStage.BackfillOnlyCompleted,
                CompletionReason.AutoPublishTrendDisabled, cancellationToken);

        return await GenerateResponseAsync(claim, update, actor, company, recognition, cancellationToken);
    }

    private static bool IsSupported(
        TelegramAssistantUpdate update, CurrentActor actor, TelegramChannelMonthlyReportOptions options) =>
        update.Kind == TelegramAssistantUpdateKind.ChannelPost &&
        update.TelegramChatId != 0 && update.TelegramMessageId is > 0 &&
        string.Equals(update.ChatType, "channel", StringComparison.OrdinalIgnoreCase) &&
        options.MaximumTextLength is > 0 and <= 4096 &&
        !string.IsNullOrWhiteSpace(update.Text) && update.Text.Length <= options.MaximumTextLength &&
        (options.AllowedChannelIds.Length == 0 ||
            options.AllowedChannelIds.Contains(update.TelegramChatId.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal)) &&
        MatchesActor(options, actor);

    private async Task<(TelegramProcessedUpdateRow? Claim, TelegramAssistantResult? Replay)> ClaimOrReplayAsync(
        TelegramAssistantUpdate update, CurrentActor actor, CancellationToken cancellationToken)
    {
        var key = $"channel-monthly:{actor.ApiClientId ?? actor.ActorId}:{update.TelegramChatId}:{update.TelegramMessageId}";
        var existing = await auth.TelegramProcessedUpdates.AsNoTracking()
            .SingleOrDefaultAsync(x => x.IdempotencyKey == key, cancellationToken);
        if (existing is not null && !string.IsNullOrWhiteSpace(existing.ResponseJson))
            return (null, ReplayOrEmpty(existing, update));

        var now = clock.GetUtcNow();
        var claim = new TelegramProcessedUpdateRow
        {
            Id = Guid.NewGuid(),
            IdempotencyKey = key,
            TelegramUpdateId = update.TelegramUpdateId,
            TelegramUserId = 0,
            TelegramChatId = update.TelegramChatId,
            Status = StageName(ProcessingStage.Claimed),
            TenantId = actor.TenantId,
            ResponseJson = "{}",
            ProcessedAtUtc = now,
            ExpiresAtUtc = now.AddDays(90),
            CorrelationId = update.CorrelationId
        };
        auth.TelegramProcessedUpdates.Add(claim);
        try
        {
            await auth.SaveChangesAsync(cancellationToken);
            return (claim, null);
        }
        catch (DbUpdateException)
        {
            auth.Entry(claim).State = EntityState.Detached;
            var winner = await auth.TelegramProcessedUpdates.AsNoTracking()
                .SingleOrDefaultAsync(x => x.IdempotencyKey == key, cancellationToken);
            // A persistence failure is a duplicate only when another claim actually exists.
            if (winner is null)
                throw;

            return (null, ReplayOrEmpty(winner, update));
        }
    }

    private TelegramAssistantResult ReplayOrEmpty(
        TelegramProcessedUpdateRow row, TelegramAssistantUpdate update)
    {
        // "{}" is an in-progress claim, not a serialized response.
        if (!string.IsNullOrWhiteSpace(row.ResponseJson) && row.ResponseJson != "{}")
        {
            try
            {
                var result = JsonSerializer.Deserialize<TelegramAssistantResult>(row.ResponseJson, JsonOptions);
                if (result is not null)
                    return result with { Status = TelegramAssistantResultStatus.Replayed };
            }
            catch (JsonException)
            {
                logger.LogWarning("Feature 133 stored result could not be replayed for {Key}.", row.IdempotencyKey);
            }
        }

        return Empty(TelegramAssistantResultStatus.Replayed, update);
    }

    private sealed record EligibleCompany(ResolvedCompany Company, int ProviderCompanyId);

    private async Task<EligibleCompany?> ResolveEligibleCompanyAsync(string symbol, CancellationToken cancellationToken)
    {
        var company = await companies.ResolveBySymbolAsync(symbol, cancellationToken);
        if (company is null || !int.TryParse(company.ExternalCompanyId, out var providerCompanyId))
            return null;

        var eligible = await financial.NoavaranEligibleCompanies.AsNoTracking()
            .AnyAsync(x => x.ExternalCompanyId == company.ExternalCompanyId, cancellationToken);
        return eligible ? new EligibleCompany(company, providerCompanyId) : null;
    }

    private async Task<bool> RefreshCompanyMonthAsync(
        TelegramProcessedUpdateRow claim, EligibleCompany company,
        TelegramMonthlyReportRecognition recognition, CancellationToken cancellationToken)
    {
        await SetStageAsync(claim, ProcessingStage.Refreshing, cancellationToken);
        var request = new SingleCompanyMonthlyDirectIngestionRequest(
            company.ProviderCompanyId, recognition.ShamsiYear, recognition.ShamsiMonth);
        var refresh = await ingestion.ExecuteDirectAsync(request, cancellationToken);
        var run = refresh.Run;
        if (run.Status == DataSyncRunStatus.Completed && run.ErrorCount == 0 && run.CompletedAt is not null)
            return true;

        logger.LogWarning(
            "Feature 133 refresh failed. CorrelationId={CorrelationId}, SyncRunId={SyncRunId}, " +
            "CompanyId={CompanyId}, Year={Year}, Month={Month}, Status={Status}, " +
            "ProcessedRecords={ProcessedRecords}, ErrorCount={ErrorCount}, ErrorMessage={ErrorMessage}.",
            claim.CorrelationId, run.Id, company.ProviderCompanyId, recognition.ShamsiYear,
            recognition.ShamsiMonth, run.Status, run.ProcessedRecords, run.ErrorCount, run.ErrorMessage);
        return false;
    }

    private async Task<bool> HasFreshSnapshotAsync(
        EligibleCompany company, TelegramMonthlyReportRecognition recognition,
        DateTimeOffset refreshStarted, CancellationToken cancellationToken)
    {
        var externalCompanyId = company.Company.ExternalCompanyId;
        var trend = await snapshots.GetCompanyTrendAsync(
            externalCompanyId, recognition.ShamsiYear, recognition.ShamsiMonth,
            recognition.ShamsiYear, recognition.ShamsiMonth, cancellationToken);
        return trend.Any(x => x.ExternalCompanyId == externalCompanyId &&
            x.ReportYear == recognition.ShamsiYear && x.ReportMonth == recognition.ShamsiMonth &&
            x.CalculatedAtUtc >= refreshStarted);
    }

    private async Task<TelegramAssistantResult> GenerateResponseAsync(
        TelegramProcessedUpdateRow claim, TelegramAssistantUpdate update, CurrentActor actor,
        EligibleCompany company, TelegramMonthlyReportRecognition recognition,
        CancellationToken cancellationToken)
    {
        await SetStageAsync(claim, ProcessingStage.Querying, cancellationToken);
        var conversationId = await conversations.CreateEmptyAsync(
            actor.TenantId, actor.ActorId, clock.GetUtcNow(), cancellationToken);
        var symbol = company.Company.TseSymbol ?? company.Company.Ticker ?? recognition.Symbol;
        var response = await orchestration.ExecuteAsync(new AiQueryRequest(
            $"روند تولید و فروش {symbol}", actor.TenantId, actor.ActorId,
            update.CorrelationId, conversationId, UserId: null, ApiClientId: actor.ApiClientId,
            ExternalUserId: $"telegram-channel:{update.TelegramChatId}", ActorType: actor.ActorType,
            AuthenticationMode: actor.AuthenticationMode), cancellationToken);

        if (!MatchesRequestedReport(response, company.Company, recognition))
            return await FailAsync(claim, update, CompletionReason.PeriodMismatch, cancellationToken);

        var result = new TelegramAssistantResult(
            TelegramAssistantResultStatus.Accepted, actor.ActorId, actor.TenantId, response.ConversationId,
            renderer.Render(response, update.Locale), update.CorrelationId, response, renderer.Version);
        return await CompleteAsync(claim, result, ProcessingStage.Ready, CompletionReason.PublishedReady, cancellationToken);
    }

    private static bool MatchesRequestedReport(
        AiQueryResponse response, ResolvedCompany company, TelegramMonthlyReportRecognition recognition)
    {
        var trend = response.MonthlyActivityTrendResult;
        return !response.ClarificationRequired && trend is not null &&
            string.Equals(trend.CompanySymbol, company.TseSymbol ?? recognition.Symbol, StringComparison.OrdinalIgnoreCase) &&
            trend.LatestReportYear == recognition.ShamsiYear && trend.LatestReportMonth == recognition.ShamsiMonth;
    }

    private Task<TelegramAssistantResult> FailAsync(
        TelegramProcessedUpdateRow claim, TelegramAssistantUpdate update,
        CompletionReason reason, CancellationToken cancellationToken) =>
        CompleteWithoutMessageAsync(claim, update, ProcessingStage.Failed, reason, cancellationToken);

    private Task<TelegramAssistantResult> CompleteWithoutMessageAsync(
        TelegramProcessedUpdateRow claim, TelegramAssistantUpdate update,
        ProcessingStage stage, CompletionReason reason, CancellationToken cancellationToken) =>
        CompleteAsync(claim, Empty(TelegramAssistantResultStatus.Accepted, update), stage, reason, cancellationToken);

    private async Task SetStageAsync(
        TelegramProcessedUpdateRow claim, ProcessingStage stage, CancellationToken cancellationToken)
    {
        claim.Status = StageName(stage);
        await auth.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Feature 133 entered stage {Status} for {CorrelationId}.", claim.Status, claim.CorrelationId);
    }

    private async Task<TelegramAssistantResult> CompleteAsync(
        TelegramProcessedUpdateRow claim, TelegramAssistantResult result,
        ProcessingStage stage, CompletionReason reason, CancellationToken cancellationToken)
    {
        claim.Status = StageName(stage);
        claim.ResponseJson = JsonSerializer.Serialize(result, JsonOptions);
        await auth.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Feature 133 completed stage {Status} with reason {Reason} for {CorrelationId}.",
            claim.Status, reason.ToString(), claim.CorrelationId);
        return result;
    }

    private static string StageName(ProcessingStage stage) => $"Feature133:{stage}";

    private static bool MatchesActor(TelegramChannelMonthlyReportOptions options, CurrentActor actor) =>
        actor.ActorType == ActorType.ApiClient && actor.AuthenticationMode == AuthenticationMode.ApiClient &&
        (!Guid.TryParse(options.AuthorizedApiClientId, out var client) || client == actor.ApiClientId) &&
        (!Guid.TryParse(options.AuthorizedTenantId, out var tenant) || tenant == actor.TenantId);

    private static TelegramAssistantResult Empty(TelegramAssistantResultStatus status, TelegramAssistantUpdate update) =>
        new(status, null, null, null, [], update.CorrelationId);
}
