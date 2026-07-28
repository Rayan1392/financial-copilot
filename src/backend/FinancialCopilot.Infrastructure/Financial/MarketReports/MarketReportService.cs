using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.FinancialData.MarketReports;
using FinancialCopilot.Application.Notifications;
using FinancialCopilot.Billing;
using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Pricing;
using FinancialCopilot.Domain.Financial.Insights;
using FinancialCopilot.Domain.Financial.Reports;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.MarketReports;

internal sealed class MarketReportService(
    FinancialIngestionDbContext dbContext,
    MarketReportEvidenceAssembler evidenceAssembler,
    MarketReportNarrativePolicy narrativePolicy,
    IAiModelExecutionService aiModels,
    IBillableAccountResolver accountResolver,
    IWalletService wallets,
    IEntitlementService entitlements,
    ICreditReservationService reservations,
    IUsageChargeCalculator chargeCalculator,
    IUsageFinalizationService finalization,
    INotificationIntentPublisher notifications,
    IOptions<MarketReportOptions> options,
    TimeProvider timeProvider,
    ILogger<MarketReportService> logger) : IMarketReportService, IMarketReportScheduler
{
    public const string PersonalDigestOperationCode = "AiQuery.PersonalDigest";
    public const string ReportVersion = "market-report-v1";
    public const string Disclaimer = "Informational, evidence-based market narrative; not financial advice.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly ActivitySource Activities = new("FinancialCopilot.MarketReports");
    private static readonly Meter Meter = new("FinancialCopilot.MarketReports");
    private static readonly Counter<long> Generations = Meter.CreateCounter<long>("market_report.generations");
    private static readonly Counter<long> Fallbacks = Meter.CreateCounter<long>("market_report.fallbacks");
    private static readonly Counter<long> Rejections = Meter.CreateCounter<long>("market_report.unsupported_claim_rejections");
    private static readonly Counter<long> NotificationHandoffs = Meter.CreateCounter<long>("market_report.notification_handoffs");
    private static readonly Histogram<double> GenerationDuration = Meter.CreateHistogram<double>("market_report.generation.duration", "ms");
    private static readonly Histogram<double> EvidenceAge = Meter.CreateHistogram<double>("market_report.evidence.age", "s");
    private readonly MarketReportOptions _options = options.Value;

    public async Task<MarketReportView?> GetLatestPublicAsync(CancellationToken cancellationToken)
    {
        var row = await dbContext.MarketReports.AsNoTracking()
            .Where(item => item.TenantId == null && item.ActorId == null && item.IsCurrent &&
                           (item.Status == nameof(MarketReportStatus.Generated) || item.Status == nameof(MarketReportStatus.Fallback)))
            .OrderByDescending(item => item.TradingDate)
            .ThenByDescending(item => item.PublishedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : Map(row);
    }

    public Task<MarketReportHistoryPage> GetPublicHistoryAsync(
        MarketReportHistoryQuery query,
        CancellationToken cancellationToken) =>
        QueryHistoryAsync(null, query, cancellationToken);

    public async Task<MarketReportView?> GetPublicVersionAsync(Guid reportId, CancellationToken cancellationToken)
    {
        var row = await dbContext.MarketReports.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == reportId && item.TenantId == null && item.ActorId == null,
                cancellationToken);
        return row is null ? null : Map(row);
    }

    public async Task<MarketReportView> GeneratePublicAsync(
        GeneratePublicMarketReportCommand command,
        CancellationToken cancellationToken)
    {
        var evidence = await evidenceAssembler.BuildPublicAsync(command.Segment, cancellationToken);
        var scope = evidence.IsFinal ? MarketReportScope.PublicMarket : MarketReportScope.IntradayMarket;
        return await GenerateAsync(scope, null, evidence, command.CorrelationId, false, cancellationToken);
    }

    public async Task<MarketReportView?> GetLatestPersonalAsync(CurrentActor actor, CancellationToken cancellationToken)
    {
        var actorType = actor.ActorType.ToString();
        var row = await dbContext.MarketReports.AsNoTracking()
            .Where(item => item.Scope == nameof(MarketReportScope.PersonalDigest) &&
                           item.TenantId == actor.TenantId && item.ActorId == actor.ActorId &&
                           item.ActorType == actorType && item.IsCurrent &&
                           (item.Status == nameof(MarketReportStatus.Generated) || item.Status == nameof(MarketReportStatus.Fallback)))
            .OrderByDescending(item => item.TradingDate)
            .ThenByDescending(item => item.PublishedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : Map(row);
    }

    public Task<MarketReportHistoryPage> GetPersonalHistoryAsync(
        CurrentActor actor,
        MarketReportHistoryQuery query,
        CancellationToken cancellationToken) =>
        QueryHistoryAsync(actor, query, cancellationToken);

    public async Task<MarketReportView?> GetPersonalVersionAsync(
        CurrentActor actor,
        Guid reportId,
        CancellationToken cancellationToken)
    {
        var actorType = actor.ActorType.ToString();
        var row = await dbContext.MarketReports.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == reportId &&
                                          item.Scope == nameof(MarketReportScope.PersonalDigest) &&
                                          item.TenantId == actor.TenantId && item.ActorId == actor.ActorId &&
                                          item.ActorType == actorType,
                cancellationToken);
        return row is null ? null : Map(row);
    }

    public async Task<MarketReportView> GeneratePersonalAsync(
        GeneratePersonalDigestCommand command,
        CancellationToken cancellationToken)
    {
        var account = await ResolveAndAuthorizeAsync(command.Actor, cancellationToken);
        var actorType = command.Actor.ActorType.ToString();
        var evidence = await evidenceAssembler.BuildPersonalAsync(
            command.Actor.TenantId, command.Actor.ActorId, actorType, cancellationToken);
        var evidenceHash = HashEvidence(evidence);
        var generationKey = GenerationKey(MarketReportScope.PersonalDigest, command.Actor, evidence, evidenceHash);
        var existing = await dbContext.MarketReports.AsNoTracking()
            .SingleOrDefaultAsync(row => row.GenerationIdempotencyKey == generationKey, cancellationToken);
        if (existing is not null && existing.Status is nameof(MarketReportStatus.Generated) or nameof(MarketReportStatus.Fallback))
            return Map(existing);

        var generatedToday = await dbContext.MarketReports.AsNoTracking().CountAsync(row =>
            row.Scope == nameof(MarketReportScope.PersonalDigest) &&
            row.TenantId == command.Actor.TenantId && row.ActorId == command.Actor.ActorId && row.ActorType == actorType &&
            row.TradingDate == evidence.TradingDate &&
            row.Status != nameof(MarketReportStatus.Failed), cancellationToken);
        if (generatedToday >= Math.Max(1, _options.PersonalDailyGenerationLimit))
            throw new MarketReportAccessDeniedException("The daily personal-digest generation limit is exhausted.");

        return await GenerateAsync(
            MarketReportScope.PersonalDigest,
            new PersonalContext(command.Actor, account),
            evidence,
            command.CorrelationId,
            command.PublishNotification,
            cancellationToken);
    }

    public async Task<int> GenerateDueAsync(CancellationToken cancellationToken)
    {
        var generated = 0;
        foreach (var segment in _options.Segments.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var latest = await dbContext.MarketPulseSnapshots.AsNoTracking()
                    .Where(row => row.IsCurrent && row.Segment == segment)
                    .OrderByDescending(row => row.GeneratedAtUtc)
                    .Select(row => new { row.Id })
                    .FirstOrDefaultAsync(cancellationToken);
                if (latest is null) continue;
                await GeneratePublicAsync(
                    new GeneratePublicMarketReportCommand(segment, $"market-report-scheduled:{latest.Id:N}"),
                    cancellationToken);
                generated++;
            }
            catch (MarketReportValidationException exception)
            {
                logger.LogDebug(exception, "Market report schedule skipped segment {Segment} because evidence is not ready.", segment);
            }
        }
        return generated;
    }

    private async Task<MarketReportView> GenerateAsync(
        MarketReportScope scope,
        PersonalContext? personal,
        MarketReportEvidenceBundle evidence,
        string correlationId,
        bool publishNotification,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        using var activity = Activities.StartActivity("market-report.generate");
        activity?.SetTag("market.report.scope", scope.ToString());
        activity?.SetTag("market.report.trading_date", evidence.TradingDate.ToString("yyyy-MM-dd"));
        activity?.SetTag("market.report.evidence_items", evidence.Items.Count);

        var evidenceHash = HashEvidence(evidence);
        var actor = personal?.Actor;
        var generationKey = GenerationKey(scope, actor, evidence, evidenceHash);
        var claim = await ClaimAsync(scope, actor, evidence, evidenceHash, generationKey, correlationId, cancellationToken);
        if (!claim.Acquired || claim.Aggregate is null) return Map(claim.Row);

        var row = claim.Row;
        var aggregate = claim.Aggregate;
        string? reservationKey = null;
        try
        {
            if (personal is not null)
            {
                var wallet = await wallets.GetSnapshotAsync(personal.Account.Id, cancellationToken);
                reservationKey = $"market-digest:{generationKey}";
                var reservation = await reservations.ReserveAsync(
                    personal.Account, wallet, PersonalDigestOperationCode, 4m, reservationKey, cancellationToken);
                row.ReservationIdempotencyKey = reservation.IdempotencyKey;
                reservationKey = reservation.IdempotencyKey;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            var fallbackReason = string.Empty;
            AiModelResult? result = null;
            try
            {
                result = await aiModels.ExecuteAsync(
                    new AiModelSelectionRequest(
                        actor?.TenantId ?? Guid.Empty,
                        AiWorkloadKind.Summarization,
                        AiWorkloadCapabilities.RequiredFor(AiWorkloadKind.Summarization),
                        correlationId),
                    new AiModelRequest(
                        correlationId,
                        actor?.TenantId ?? Guid.Empty,
                        AiWorkloadKind.Summarization,
                        [
                            new AiConversationMessage(AiMessageRole.System, narrativePolicy.BuildSystemPrompt(scope)),
                            new AiConversationMessage(AiMessageRole.User, JsonSerializer.Serialize(evidence, JsonOptions))
                        ]),
                    cancellationToken);
                if (!narrativePolicy.TryValidate(result.Text, evidence, out fallbackReason))
                {
                    Rejections.Add(1, new KeyValuePair<string, object?>("scope", scope.ToString()));
                    result = null;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                fallbackReason = $"AI provider failure: {exception.Message}";
            }

            var now = timeProvider.GetUtcNow();
            if (result is null)
            {
                fallbackReason = string.IsNullOrWhiteSpace(fallbackReason)
                    ? "AI output did not satisfy the evidence policy."
                    : fallbackReason;
                if (personal is not null && reservationKey is not null)
                    await finalization.ReleaseAsync(
                        new UsageReleaseCommand(personal.Account.Id, personal.Actor.TenantId, reservationKey,
                            "Personal digest used deterministic fallback; AI generation was not charged."),
                        cancellationToken);
                aggregate.PublishFallback(narrativePolicy.BuildFallback(scope, evidence), fallbackReason, now);
                Fallbacks.Add(1, new KeyValuePair<string, object?>("scope", scope.ToString()));
            }
            else
            {
                if (personal is not null && reservationKey is not null)
                {
                    var charge = chargeCalculator.Calculate(new UsageChargeRequest(
                        PersonalDigestOperationCode, "v1", false, "Completed", [], []));
                    await finalization.CommitAsync(
                        new UsageCommitCommand(
                            personal.Account.Id,
                            personal.Actor.ActorId,
                            personal.Actor.TenantId,
                            personal.Actor.ApiClientId,
                            null,
                            reservationKey,
                            reservationKey + ":commit",
                            charge,
                            ProviderName: result.Usage.ProviderKey,
                            ModelName: result.Usage.ModelKey,
                            PromptTokens: result.Usage.InputTokens,
                            CompletionTokens: result.Usage.OutputTokens,
                            TotalTokens: result.Usage.InputTokens is null && result.Usage.OutputTokens is null
                                ? null
                                : (result.Usage.InputTokens ?? 0) + (result.Usage.OutputTokens ?? 0)),
                        cancellationToken);
                }
                aggregate.PublishGenerated(result.Text!, now);
                row.ProviderName = result.Usage.ProviderKey;
                row.ModelName = result.Usage.ModelKey;
                row.ModelMetadataJson = JsonSerializer.Serialize(new
                {
                    result.Usage.Status,
                    result.Usage.AttemptNumber,
                    result.Usage.InputTokens,
                    result.Usage.OutputTokens,
                    result.Usage.Duration
                }, JsonOptions);
            }

            await PublishAsync(row, aggregate, cancellationToken);
            if (personal is not null && publishNotification)
            {
                await notifications.EnqueueAsync(
                    new NotificationIntentRequest(
                        new NotificationActor(personal.Actor.TenantId, personal.Actor.ActorId, personal.Actor.ActorType.ToString()),
                        NotificationChannel.Telegram,
                        "PersonalMarketDigestReady",
                        row.Id.ToString(),
                        $"personal-market-digest-ready:v1:{personal.Actor.TenantId}:{personal.Actor.ActorId}:{row.Id}",
                        InsightSeverity.Notice,
                        JsonSerializer.Serialize(new { reportId = row.Id, row.EvidenceHash, row.TradingDate, row.Revision }, JsonOptions),
                        timeProvider.GetUtcNow(),
                        timeProvider.GetUtcNow().AddDays(2),
                        correlationId,
                        SourceEventId: row.Id,
                        EvidenceReference: row.EvidenceHash,
                        Category: "MarketReport",
                        CooldownKey: $"PersonalMarketDigest:{row.TradingDate:yyyy-MM-dd}"),
                    cancellationToken);
                NotificationHandoffs.Add(1);
            }

            Generations.Add(1, new KeyValuePair<string, object?>("scope", scope.ToString()),
                new KeyValuePair<string, object?>("status", row.Status));
            if (evidence.SourceFreshnessUtc.HasValue)
                EvidenceAge.Record(Math.Max(0, (timeProvider.GetUtcNow() - evidence.SourceFreshnessUtc.Value).TotalSeconds));
            return Map(row);
        }
        catch (InsufficientCreditException exception)
        {
            await MarkFailedAsync(row, exception.Message, cancellationToken);
            throw new MarketReportAccessDeniedException(exception.Message);
        }
        catch (OperationCanceledException)
        {
            if (personal is not null && reservationKey is not null)
                await finalization.ReleaseAsync(
                    new UsageReleaseCommand(personal.Account.Id, personal.Actor.TenantId, reservationKey,
                        "Personal digest generation was cancelled."), CancellationToken.None);
            await ReleaseClaimForRetryAsync(row, "Generation was cancelled.", CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            if (personal is not null && reservationKey is not null)
            {
                try
                {
                    await finalization.ReleaseAsync(
                        new UsageReleaseCommand(personal.Account.Id, personal.Actor.TenantId, reservationKey,
                            "Personal digest generation failed."), CancellationToken.None);
                }
                catch (Exception releaseException)
                {
                    logger.LogError(releaseException, "Failed to release market-digest reservation {ReservationKey}.", reservationKey);
                }
            }
            await ReleaseClaimForRetryAsync(row, exception.Message, CancellationToken.None);
            throw;
        }
        finally
        {
            GenerationDuration.Record(started.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("scope", scope.ToString()));
        }
    }

    private async Task<CustomerAccount> ResolveAndAuthorizeAsync(CurrentActor actor, CancellationToken cancellationToken)
    {
        var account = await accountResolver.ResolveAsync(
            new BillableActorContext(actor.ActorId, actor.TenantId, actor.UserId, actor.ApiClientId, null),
            cancellationToken);
        try
        {
            await entitlements.ValidateCanExecuteAsync(account, PersonalDigestOperationCode, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw new MarketReportAccessDeniedException(exception.Message);
        }
        return account;
    }

    private async Task<ClaimResult> ClaimAsync(
        MarketReportScope scope,
        CurrentActor? actor,
        MarketReportEvidenceBundle evidence,
        string evidenceHash,
        string generationKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var existing = await dbContext.MarketReports
            .SingleOrDefaultAsync(row => row.GenerationIdempotencyKey == generationKey, cancellationToken);
        if (existing is not null)
        {
            if (existing.Status is nameof(MarketReportStatus.Generated) or nameof(MarketReportStatus.Fallback) or nameof(MarketReportStatus.Superseded) ||
                existing.Status == nameof(MarketReportStatus.Failed) && existing.AttemptCount >= _options.MaximumAttempts ||
                existing.NextAttemptAtUtc > now || existing.LeaseExpiresAtUtc > now)
                return new ClaimResult(existing, null, false);

            existing.Status = nameof(MarketReportStatus.Pending);
            existing.AttemptCount++;
            existing.LeaseOwner = Truncate(correlationId, 160);
            existing.LeaseExpiresAtUtc = now.AddMinutes(Math.Max(1, _options.LeaseMinutes));
            existing.NextAttemptAtUtc = null;
            existing.FailureReason = null;
            await dbContext.SaveChangesAsync(cancellationToken);
            return new ClaimResult(existing, Resume(existing), true);
        }

        var scopeName = scope.ToString();
        var actorType = actor?.ActorType.ToString();
        var revision = actor is null
            ? (await dbContext.MarketReports.AsNoTracking()
                .Where(row => row.Scope == scopeName && row.TenantId == null && row.ActorId == null &&
                              row.TradingDate == evidence.TradingDate && row.WindowKey == evidence.WindowKey)
                .Select(row => (int?)row.Revision)
                .MaxAsync(cancellationToken) ?? 0) + 1
            : (await dbContext.MarketReports.AsNoTracking()
                .Where(row => row.Scope == scopeName && row.TenantId == actor.TenantId && row.ActorId == actor.ActorId &&
                              row.ActorType == actorType && row.TradingDate == evidence.TradingDate && row.WindowKey == evidence.WindowKey)
                .Select(row => (int?)row.Revision)
                .MaxAsync(cancellationToken) ?? 0) + 1;

        var aggregate = MarketReport.Start(
            scope, actor?.TenantId, actor?.ActorId, actorType, evidence.TradingDate, evidence.WindowKey,
            revision, ReportVersion, evidenceHash, generationKey, now);
        var row = new MarketReportRow
        {
            Id = aggregate.Id,
            Scope = scopeName,
            TenantId = actor?.TenantId,
            ActorId = actor?.ActorId,
            ActorType = actorType,
            TradingDate = evidence.TradingDate,
            WindowKey = evidence.WindowKey,
            Status = aggregate.Status.ToString(),
            IsCurrent = false,
            Revision = revision,
            ReportVersion = ReportVersion,
            EvidenceSchemaVersion = evidence.SchemaVersion,
            PromptPolicyVersion = MarketReportNarrativePolicy.PromptPolicyVersion,
            RenderingPolicyVersion = MarketReportNarrativePolicy.RenderingPolicyVersion,
            SafetyPolicyVersion = MarketReportNarrativePolicy.SafetyPolicyVersion,
            EvidenceHash = evidenceHash,
            EvidenceJson = JsonSerializer.Serialize(evidence, JsonOptions),
            SnapshotIdsJson = JsonSerializer.Serialize(evidence.SnapshotIds, JsonOptions),
            InsightEventIdsJson = JsonSerializer.Serialize(evidence.InsightEventIds, JsonOptions),
            CaveatsJson = JsonSerializer.Serialize(evidence.Caveats, JsonOptions),
            Confidence = evidence.Confidence,
            GenerationIdempotencyKey = generationKey,
            AttemptCount = 1,
            LeaseOwner = Truncate(correlationId, 160),
            LeaseExpiresAtUtc = now.AddMinutes(Math.Max(1, _options.LeaseMinutes)),
            CreatedAtUtc = now
        };
        dbContext.MarketReports.Add(row);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new ClaimResult(row, aggregate, true);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(row).State = EntityState.Detached;
            var winner = await dbContext.MarketReports
                .SingleAsync(item => item.GenerationIdempotencyKey == generationKey, cancellationToken);
            return new ClaimResult(winner, null, false);
        }
    }

    private async Task PublishAsync(MarketReportRow row, MarketReport aggregate, CancellationToken cancellationToken)
    {
        var current = await dbContext.MarketReports.Where(item =>
                item.Id != row.Id && item.Scope == row.Scope && item.TenantId == row.TenantId &&
                item.ActorId == row.ActorId && item.ActorType == row.ActorType && item.TradingDate == row.TradingDate &&
                item.WindowKey == row.WindowKey && item.IsCurrent)
            .ToArrayAsync(cancellationToken);
        foreach (var previous in current)
        {
            previous.IsCurrent = false;
            previous.Status = nameof(MarketReportStatus.Superseded);
            row.SupersedesReportId ??= previous.Id;
        }

        row.Status = aggregate.Status.ToString();
        row.Narrative = aggregate.Narrative;
        row.FailureReason = aggregate.FailureReason;
        row.GeneratedAtUtc = aggregate.GeneratedAtUtc;
        row.PublishedAtUtc = aggregate.PublishedAtUtc;
        row.IsCurrent = true;
        row.LeaseOwner = null;
        row.LeaseExpiresAtUtc = null;
        row.NextAttemptAtUtc = null;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkFailedAsync(MarketReportRow row, string reason, CancellationToken cancellationToken)
    {
        row.Status = nameof(MarketReportStatus.Failed);
        row.FailureReason = Truncate(reason, 2000);
        row.GeneratedAtUtc = timeProvider.GetUtcNow();
        row.LeaseOwner = null;
        row.LeaseExpiresAtUtc = null;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ReleaseClaimForRetryAsync(MarketReportRow row, string reason, CancellationToken cancellationToken)
    {
        row.FailureReason = Truncate(reason, 2000);
        row.LeaseOwner = null;
        row.LeaseExpiresAtUtc = null;
        if (row.AttemptCount >= Math.Max(1, _options.MaximumAttempts))
        {
            row.Status = nameof(MarketReportStatus.Failed);
            row.GeneratedAtUtc = timeProvider.GetUtcNow();
        }
        else
        {
            row.Status = nameof(MarketReportStatus.Pending);
            row.NextAttemptAtUtc = timeProvider.GetUtcNow().AddSeconds(Math.Pow(2, row.AttemptCount));
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<MarketReportHistoryPage> QueryHistoryAsync(
        CurrentActor? actor,
        MarketReportHistoryQuery query,
        CancellationToken cancellationToken)
    {
        if (query.From.HasValue && query.To.HasValue && query.From > query.To)
            throw new MarketReportValidationException("The report history from date must not be after the to date.");
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 20 : query.PageSize, 1, 100);
        var rows = dbContext.MarketReports.AsNoTracking().AsQueryable();
        if (actor is null)
            rows = rows.Where(row => row.TenantId == null && row.ActorId == null);
        else
        {
            var actorType = actor.ActorType.ToString();
            rows = rows.Where(row => row.Scope == nameof(MarketReportScope.PersonalDigest) &&
                                     row.TenantId == actor.TenantId && row.ActorId == actor.ActorId &&
                                     row.ActorType == actorType);
        }
        if (query.From.HasValue) rows = rows.Where(row => row.TradingDate >= query.From.Value);
        if (query.To.HasValue) rows = rows.Where(row => row.TradingDate <= query.To.Value);
        if (query.Status.HasValue)
        {
            var status = query.Status.Value.ToString();
            rows = rows.Where(row => row.Status == status);
        }
        var total = await rows.CountAsync(cancellationToken);
        var pageRows = await rows.OrderByDescending(row => row.TradingDate)
            .ThenByDescending(row => row.Revision)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken);
        return new MarketReportHistoryPage(pageRows.Select(Map).ToArray(), page, pageSize, total);
    }

    private static MarketReport Resume(MarketReportRow row) => MarketReport.ResumePending(
        row.Id,
        Enum.Parse<MarketReportScope>(row.Scope),
        row.TenantId,
        row.ActorId,
        row.ActorType,
        row.TradingDate,
        row.WindowKey,
        row.Revision,
        row.ReportVersion,
        row.EvidenceHash,
        row.GenerationIdempotencyKey,
        row.CreatedAtUtc);

    private static MarketReportView Map(MarketReportRow row)
    {
        var evidence = JsonSerializer.Deserialize<MarketReportEvidenceBundle>(row.EvidenceJson, JsonOptions)
            ?? throw new InvalidOperationException($"Report {row.Id} has invalid evidence JSON.");
        var caveats = JsonSerializer.Deserialize<string[]>(row.CaveatsJson, JsonOptions) ?? [];
        return new MarketReportView(
            row.Id,
            Enum.Parse<MarketReportScope>(row.Scope),
            Enum.Parse<MarketReportStatus>(row.Status),
            row.TradingDate,
            row.WindowKey,
            row.Revision,
            row.SupersedesReportId,
            row.ReportVersion,
            row.EvidenceSchemaVersion,
            row.PromptPolicyVersion,
            row.RenderingPolicyVersion,
            row.SafetyPolicyVersion,
            row.EvidenceHash,
            evidence,
            row.Narrative,
            caveats,
            row.Confidence,
            row.ProviderName,
            row.ModelName,
            row.FailureReason,
            row.CreatedAtUtc,
            row.GeneratedAtUtc,
            row.PublishedAtUtc,
            row.IsCurrent,
            Disclaimer);
    }

    private static string HashEvidence(MarketReportEvidenceBundle evidence)
    {
        var canonical = evidence with { AssembledAtUtc = DateTimeOffset.UnixEpoch };
        var json = JsonSerializer.Serialize(canonical, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private static string GenerationKey(
        MarketReportScope scope,
        CurrentActor? actor,
        MarketReportEvidenceBundle evidence,
        string evidenceHash) =>
        $"market-report:{scope}:{actor?.TenantId.ToString("N") ?? "public"}:{actor?.ActorId.ToString("N") ?? "public"}:" +
        $"{evidence.TradingDate:yyyyMMdd}:{evidence.WindowKey}:{evidenceHash}:{ReportVersion}:{MarketReportNarrativePolicy.RenderingPolicyVersion}";

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    private sealed record PersonalContext(CurrentActor Actor, CustomerAccount Account);
    private sealed record ClaimResult(MarketReportRow Row, MarketReport? Aggregate, bool Acquired);
}
