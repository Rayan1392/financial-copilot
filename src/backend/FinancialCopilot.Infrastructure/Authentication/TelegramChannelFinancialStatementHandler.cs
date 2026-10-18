using System.Globalization;
using System.Text.Json;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Telegram;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Authentication;

/// <summary>Ingestion-only handler for authorized CodalMirror announcement posts.</summary>
public sealed class TelegramChannelFinancialStatementHandler(
    AuthDbContext auth,
    FinancialIngestionDbContext financial,
    ICompanyResolverService companies,
    IManualNoavaranFinancialStatementBackfillService backfill,
    ITelegramFinancialStatementRecognizer recognizer,
    IOptions<TelegramChannelMonthlyReportOptions> channelAuthorization,
    IOptions<TelegramChannelFinancialStatementOptions> configured,
    TimeProvider clock,
    ILogger<TelegramChannelFinancialStatementHandler> logger) : ITelegramChannelFinancialStatementHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly ManualNoavaranFinancialStatementType[] Categories =
    [
        ManualNoavaranFinancialStatementType.BalanceSheet,
        ManualNoavaranFinancialStatementType.IncomeStatement,
        ManualNoavaranFinancialStatementType.CashFlow,
        ManualNoavaranFinancialStatementType.FinancialRatios
    ];

    public async Task<TelegramAssistantResult?> HandleIfRecognizedAsync(
        TelegramAssistantUpdate update, CurrentActor actor, CancellationToken cancellationToken)
        => await HandleRecognizedAsync(update, actor, null, allowRecoveryClaim: false, cancellationToken);

    private async Task<TelegramAssistantResult?> HandleRecognizedAsync(
        TelegramAssistantUpdate update,
        CurrentActor actor,
        TelegramFinancialStatementRecognition? suppliedAnnouncement,
        bool allowRecoveryClaim,
        CancellationToken cancellationToken)
    {
        if (update.Kind != TelegramAssistantUpdateKind.ChannelPost ||
            !string.Equals(update.ChatType, "channel", StringComparison.OrdinalIgnoreCase) ||
            update.TelegramChatId == 0 || update.TelegramMessageId is not > 0)
            return null;

        var announcement = suppliedAnnouncement ?? recognizer.Recognize(update.Text);
        var candidate = update.Text?.Contains("صورت‌های مالی", StringComparison.Ordinal) == true ||
            update.Text?.Contains("صورت های مالی", StringComparison.Ordinal) == true ||
            update.Text?.Contains("صورتهای مالی", StringComparison.Ordinal) == true;
        if (!candidate && suppliedAnnouncement is null) return null;

        var authOptions = channelAuthorization.Value;
        if ((!allowRecoveryClaim && (actor.ActorType != ActorType.ApiClient || actor.AuthenticationMode != AuthenticationMode.ApiClient)) ||
            authOptions.AllowedChannelIds.Length == 0 || !authOptions.AllowedChannelIds.Contains(
                update.TelegramChatId.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal) ||
            (!allowRecoveryClaim && Guid.TryParse(authOptions.AuthorizedApiClientId, out var apiClientId) && actor.ApiClientId != apiClientId) ||
            (Guid.TryParse(authOptions.AuthorizedTenantId, out var tenantId) && actor.TenantId != tenantId))
        {
            logger.LogWarning("Financial statement channel post rejected by authorization. ChatId={ChatId}, MessageId={MessageId}, CorrelationId={CorrelationId}",
                update.TelegramChatId, update.TelegramMessageId, update.CorrelationId);
            return Empty(TelegramAssistantResultStatus.Unsupported, update);
        }

        if (announcement is null)
        {
            logger.LogWarning("Feature 143 rejected an ambiguous or malformed financial statement post. ChatId={ChatId}, MessageId={MessageId}, CorrelationId={CorrelationId}",
                update.TelegramChatId, update.TelegramMessageId, update.CorrelationId);
            return Empty(TelegramAssistantResultStatus.Accepted, update);
        }

        var key = $"channel-financial-statement:{update.TelegramChatId}:{update.TelegramMessageId}";
        var existing = await auth.TelegramProcessedUpdates.AsNoTracking()
            .SingleOrDefaultAsync(x => x.IdempotencyKey == key, cancellationToken);
        Dictionary<string, FinancialStatementCategoryOutcome> prior = new(StringComparer.Ordinal);
        if (existing is not null)
        {
            if (existing.Status == "Feature143:RecoveryClaimed" && allowRecoveryClaim)
            {
                prior = ReadCategoryOutcomes(existing.ResponseJson);
            }
            else if (existing.Status is "Feature143:Completed" or "Feature143:Disabled" or "Feature143:Unresolved")
                return Empty(TelegramAssistantResultStatus.Replayed, update);
            if (existing.Status == "Feature143:Partial")
            {
                var acquired = await auth.TelegramProcessedUpdates.Where(x => x.Id == existing.Id && x.Status == "Feature143:Partial")
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, "Feature143:Processing"), cancellationToken);
                if (acquired != 1) return Empty(TelegramAssistantResultStatus.Replayed, update);
                prior = ReadCategoryOutcomes(existing.ResponseJson);
            }
            else if (!(existing.Status == "Feature143:RecoveryClaimed" && allowRecoveryClaim))
            {
                if (existing.Status == "Feature143:Claimed")
                    return Empty(TelegramAssistantResultStatus.Replayed, update);
            // A prior invocation stopped after its durable claim. Its provider-side completion
            // cannot be proven; fail closed for operator review rather than replaying blindly.
                logger.LogWarning("Financial statement post requires review after an ambiguous in-flight attempt. ChatId={ChatId}, MessageId={MessageId}, Status={Status}, CorrelationId={CorrelationId}",
                    update.TelegramChatId, update.TelegramMessageId, existing.Status, existing.CorrelationId);
                return Empty(TelegramAssistantResultStatus.Replayed, update);
            }
        }

        var claim = existing is null ? new TelegramProcessedUpdateRow
        {
            Id = Guid.NewGuid(), IdempotencyKey = key, TelegramUpdateId = update.TelegramUpdateId,
            TelegramUserId = 0, TelegramChatId = update.TelegramChatId, TenantId = actor.TenantId,
            Status = "Feature143:Claimed", ResponseJson = "{}", ProcessedAtUtc = clock.GetUtcNow(),
            ExpiresAtUtc = clock.GetUtcNow().AddDays(3650), CorrelationId = update.CorrelationId
        } : await auth.TelegramProcessedUpdates.SingleAsync(x => x.Id == existing.Id, cancellationToken);
        if (existing is null) auth.TelegramProcessedUpdates.Add(claim);
        try { if (existing is null) await auth.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            auth.Entry(claim).State = EntityState.Detached;
            var winner = await auth.TelegramProcessedUpdates.AsNoTracking().AnyAsync(x => x.IdempotencyKey == key, cancellationToken);
            if (!winner) throw;
            logger.LogInformation("Duplicate financial statement channel delivery was suppressed. ChatId={ChatId}, MessageId={MessageId}, CorrelationId={CorrelationId}",
                update.TelegramChatId, update.TelegramMessageId, update.CorrelationId);
            return Empty(TelegramAssistantResultStatus.Replayed, update);
        }

        var options = configured.Value;
        if (!options.AutoBackfillEnabled)
        {
            claim.Status = "Feature143:Disabled";
            claim.ResponseJson = JsonSerializer.Serialize(new { Outcome = "Disabled", Announcement = announcement }, JsonOptions);
            await auth.SaveChangesAsync(cancellationToken);
            Log(update, announcement, null, null, "Disabled", "Automation is disabled.");
            return Empty(TelegramAssistantResultStatus.Accepted, update);
        }

        var resolved = await companies.ResolveBySymbolAsync(announcement.Symbol, cancellationToken);
        if (resolved is null || !int.TryParse(resolved.ExternalCompanyId, out _))
        {
            claim.Status = "Feature143:Unresolved";
            claim.ResponseJson = JsonSerializer.Serialize(new { Outcome = "CompanyUnresolved", Announcement = announcement }, JsonOptions);
            await auth.SaveChangesAsync(cancellationToken);
            Log(update, announcement, null, null, "CompanyUnresolved", "Ticker did not resolve to a provider company.");
            return Empty(TelegramAssistantResultStatus.Accepted, update);
        }

        var eligible = await financial.NoavaranEligibleCompanies.AsNoTracking()
            .AnyAsync(x => x.ExternalCompanyId == resolved.ExternalCompanyId, cancellationToken);
        if (!eligible)
        {
            claim.Status = "Feature143:Unresolved";
            claim.ResponseJson = JsonSerializer.Serialize(new { Outcome = "CompanyUnresolved", Reason = "NotNoavaranEligible", Announcement = announcement }, JsonOptions);
            await auth.SaveChangesAsync(cancellationToken);
            Log(update, announcement, resolved.ExternalCompanyId, null, "CompanyUnresolved", "Company is outside the Noavaran eligible universe.");
            return Empty(TelegramAssistantResultStatus.Accepted, update);
        }

        var periodEnd = DateOnly.FromDateTime(new PersianCalendar().ToDateTime(
            announcement.PeriodYear, announcement.PeriodMonth, announcement.PeriodDay, 0, 0, 0, 0));
        var periodType = announcement.PeriodMonths switch
        {
            3 => "ThreeMonths", 6 => "SixMonths", 9 => "NineMonths", 12 => "TwelveMonths", _ => string.Empty
        };
        // The company catalog currently has no fiscal-calendar field. Use an explicit annual
        // header when present, otherwise reconcile provider-reported fiscal ends from any persisted
        // Noavaran period whose fiscal-year interval contains the announced period. Never infer an
        // exact fiscal-year end from the Jalali year or reporting-period end.
        var evidenceRows = await financial.FinancialStatements.AsNoTracking()
            .Where(x => x.ExternalCompanyId == resolved.ExternalCompanyId && x.ProviderName == ProviderSources.NoavaranCurrentApiName &&
                !string.IsNullOrWhiteSpace(x.WarningsJson))
            .Select(x => x.WarningsJson).ToListAsync(cancellationToken);
        var annualFiscalYearEnd = announcement.IsAnnualFiscalYearHeader
            ? $"{announcement.PeriodYear:D4}/{announcement.PeriodMonth:D2}/{announcement.PeriodDay:D2}"
            : null;
        var fiscalYearEnd = NoavaranFiscalYearEvidenceResolver.Resolve(
            periodEnd, annualFiscalYearEnd, evidenceRows);
        if (string.IsNullOrWhiteSpace(fiscalYearEnd))
        {
            claim.Status = "Feature143:Unresolved";
            claim.ResponseJson = JsonSerializer.Serialize(new { Outcome = "FiscalYearUnresolved", Announcement = announcement }, JsonOptions);
            await auth.SaveChangesAsync(cancellationToken);
            Log(update, announcement, resolved.ExternalCompanyId, null, "FiscalYearUnresolved", "No unique authoritative fiscal-year end exists for the exact announcement period.");
            return Empty(TelegramAssistantResultStatus.Accepted, update);
        }
        var fiscalYearEnding = int.Parse(fiscalYearEnd[..4], CultureInfo.InvariantCulture);

        foreach (var category in Categories)
        {
            prior.TryGetValue(category.ToString(), out var checkpoint);
            if (checkpoint is not null && (checkpoint.Outcome is "Persisted" or "AlreadyExists" or "ReviewRequired" || checkpoint.Attempts >= 3))
                continue;
            try
            {
                var result = await backfill.ExecuteAsync(new ManualNoavaranFinancialStatementBackfillRequest(
                    resolved.ExternalCompanyId, fiscalYearEnding, announcement.PeriodMonths, category), cancellationToken);
                var outcome = result.Outcome;
                var message = result.Message;
                if (result.Outcome is "Persisted" or "AlreadyExists")
                {
                    var validation = await ValidatePersistedPeriodAsync(
                        resolved.ExternalCompanyId, periodEnd, periodType, fiscalYearEnd, category, announcement, result.RunId, cancellationToken);
                    if (!validation.Matches)
                    {
                        outcome = "ReviewRequired";
                        message = validation.Reason;
                    }
                    else if (announcement.IsAmendment)
                    {
                        outcome = "ReviewRequired";
                        message = "Provider variant semantics cannot verify that this is the announced amendment.";
                    }
                    else if (validation.ReconciliationNeeded)
                    {
                        outcome = "ReconcilePending";
                        message = validation.Reason;
                    }
                }
                prior[category.ToString()] = new(outcome, result.ProcessedRecords, message, (checkpoint?.Attempts ?? 0) + 1);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                prior[category.ToString()] = new("UpstreamFailed", 0, exception.Message, (checkpoint?.Attempts ?? 0) + 1);
            }

            claim.Status = "Feature143:Processing";
            claim.ResponseJson = SerializeCategoryOutcomes(prior, announcement);
            await auth.SaveChangesAsync(cancellationToken);
            Log(update, announcement, resolved.ExternalCompanyId, fiscalYearEnding, category + ":" + prior[category.ToString()].Outcome,
                prior[category.ToString()].Message, prior[category.ToString()].ProcessedRecords);
        }

        claim.Status = prior.Values.All(x => x.Outcome is "Persisted" or "AlreadyExists")
            ? "Feature143:Completed"
            : prior.Values.All(x => x.Outcome is "Persisted" or "AlreadyExists" or "ReviewRequired" || x.Attempts >= 3)
                ? "Feature143:ReviewRequired" : "Feature143:Partial";
        claim.ResponseJson = SerializeCategoryOutcomes(prior, announcement);
        claim.ProcessedAtUtc = clock.GetUtcNow();
        await auth.SaveChangesAsync(cancellationToken);
        Log(update, announcement, resolved.ExternalCompanyId, fiscalYearEnding,
            claim.Status == "Feature143:Completed" ? "Persisted" : claim.Status == "Feature143:Partial" ? "PartiallyPersisted" : "ReviewRequired",
            null, prior.Values.Sum(x => x.ProcessedRecords));
        return Empty(TelegramAssistantResultStatus.Accepted, update);
    }

    // Called only by the bounded API reconciliation worker. The normal Partial CAS in
    // HandleRecognizedAsync prevents duplicate workers or a Telegram redelivery from racing.
    public async Task<bool> ReconcilePendingAsync(Guid checkpointId, CancellationToken cancellationToken)
    {
        var checkpoint = await auth.TelegramProcessedUpdates.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == checkpointId, cancellationToken);
        if (checkpoint?.Status != "Feature143:Partial" || checkpoint.TenantId is not Guid tenantId ||
            !configured.Value.AutoBackfillEnabled)
            return false;
        var outcomes = ReadCategoryOutcomes(checkpoint.ResponseJson);
        if (!outcomes.TryGetValue(nameof(ManualNoavaranFinancialStatementType.IncomeStatement), out var income) ||
            income.Outcome != "ReconcilePending" || income.Attempts is < 1 or >= 3)
            return false;
        var delay = income.Attempts == 1 ? TimeSpan.FromMinutes(30) : TimeSpan.FromMinutes(60);
        if (checkpoint.ProcessedAtUtc.Add(delay) > clock.GetUtcNow()) return false;
        var announcement = ReadAnnouncement(checkpoint.ResponseJson);
        if (announcement is null) return false;

        var messageId = ParseMessageId(checkpoint.IdempotencyKey);
        if (messageId is null) return false;
        var update = new TelegramAssistantUpdate(
            checkpoint.TelegramUpdateId, TelegramAssistantUpdateKind.ChannelPost, 0,
            checkpoint.TelegramChatId, null, messageId,
            null, null, null, "fa-IR", clock.GetUtcNow(),
            $"feature143-reconciliation-{checkpoint.Id:N}", "channel");
        var actor = new CurrentActor(ActorType.ApiClient, Guid.Empty, tenantId, AuthenticationMode.ApiClient);
        await HandleRecognizedAsync(update, actor, announcement, allowRecoveryClaim: true, cancellationToken);
        return true;
    }

    private static long? ParseMessageId(string key) =>
        long.TryParse(key[(key.LastIndexOf(':') + 1)..], CultureInfo.InvariantCulture, out var id) && id > 0
            ? id : null;

    private static TelegramFinancialStatementRecognition? ReadAnnouncement(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("announcement", out var value)
                ? value.Deserialize<TelegramFinancialStatementRecognition>(JsonOptions)
                : null;
        }
        catch (JsonException) { return null; }
    }

    private static string SerializeCategoryOutcomes(
        Dictionary<string, FinancialStatementCategoryOutcome> outcomes,
        TelegramFinancialStatementRecognition announcement)
    {
        var state = new Dictionary<string, object>(StringComparer.Ordinal) { ["announcement"] = announcement };
        foreach (var (category, outcome) in outcomes) state[category] = outcome;
        return JsonSerializer.Serialize(state, JsonOptions);
    }

    public async Task<TelegramFinancialStatementRecoveryResult> RecoverSkippedAsync(
        long telegramChatId, long telegramMessageId, CancellationToken cancellationToken)
    {
        if (telegramChatId == 0 || telegramMessageId <= 0)
            return new("InvalidTarget", "", "A non-zero chat ID and positive Telegram message ID are required.");
        if (!configured.Value.AutoBackfillEnabled)
            return new("FeatureDisabled", "", "Feature 143 automation is disabled; the checkpoint was not changed.");
        if (channelAuthorization.Value.AllowedChannelIds.Length == 0 || !channelAuthorization.Value.AllowedChannelIds.Contains(
                telegramChatId.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal))
            return new("ChannelNotAllowed", "", "The target channel is not in the configured Feature 133 channel allowlist.");

        var key = $"channel-financial-statement:{telegramChatId}:{telegramMessageId}";
        var checkpoint = await auth.TelegramProcessedUpdates.AsNoTracking()
            .SingleOrDefaultAsync(x => x.IdempotencyKey == key, cancellationToken);
        if (checkpoint is null)
            return new("CheckpointNotFound", "", "No Feature 143 checkpoint exists for the supplied chat and message IDs.");
        if (checkpoint.Status is not ("Feature143:Disabled" or "Feature143:Unresolved"))
            return new("CheckpointNotEligible", checkpoint.Status,
                "Only explicitly skipped Disabled or Unresolved Feature 143 checkpoints can be recovered. Claimed, in-flight, partial, review, and completed checkpoints are not reset.");

        TelegramFinancialStatementRecognition? announcement;
        string? outcome;
        try
        {
            using var document = JsonDocument.Parse(checkpoint.ResponseJson);
            var root = document.RootElement;
            outcome = root.TryGetProperty("outcome", out var outcomeNode) && outcomeNode.ValueKind == JsonValueKind.String
                ? outcomeNode.GetString() : null;
            announcement = root.TryGetProperty("announcement", out var announcementNode)
                ? announcementNode.Deserialize<TelegramFinancialStatementRecognition>(JsonOptions) : null;
        }
        catch (JsonException)
        {
            return new("StoredMetadataMissing", checkpoint.Status,
                "The checkpoint does not contain valid structured announcement metadata; a Telegram redelivery or operator-supplied source post is required.");
        }

        if (announcement is null || outcome is not ("Disabled" or "FiscalYearUnresolved" or "CompanyUnresolved"))
            return new("StoredMetadataMissing", checkpoint.Status,
                "The checkpoint does not contain an eligible stored announcement. No checkpoint state was changed.");
        if (checkpoint.TenantId is not Guid tenantId)
            return new("StoredMetadataMissing", checkpoint.Status,
                "The checkpoint has no tenant identity; no checkpoint state was changed.");

        var acquired = await auth.TelegramProcessedUpdates
            .Where(x => x.Id == checkpoint.Id && x.Status == checkpoint.Status && x.ResponseJson == checkpoint.ResponseJson)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, "Feature143:RecoveryClaimed"), cancellationToken);
        if (acquired != 1)
            return new("ConcurrentChange", checkpoint.Status, "The checkpoint changed concurrently; no recovery was started.");

        var update = new TelegramAssistantUpdate(
            checkpoint.TelegramUpdateId, TelegramAssistantUpdateKind.ChannelPost, 0,
            telegramChatId, null, telegramMessageId, null, null, null, "fa-IR",
            clock.GetUtcNow(), $"feature143-recovery-{checkpoint.Id:N}", "channel");
        var recoveryActor = new CurrentActor(ActorType.ApiClient, Guid.Empty, tenantId, AuthenticationMode.ApiClient);
        await HandleRecognizedAsync(update, recoveryActor, announcement, allowRecoveryClaim: true, cancellationToken);

        var final = await auth.TelegramProcessedUpdates.AsNoTracking()
            .Where(x => x.Id == checkpoint.Id).Select(x => x.Status).SingleAsync(cancellationToken);
        return new("Recovered", final, $"Targeted recovery finished with checkpoint status {final}.");
    }

    private static Dictionary<string, FinancialStatementCategoryOutcome> ReadCategoryOutcomes(string json)
    {
        var outcomes = new Dictionary<string, FinancialStatementCategoryOutcome>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return outcomes;
            foreach (var category in Enum.GetNames<ManualNoavaranFinancialStatementType>())
            {
                if (!document.RootElement.TryGetProperty(category, out var item)) continue;
                var outcome = item.Deserialize<FinancialStatementCategoryOutcome>(JsonOptions);
                if (outcome is not null) outcomes[category] = outcome;
            }
        }
        catch (JsonException) { }
        return outcomes;
    }

    private async Task<(bool Matches, string? Reason, bool ReconciliationNeeded)> ValidatePersistedPeriodAsync(
        string externalCompanyId,
        DateOnly periodEnd,
        string periodType,
        string fiscalYearEnd,
        ManualNoavaranFinancialStatementType category,
        TelegramFinancialStatementRecognition announcement,
        Guid? ingestionRunId,
        CancellationToken cancellationToken)
    {
        var expectedDataset = category == ManualNoavaranFinancialStatementType.FinancialRatios
            ? "FundamentalIndexes"
            : "FinancialStatements";
        var ingestionRun = ingestionRunId is null
            ? null
            : await financial.SyncRuns.AsNoTracking()
                .Where(x => x.Id == ingestionRunId.Value &&
                    x.ProviderName == ProviderSources.NoavaranCurrentApiName &&
                    x.ExternalReference == externalCompanyId &&
                    x.Dataset == expectedDataset &&
                    x.Status == "Completed" &&
                    x.ProcessedRecords > 0)
                .Select(x => new { x.SourcePayloadChecksum })
                .SingleOrDefaultAsync(cancellationToken);
        if (ingestionRun is null || string.IsNullOrWhiteSpace(ingestionRun.SourcePayloadChecksum))
            return (false, "The completed Noavaran ingestion run for this company and category could not be verified.", false);

        if (category == ManualNoavaranFinancialStatementType.FinancialRatios)
        {
            var evidence = await financial.DerivedMetrics.AsNoTracking()
                .Where(x => x.ExternalCompanyId == externalCompanyId && x.PeriodEnd == periodEnd && x.PeriodType == periodType)
                .Select(x => x.SourceEvidenceJson).ToListAsync(cancellationToken);
            var matched = evidence.Any(x => EvidenceMatchesVariant(x, announcement, fiscalYearEnd));
            return matched ? (true, null, false) : (false, "No persisted fundamental-index report matches the exact company, period, and explicit variants.", false);
        }

        var statementType = category switch
        {
            ManualNoavaranFinancialStatementType.BalanceSheet => "BalanceSheet",
            ManualNoavaranFinancialStatementType.IncomeStatement => "IncomeStatement",
            ManualNoavaranFinancialStatementType.CashFlow => "CashFlow",
            _ => string.Empty
        };
        var rows = await financial.FinancialStatements.AsNoTracking()
            .Where(x => x.ProviderName == ProviderSources.NoavaranCurrentApiName &&
                x.SourcePayloadChecksum == ingestionRun.SourcePayloadChecksum &&
                x.ExternalCompanyId == externalCompanyId &&
                x.StatementType == statementType && x.PeriodEnd == periodEnd && x.PeriodType == periodType)
            .Select(x => new { x.Id, x.ExternalStatementId, x.PeriodStart, x.IsAudited, x.IsRepresented, x.IsComposing, x.WarningsJson })
            .ToListAsync(cancellationToken);
        var matchingRows = rows.Where(x =>
            !string.IsNullOrWhiteSpace(x.ExternalStatementId) &&
            (announcement.IsAudited is null || x.IsAudited == announcement.IsAudited.Value) &&
            (announcement.IsConsolidated is null || x.IsComposing == announcement.IsConsolidated.Value) &&
            NoavaranFiscalYearEvidenceResolver.ContainsFiscalYearEnd(x.WarningsJson, fiscalYearEnd)).ToArray();
        if (matchingRows.Length == 0)
            return (false, "No persisted statement matches the exact company, period, and explicit variants.", false);

        // This is a conservative reconciliation signal, not a financial validity rule. A
        // six-month response that omits approved items present in this company's matching
        // three-month report remains persisted, then gets at most two delayed re-fetches.
        if (category == ManualNoavaranFinancialStatementType.IncomeStatement && periodType == "SixMonths")
        {
            var approved = QuarterlyFinancialReportsNormalizer.ApprovedMappings;
            var approvedCatalogIds = approved.Select(x => Guid.Parse(x.CatalogId)).ToArray();
            var currentIds = matchingRows.Select(x => x.Id).ToArray();
            var starts = matchingRows.Select(x => x.PeriodStart).Distinct().ToArray();
            var scopes = matchingRows.Select(x => x.IsComposing).Distinct().ToArray();
            var auditFlags = matchingRows.Select(x => x.IsAudited).Distinct().ToArray();
            var restatementFlags = matchingRows.Select(x => x.IsRepresented).Distinct().ToArray();
            var priorIds = await financial.FinancialStatements.AsNoTracking()
                .Where(x => x.ProviderName == ProviderSources.NoavaranCurrentApiName &&
                    x.ExternalCompanyId == externalCompanyId && x.StatementType == statementType &&
                    x.PeriodType == "ThreeMonths" && x.PeriodEnd < periodEnd &&
                    starts.Contains(x.PeriodStart) && scopes.Contains(x.IsComposing) &&
                    auditFlags.Contains(x.IsAudited) && restatementFlags.Contains(x.IsRepresented))
                .Select(x => x.Id).ToArrayAsync(cancellationToken);
            if (priorIds.Length > 0)
            {
                var priorCatalogIds = await financial.FinancialStatementLineItems.AsNoTracking()
                    .Where(x => priorIds.Contains(x.FinancialStatementId) &&
                        x.SourceItemCatalogId.HasValue && approvedCatalogIds.Contains(x.SourceItemCatalogId.Value))
                    .Select(x => x.SourceItemCatalogId!.Value).Distinct().ToArrayAsync(cancellationToken);
                var currentCatalogIds = await financial.FinancialStatementLineItems.AsNoTracking()
                    .Where(x => currentIds.Contains(x.FinancialStatementId) &&
                        x.SourceItemCatalogId.HasValue && approvedCatalogIds.Contains(x.SourceItemCatalogId.Value))
                    .Select(x => x.SourceItemCatalogId!.Value).Distinct().ToArrayAsync(cancellationToken);
                var missing = priorCatalogIds.Except(currentCatalogIds).ToHashSet();
                if (missing.Count > 0)
                {
                    var missingSourceIds = approved.Where(x => missing.Contains(Guid.Parse(x.CatalogId)))
                        .Select(x => x.SourceItemId).OrderBy(x => x).ToArray();
                    return (true, $"Six-month source response lacks previously observed approved item IDs: {string.Join(",", missingSourceIds)}.", true);
                }
            }
        }
        return (true, null, false);
    }

    private static bool EvidenceMatchesVariant(string json, TelegramFinancialStatementRecognition announcement, string fiscalYearEnd)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var rows = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().ToArray() : [document.RootElement];
            return rows.Any(row =>
                (row.TryGetProperty("jalaliFiscalYearEnd", out var yearEnd) && yearEnd.ValueKind == JsonValueKind.String && CanonicalJalaliDate(yearEnd.GetString()) == fiscalYearEnd) &&
                (announcement.IsAudited is null || (row.TryGetProperty("isAudited", out var audited) &&
                    audited.ValueKind is (JsonValueKind.True or JsonValueKind.False) && audited.GetBoolean() == announcement.IsAudited.Value)) &&
                (announcement.IsConsolidated is null || (row.TryGetProperty("isComposing", out var composing) &&
                    composing.ValueKind is (JsonValueKind.True or JsonValueKind.False) && composing.GetBoolean() == announcement.IsConsolidated.Value)));
        }
        catch (JsonException) { return false; }
    }

    private static string? CanonicalJalaliDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length == 8 ? $"{digits[..4]}/{digits[4..6]}/{digits[6..8]}" : null;
    }

    private void Log(TelegramAssistantUpdate update, TelegramFinancialStatementRecognition item, string? companyId,
        int? fiscalYear, string outcome, string? reason, int records = 0) =>
        logger.LogInformation("Feature 143 outcome={Outcome}, ChatId={ChatId}, MessageId={MessageId}, Ticker={Ticker}, CompanyId={CompanyId}, Category={Category}, PeriodEnd={PeriodEnd}, FiscalYearEnding={FiscalYearEnding}, PeriodMonths={PeriodMonths}, IsAudited={IsAudited}, IsAmendment={IsAmendment}, IsConsolidated={IsConsolidated}, Records={Records}, FailureReason={FailureReason}, CorrelationId={CorrelationId}",
            outcome, update.TelegramChatId, update.TelegramMessageId, item.Symbol, companyId, item.Category,
            $"{item.PeriodYear:D4}/{item.PeriodMonth:D2}/{item.PeriodDay:D2}", fiscalYear, item.PeriodMonths,
            item.IsAudited, item.IsAmendment, item.IsConsolidated, records, reason, update.CorrelationId);

    private static TelegramAssistantResult Empty(TelegramAssistantResultStatus status, TelegramAssistantUpdate update) =>
        new(status, null, null, null, [], update.CorrelationId);

    private sealed record FinancialStatementCategoryOutcome(string Outcome, int ProcessedRecords, string? Message, int Attempts);
}
