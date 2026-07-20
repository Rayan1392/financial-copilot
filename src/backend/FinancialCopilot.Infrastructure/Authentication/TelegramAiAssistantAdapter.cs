using System.Globalization;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.FinancialData.CodalAlerts;
using FinancialCopilot.Application.FinancialData.ConditionalTrackers;
using FinancialCopilot.Application.FinancialData.Radar;
using FinancialCopilot.Application.FinancialData.ProfessionalScanners;
using FinancialCopilot.Application.FinancialData.MarketReports;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Application.Notifications;
using FinancialCopilot.Application.Telegram;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Purchases;
using FinancialCopilot.Domain.Financial.ConditionalTrackers;
using FinancialCopilot.Domain.Financial.Insights;
using FinancialCopilot.Domain.Financial.Radar;
using FinancialCopilot.Domain.Notifications;
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
    IRadarUseCases radar,
    IProfessionalScannerUseCases professionalScanners,
    IMarketReportService marketReports,
    INotificationUseCases notificationUseCases,
    IAlertHistoryUseCases alertHistoryUseCases,
    IBillingPurchaseUseCases billingPurchases,
    IAiQueryOrchestrationService aiQueryOrchestrationService,
    IConversationRepository conversations,
    ITelegramAssistantResponseRenderer responseRenderer,
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
            responseRenderer.Render(response, update.Locale),
            update.CorrelationId,
            response,
            responseRenderer.Version);
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
                /scanners - professional filter catalog
                /scanner FILTER_CODE key=value - run a filter
                /save_scanner name | FILTER_CODE | key=value - save a filter
                /saved_scanners - saved filters
                /report - latest published market report
                /digest - generate your evidence-bound followed-symbol digest
                /notifications - notification mode, quiet hours, severity, categories, symbols and daily cap
                /alerts - alert history and explanations
                /alert ALERT_ID - alert detail
                /plans - Telegram subscription and credit products
                /buy PRODUCT_CODE - create a manual receipt checkout
                /receipt CHECKOUT_ID VERSION Image|Document RECEIPT_REFERENCE - submit receipt metadata
                /checkout CHECKOUT_ID - view checkout status
                """),
            "/credits" => BuildCreditsResult(
                actor,
                update.CorrelationId,
                await membershipService.GetMyTelegramEntitlementAsync(actor, update.CorrelationId, cancellationToken)),
            "/plans" => await HandlePlansCommandAsync(update, actor, cancellationToken),
            "/buy" => await HandleBuyCommandAsync(text, update, actor, cancellationToken),
            "/receipt" => await HandleReceiptCommandAsync(text, update, actor, cancellationToken),
            "/checkout" => await HandleCheckoutCommandAsync(text, update, actor, cancellationToken),
            "/cancel_checkout" => await HandleCancelCheckoutCommandAsync(text, update, actor, cancellationToken),
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
            "/radar" => await HandleRadarCommandAsync(text, update, actor, cancellationToken),
            "/radar_override" => await HandleRadarOverrideCommandAsync(text, update, actor, cancellationToken),
            "/scanners" => HandleScannersCommand(text, update, actor),
            "/scanner" => await HandleProfessionalScannerCommandAsync(text, update, actor, cancellationToken),
            "/save_scanner" => await HandleSaveScannerCommandAsync(text, update, actor, cancellationToken),
            "/saved_scanners" => await HandleSavedScannersCommandAsync(text, update, actor, cancellationToken),
            "/codal_alerts" => BuildResult(
                TelegramAssistantResultStatus.Accepted,
                actor.ActorId,
                actor.TenantId,
                null,
                update.CorrelationId,
                "Codal alerts are managed from the web app at /codal-alerts. AI summary buttons use callback data calert.summary.v1:{insightEventId}."),
            "/report" => await HandleLatestMarketReportCommandAsync(update, actor, cancellationToken),
            "/digest" => await HandlePersonalDigestCommandAsync(update, actor, cancellationToken),
            "/notifications" or "/settings" => await HandleNotificationSettingsCommandAsync(
                text, update, actor, cancellationToken),
            "/alerts" => await HandleAlertsCommandAsync(text, update, actor, cancellationToken),
            "/alert" => await HandleAlertDetailCommandAsync(text, update, actor, cancellationToken),
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

    private async Task<TelegramAssistantResult> HandleNotificationSettingsCommandAsync(
        string text,
        TelegramAssistantUpdate update,
        CurrentActor actor,
        CancellationToken cancellationToken)
    {
        try
        {
            var current = await notificationUseCases.GetPreferencesAsync(actor, cancellationToken);
            var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) return RenderNotificationSettings(current, actor, update.CorrelationId);

            var input = ToInput(current);
            switch (parts[1].ToLowerInvariant())
            {
                case "mode" when parts.Length == 3 && Enum.TryParse<NotificationDeliveryMode>(parts[2], true, out var mode):
                    input = input with { DeliveryMode = mode };
                    break;
                case "timezone" when parts.Length == 3:
                    input = input with { TimeZoneId = parts[2] };
                    break;
                case "quiet" when parts.Length == 3 && parts[2].Equals("off", StringComparison.OrdinalIgnoreCase):
                    input = input with { QuietHoursStart = null, QuietHoursEnd = null };
                    break;
                case "quiet" when parts.Length == 4 && TimeOnly.TryParse(parts[2], out var start) &&
                                                        TimeOnly.TryParse(parts[3], out var end):
                    input = input with { QuietHoursStart = start, QuietHoursEnd = end };
                    break;
                case "severity" when parts.Length == 3 && Enum.TryParse<InsightSeverity>(parts[2], true, out var severity):
                    input = input with { MinimumSeverity = severity };
                    break;
                case "cap" when parts.Length == 3 && int.TryParse(parts[2], out var cap):
                    input = input with { DailyCap = cap };
                    break;
                case "category" when parts.Length == 4 &&
                    (parts[3].Equals("on", StringComparison.OrdinalIgnoreCase) ||
                     parts[3].Equals("off", StringComparison.OrdinalIgnoreCase)):
                    var categories = input.Categories.Where(item =>
                        !item.EventType.Equals(parts[2], StringComparison.OrdinalIgnoreCase)).ToList();
                    categories.Add(new NotificationCategoryPreferenceInput(parts[2],
                        parts[3].Equals("on", StringComparison.OrdinalIgnoreCase)));
                    input = input with { Categories = categories };
                    break;
                case "mute" when parts.Length == 3:
                case "unmute" when parts.Length == 3:
                    var symbols = input.Symbols.Where(item => item.ExternalCompanyId != parts[2]).ToList();
                    symbols.Add(new NotificationSymbolPreferenceInput(parts[2],
                        parts[1].Equals("mute", StringComparison.OrdinalIgnoreCase)));
                    input = input with { Symbols = symbols };
                    break;
                case "reset" when parts.Length == 2:
                    input = new NotificationPreferenceInput(NotificationPreferencePolicy.DefaultTimeZoneId,
                        NotificationDeliveryMode.Immediate, new TimeOnly(23, 0), new TimeOnly(7, 0),
                        InsightSeverity.Notice, 20, new TimeOnly(18, 0), 30, [], []);
                    break;
                default:
                    return BuildResult(TelegramAssistantResultStatus.ValidationError, actor.ActorId,
                        actor.TenantId, null, update.CorrelationId,
                        "تنظیم اعلان نامعتبر است. برای دیدن فرمان‌های پشتیبانی‌شده /notifications را اجرا کنید.");
            }

            var updated = await notificationUseCases.UpdatePreferencesAsync(
                new UpdateNotificationPreferenceCommand(actor, current.Version, input,
                    "Telegram", update.CorrelationId), cancellationToken);
            return RenderNotificationSettings(updated, actor, update.CorrelationId);
        }
        catch (InvalidOperationException exception)
        {
            return BuildResult(TelegramAssistantResultStatus.ValidationError, actor.ActorId,
                actor.TenantId, null, update.CorrelationId, LocalizeNotificationError(exception.Message));
        }
    }

    private async Task<TelegramAssistantResult> HandlePlansCommandAsync(
        TelegramAssistantUpdate update,
        CurrentActor actor,
        CancellationToken cancellationToken)
    {
        var catalog = await billingPurchases.GetCatalogAsync("Telegram", cancellationToken);
        var builder = new StringBuilder("Telegram purchase catalog\n\n");
        foreach (var product in catalog.Products)
        {
            builder.AppendLine($"{product.Code} - {product.DisplayName}");
            builder.AppendLine(product.ProductType == BillingPurchaseProductType.CreditPack
                ? $"Credits: {FormatDecimal(product.Credits)}"
                : $"Plan: {product.PlanCode}; duration: {product.DurationDays} days");
            builder.AppendLine($"Amount: {FormatDecimal(product.Amount)} {product.Currency}");
            builder.AppendLine();
        }
        builder.AppendLine("Create checkout: /buy PRODUCT_CODE");
        builder.AppendLine("Manual MVP: pay externally, then submit receipt metadata. Do not send card or PIN data.");
        var actions = catalog.Products.Take(4)
            .Select(product => new TelegramAssistantAction(product.Code, $"bp.buy.v1:{product.Code}"))
            .ToArray();
        return BuildResult(TelegramAssistantResultStatus.Accepted, actor.ActorId, actor.TenantId, null,
            update.CorrelationId, builder.ToString(), actions);
    }

    private async Task<TelegramAssistantResult> HandleBuyCommandAsync(
        string text,
        TelegramAssistantUpdate update,
        CurrentActor actor,
        CancellationToken cancellationToken)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return BuildResult(TelegramAssistantResultStatus.ValidationError, actor.ActorId, actor.TenantId, null,
                update.CorrelationId, "Usage: /buy PRODUCT_CODE");
        return await CreateTelegramCheckoutAsync(parts[1], update, actor, cancellationToken);
    }

    private async Task<TelegramAssistantResult> HandleReceiptCommandAsync(
        string text,
        TelegramAssistantUpdate update,
        CurrentActor actor,
        CancellationToken cancellationToken)
    {
        var parts = text.Split(' ', 5, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5 ||
            !Guid.TryParse(parts[1], out var checkoutId) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var version))
        {
            return BuildResult(TelegramAssistantResultStatus.ValidationError, actor.ActorId, actor.TenantId, null,
                update.CorrelationId, "Usage: /receipt CHECKOUT_ID VERSION Image|Document RECEIPT_REFERENCE");
        }

        try
        {
            var checkout = await billingPurchases.SubmitReceiptAsync(
                new SubmitBillingReceiptCommand(
                    ToBillableActor(actor),
                    checkoutId,
                    version,
                    parts[3],
                    parts[4],
                    ProviderReference: null,
                    IdempotencyKey: $"telegram:{update.TelegramUpdateId}:receipt:{checkoutId:N}",
                    update.CorrelationId),
                cancellationToken);
            return RenderCheckout(checkout, actor, update.CorrelationId,
                "Receipt metadata received. It is now under manual review.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException)
        {
            return BuildResult(TelegramAssistantResultStatus.ValidationError, actor.ActorId, actor.TenantId, null,
                update.CorrelationId, exception.Message);
        }
    }

    private async Task<TelegramAssistantResult> HandleCheckoutCommandAsync(
        string text,
        TelegramAssistantUpdate update,
        CurrentActor actor,
        CancellationToken cancellationToken)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !Guid.TryParse(parts[1], out var checkoutId))
            return BuildResult(TelegramAssistantResultStatus.ValidationError, actor.ActorId, actor.TenantId, null,
                update.CorrelationId, "Usage: /checkout CHECKOUT_ID");
        var checkout = await billingPurchases.GetCheckoutAsync(ToBillableActor(actor), checkoutId, cancellationToken);
        return checkout is null
            ? BuildResult(TelegramAssistantResultStatus.ValidationError, actor.ActorId, actor.TenantId, null,
                update.CorrelationId, "Checkout was not found for this account.")
            : RenderCheckout(checkout, actor, update.CorrelationId, "Checkout status");
    }

    private async Task<TelegramAssistantResult> HandleCancelCheckoutCommandAsync(
        string text,
        TelegramAssistantUpdate update,
        CurrentActor actor,
        CancellationToken cancellationToken)
    {
        var parts = text.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 ||
            !Guid.TryParse(parts[1], out var checkoutId) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var version))
        {
            return BuildResult(TelegramAssistantResultStatus.ValidationError, actor.ActorId, actor.TenantId, null,
                update.CorrelationId, "Usage: /cancel_checkout CHECKOUT_ID VERSION reason");
        }

        try
        {
            var checkout = await billingPurchases.CancelCheckoutAsync(
                new CancelBillingCheckoutCommand(
                    ToBillableActor(actor),
                    checkoutId,
                    version,
                    parts[3],
                    update.CorrelationId),
                cancellationToken);
            return RenderCheckout(checkout, actor, update.CorrelationId, "Checkout cancelled");
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException)
        {
            return BuildResult(TelegramAssistantResultStatus.ValidationError, actor.ActorId, actor.TenantId, null,
                update.CorrelationId, exception.Message);
        }
    }

    private async Task<TelegramAssistantResult> CreateTelegramCheckoutAsync(
        string productCode,
        TelegramAssistantUpdate update,
        CurrentActor actor,
        CancellationToken cancellationToken)
    {
        try
        {
            var checkout = await billingPurchases.CreateCheckoutAsync(
                new CreateBillingCheckoutCommand(
                    ToBillableActor(actor),
                    productCode,
                    $"telegram:{update.TelegramUpdateId}:checkout:{productCode}",
                    update.CorrelationId),
                cancellationToken);
            return RenderCheckout(checkout, actor, update.CorrelationId,
                "Checkout created. Pay using the approved external payment path, then submit receipt metadata.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException)
        {
            return BuildResult(TelegramAssistantResultStatus.ValidationError, actor.ActorId, actor.TenantId, null,
                update.CorrelationId, exception.Message);
        }
    }

    private static TelegramAssistantResult RenderCheckout(
        BillingCheckoutView checkout,
        CurrentActor actor,
        string correlationId,
        string title)
    {
        var builder = new StringBuilder();
        builder.AppendLine(title);
        builder.AppendLine($"Product: {checkout.ProductDisplayName} ({checkout.ProductCode})");
        builder.AppendLine($"Amount: {FormatDecimal(checkout.Amount)} {checkout.Currency}");
        builder.AppendLine($"Payment reference: {checkout.PaymentReference}");
        builder.AppendLine($"Status: {checkout.Status}; version: {checkout.Version}");
        builder.AppendLine($"Expires: {checkout.ExpiresAtUtc:O}");
        builder.AppendLine($"Checkout id: {checkout.Id:D}");
        builder.AppendLine("Do not send card number, PIN, CVV, or bank credentials.");
        if (checkout.Status == BillingCheckoutStatus.AwaitingPayment)
            builder.AppendLine($"Submit receipt: /receipt {checkout.Id:D} {checkout.Version} Image RECEIPT_REFERENCE");
        if (checkout.Status == BillingCheckoutStatus.UnderReview)
            builder.AppendLine("Review status: under manual Billing review.");
        if (checkout.Status == BillingCheckoutStatus.Fulfilled)
            builder.AppendLine("Fulfillment: completed once in Billing.");
        return BuildResult(TelegramAssistantResultStatus.Accepted, actor.ActorId, actor.TenantId, null,
            correlationId, builder.ToString());
    }

    private static BillableActorContext ToBillableActor(CurrentActor actor) =>
        new(actor.ActorId, actor.TenantId, actor.UserId, actor.ApiClientId, ExternalUserId: null);

    private async Task<TelegramAssistantResult> HandleNotificationSettingsCallbackAsync(
        string data,
        TelegramAssistantUpdate update,
        CurrentActor actor,
        CancellationToken cancellationToken)
    {
        try
        {
            var parts = data.Split(':');
            if (parts.Length != 3 || !int.TryParse(parts[2], out var expectedVersion))
                throw new NotificationValidationException("callback-malformed");
            var current = await notificationUseCases.GetPreferencesAsync(actor, cancellationToken);
            if (current.Version != expectedVersion)
                throw new NotificationValidationException("callback-stale");
            var input = ToInput(current);
            if (parts[0].Equals("nt.mode.v1", StringComparison.OrdinalIgnoreCase) &&
                Enum.TryParse<NotificationDeliveryMode>(parts[1], true, out var mode))
                input = input with { DeliveryMode = mode };
            else if (parts[0].Equals("nt.sev.v1", StringComparison.OrdinalIgnoreCase) &&
                     Enum.TryParse<InsightSeverity>(parts[1], true, out var severity))
                input = input with { MinimumSeverity = severity };
            else if (parts[0].Equals("nt.reset.v1", StringComparison.OrdinalIgnoreCase))
                input = new NotificationPreferenceInput(NotificationPreferencePolicy.DefaultTimeZoneId,
                    NotificationDeliveryMode.Immediate, new TimeOnly(23, 0), new TimeOnly(7, 0),
                    InsightSeverity.Notice, 20, new TimeOnly(18, 0), 30, [], []);
            else
                throw new NotificationValidationException("callback-unsupported");
            var updated = await notificationUseCases.UpdatePreferencesAsync(
                new UpdateNotificationPreferenceCommand(actor, expectedVersion, input,
                    "TelegramCallback", update.CorrelationId), cancellationToken);
            return RenderNotificationSettings(updated, actor, update.CorrelationId);
        }
        catch (InvalidOperationException exception)
        {
            return BuildResult(TelegramAssistantResultStatus.ValidationError, actor.ActorId,
                actor.TenantId, null, update.CorrelationId, LocalizeNotificationError(exception.Message));
        }
    }

    private static NotificationPreferenceInput ToInput(NotificationPreferenceDto value) => new(
        value.TimeZoneId, value.DeliveryMode, value.QuietHoursStart, value.QuietHoursEnd,
        value.MinimumSeverity, value.DailyCap, value.DigestTime, value.CooldownMinutes,
        value.Categories.Select(item => new NotificationCategoryPreferenceInput(
            item.EventType, item.Enabled, item.MinimumSeverity, item.CooldownMinutes)).ToArray(),
        value.Symbols.Select(item => new NotificationSymbolPreferenceInput(
            item.ExternalCompanyId, item.Muted)).ToArray());

    private static TelegramAssistantResult RenderNotificationSettings(
        NotificationPreferenceDto value,
        CurrentActor actor,
        string correlationId)
    {
        var quiet = value.QuietHoursStart is null
            ? "off" : $"{value.QuietHoursStart:HH\\:mm}-{value.QuietHoursEnd:HH\\:mm}";
        var text = $"تنظیمات اعلان نسخه {value.Version}\n" +
                   $"حالت: {value.DeliveryMode}؛ منطقه زمانی: {value.TimeZoneId}؛ ساعات سکوت: {quiet}\n" +
                   $"حداقل شدت: {value.MinimumSeverity}؛ سقف روزانه: {value.DailyCap}؛ زمان خلاصه: {value.DigestTime:HH\\:mm}\n" +
                   $"دسته‌های بی‌صدا: {string.Join(", ", value.Categories.Where(item => !item.Enabled).Select(item => item.EventType))}\n" +
                   $"نمادهای بی‌صدا: {string.Join(", ", value.Symbols.Where(item => item.Muted).Select(item => item.ExternalCompanyId))}\n\n" +
                   "فرمان‌ها: /notifications mode Immediate|Digest; timezone ZONE; quiet HH:mm HH:mm|off; " +
                   "severity Informational|Notice|Important|Critical; cap N; category EVENT on|off; " +
                   "mute COMPANY_ID; unmute COMPANY_ID; reset.";
        var version = value.Version.ToString(CultureInfo.InvariantCulture);
        return BuildResult(TelegramAssistantResultStatus.Accepted, actor.ActorId, actor.TenantId,
            null, correlationId, text,
            [
                new TelegramAssistantAction("Immediate", $"nt.mode.v1:Immediate:{version}"),
                new TelegramAssistantAction("Digest", $"nt.mode.v1:Digest:{version}"),
                new TelegramAssistantAction("Notice+", $"nt.sev.v1:Notice:{version}"),
                new TelegramAssistantAction("Important+", $"nt.sev.v1:Important:{version}"),
                new TelegramAssistantAction("Reset", $"nt.reset.v1:default:{version}")
            ]);
    }

    private static string LocalizeNotificationError(string message)
    {
        if (message.Contains("callback-malformed", StringComparison.OrdinalIgnoreCase))
            return "دکمه تنظیمات اعلان نامعتبر است. دوباره /notifications را باز کنید.";
        if (message.Contains("callback-stale", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("version", StringComparison.OrdinalIgnoreCase))
            return "تنظیمات اعلان تغییر کرده است. دوباره /notifications را باز کنید.";
        if (message.Contains("timezone", StringComparison.OrdinalIgnoreCase))
            return "منطقه زمانی معتبر نیست. یک شناسه مانند Asia/Tehran وارد کنید.";
        if (message.Contains("Quiet-hours", StringComparison.OrdinalIgnoreCase))
            return "بازه ساعات سکوت معتبر نیست؛ زمان شروع و پایان باید متفاوت و هر دو مشخص باشند.";
        if (message.Contains("Daily cap", StringComparison.OrdinalIgnoreCase))
            return "سقف روزانه باید بین ۱ تا ۱۰۰ باشد.";
        if (message.Contains("Cooldown", StringComparison.OrdinalIgnoreCase))
            return "فاصله تکرار اعلان باید بین صفر تا ۱۴۴۰ دقیقه باشد.";
        if (message.Contains("plan", StringComparison.OrdinalIgnoreCase))
            return "طرح اشتراک فعال اجازه مدیریت یا ارسال اعلان تلگرام را نمی‌دهد.";
        return $"تنظیمات اعلان نامعتبر است: {message}";
    }

    private async Task<TelegramAssistantResult> HandleAlertsCommandAsync(
        string text,
        TelegramAssistantUpdate update,
        CurrentActor actor,
        CancellationToken cancellationToken)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var symbol = parts.Length >= 2 ? parts[1] : null;
        var page = await alertHistoryUseCases.GetHistoryAsync(new AlertHistoryQuery(
            actor, PageSize: 5, SymbolKey: symbol), cancellationToken);
        if (page.Items.Count == 0)
            return BuildResult(TelegramAssistantResultStatus.Accepted, actor.ActorId, actor.TenantId,
                null, update.CorrelationId, "No alert history records were found for your account.");

        var builder = new StringBuilder("Alert history:\n");
        foreach (var item in page.Items)
        {
            builder.AppendLine($"- {item.CreatedAtUtc:yyyy-MM-dd HH:mm} {item.SymbolKey} {item.EventType} [{item.DeliveryStatus}]");
            builder.AppendLine($"  id: {item.Id:N}");
            builder.AppendLine($"  why: {BoundedTelegram(item.WhyText, 220)}");
        }
        builder.AppendLine("Use /alert ALERT_ID for immutable evidence, why text, reactions, dismiss and mute actions.");
        return BuildResult(TelegramAssistantResultStatus.Accepted, actor.ActorId, actor.TenantId,
            null, update.CorrelationId, builder.ToString(),
            page.Items.Take(5).Select(item => new TelegramAssistantAction(
                $"{item.SymbolKey} details", $"ah.detail.v1:{item.Id:N}")).ToArray());
    }

    private async Task<TelegramAssistantResult> HandleAlertDetailCommandAsync(
        string text,
        TelegramAssistantUpdate update,
        CurrentActor actor,
        CancellationToken cancellationToken)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !Guid.TryParse(parts[1], out var alertId))
            return BuildResult(TelegramAssistantResultStatus.ValidationError, actor.ActorId, actor.TenantId,
                null, update.CorrelationId, "Usage: /alert ALERT_ID");
        return await RenderAlertDetailAsync(alertId, update, actor, cancellationToken);
    }

    private async Task<TelegramAssistantResult> RenderAlertDetailAsync(
        Guid alertId,
        TelegramAssistantUpdate update,
        CurrentActor actor,
        CancellationToken cancellationToken)
    {
        var detail = await alertHistoryUseCases.GetDetailAsync(actor, alertId, cancellationToken);
        if (detail is null)
            return BuildResult(TelegramAssistantResultStatus.ValidationError, actor.ActorId, actor.TenantId,
                null, update.CorrelationId, "Alert was not found for your account.");

        var builder = new StringBuilder();
        builder.AppendLine($"{detail.Record.SymbolKey} {detail.Record.EventType}");
        builder.AppendLine($"Status: {detail.Record.DeliveryStatus}; reason: {detail.Record.DeliveryReason}");
        builder.AppendLine($"Created: {detail.Record.CreatedAtUtc:O}");
        builder.AppendLine($"Evidence hash: {detail.Record.EvidenceHash}");
        builder.AppendLine($"Why: {BoundedTelegram(detail.Record.WhyText, 900)}");
        foreach (var reaction in detail.Reactions)
            builder.AppendLine($"Reaction {reaction.HorizonCode}: {reaction.Status} - {reaction.Reason}");
        builder.AppendLine("Dismiss affects this record only. Mute changes future notifications and requires confirmation.");

        var id = detail.Record.Id.ToString("N");
        return BuildResult(TelegramAssistantResultStatus.Accepted, actor.ActorId, actor.TenantId,
            null, update.CorrelationId, builder.ToString(),
            [
                new TelegramAssistantAction("Why", $"ah.why.v1:{id}"),
                new TelegramAssistantAction("Dismiss", $"ah.dismiss.v1:{id}"),
                new TelegramAssistantAction("Mute symbol", $"ah.mute.symbol.v1:{id}"),
                new TelegramAssistantAction("Helpful", $"ah.feedback.v1:{id}:helpful"),
                new TelegramAssistantAction("Not useful", $"ah.feedback.v1:{id}:not_useful")
            ]);
    }

    private async Task<TelegramAssistantResult> HandleLatestMarketReportCommandAsync(
        TelegramAssistantUpdate update,
        CurrentActor actor,
        CancellationToken cancellationToken)
    {
        var report = await marketReports.GetLatestPublicAsync(cancellationToken);
        return report is null
            ? BuildResult(TelegramAssistantResultStatus.Unsupported, actor.ActorId, actor.TenantId, null,
                update.CorrelationId, "No published market report is available yet. Open /market-reports in the web app and try again later.")
            : RenderMarketReport(report, actor, update.CorrelationId);
    }

    private async Task<TelegramAssistantResult> HandlePersonalDigestCommandAsync(
        TelegramAssistantUpdate update,
        CurrentActor actor,
        CancellationToken cancellationToken)
    {
        try
        {
            var report = await marketReports.GeneratePersonalAsync(
                new GeneratePersonalDigestCommand(actor, update.CorrelationId, PublishNotification: false),
                cancellationToken);
            return RenderMarketReport(report, actor, update.CorrelationId);
        }
        catch (MarketReportValidationException exception)
        {
            return BuildResult(TelegramAssistantResultStatus.ValidationError, actor.ActorId, actor.TenantId, null,
                update.CorrelationId, exception.Message);
        }
        catch (MarketReportAccessDeniedException exception)
        {
            return BuildResult(TelegramAssistantResultStatus.ValidationError, actor.ActorId, actor.TenantId, null,
                update.CorrelationId, exception.Message);
        }
    }

    private static TelegramAssistantResult RenderMarketReport(
        MarketReportView report,
        CurrentActor actor,
        string correlationId)
    {
        var builder = new StringBuilder();
        builder.AppendLine(report.Scope == FinancialCopilot.Domain.Financial.Reports.MarketReportScope.PersonalDigest
            ? "Personal market digest"
            : "Market report");
        builder.AppendLine($"Status: {report.Status}; revision: {report.Revision}; trading date: {report.TradingDate:yyyy-MM-dd}");
        builder.AppendLine(report.Narrative ?? "The report narrative is not available.");
        builder.AppendLine($"Evidence freshness: {report.Evidence.SourceFreshnessUtc?.ToString("O") ?? "unavailable"}");
        builder.AppendLine($"Confidence: {report.Confidence:P0}");
        builder.AppendLine("Open web: /market-reports/" + report.Id.ToString("N"));
        return BuildResult(
            TelegramAssistantResultStatus.Accepted,
            actor.ActorId,
            actor.TenantId,
            null,
            correlationId,
            builder.ToString(),
            [new TelegramAssistantAction("Sources", $"mreport.sources.v1:{report.Id:N}")]);
    }

    private TelegramAssistantResult HandleScannersCommand(string text, TelegramAssistantUpdate update, CurrentActor actor)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        ProfessionalFilterCategory? category = null;
        var page = 1;
        if (parts.Length > 1 && !int.TryParse(parts[1], out page) &&
            Enum.TryParse<ProfessionalFilterCategory>(parts[1], true, out var parsed)) category = parsed;
        if (parts.Length > 2) int.TryParse(parts[2], out page);
        page = Math.Max(1, page);
        var catalogPage = professionalScanners.ListCatalog(new ProfessionalCatalogQuery(category, null, page, 8));
        var builder = new StringBuilder($"فیلترهای حرفه‌ای — صفحه {catalogPage.Page} از {catalogPage.TotalPages}\n");
        foreach (var item in catalogPage.Items)
            builder.AppendLine($"{item.Code} — {item.TitleFa} ({item.Category})");
        builder.AppendLine("اجرا: /scanner FILTER_CODE key=value");
        var actions = new List<TelegramAssistantAction>();
        if (catalogPage.Page > 1) actions.Add(new("قبلی", $"pf.cat.v1:{category?.ToString() ?? "all"}:{catalogPage.Page - 1}"));
        if (catalogPage.Page < catalogPage.TotalPages) actions.Add(new("بعدی", $"pf.cat.v1:{category?.ToString() ?? "all"}:{catalogPage.Page + 1}"));
        return BuildResult(TelegramAssistantResultStatus.Accepted, actor.ActorId, actor.TenantId, null,
            update.CorrelationId, builder.ToString(), actions);
    }

    private async Task<TelegramAssistantResult> HandleProfessionalScannerCommandAsync(
        string text, TelegramAssistantUpdate update, CurrentActor actor, CancellationToken cancellationToken)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return BuildResult(TelegramAssistantResultStatus.ValidationError, actor.ActorId, actor.TenantId, null,
                update.CorrelationId, "کاربرد: /scanner FILTER_CODE key=value");
        try
        {
            var parameters = ParseKeyValues(parts.Skip(2));
            var result = await professionalScanners.ExecuteAsync(new ProfessionalExecuteCommand(actor, parts[1], null,
                parameters, null, null, new ProfessionalScannerScope(), 1, 8, update.CorrelationId, "Telegram"), cancellationToken);
            return RenderProfessionalScanner(result, actor, update.CorrelationId);
        }
        catch (InvalidOperationException exception)
        {
            return BuildResult(TelegramAssistantResultStatus.ValidationError, actor.ActorId, actor.TenantId, null,
                update.CorrelationId, exception.Message);
        }
    }

    private async Task<TelegramAssistantResult> HandleSaveScannerCommandAsync(
        string text, TelegramAssistantUpdate update, CurrentActor actor, CancellationToken cancellationToken)
    {
        var body = text[(text.IndexOf(' ') + 1)..].Split('|', StringSplitOptions.TrimEntries);
        if (body.Length < 2 || string.IsNullOrWhiteSpace(body[0]) || string.IsNullOrWhiteSpace(body[1]))
            return BuildResult(TelegramAssistantResultStatus.ValidationError, actor.ActorId, actor.TenantId, null,
                update.CorrelationId, "کاربرد: /save_scanner نام | FILTER_CODE | key=value");
        try
        {
            var saved = await professionalScanners.SaveAsync(new SaveProfessionalFilterCommand(actor, body[0], body[1], null,
                body.Length > 2 ? ParseKeyValues(body[2].Split(' ', StringSplitOptions.RemoveEmptyEntries)) : null), cancellationToken);
            return BuildResult(TelegramAssistantResultStatus.Accepted, actor.ActorId, actor.TenantId, null,
                update.CorrelationId, $"فیلتر «{saved.Name}» با نسخه {saved.FilterVersion} ذخیره شد.",
                [new TelegramAssistantAction("اجرا", $"pf.saved.v1:{saved.Id:N}:1")]);
        }
        catch (InvalidOperationException exception)
        {
            return BuildResult(TelegramAssistantResultStatus.ValidationError, actor.ActorId, actor.TenantId, null,
                update.CorrelationId, exception.Message);
        }
    }

    private async Task<TelegramAssistantResult> HandleSavedScannersCommandAsync(
        string text, TelegramAssistantUpdate update, CurrentActor actor, CancellationToken cancellationToken)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var page = parts.Length > 1 && int.TryParse(parts[1], out var parsed) ? Math.Max(1, parsed) : 1;
        var items = await professionalScanners.ListSavedAsync(actor, page, 8, cancellationToken);
        var builder = new StringBuilder($"فیلترهای ذخیره‌شده — صفحه {page}\n");
        foreach (var item in items) builder.AppendLine($"{item.Name} — {item.FilterCode}/{item.FilterVersion} — {item.Id:N}");
        return BuildResult(TelegramAssistantResultStatus.Accepted, actor.ActorId, actor.TenantId, null,
            update.CorrelationId, builder.ToString());
    }

    private static Dictionary<string, string> ParseKeyValues(IEnumerable<string> values)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in values)
        {
            var separator = token.IndexOf('=');
            if (separator <= 0 || separator == token.Length - 1)
                throw new ProfessionalScannerValidationException($"پارامتر نامعتبر است: {token}. قالب key=value را استفاده کنید.");
            result[token[..separator]] = token[(separator + 1)..];
        }
        return result;
    }

    private static TelegramAssistantResult RenderProfessionalScanner(
        ProfessionalScannerExecutionResult result, CurrentActor actor, string correlationId)
    {
        var builder = new StringBuilder($"{result.FilterCode}/{result.FilterVersion} — {result.Status}\n");
        foreach (var row in result.Rows.Take(8))
        {
            builder.AppendLine($"{row.Rank}. {row.Symbol}");
            foreach (var reason in row.Reasons) builder.AppendLine($"  {reason.Text}");
            builder.AppendLine($"  freshness: {row.SourceFreshnessUtc:O}");
        }
        if (result.Rows.Count == 0) builder.AppendLine(string.Join("\n", result.DatasetMessages.DefaultIfEmpty("نتیجه‌ای مطابق شرایط یافت نشد.")));
        builder.AppendLine($"evidence: {result.EvidenceHash}");
        builder.AppendLine($"جدول کامل: /scanners/{result.FilterCode}");
        var actions = new List<TelegramAssistantAction>();
        if (result.Page > 1) actions.Add(new("قبلی", $"pf.run.v1:{result.FilterCode}:{result.Page - 1}"));
        if (result.Page < result.TotalPages) actions.Add(new("بعدی", $"pf.run.v1:{result.FilterCode}:{result.Page + 1}"));
        actions.Add(new("اجرای دوباره", $"pf.run.v1:{result.FilterCode}:{result.Page}"));
        return BuildResult(TelegramAssistantResultStatus.Accepted, actor.ActorId, actor.TenantId, null,
            correlationId, builder.ToString(), actions);
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

    private async Task<TelegramAssistantResult> HandleRadarCommandAsync(
        string text,
        TelegramAssistantUpdate update,
        CurrentActor actor,
        CancellationToken cancellationToken)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var page = parts.Length == 1
            ? 1
            : parts.Length == 2 && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
                ? parsed
                : 0;
        if (page == 0)
            return BuildResult(TelegramAssistantResultStatus.ValidationError, actor.ActorId, actor.TenantId, null,
                update.CorrelationId, "Usage: /radar [page-number]");

        var profile = await radar.GetAsync(new GetMyRadarQuery(actor), cancellationToken);
        return BuildRadarResult(profile, actor, update.CorrelationId, page, "Personal market radar");
    }

    private async Task<TelegramAssistantResult> HandleRadarOverrideCommandAsync(
        string text,
        TelegramAssistantUpdate update,
        CurrentActor actor,
        CancellationToken cancellationToken)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
            return BuildResult(TelegramAssistantResultStatus.ValidationError, actor.ActorId, actor.TenantId, null,
                update.CorrelationId, "Usage: /radar_override <company-id> <broad|balanced|focused|paused|inherit>");

        try
        {
            var current = await radar.GetAsync(new GetMyRadarQuery(actor), cancellationToken);
            var existing = current.SymbolOverrides.SingleOrDefault(item =>
                item.ExternalCompanyId.Equals(parts[1], StringComparison.OrdinalIgnoreCase));
            RadarProfileDto result;
            if (parts[2].Equals("inherit", StringComparison.OrdinalIgnoreCase))
            {
                if (existing is null) throw new RadarValidationException("No override exists for that followed symbol.");
                result = await radar.RemoveOverrideAsync(new RemoveRadarSymbolOverrideCommand(
                    actor, existing.ExternalCompanyId, existing.Version, "Telegram"), cancellationToken);
            }
            else
            {
                var paused = parts[2].Equals("paused", StringComparison.OrdinalIgnoreCase);
                if (!paused && !Enum.TryParse<RadarSensitivity>(parts[2], true, out _))
                    throw new RadarValidationException("Sensitivity must be broad, balanced, focused, paused, or inherit.");
                RadarSensitivity? sensitivity = paused ? null : Enum.Parse<RadarSensitivity>(parts[2], true);
                result = await radar.UpsertOverrideAsync(new UpsertRadarSymbolOverrideCommand(
                    actor, parts[1], existing?.Version ?? 0,
                    new RadarSymbolOverrideInput(paused ? RadarState.Paused : RadarState.Active,
                        null, null, null, sensitivity), "Telegram"), cancellationToken);
            }
            return BuildRadarResult(result, actor, update.CorrelationId, 1, "Radar symbol override updated.");
        }
        catch (InvalidOperationException exception)
        {
            return BuildResult(TelegramAssistantResultStatus.ValidationError, actor.ActorId, actor.TenantId, null,
                update.CorrelationId, exception.Message);
        }
    }

    private async Task<TelegramAssistantResult> HandleRadarCallbackAsync(
        string data,
        TelegramAssistantUpdate update,
        CurrentActor actor,
        CancellationToken cancellationToken)
    {
        var parts = data.Split(':', StringSplitOptions.RemoveEmptyEntries);
        try
        {
            var profile = await radar.GetAsync(new GetMyRadarQuery(actor), cancellationToken);
            if (parts.Length == 3 && int.TryParse(parts[1], out var version) && profile.Version == version)
            {
                RadarProfileInput input;
                if (parts[0].Equals("rd.s1", StringComparison.OrdinalIgnoreCase))
                {
                    var state = parts[2] == "a" ? RadarState.Active : parts[2] == "p" ? RadarState.Paused
                        : throw new RadarValidationException("The radar state callback is malformed.");
                    input = ProfileInput(profile, state: state);
                }
                else if (parts[0].Equals("rd.n1", StringComparison.OrdinalIgnoreCase))
                {
                    var sensitivity = parts[2] == "b" ? RadarSensitivity.Broad : parts[2] == "f"
                        ? RadarSensitivity.Focused : parts[2] == "m" ? RadarSensitivity.Balanced
                        : throw new RadarValidationException("The radar sensitivity callback is malformed.");
                    input = ProfileInput(profile, sensitivity: sensitivity);
                }
                else if (parts[0].Equals("rd.c1", StringComparison.OrdinalIgnoreCase) &&
                         int.TryParse(parts[2], out var eventTypeValue) &&
                         Enum.IsDefined(typeof(InsightType), eventTypeValue))
                {
                    var eventType = (InsightType)eventTypeValue;
                    var categories = profile.EventTypes.Contains(eventType)
                        ? profile.EventTypes.Where(item => item != eventType).ToArray()
                        : profile.EventTypes.Append(eventType).ToArray();
                    if (categories.Length == 0) throw new RadarValidationException("At least one radar category must remain enabled.");
                    input = ProfileInput(profile, eventTypes: categories);
                }
                else throw new RadarValidationException("The radar callback is malformed or stale.");

                var changed = await radar.UpdateAsync(new UpdateMyRadarCommand(actor, version, input, "Telegram"), cancellationToken);
                return BuildRadarResult(changed, actor, update.CorrelationId, 1, "Radar preferences updated.");
            }

            if (parts.Length == 4 && parts[0].Equals("rd.o1", StringComparison.OrdinalIgnoreCase) &&
                Guid.TryParseExact(parts[1], "N", out var overrideId) && int.TryParse(parts[2], out var overrideVersion))
            {
                var symbolOverride = profile.SymbolOverrides.SingleOrDefault(item => item.Id == overrideId)
                    ?? throw new RadarValidationException("The radar override callback is stale.");
                if (symbolOverride.Version != overrideVersion) throw new RadarValidationException("The radar override callback is stale.");
                RadarProfileDto changed;
                if (parts[3] == "x")
                    changed = await radar.RemoveOverrideAsync(new RemoveRadarSymbolOverrideCommand(
                        actor, symbolOverride.ExternalCompanyId, overrideVersion, "Telegram"), cancellationToken);
                else
                {
                    var state = parts[3] == "p" ? RadarState.Paused : parts[3] == "r" ? RadarState.Active
                        : throw new RadarValidationException("The radar override callback is malformed.");
                    changed = await radar.UpsertOverrideAsync(new UpsertRadarSymbolOverrideCommand(
                        actor, symbolOverride.ExternalCompanyId, overrideVersion,
                        new RadarSymbolOverrideInput(state, symbolOverride.EventTypes, symbolOverride.MinimumSeverity,
                            symbolOverride.MinimumImportance, symbolOverride.Sensitivity), "Telegram"), cancellationToken);
                }
                return BuildRadarResult(changed, actor, update.CorrelationId, 1, "Radar symbol override updated.");
            }

            throw new RadarValidationException("The radar callback is malformed or stale.");
        }
        catch (InvalidOperationException exception)
        {
            return BuildResult(TelegramAssistantResultStatus.ValidationError, actor.ActorId, actor.TenantId, null,
                update.CorrelationId, exception.Message);
        }
    }

    private static RadarProfileInput ProfileInput(
        RadarProfileDto profile,
        RadarState? state = null,
        RadarSensitivity? sensitivity = null,
        IReadOnlyCollection<InsightType>? eventTypes = null) =>
        new(eventTypes ?? profile.EventTypes, profile.MinimumSeverity, profile.MinimumImportance,
            sensitivity ?? profile.Sensitivity, profile.DeliveryMode, state ?? profile.State);

    private static TelegramAssistantResult BuildRadarResult(
        RadarProfileDto profile,
        CurrentActor actor,
        string correlationId,
        int page,
        string heading)
    {
        const int pageSize = 5;
        var overrides = profile.SymbolOverrides.OrderBy(item => item.Symbol).Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        var text = $"{heading}\nState: {profile.State} | sensitivity: {profile.Sensitivity} | minimum: {profile.MinimumSeverity}/{profile.MinimumImportance} | v{profile.Version}\n" +
                   $"Categories: {string.Join(", ", profile.EventTypes)}\n{profile.FreshnessDisclosure}";
        if (overrides.Length > 0)
            text += "\nOverrides:\n" + string.Join("\n", overrides.Select(item =>
                $"- {item.Symbol} ({item.ExternalCompanyId}): {item.State}/{item.Sensitivity?.ToString() ?? "inherit"} v{item.Version}"));
        if (profile.SymbolOverrides.Count > pageSize)
            text += $"\nOverride page {page} of {(int)Math.Ceiling(profile.SymbolOverrides.Count / (decimal)pageSize)}.";
        return BuildResult(TelegramAssistantResultStatus.Accepted, actor.ActorId, actor.TenantId, null,
            correlationId, text, BuildRadarActions(profile, overrides));
    }

    private static IReadOnlyList<TelegramAssistantAction> BuildRadarActions(
        RadarProfileDto profile,
        IReadOnlyCollection<RadarSymbolOverrideDto> overrides)
    {
        var version = profile.Version.ToString(CultureInfo.InvariantCulture);
        var actions = new List<TelegramAssistantAction>
        {
            new(profile.State == RadarState.Active ? "Pause radar" : "Enable radar",
                $"rd.s1:{version}:{(profile.State == RadarState.Active ? "p" : "a")}"),
            new("Broad", $"rd.n1:{version}:b"),
            new("Balanced", $"rd.n1:{version}:m"),
            new("Focused", $"rd.n1:{version}:f")
        };
        actions.AddRange(Enum.GetValues<InsightType>().Select(type =>
            new TelegramAssistantAction($"{(profile.EventTypes.Contains(type) ? "✓" : "+")} {type}",
                $"rd.c1:{version}:{(int)type}")));
        foreach (var item in overrides)
        {
            var id = item.Id.ToString("N");
            var overrideVersion = item.Version.ToString(CultureInfo.InvariantCulture);
            actions.Add(new TelegramAssistantAction(item.State == RadarState.Active ? $"Pause {item.Symbol}" : $"Resume {item.Symbol}",
                $"rd.o1:{id}:{overrideVersion}:{(item.State == RadarState.Active ? "p" : "r")}"));
            actions.Add(new TelegramAssistantAction($"Inherit {item.Symbol}", $"rd.o1:{id}:{overrideVersion}:x"));
        }
        return actions;
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

    private async Task<TelegramAssistantResult> HandleAlertHistoryCallbackAsync(
        string data,
        TelegramAssistantUpdate update,
        CurrentActor actor,
        CancellationToken cancellationToken)
    {
        var parts = data.Split(':');
        if (parts.Length < 2 || !Guid.TryParseExact(parts[1], "N", out var alertId))
            return BuildResult(TelegramAssistantResultStatus.ValidationError, actor.ActorId, actor.TenantId,
                null, update.CorrelationId, "Alert callback is malformed or stale.");

        if (data.StartsWith("ah.detail.v1:", StringComparison.OrdinalIgnoreCase))
            return await RenderAlertDetailAsync(alertId, update, actor, cancellationToken);
        if (data.StartsWith("ah.why.v1:", StringComparison.OrdinalIgnoreCase))
        {
            var why = await alertHistoryUseCases.GetWhyAsync(actor, alertId, cancellationToken);
            return why is null
                ? BuildResult(TelegramAssistantResultStatus.ValidationError, actor.ActorId, actor.TenantId,
                    null, update.CorrelationId, "Alert was not found for your account.")
                : BuildResult(TelegramAssistantResultStatus.Accepted, actor.ActorId, actor.TenantId,
                    null, update.CorrelationId,
                    $"{why.WhyText}\n\nEvidence hash: {why.EvidenceHash}\nMethodology: {why.Methodology}");
        }
        if (data.StartsWith("ah.dismiss.v1:", StringComparison.OrdinalIgnoreCase))
        {
            var detail = await alertHistoryUseCases.DismissAsync(new DismissAlertCommand(
                actor, alertId, update.CorrelationId), cancellationToken);
            return detail is null
                ? BuildResult(TelegramAssistantResultStatus.ValidationError, actor.ActorId, actor.TenantId,
                    null, update.CorrelationId, "Alert was not found for your account.")
                : BuildResult(TelegramAssistantResultStatus.Accepted, actor.ActorId, actor.TenantId,
                    null, update.CorrelationId, "Alert dismissed. This does not mute future notifications.");
        }
        if (data.StartsWith("ah.mute.symbol.v1:", StringComparison.OrdinalIgnoreCase))
        {
            var detail = await alertHistoryUseCases.MuteAsync(new MuteAlertCommand(
                actor, alertId, "Symbol", Confirmed: true, update.CorrelationId), cancellationToken);
            return detail is null
                ? BuildResult(TelegramAssistantResultStatus.ValidationError, actor.ActorId, actor.TenantId,
                    null, update.CorrelationId, "Alert was not found for your account.")
                : BuildResult(TelegramAssistantResultStatus.Accepted, actor.ActorId, actor.TenantId,
                    null, update.CorrelationId, $"Future notifications for {detail.Record.SymbolKey} were muted.");
        }
        if (data.StartsWith("ah.feedback.v1:", StringComparison.OrdinalIgnoreCase) && parts.Length == 3)
        {
            var detail = await alertHistoryUseCases.RecordFeedbackAsync(new FeedbackAlertCommand(
                actor, alertId, parts[2], update.CorrelationId), cancellationToken);
            return detail is null
                ? BuildResult(TelegramAssistantResultStatus.ValidationError, actor.ActorId, actor.TenantId,
                    null, update.CorrelationId, "Alert was not found for your account.")
                : BuildResult(TelegramAssistantResultStatus.Accepted, actor.ActorId, actor.TenantId,
                    null, update.CorrelationId, "Alert feedback recorded.");
        }

        return BuildResult(TelegramAssistantResultStatus.Unsupported, actor.ActorId, actor.TenantId,
            null, update.CorrelationId, "Alert callback operation is not supported.");
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

        if (data is not null && data.StartsWith("rd.", StringComparison.OrdinalIgnoreCase))
        {
            return await HandleRadarCallbackAsync(data, update, actor, cancellationToken);
        }

        if (data is not null && data.StartsWith("nt.", StringComparison.OrdinalIgnoreCase))
        {
            return await HandleNotificationSettingsCallbackAsync(data, update, actor, cancellationToken);
        }

        if (data is not null && data.StartsWith("ah.", StringComparison.OrdinalIgnoreCase))
        {
            return await HandleAlertHistoryCallbackAsync(data, update, actor, cancellationToken);
        }

        if (data is not null && data.StartsWith("pf.cat.v1:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = data.Split(':');
            if (parts.Length == 3 && int.TryParse(parts[2], out var page))
            {
                var category = parts[1].Equals("all", StringComparison.OrdinalIgnoreCase) ? string.Empty : parts[1];
                return HandleScannersCommand($"/scanners {category} {page}", update, actor);
            }
        }

        if (data is not null && data.StartsWith("pf.run.v1:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = data.Split(':');
            if (parts.Length == 3 && int.TryParse(parts[2], out var page))
            {
                try
                {
                    var result = await professionalScanners.ExecuteAsync(new ProfessionalExecuteCommand(actor, parts[1], null,
                        null, null, null, new ProfessionalScannerScope(), Math.Max(1, page), 8,
                        update.CorrelationId, "TelegramCallback"), cancellationToken);
                    return RenderProfessionalScanner(result, actor, update.CorrelationId);
                }
                catch (InvalidOperationException exception)
                {
                    return BuildResult(TelegramAssistantResultStatus.ValidationError, actor.ActorId, actor.TenantId,
                        null, update.CorrelationId, exception.Message);
            }
        }
    }

        if (data is not null && data.StartsWith("pf.saved.v1:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = data.Split(':');
            if (parts.Length == 3 && Guid.TryParseExact(parts[1], "N", out var savedId) && int.TryParse(parts[2], out var page))
            {
                try
                {
                    var result = await professionalScanners.RunSavedAsync(new RunSavedProfessionalFilterCommand(actor,
                        savedId, null, null, new ProfessionalScannerScope(), Math.Max(1, page), 8,
                        update.CorrelationId, "TelegramCallback"), cancellationToken);
                    return RenderProfessionalScanner(result, actor, update.CorrelationId);
                }
                catch (InvalidOperationException exception)
                {
                    return BuildResult(TelegramAssistantResultStatus.ValidationError, actor.ActorId, actor.TenantId,
                        null, update.CorrelationId, exception.Message);
                }
            }
        }

        const string buyPrefix = "bp.buy.v1:";
        if (data is not null && data.StartsWith(buyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return await CreateTelegramCheckoutAsync(data[buyPrefix.Length..], update, actor, cancellationToken);
        }

        const string reportSourcesPrefix = "mreport.sources.v1:";
        if (data is not null &&
            data.StartsWith(reportSourcesPrefix, StringComparison.OrdinalIgnoreCase) &&
            Guid.TryParseExact(data[reportSourcesPrefix.Length..], "N", out var reportId))
        {
            var report = await marketReports.GetPersonalVersionAsync(actor, reportId, cancellationToken)
                ?? await marketReports.GetPublicVersionAsync(reportId, cancellationToken);
            if (report is not null)
            {
                var sources = new StringBuilder("Report evidence sources:\n");
                foreach (var item in report.Evidence.Items.Take(12))
                    sources.AppendLine($"- {item.Id}: {item.Source}; freshness {item.FreshnessUtc?.ToString("O") ?? "unavailable"}");
                sources.AppendLine($"Evidence hash: {report.EvidenceHash}");
                return BuildResult(TelegramAssistantResultStatus.Accepted, actor.ActorId, actor.TenantId, null,
                    update.CorrelationId, sources.ToString());
            }
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
            update.CorrelationId,
            RenderVersion: persisted.RenderVersion);
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
            result.Messages,
            result.RenderVersion);

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

    private static string BoundedTelegram(string value, int maximumLength)
    {
        if (value.Length <= maximumLength) return value;
        return value[..Math.Max(0, maximumLength - 1)] + "…";
    }

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
        IReadOnlyList<TelegramAssistantRenderedMessage> Messages,
        string RenderVersion = "telegram-render-v1");
}
