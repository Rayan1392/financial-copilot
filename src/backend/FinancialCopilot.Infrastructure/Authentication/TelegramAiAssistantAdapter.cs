using System.Globalization;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.FinancialData.CodalAlerts;
using FinancialCopilot.Application.FinancialData.ConditionalTrackers;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Application.Telegram;
using FinancialCopilot.Domain.Financial.ConditionalTrackers;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Authentication;

public sealed class TelegramAiAssistantAdapter(
    AuthDbContext authDbContext,
    ITelegramIdentityLinkReader linkReader,
    ITelegramMembershipService membershipService,
    IGenerateCodalAlertSummaryUseCase codalAlertSummaries,
    IConditionalTrackerUseCases conditionalTrackers,
    IAiQueryOrchestrationService aiQueryOrchestrationService,
    IConversationRepository conversations,
    TimeProvider timeProvider,
    ILogger<TelegramAiAssistantAdapter> logger) : ITelegramAiAssistantAdapter
{
    private const int TelegramMessageLimit = 3900;
    private const int ProcessedUpdateRetentionDays = 7;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<TelegramAssistantResult> HandleAsync(
        TelegramAssistantUpdate update,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = BuildIdempotencyKey(update);
        var replay = await TryReplayAsync(update, idempotencyKey, cancellationToken);
        if (replay is not null)
        {
            return replay with { Status = TelegramAssistantResultStatus.Replayed };
        }

        TelegramAssistantResult result;
        try
        {
            result = await HandleCoreAsync(update, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Telegram AI assistant update {TelegramUpdateId} failed with correlation {CorrelationId}.",
                update.TelegramUpdateId,
                update.CorrelationId);
            result = BuildResult(
                TelegramAssistantResultStatus.TransientError,
                null,
                null,
                null,
                update.CorrelationId,
                "در حال حاضر پاسخ‌گویی از طریق تلگرام با خطا روبه‌رو شد. لطفاً چند دقیقه دیگر دوباره تلاش کنید.");
        }

        await PersistProcessedAsync(update, idempotencyKey, result, cancellationToken);
        return result;
    }

    private async Task<TelegramAssistantResult> HandleCoreAsync(
        TelegramAssistantUpdate update,
        CancellationToken cancellationToken)
    {
        var validationError = Validate(update);
        if (validationError is not null)
        {
            return BuildResult(
                TelegramAssistantResultStatus.ValidationError,
                null,
                null,
                null,
                update.CorrelationId,
                validationError);
        }

        var actor = await linkReader.ResolveActorAsync(update.TelegramUserId, cancellationToken);
        if (actor is null)
        {
            return BuildResult(
                TelegramAssistantResultStatus.Unlinked,
                null,
                null,
                null,
                update.CorrelationId,
                "برای استفاده از دستیار مالی در تلگرام، ابتدا حساب تلگرام خود را از داخل وب‌اپ به حساب Financial Copilot وصل کنید.");
        }

        if (update.Kind == TelegramAssistantUpdateKind.CallbackQuery)
        {
            return await HandleCallbackAsync(update, actor, cancellationToken);
        }

        var text = NormalizeText(update.Text);
        if (string.IsNullOrWhiteSpace(text))
        {
            return BuildResult(
                TelegramAssistantResultStatus.ValidationError,
                actor.ActorId,
                actor.TenantId,
                null,
                update.CorrelationId,
                "متن پیام خالی است. لطفاً پرسش خود را درباره بازار یا نماد ارسال کنید.");
        }

        if (text.StartsWith("/", StringComparison.Ordinal))
        {
            return await HandleCommandAsync(text, update, actor, cancellationToken);
        }

        var conversationId = await GetOrCreateConversationBindingAsync(update, actor, cancellationToken);
        var response = await aiQueryOrchestrationService.ExecuteAsync(
            new AiQueryRequest(
                text,
                actor.TenantId,
                actor.ActorId,
                update.CorrelationId,
                conversationId,
                actor.UserId,
                actor.ApiClientId,
                ExternalUserId: $"telegram:{update.TelegramUserId.ToString(CultureInfo.InvariantCulture)}",
                ActorType: actor.ActorType,
                AuthenticationMode: actor.AuthenticationMode),
            cancellationToken);

        await TouchConversationBindingAsync(update, actor, response.ConversationId, cancellationToken);
        return new TelegramAssistantResult(
            TelegramAssistantResultStatus.Accepted,
            actor.ActorId,
            actor.TenantId,
            response.ConversationId,
            Render(response),
            update.CorrelationId,
            response);
    }

    private async Task<TelegramAssistantResult> HandleCommandAsync(
        string text,
        TelegramAssistantUpdate update,
        CurrentActor actor,
        CancellationToken cancellationToken)
    {
        var command = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].Split('@')[0].ToLowerInvariant();
        return command switch
        {
            "/start" or "/help" => BuildResult(
                TelegramAssistantResultStatus.Accepted,
                actor.ActorId,
                actor.TenantId,
                null,
                update.CorrelationId,
                """
                دستیار مالی Financial Copilot آماده است.

                سؤال خود را درباره نمادها، فیلتر سهام، نسبت‌های مالی یا تحلیل‌های منتشرشده ارسال کنید.

                فرمان‌ها:
                /credits وضعیت اعتبار و سهمیه تلگرام
                /followed لینک واچ‌لیست در وب‌اپ
                /market لینک نمای بازار در وب‌اپ
                """),
            "/credits" => BuildCreditsResult(
                actor,
                update.CorrelationId,
                await membershipService.GetMyTelegramEntitlementAsync(actor, update.CorrelationId, cancellationToken)),
            "/followed" => BuildResult(
                TelegramAssistantResultStatus.Unsupported,
                actor.ActorId,
                actor.TenantId,
                null,
                update.CorrelationId,
                "واچ‌لیست شما در وب‌اپ در مسیر /followed-symbols در دسترس است. برای تلگرام فعلاً فقط پرسش متنی و پاسخ دستیار فعال شده است."),
            "/track" => await HandleTrackCommandAsync(text, update, actor, cancellationToken),
            "/track_edit" => await HandleTrackEditCommandAsync(text, update, actor, cancellationToken),
            "/trackers" => await HandleTrackersCommandAsync(text, update, actor, cancellationToken),
            "/codal_alerts" => BuildResult(
                TelegramAssistantResultStatus.Accepted,
                actor.ActorId,
                actor.TenantId,
                null,
                update.CorrelationId,
                "Codal alerts are managed from the web app at /codal-alerts. AI summary buttons use callback data calert.summary.v1:{insightEventId}."),
            "/market" => BuildResult(
                TelegramAssistantResultStatus.Unsupported,
                actor.ActorId,
                actor.TenantId,
                null,
                update.CorrelationId,
                "نمای بازار فعلاً از وب‌اپ در دسترس است. سؤال مشخص خود را همین‌جا ارسال کنید تا دستیار از مسیر AI موجود پاسخ دهد."),
            _ => BuildResult(
                TelegramAssistantResultStatus.Unsupported,
                actor.ActorId,
                actor.TenantId,
                null,
                update.CorrelationId,
                "این فرمان پشتیبانی نمی‌شود. برای راهنما /help را ارسال کنید یا سؤال مالی خود را به‌صورت متن بفرستید.")
        };
    }

    private async Task<TelegramAssistantResult> HandleTrackCommandAsync(
        string text,
        TelegramAssistantUpdate update,
        CurrentActor actor,
        CancellationToken cancellationToken)
    {
        var parts = text.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
        {
            return BuildResult(
                TelegramAssistantResultStatus.ValidationError,
                actor.ActorId,
                actor.TenantId,
                null,
                update.CorrelationId,
                "Usage: /track <company-id> <condition>. Example: /track 123 price above 5000 toman");
        }

        try
        {
            var rule = await conditionalTrackers.ParseAsync(
                new ParseNaturalLanguageAlertRuleCommand(
                    actor,
                    parts[1],
                    parts[2],
                    $"telegram:{update.TelegramUpdateId.ToString(CultureInfo.InvariantCulture)}"),
                cancellationToken);
            return BuildTrackerResult(rule, actor, update.CorrelationId, "Rule parsed. Confirm before it expires.");
        }
        catch (InvalidOperationException exception)
        {
            return BuildResult(
                TelegramAssistantResultStatus.ValidationError,
                actor.ActorId,
                actor.TenantId,
                null,
                update.CorrelationId,
                exception.Message);
        }
    }

    private async Task<TelegramAssistantResult> HandleTrackersCommandAsync(
        string text,
        TelegramAssistantUpdate update,
        CurrentActor actor,
        CancellationToken cancellationToken)
    {
        var rules = await conditionalTrackers.GetAsync(new GetMyAlertRulesQuery(actor), cancellationToken);
        if (rules.Count == 0)
        {
            return BuildResult(
                TelegramAssistantResultStatus.Accepted,
                actor.ActorId,
                actor.TenantId,
                null,
                update.CorrelationId,
                "You have no conditional trackers. Use /track <company-id> <condition> to create one.");
        }

        var commandParts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var page = commandParts.Length == 1
            ? 1
            : int.TryParse(commandParts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var parsedPage) && parsedPage > 0
                ? parsedPage
                : 0;
        if (page == 0)
        {
            return BuildResult(
                TelegramAssistantResultStatus.ValidationError,
                actor.ActorId,
                actor.TenantId,
                null,
                update.CorrelationId,
                "Usage: /trackers [page-number]");
        }

        const int pageSize = 10;
        var visible = rules.OrderByDescending(item => item.UpdatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArray();
        if (visible.Length == 0)
        {
            return BuildResult(
                TelegramAssistantResultStatus.ValidationError,
                actor.ActorId,
                actor.TenantId,
                null,
                update.CorrelationId,
                $"Tracker page {page} does not exist.");
        }
        var lines = visible.Select((rule, index) => $"{index + 1}. {FormatRule(rule)}");
        var actions = visible.SelectMany(BuildTrackerActions).ToArray();
        var totalPages = (int)Math.Ceiling(rules.Count / (decimal)pageSize);
        var suffix = $"\nPage {page} of {totalPages}; {rules.Count} rules total.";
        return BuildResult(
            TelegramAssistantResultStatus.Accepted,
            actor.ActorId,
            actor.TenantId,
            null,
            update.CorrelationId,
            string.Join("\n", lines) + suffix,
            actions);
    }

    private async Task<TelegramAssistantResult> HandleTrackEditCommandAsync(
        string text,
        TelegramAssistantUpdate update,
        CurrentActor actor,
        CancellationToken cancellationToken)
    {
        var parts = text.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 ||
            !Guid.TryParseExact(parts[1], "N", out var ruleId) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var version) ||
            version <= 0)
        {
            return BuildResult(
                TelegramAssistantResultStatus.ValidationError,
                actor.ActorId,
                actor.TenantId,
                null,
                update.CorrelationId,
                "Usage: /track_edit <rule-id> <version> <new-condition>");
        }

        try
        {
            var rule = await conditionalTrackers.ParseUpdateAsync(
                new ParseNaturalLanguageAlertRuleUpdateCommand(actor, ruleId, version, parts[3]),
                cancellationToken);
            return BuildTrackerResult(rule, actor, update.CorrelationId, "Rule updated. Confirm the new version before it expires.");
        }
        catch (InvalidOperationException exception)
        {
            return BuildResult(
                TelegramAssistantResultStatus.ValidationError,
                actor.ActorId,
                actor.TenantId,
                null,
                update.CorrelationId,
                exception.Message);
        }
    }

    private async Task<TelegramAssistantResult> HandleTrackerCallbackAsync(
        string data,
        TelegramAssistantUpdate update,
        CurrentActor actor,
        CancellationToken cancellationToken)
    {
        var parts = data.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 ||
            !Guid.TryParseExact(parts[1], "N", out var ruleId) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var version) ||
            version <= 0)
        {
            return BuildResult(
                TelegramAssistantResultStatus.ValidationError,
                actor.ActorId,
                actor.TenantId,
                null,
                update.CorrelationId,
                "The tracker callback is malformed or stale.");
        }

        try
        {
            if (string.Equals(parts[0], "tr.c1", StringComparison.OrdinalIgnoreCase) && parts.Length == 4)
            {
                var confirmed = await conditionalTrackers.ConfirmAsync(
                    new ConfirmAlertRuleCommand(actor, ruleId, version, parts[3]),
                    cancellationToken);
                return BuildTrackerResult(confirmed, actor, update.CorrelationId, "Tracker activated.");
            }

            if (string.Equals(parts[0], "tr.e1", StringComparison.OrdinalIgnoreCase) && parts.Length == 3)
            {
                var editable = await conditionalTrackers.GetAsync(actor, ruleId, cancellationToken)
                    ?? throw new AlertRuleValidationException("Alert rule was not found.");
                if (editable.Version != version || editable.State != AlertRuleState.Draft)
                    throw new AlertRuleValidationException("Only the current draft version can be edited.");
                return BuildResult(
                    TelegramAssistantResultStatus.Accepted,
                    actor.ActorId,
                    actor.TenantId,
                    null,
                    update.CorrelationId,
                    $"Send /track_edit {ruleId:N} {version} <new-condition>. The edited rule remains Draft and requires a new confirmation.");
            }

            if (parts.Length != 3)
            {
                throw new AlertRuleValidationException("The tracker callback is malformed or stale.");
            }

            if (string.Equals(parts[0], "tr.p1", StringComparison.OrdinalIgnoreCase))
            {
                var paused = await conditionalTrackers.UpdateAsync(
                    new UpdateAlertRuleCommand(actor, ruleId, version, null, AlertRuleState.Paused),
                    cancellationToken);
                return BuildTrackerResult(paused, actor, update.CorrelationId, "Tracker paused.");
            }

            if (string.Equals(parts[0], "tr.r1", StringComparison.OrdinalIgnoreCase))
            {
                var resumed = await conditionalTrackers.UpdateAsync(
                    new UpdateAlertRuleCommand(actor, ruleId, version, null, AlertRuleState.Active),
                    cancellationToken);
                return BuildTrackerResult(resumed, actor, update.CorrelationId, "Tracker resumed.");
            }

            if (string.Equals(parts[0], "tr.x1", StringComparison.OrdinalIgnoreCase))
            {
                await conditionalTrackers.RemoveAsync(new RemoveAlertRuleCommand(actor, ruleId, version), cancellationToken);
                return BuildResult(
                    TelegramAssistantResultStatus.Accepted,
                    actor.ActorId,
                    actor.TenantId,
                    null,
                    update.CorrelationId,
                    "Tracker removed.");
            }

            throw new AlertRuleValidationException("The tracker callback operation is not supported.");
        }
        catch (InvalidOperationException exception)
        {
            return BuildResult(
                TelegramAssistantResultStatus.ValidationError,
                actor.ActorId,
                actor.TenantId,
                null,
                update.CorrelationId,
                exception.Message);
        }
    }

    private static TelegramAssistantResult BuildTrackerResult(
        AlertRuleDto rule,
        CurrentActor actor,
        string correlationId,
        string heading) =>
        BuildResult(
            TelegramAssistantResultStatus.Accepted,
            actor.ActorId,
            actor.TenantId,
            null,
            correlationId,
            $"{heading}\n{FormatRule(rule)}" +
            (rule.State == AlertRuleState.Draft
                ? $"\nConfirmation expires: {rule.ConfirmationExpiresAtUtc:O}"
                : string.Empty),
            BuildTrackerActions(rule));

    private static string FormatRule(AlertRuleDto rule) =>
        $"{rule.Symbol} | {rule.RuleType}/{rule.MetricOrEventCode} {rule.Operator} {FormatDecimal(rule.Threshold)} {rule.Unit} | {rule.State} | v{rule.Version}";

    private static IReadOnlyList<TelegramAssistantAction> BuildTrackerActions(AlertRuleDto rule)
    {
        var id = rule.Id.ToString("N");
        var version = rule.Version.ToString(CultureInfo.InvariantCulture);
        return rule.State switch
        {
            AlertRuleState.Draft =>
                [
                    new TelegramAssistantAction("Confirm", $"tr.c1:{id}:{version}:{rule.ConfirmationToken}"),
                    new TelegramAssistantAction("Edit", $"tr.e1:{id}:{version}"),
                    new TelegramAssistantAction("Cancel", $"tr.x1:{id}:{version}")
                ],
            AlertRuleState.Active =>
                [new TelegramAssistantAction("Pause", $"tr.p1:{id}:{version}"), new TelegramAssistantAction("Remove", $"tr.x1:{id}:{version}")],
            AlertRuleState.Paused =>
                [new TelegramAssistantAction("Resume", $"tr.r1:{id}:{version}"), new TelegramAssistantAction("Remove", $"tr.x1:{id}:{version}")],
            _ => []
        };
    }

    private async Task<TelegramAssistantResult> HandleCallbackAsync(
        TelegramAssistantUpdate update,
        CurrentActor actor,
        CancellationToken cancellationToken)
    {
        var data = update.CallbackData?.Trim();
        if (string.Equals(data, "tgm.recheck.v1", StringComparison.OrdinalIgnoreCase))
        {
            var entitlement = await membershipService.GetMyTelegramEntitlementAsync(actor, update.CorrelationId, cancellationToken);
            return BuildCreditsResult(actor, update.CorrelationId, entitlement);
        }

        if (data is not null && data.StartsWith("tr.", StringComparison.OrdinalIgnoreCase))
        {
            return await HandleTrackerCallbackAsync(data, update, actor, cancellationToken);
        }

        const string codalSummaryPrefix = "calert.summary.v1:";
        if (data is not null &&
            data.StartsWith(codalSummaryPrefix, StringComparison.OrdinalIgnoreCase) &&
            Guid.TryParse(data[codalSummaryPrefix.Length..], out var insightEventId))
        {
            var summary = await codalAlertSummaries.ExecuteAsync(
                new GenerateCodalAlertSummaryCommand(actor, insightEventId, update.CorrelationId),
                cancellationToken);
            var text = summary.Status == "Completed"
                ? $"AI summary is ready:\n\n{summary.SummaryText}\n\nEvidence hash: {summary.EvidenceHash}"
                : $"AI summary is unavailable. Status: {summary.Status}. Reason: {summary.FailureReason ?? "not provided"}";
            return BuildResult(
                TelegramAssistantResultStatus.Accepted,
                actor.ActorId,
                actor.TenantId,
                null,
                update.CorrelationId,
                text);
        }

        return BuildResult(
            TelegramAssistantResultStatus.Unsupported,
            actor.ActorId,
            actor.TenantId,
            null,
            update.CorrelationId,
            "این عملیات تلگرام پشتیبانی نمی‌شود. برای مشاهده وضعیت اعتبار /credits را ارسال کنید.");
    }

    private async Task<Guid> GetOrCreateConversationBindingAsync(
        TelegramAssistantUpdate update,
        CurrentActor actor,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var messageThreadKey = MessageThreadKey(update);
        var binding = await authDbContext.TelegramConversationBindings.SingleOrDefaultAsync(
            row => row.ActorId == actor.ActorId &&
                row.TenantId == actor.TenantId &&
                row.TelegramChatId == update.TelegramChatId &&
                row.MessageThreadKey == messageThreadKey &&
                row.RevokedAtUtc == null,
            cancellationToken);
        if (binding is not null)
        {
            binding.LastMessageAtUtc = now;
            await authDbContext.SaveChangesAsync(cancellationToken);
            return binding.ConversationId;
        }

        var conversationId = await conversations.CreateEmptyAsync(actor.TenantId, actor.ActorId, now, cancellationToken);
        authDbContext.TelegramConversationBindings.Add(new TelegramConversationBindingRow
        {
            Id = Guid.NewGuid(),
            ActorId = actor.ActorId,
            TenantId = actor.TenantId,
            TelegramChatId = update.TelegramChatId,
            MessageThreadId = update.MessageThreadId,
            MessageThreadKey = messageThreadKey,
            ConversationId = conversationId,
            CreatedAtUtc = now,
            LastMessageAtUtc = now,
            CorrelationId = update.CorrelationId
        });
        await authDbContext.SaveChangesAsync(cancellationToken);
        return conversationId;
    }

    private async Task TouchConversationBindingAsync(
        TelegramAssistantUpdate update,
        CurrentActor actor,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var messageThreadKey = MessageThreadKey(update);
        var binding = await authDbContext.TelegramConversationBindings.SingleOrDefaultAsync(
            row => row.ActorId == actor.ActorId &&
                row.TenantId == actor.TenantId &&
                row.TelegramChatId == update.TelegramChatId &&
                row.MessageThreadKey == messageThreadKey &&
                row.ConversationId == conversationId &&
                row.RevokedAtUtc == null,
            cancellationToken);
        if (binding is null)
        {
            return;
        }

        binding.LastMessageAtUtc = timeProvider.GetUtcNow();
        await authDbContext.SaveChangesAsync(cancellationToken);
    }

    private static TelegramAssistantResult BuildCreditsResult(
        CurrentActor actor,
        string correlationId,
        TelegramEntitlementView entitlement)
    {
        var membership = entitlement.Membership is null
            ? "ثبت نشده"
            : entitlement.Membership.IsEligible
                ? "معتبر"
                : $"نامعتبر ({entitlement.Membership.Status})";
        var text = $"""
        وضعیت اعتبار تلگرام:

        سهمیه رایگان امروز: {FormatDecimal(entitlement.FreeDailyAllowance.RemainingCredits)} از {FormatDecimal(entitlement.FreeDailyAllowance.TotalCredits)} اعتبار باقی مانده
        ظرفیت پرداختی: {FormatDecimal(entitlement.PaidAvailableSpendingCapacity)} اعتبار
        عضویت کانال: {membership}
        اقدام بعدی: {entitlement.NextAction}
        """;
        return BuildResult(
            TelegramAssistantResultStatus.Accepted,
            actor.ActorId,
            actor.TenantId,
            null,
            correlationId,
            text);
    }

    private static IReadOnlyList<TelegramAssistantRenderedMessage> Render(AiQueryResponse response)
    {
        var builder = new StringBuilder();
        if (response.ClarificationRequired && !string.IsNullOrWhiteSpace(response.ClarificationMessage))
        {
            builder.AppendLine(response.ClarificationMessage);
        }
        else if (!string.IsNullOrWhiteSpace(response.TextAnswer))
        {
            builder.AppendLine(response.TextAnswer);
        }

        AppendAnalysis(builder, response);
        AppendTable(builder, "جدول نمادها", response.SymbolLookupTable);
        AppendTable(builder, "نتایج فیلتر", response.ScannerTable);

        if (response.ConfidenceScore is not null)
        {
            builder.AppendLine();
            builder.AppendLine($"اطمینان پاسخ: {response.ConfidenceScore.Score:P0}");
        }

        if (response.Usage is not null)
        {
            builder.AppendLine();
            builder.AppendLine($"اعتبار مصرف‌شده: {FormatDecimal(response.Usage.CreditsCharged)}");
            builder.AppendLine($"اعتبار باقی‌مانده: {FormatDecimal(response.Usage.RemainingSpendingCapacity)}");
        }

        if (response.ExplainableAnswer?.DataCitations.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("استناد داده:");
            foreach (var citation in response.ExplainableAnswer.DataCitations.Take(5))
            {
                builder.AppendLine($"- {citation.SymbolCode} / {citation.MetricCode}: {citation.FreshnessStatus}");
            }
        }

        var text = builder.ToString().Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            text = "پاسخ دستیار آماده شد، اما متن قابل نمایش برای تلگرام تولید نشد.";
        }

        return Split(EscapeMarkdownV2(text));
    }

    private static void AppendAnalysis(StringBuilder builder, AiQueryResponse response)
    {
        var items = response.ComprehensiveAnalysisResult?.Items;
        if (items is null || items.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("تحلیل‌های یافت‌شده:");
        foreach (var item in items.Take(3))
        {
            builder.AppendLine($"{item.Title} — {item.PersianCreatedAt}");
            builder.AppendLine(item.PlainTextSummary);
            builder.AppendLine($"منبع: ComprehensiveAnalyses | نویسنده: {item.AuthorName}");
            builder.AppendLine();
        }
    }

    private static void AppendTable(StringBuilder builder, string title, SymbolLookupTableResult? table)
    {
        if (table is null)
        {
            return;
        }

        AppendRows(builder, title, table.Rows);
    }

    private static void AppendTable(StringBuilder builder, string title, ScannerTableResult? table)
    {
        if (table is null)
        {
            return;
        }

        AppendRows(builder, title, table.Rows);
    }

    private static void AppendRows(
        StringBuilder builder,
        string title,
        IReadOnlyCollection<ScannerTableRow> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine(title + ":");
        foreach (var row in rows.Take(8))
        {
            var cells = row.Cells
                .Take(4)
                .Select(cell => $"{cell.Key}: {cell.Value.FormattedValue ?? cell.Value.Value?.ToString(CultureInfo.InvariantCulture) ?? "-"}");
            builder.AppendLine($"- {row.SymbolCode}: {string.Join(" | ", cells)}");
        }
    }

    private async Task<TelegramAssistantResult?> TryReplayAsync(
        TelegramAssistantUpdate update,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var row = await authDbContext.TelegramProcessedUpdates.AsNoTracking()
            .SingleOrDefaultAsync(candidate =>
                candidate.IdempotencyKey == idempotencyKey &&
                candidate.ExpiresAtUtc > now,
                cancellationToken);
        if (row is null)
        {
            return null;
        }

        var persisted = JsonSerializer.Deserialize<PersistedTelegramAssistantResult>(row.ResponseJson, JsonOptions);
        if (persisted is null)
        {
            return null;
        }

        return new TelegramAssistantResult(
            persisted.Status,
            persisted.ActorId,
            persisted.TenantId,
            persisted.ConversationId,
            persisted.Messages,
            update.CorrelationId);
    }

    private async Task PersistProcessedAsync(
        TelegramAssistantUpdate update,
        string idempotencyKey,
        TelegramAssistantResult result,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var persisted = new PersistedTelegramAssistantResult(
            result.Status,
            result.ActorId,
            result.TenantId,
            result.ConversationId,
            result.Messages);

        authDbContext.TelegramProcessedUpdates.Add(new TelegramProcessedUpdateRow
        {
            Id = Guid.NewGuid(),
            IdempotencyKey = idempotencyKey,
            TelegramUpdateId = update.TelegramUpdateId,
            CallbackQueryId = update.CallbackQueryId,
            ActorId = result.ActorId,
            TenantId = result.TenantId,
            TelegramUserId = update.TelegramUserId,
            TelegramChatId = update.TelegramChatId,
            MessageThreadId = update.MessageThreadId,
            Status = result.Status.ToString(),
            ConversationId = result.ConversationId,
            ResponseJson = JsonSerializer.Serialize(persisted, JsonOptions),
            ProcessedAtUtc = now,
            ExpiresAtUtc = now.AddDays(ProcessedUpdateRetentionDays),
            CorrelationId = result.CorrelationId
        });

        try
        {
            await authDbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            logger.LogWarning(
                exception,
                "Telegram processed update idempotency persisted concurrently for {IdempotencyKey}.",
                idempotencyKey);
        }
    }

    private static string? Validate(TelegramAssistantUpdate update)
    {
        if (update.TelegramUpdateId <= 0) return "شناسه آپدیت تلگرام نامعتبر است.";
        if (update.TelegramUserId <= 0) return "شناسه کاربر تلگرام نامعتبر است.";
        if (update.TelegramChatId == 0) return "شناسه چت تلگرام نامعتبر است.";
        if (string.IsNullOrWhiteSpace(update.CorrelationId)) return "شناسه رهگیری نامعتبر است.";
        if (update.Kind == TelegramAssistantUpdateKind.CallbackQuery && string.IsNullOrWhiteSpace(update.CallbackData))
        {
            return "داده callback تلگرام خالی است.";
        }

        return null;
    }

    private static TelegramAssistantResult BuildResult(
        TelegramAssistantResultStatus status,
        Guid? actorId,
        Guid? tenantId,
        Guid? conversationId,
        string correlationId,
        string text,
        IReadOnlyList<TelegramAssistantAction>? actions = null) =>
        new(status, actorId, tenantId, conversationId, Split(EscapeMarkdownV2(text), actions), correlationId);

    private static IReadOnlyList<TelegramAssistantRenderedMessage> Split(
        string text,
        IReadOnlyList<TelegramAssistantAction>? actions = null)
    {
        var parts = new List<string>();
        var remaining = text;
        while (remaining.Length > TelegramMessageLimit)
        {
            var splitAt = remaining.LastIndexOf('\n', TelegramMessageLimit);
            if (splitAt < TelegramMessageLimit / 2)
            {
                splitAt = TelegramMessageLimit;
            }

            parts.Add(remaining[..splitAt].Trim());
            remaining = remaining[splitAt..].Trim();
        }

        if (!string.IsNullOrWhiteSpace(remaining))
        {
            parts.Add(remaining);
        }

        if (parts.Count == 0)
        {
            parts.Add(EscapeMarkdownV2("پاسخی برای نمایش وجود ندارد."));
        }

        return parts
            .Select((part, index) => new TelegramAssistantRenderedMessage(
                index + 1,
                parts.Count,
                parts.Count == 1 ? part : EscapeMarkdownV2($"بخش {index + 1}/{parts.Count}") + "\n" + part))
            .Select((message, index) => index == parts.Count - 1
                ? message with { Actions = actions }
                : message)
            .ToArray();
    }

    private static string BuildIdempotencyKey(TelegramAssistantUpdate update) =>
        string.IsNullOrWhiteSpace(update.CallbackQueryId)
            ? $"message:{update.TelegramUpdateId.ToString(CultureInfo.InvariantCulture)}"
            : $"callback:{update.TelegramUpdateId.ToString(CultureInfo.InvariantCulture)}:{update.CallbackQueryId.Trim()}";

    private static int MessageThreadKey(TelegramAssistantUpdate update) =>
        update.MessageThreadId ?? 0;

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim()
            .Replace('ي', 'ی')
            .Replace('ك', 'ک')
            .Replace('\u200c', ' ');
        var builder = new StringBuilder(normalized.Length);
        var previousWasSpace = false;
        foreach (var ch in normalized)
        {
            var current = NormalizeDigit(ch);
            if (char.IsWhiteSpace(current))
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                    previousWasSpace = true;
                }

                continue;
            }

            builder.Append(current);
            previousWasSpace = false;
        }

        return builder.ToString().Trim();
    }

    private static char NormalizeDigit(char ch) => ch switch
    {
        >= '۰' and <= '۹' => (char)('0' + ch - '۰'),
        >= '٠' and <= '٩' => (char)('0' + ch - '٠'),
        _ => ch
    };

    private static string FormatDecimal(decimal value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string EscapeMarkdownV2(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch is '_' or '*' or '[' or ']' or '(' or ')' or '~' or '`' or '>' or '#' or '+' or '-' or '=' or '|' or '{' or '}' or '.' or '!')
            {
                builder.Append('\\');
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private sealed record PersistedTelegramAssistantResult(
        TelegramAssistantResultStatus Status,
        Guid? ActorId,
        Guid? TenantId,
        Guid? ConversationId,
        IReadOnlyList<TelegramAssistantRenderedMessage> Messages);
}
