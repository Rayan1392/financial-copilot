using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.FinancialData.CodalAlerts;
using FinancialCopilot.Application.FinancialData.Insights;
using FinancialCopilot.Application.Notifications;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Pricing;
using FinancialCopilot.Domain.Financial.Insights;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.CodalAlerts;

public sealed class GenerateCodalAlertSummaryUseCase(
    FinancialIngestionDbContext dbContext,
    IInsightEventRepository insights,
    IBillableAccountResolver accountResolver,
    IWalletService walletService,
    IEntitlementService entitlementService,
    ICreditReservationService reservationService,
    IUsageChargeCalculator chargeCalculator,
    IUsageFinalizationService finalizationService,
    IAiModelProviderResolver modelResolver,
    INotificationIntentPublisher notificationPublisher,
    TimeProvider timeProvider) : IGenerateCodalAlertSummaryUseCase
{
    private const string OperationCode = "AiQuery.CodalAnalysis";
    private const string PromptPolicyVersion = "codal-alert-summary-v1";

    public async Task<CodalAlertSummaryDto> ExecuteAsync(
        GenerateCodalAlertSummaryCommand command,
        CancellationToken cancellationToken)
    {
        var actorType = command.Actor.ActorType.ToString();
        var existing = await dbContext.CodalAlertSummaries.SingleOrDefaultAsync(row =>
            row.TenantId == command.Actor.TenantId &&
            row.ActorId == command.Actor.ActorId &&
            row.ActorType == actorType &&
            row.InsightEventId == command.InsightEventId,
            cancellationToken);
        if (existing?.Status == "Completed")
        {
            return Map(existing);
        }

        var insight = await insights.FindAsync(command.InsightEventId, cancellationToken)
            ?? throw new CodalAlertSubscriptionValidationException("Codal alert insight was not found.");
        var evidenceHash = HashEvidence(insight);
        var row = existing ?? new CodalAlertSummaryRow
        {
            Id = Guid.NewGuid(),
            TenantId = command.Actor.TenantId,
            ActorId = command.Actor.ActorId,
            ActorType = actorType,
            InsightEventId = insight.Id,
            Status = "Pending",
            EvidenceHash = evidenceHash,
            PromptPolicyVersion = PromptPolicyVersion,
            CreatedAtUtc = timeProvider.GetUtcNow(),
            UpdatedAtUtc = timeProvider.GetUtcNow()
        };
        if (existing is null)
        {
            dbContext.CodalAlertSummaries.Add(row);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var billableActor = new BillableActorContext(
            command.Actor.ActorId,
            command.Actor.TenantId,
            command.Actor.UserId ?? (actorType == "User" ? command.Actor.ActorId : null),
            command.Actor.ApiClientId,
            null);
        var account = await accountResolver.ResolveAsync(billableActor, cancellationToken);
        await entitlementService.ValidateCanExecuteAsync(account, OperationCode, cancellationToken);
        var wallet = await walletService.GetSnapshotAsync(account.Id, cancellationToken);
        var reservationKey = $"codal-alert-summary:{command.Actor.TenantId}:{command.Actor.ActorId}:{insight.Id}";
        var reservation = await reservationService.ReserveAsync(
            account,
            wallet,
            OperationCode,
            8m,
            reservationKey,
            cancellationToken);
        row.ReservationIdempotencyKey = reservation.IdempotencyKey;
        row.Status = "Generating";
        row.UpdatedAtUtc = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var client = modelResolver.ResolveCandidates(new AiModelSelectionRequest(
                    command.Actor.TenantId,
                    AiWorkloadKind.Summarization,
                    AiWorkloadCapabilities.RequiredFor(AiWorkloadKind.Summarization),
                    command.CorrelationId))
                .FirstOrDefault(client => client.Descriptor.Enabled);
            if (client is null)
            {
                throw new InvalidOperationException("No AI model provider is configured for Codal alert summaries.");
            }

            var result = await client.CompleteAsync(
                new AiModelRequest(
                    command.CorrelationId,
                    command.Actor.TenantId,
                    AiWorkloadKind.Summarization,
                    [
                        new AiConversationMessage(AiMessageRole.System, "Summarize the Codal announcement evidence in Persian. Do not invent sentiment, causality, valuation, or recommendations. State unsupported facts as unavailable."),
                        new AiConversationMessage(AiMessageRole.User, BuildEvidenceBundle(insight))
                    ]),
                cancellationToken);

            var summary = string.IsNullOrWhiteSpace(result.Text)
                ? "خلاصه AI برای این اطلاعیه در دسترس نیست."
                : result.Text.Trim();
            row.Status = "Completed";
            row.SummaryText = summary;
            row.ProviderName = result.Usage.ProviderKey;
            row.ModelName = result.Usage.ModelKey;
            row.FailureReason = null;
            row.UpdatedAtUtc = timeProvider.GetUtcNow();
            await dbContext.SaveChangesAsync(cancellationToken);

            var charge = chargeCalculator.Calculate(new UsageChargeRequest(
                OperationCode,
                "v1",
                Cached: false,
                CompletionStatus: "Completed",
                UsageUnits: [],
                ProviderCosts: []));
            await finalizationService.CommitAsync(
                new UsageCommitCommand(
                    account.Id,
                    command.Actor.ActorId,
                    command.Actor.TenantId,
                    command.Actor.ApiClientId,
                    null,
                    reservation.IdempotencyKey,
                    reservation.IdempotencyKey + ":commit",
                    charge,
                    ProviderName: result.Usage.ProviderKey,
                    ModelName: result.Usage.ModelKey,
                    PromptTokens: result.Usage.InputTokens,
                    CompletionTokens: result.Usage.OutputTokens,
                    TotalTokens: result.Usage.InputTokens is null && result.Usage.OutputTokens is null
                        ? null
                        : (result.Usage.InputTokens ?? 0) + (result.Usage.OutputTokens ?? 0)),
                cancellationToken);

            await notificationPublisher.EnqueueAsync(
                new NotificationIntentRequest(
                    new NotificationActor(command.Actor.TenantId, command.Actor.ActorId, actorType),
                    NotificationChannel.Telegram,
                    "CodalAlertSummaryReady",
                    insight.Id.ToString(),
                    $"codal-alert-summary-ready:v1:{command.Actor.TenantId}:{command.Actor.ActorId}:{insight.Id}",
                    InsightSeverity.Notice,
                    JsonSerializer.Serialize(new { insightEventId = insight.Id, summaryId = row.Id, summary, evidenceHash }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                    timeProvider.GetUtcNow(),
                    insight.ExpiresAtUtc,
                    command.CorrelationId,
                    SourceEventId: insight.Id,
                    EvidenceReference: evidenceHash,
                    Category: "Codal",
                    CooldownKey: $"CodalSummary:{insight.ExternalCompanyId}"),
                cancellationToken);

            return Map(row);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            row.Status = "Unavailable";
            row.FailureReason = exception.Message;
            row.UpdatedAtUtc = timeProvider.GetUtcNow();
            await dbContext.SaveChangesAsync(cancellationToken);
            await finalizationService.ReleaseAsync(
                new UsageReleaseCommand(account.Id, command.Actor.TenantId, reservation.IdempotencyKey, "Codal alert summary generation failed."),
                cancellationToken);
            return Map(row);
        }
    }

    private static string BuildEvidenceBundle(InsightFeedItem insight) =>
        JsonSerializer.Serialize(new
        {
            insight.Title,
            insight.Summary,
            insight.Reason,
            insight.Symbol,
            insight.SourceProviderName,
            insight.SourceEntityId,
            insight.SourcePeriod,
            insight.Evidence
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static string HashEvidence(InsightFeedItem insight)
    {
        var input = BuildEvidenceBundle(insight);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }

    private static CodalAlertSummaryDto Map(CodalAlertSummaryRow row) =>
        new(
            row.Id,
            row.InsightEventId,
            row.Status,
            row.SummaryText,
            row.EvidenceHash,
            row.PromptPolicyVersion,
            row.ProviderName,
            row.ModelName,
            row.FailureReason,
            row.UpdatedAtUtc);
}
