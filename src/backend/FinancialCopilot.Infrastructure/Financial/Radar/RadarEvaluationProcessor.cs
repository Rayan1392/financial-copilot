using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.FollowedSymbols;
using FinancialCopilot.Application.FinancialData.Radar;
using FinancialCopilot.Application.Notifications;
using FinancialCopilot.Domain.Financial.Insights;
using FinancialCopilot.Domain.Financial.Radar;
using FinancialCopilot.Domain.Financial.FollowedSymbols;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Radar;

public sealed class RadarEvaluationProcessor(
    FinancialIngestionDbContext dbContext,
    IRadarRepository repository,
    IFollowedSymbolRepository followedSymbols,
    IRadarEntitlementPolicy entitlements,
    IRadarNotificationPolicyGate notificationGate,
    INotificationIntentPublisher notifications,
    IOptions<RadarOptions> options,
    TimeProvider timeProvider,
    ILogger<RadarEvaluationProcessor> logger) : IRadarEvaluationProcessor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Meter Meter = new("FinancialCopilot.Radar", "1.0.0");
    private static readonly Counter<long> MatchCounter = Meter.CreateCounter<long>("radar.matches");
    private static readonly Counter<long> SuppressionCounter = Meter.CreateCounter<long>("radar.suppressions");
    private static readonly Counter<long> HandoffCounter = Meter.CreateCounter<long>("radar.notification.handoffs");
    private static readonly Counter<long> CompositeCounter = Meter.CreateCounter<long>("radar.composites.formed");
    private static readonly Counter<long> FailureCounter = Meter.CreateCounter<long>("radar.failures");
    private static readonly Histogram<double> MatchLatency = Meter.CreateHistogram<double>("radar.match.latency", "ms");
    private readonly RadarOptions _options = options.Value;

    public async Task<RadarEvaluationBatchResult> EvaluateAsync(
        int maximumProfiles,
        CancellationToken cancellationToken)
    {
        var candidates = await repository.GetActiveAsync(Math.Clamp(maximumProfiles, 1, 1_000), cancellationToken);
        var total = new MutableBatch();
        foreach (var candidate in candidates)
        {
            var leaseOwner = $"radar-{Environment.MachineName}-{Guid.NewGuid():N}";
            if (!await TryAcquireLeaseAsync(candidate.Profile.Id, leaseOwner, cancellationToken)) continue;
            total.Profiles++;
            try
            {
                await EvaluateProfileAsync(candidate, total, cancellationToken);
                await CompleteLeaseAsync(candidate.Profile.Id, leaseOwner, null, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                total.Failed++;
                FailureCounter.Add(1, KeyValuePair.Create<string, object?>("stage", "profile"));
                logger.LogError(exception, "A radar profile evaluation failed; actor identifiers were redacted.");
                await CompleteLeaseAsync(candidate.Profile.Id, leaseOwner, exception.Message, cancellationToken);
            }
        }

        return new RadarEvaluationBatchResult(total.Profiles, total.Events, total.Matched, total.Suppressed,
            total.Intents, total.Composites, total.Failed);
    }

    private async Task EvaluateProfileAsync(
        RadarProfileSnapshot snapshot,
        MutableBatch total,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var actor = snapshot.Profile.Actor;
        var followed = await followedSymbols.GetAsync(
            new FollowedSymbolActor(actor.TenantId, actor.ActorId, actor.ActorType),
            cancellationToken);
        if (!await entitlements.CanEvaluateAsync(actor, followed.Count, cancellationToken))
        {
            total.Suppressed++;
            SuppressionCounter.Add(1, KeyValuePair.Create<string, object?>("reason", RadarSuppressionReason.EntitlementDenied.ToString()));
            return;
        }

        var companyIds = followed.Select(item => item.ExternalCompanyId).Distinct(StringComparer.Ordinal).ToArray();
        if (companyIds.Length == 0) return;
        var since = snapshot.LastEvaluatedAtUtc?.AddSeconds(-5) ?? now.AddHours(-Math.Clamp(_options.InitialLookbackHours, 1, 168));
        var rows = await dbContext.InsightEvents.AsNoTracking()
            .Where(row => companyIds.Contains(row.ExternalCompanyId) && row.DetectedAtUtc > since &&
                          (row.ExpiresAtUtc == null || row.ExpiresAtUtc > now))
            .OrderBy(row => row.DetectedAtUtc)
            .ThenBy(row => row.Id)
            .Take(Math.Clamp(_options.MaximumEventsPerProfile, 1, 2_000))
            .ToArrayAsync(cancellationToken);
        total.Events += rows.Length;
        if (rows.Length == 0) return;

        var insightIds = rows.Select(item => item.Id).ToArray();
        var versionPrefix = $"RADAR:{snapshot.Profile.Id:N}:V{snapshot.Profile.Version}:".ToUpperInvariant();
        var processedInsightIds = await dbContext.RadarEventMatches.AsNoTracking()
            .Where(row => row.RadarProfileId == snapshot.Profile.Id &&
                          insightIds.Contains(row.InsightEventId) &&
                          row.DeduplicationKey.StartsWith(versionPrefix))
            .Select(row => row.InsightEventId)
            .ToArrayAsync(cancellationToken);
        var processed = processedInsightIds.ToHashSet();
        var decisions = new List<EvaluatedEvent>();
        foreach (var row in rows)
        {
            var fact = ToFact(row);
            var key = BuildKey(snapshot.Profile, fact.InsightEventId, "component");
            if (processed.Contains(fact.InsightEventId)) continue;
            var symbolOverride = snapshot.SymbolOverrides.SingleOrDefault(item => item.ExternalCompanyId == fact.ExternalCompanyId);
            var history = await dbContext.InsightEvents.AsNoTracking()
                .Where(item => item.ExternalCompanyId == fact.ExternalCompanyId && item.InsightType == row.InsightType &&
                               item.DetectedAtUtc < fact.DetectedAtUtc && item.DetectedAtUtc >= fact.DetectedAtUtc.AddDays(-90))
                .OrderByDescending(item => item.DetectedAtUtc)
                .Take(100)
                .Select(item => item.ImportanceScore)
                .ToArrayAsync(cancellationToken);
            decisions.Add(new EvaluatedEvent(snapshot.Profile.Id, snapshot.Profile.Version, row, fact,
                RadarSelectionPolicy.Evaluate(snapshot.Profile, symbolOverride, fact, history, now), key));
        }

        MarkComposites(decisions);
        foreach (var decision in decisions)
            await PersistDecisionAsync(snapshot.Profile, decision, total, now, cancellationToken);

        var latestFreshness = decisions.Count == 0 ? rows.Max(row => row.DetectedAtUtc) : decisions.Max(item => item.Fact.SourceFreshnessUtc);
        var profileRow = await dbContext.RadarProfiles.SingleAsync(row => row.Id == snapshot.Profile.Id, cancellationToken);
        profileRow.LastEvaluatedAtUtc = rows.Max(row => row.DetectedAtUtc);
        profileRow.LastSourceFreshnessUtc = latestFreshness;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private void MarkComposites(List<EvaluatedEvent> decisions)
    {
        foreach (var group in decisions.Where(item => item.Evaluation.Decision == RadarMatchDecision.Matched)
                     .GroupBy(item => item.Fact.ExternalCompanyId, StringComparer.Ordinal))
        {
            var latest = group.Max(item => item.Fact.DetectedAtUtc);
            var components = group.Where(item => latest - item.Fact.DetectedAtUtc <= RadarSelectionPolicy.CompositeWindow)
                .GroupBy(item => item.Fact.InsightType).Select(item => item.OrderByDescending(x => x.Fact.DetectedAtUtc).First())
                .OrderBy(item => item.Fact.DetectedAtUtc).ToArray();
            if (components.Length < 2) continue;
            var primary = components[^1];
            var componentIds = components.Select(item => item.Fact.InsightEventId).Order().ToArray();
            primary.Evaluation = primary.Evaluation with
            {
                Decision = RadarMatchDecision.CompositeMatched,
                MatchScore = RadarSelectionPolicy.CompositeScore(components.Select(item => item.Fact).ToArray())
            };
            primary.ComponentIds = componentIds;
            CompositeCounter.Add(1, KeyValuePair.Create<string, object?>("sensitivity",
                primary.Evaluation.EffectiveSensitivity.ToString()));
            primary.DeduplicationKey = BuildKey(primary.ProfileVersion, primary.Fact.InsightEventId,
                "composite-" + HashIds(componentIds), primary.ProfileId);
            foreach (var component in components.Where(item => !ReferenceEquals(item, primary)))
                component.Evaluation = component.Evaluation with
                {
                    Decision = RadarMatchDecision.Suppressed,
                    SuppressionReason = RadarSuppressionReason.ComponentOfComposite
                };
        }
    }

    private async Task PersistDecisionAsync(
        RadarProfile profile,
        EvaluatedEvent decision,
        MutableBatch total,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var gate = decision.Evaluation.Decision is RadarMatchDecision.Matched or RadarMatchDecision.CompositeMatched
            ? await notificationGate.EvaluateAsync(profile.Actor, decision.Fact.Severity, profile.DeliveryMode, now, cancellationToken)
            : new RadarNotificationGateDecision(false, decision.Evaluation.SuppressionReason, now, "selection-v1");
        if (!gate.Allowed &&
            (decision.Evaluation.Decision is RadarMatchDecision.Matched or RadarMatchDecision.CompositeMatched))
            decision.Evaluation = decision.Evaluation with
            {
                Decision = RadarMatchDecision.Suppressed,
                SuppressionReason = gate.SuppressionReason
            };

        var row = new RadarEventMatchRow
        {
            Id = Guid.NewGuid(), RadarProfileId = profile.Id, TenantId = profile.Actor.TenantId,
            ActorId = profile.Actor.ActorId, ActorType = profile.Actor.ActorType,
            InsightEventId = decision.Fact.InsightEventId, ExternalCompanyId = decision.Fact.ExternalCompanyId,
            Decision = decision.Evaluation.Decision.ToString(), SuppressionReason = decision.Evaluation.SuppressionReason.ToString(),
            AppliedSensitivity = decision.Evaluation.EffectiveSensitivity.ToString(),
            AppliedPolicyVersion = decision.Evaluation.SensitivityPolicyVersion,
            NotificationPolicyVersion = gate.PolicyVersion,
            MatchScore = decision.Evaluation.MatchScore, HistoricalPercentile = decision.Evaluation.HistoricalPercentile,
            ComponentInsightEventIdsJson = JsonSerializer.Serialize(decision.ComponentIds, JsonOptions),
            EvidenceReference = decision.Fact.EvidenceIdentity, DeduplicationKey = decision.DeduplicationKey,
            SourceFreshnessUtc = decision.Fact.SourceFreshnessUtc, EvaluatedAtUtc = now
        };
        dbContext.RadarEventMatches.Add(row);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(row).State = EntityState.Detached;
            SuppressionCounter.Add(1, KeyValuePair.Create<string, object?>("reason", RadarSuppressionReason.AlreadyProcessed.ToString()));
            return;
        }

        if (decision.Evaluation.Decision == RadarMatchDecision.Suppressed)
        {
            total.Suppressed++;
            SuppressionCounter.Add(1, KeyValuePair.Create<string, object?>("reason", decision.Evaluation.SuppressionReason.ToString()));
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            informational = true,
            radarProfileId = profile.Id,
            insightEventId = decision.Fact.InsightEventId,
            componentInsightEventIds = decision.ComponentIds,
            evidenceReference = decision.Fact.EvidenceIdentity,
            sourceFreshnessUtc = decision.Fact.SourceFreshnessUtc,
            decision.Evaluation.EffectiveSensitivity,
            decision.Evaluation.EffectiveMinimumSeverity,
            decision.Evaluation.EffectiveMinimumImportance,
            decision.Evaluation.SensitivityPolicyVersion,
            decision.Evaluation.HistoricalPercentile,
            decision.Evaluation.MatchScore,
            deliveryMode = profile.DeliveryMode,
            notificationPolicyVersion = gate.PolicyVersion
        }, JsonOptions);
        var intent = await notifications.EnqueueAsync(new NotificationIntentRequest(
            new NotificationActor(profile.Actor.TenantId, profile.Actor.ActorId, profile.Actor.ActorType),
            NotificationChannel.Telegram,
            decision.Evaluation.Decision == RadarMatchDecision.CompositeMatched ? "RadarCompositeMatched" : "RadarEventMatched",
            decision.Fact.ExternalCompanyId, decision.DeduplicationKey,
            decision.Fact.Severity, payload, gate.NotBeforeUtc, now.AddDays(2), decision.Fact.InsightEventId.ToString("N"),
            SourceEventId: decision.Fact.InsightEventId,
            EvidenceReference: decision.Fact.EvidenceIdentity,
            Category: "PersonalRadar",
            CooldownKey: $"Radar:{decision.Fact.ExternalCompanyId}:{decision.Fact.InsightType}"),
            cancellationToken);
        row.NotificationIntentId = intent.Id;
        await dbContext.SaveChangesAsync(cancellationToken);
        total.Matched++;
        total.Intents++;
        if (decision.Evaluation.Decision == RadarMatchDecision.CompositeMatched) total.Composites++;
        MatchCounter.Add(1, new("sensitivity", decision.Evaluation.EffectiveSensitivity.ToString()),
            new("kind", decision.Evaluation.Decision.ToString()));
        HandoffCounter.Add(1);
        MatchLatency.Record(Math.Max(0, (now - decision.Fact.DetectedAtUtc).TotalMilliseconds));
    }

    private async Task<bool> TryAcquireLeaseAsync(
        Guid profileId,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (!dbContext.Database.IsRelational())
        {
            var row = await dbContext.RadarProfiles.SingleAsync(item => item.Id == profileId, cancellationToken);
            if (row.LeaseExpiresAtUtc is not null && row.LeaseExpiresAtUtc > now) return false;
            row.LeaseOwner = leaseOwner;
            row.LeaseExpiresAtUtc = now.AddSeconds(Math.Clamp(_options.LeaseSeconds, 30, 600));
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        var changed = await dbContext.RadarProfiles
            .Where(row => row.Id == profileId && (row.LeaseExpiresAtUtc == null || row.LeaseExpiresAtUtc <= now))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.LeaseOwner, leaseOwner)
                .SetProperty(row => row.LeaseExpiresAtUtc, now.AddSeconds(Math.Clamp(_options.LeaseSeconds, 30, 600))),
                cancellationToken);
        return changed == 1;
    }

    private async Task CompleteLeaseAsync(
        Guid profileId,
        string leaseOwner,
        string? failure,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.RadarProfiles.SingleOrDefaultAsync(
            item => item.Id == profileId && item.LeaseOwner == leaseOwner, cancellationToken);
        if (row is null)
        {
            logger.LogWarning("A radar lease expired or changed ownership before completion; actor identifiers were redacted.");
            return;
        }
        row.LeaseOwner = null;
        row.LeaseExpiresAtUtc = null;
        if (failure is null)
        {
            row.FailureCount = 0;
            row.NextAttemptAtUtc = null;
            row.LastFailure = null;
        }
        else
        {
            row.FailureCount++;
            var poison = row.FailureCount >= Math.Clamp(_options.RetryCount, 1, 10);
            var message = poison ? $"Poison threshold reached after {row.FailureCount} attempts. {failure}" : failure;
            row.LastFailure = message.Length <= 1000 ? message : message[..1000];
            row.NextAttemptAtUtc = timeProvider.GetUtcNow().AddSeconds(
                poison ? 3600 : Math.Min(300, 1 << Math.Min(8, row.FailureCount)));
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static RadarEventFact ToFact(InsightEventRow row)
    {
        var evidence = JsonSerializer.Deserialize<InsightEvidenceItem[]>(row.EvidenceJson, JsonOptions) ?? [];
        var freshness = evidence.Where(item => item.LastSyncedAtUtc.HasValue)
            .Select(item => item.LastSyncedAtUtc!.Value).DefaultIfEmpty(row.DetectedAtUtc).Max();
        return new RadarEventFact(row.Id, row.ExternalCompanyId, Enum.Parse<InsightType>(row.InsightType),
            Enum.Parse<InsightSeverity>(row.Severity), row.ImportanceScore, row.ConfidenceScore,
            row.DetectedAtUtc, freshness, $"InsightEvent:{row.Id:N}:{row.DeduplicationKey}");
    }

    private static string BuildKey(RadarProfile profile, Guid insightId, string kind) =>
        BuildKey(profile.Version, insightId, kind, profile.Id);

    private static string BuildKey(int profileVersion, Guid insightId, string kind, Guid? profileId = null) =>
        $"RADAR:{profileId?.ToString("N") ?? "PROFILE"}:V{profileVersion}:{insightId:N}:{kind}".ToUpperInvariant();

    private static string HashIds(IEnumerable<Guid> ids) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', ids.Select(id => id.ToString("N"))))))[..16];

    private sealed class EvaluatedEvent(
        Guid profileId,
        int profileVersion,
        InsightEventRow row,
        RadarEventFact fact,
        RadarMatchEvaluation evaluation,
        string key)
    {
        public Guid ProfileId { get; } = profileId;
        public InsightEventRow Row { get; } = row;
        public RadarEventFact Fact { get; } = fact;
        public int ProfileVersion { get; } = profileVersion;
        public RadarMatchEvaluation Evaluation { get; set; } = evaluation;
        public string DeduplicationKey { get; set; } = key;
        public IReadOnlyCollection<Guid> ComponentIds { get; set; } = [fact.InsightEventId];
    }

    private sealed class MutableBatch
    {
        public int Profiles;
        public int Events;
        public int Matched;
        public int Suppressed;
        public int Intents;
        public int Composites;
        public int Failed;
    }
}
