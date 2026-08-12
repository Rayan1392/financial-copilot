using FinancialCopilot.Application.AI.ModelProviders;

namespace FinancialCopilot.Application.AI.Orchestration;

public enum DialogueOutcome
{
    Answered,
    PartialAnswer,
    ClarificationNeeded,
    DisambiguationNeeded,
    NoData,
    Unsupported,
    TemporarilyUnavailable,
    Failed
}

public static class DialogueOutcomeReasonCodes
{
    public const string None = "none";
    public const string CapabilityNotRecognized = "capability_not_recognized";
    public const string RequiredInputMissing = "required_input_missing";
    public const string EntityAmbiguous = "entity_ambiguous";
    public const string EntityNotFound = "entity_not_found";
    public const string SupportedButNoRows = "supported_but_no_rows";
    public const string DataStaleOrIneligible = "data_stale_or_ineligible";
    public const string PartialEvidence = "partial_evidence";
    public const string ProviderOrToolTimeout = "provider_or_tool_timeout";
    public const string ProviderOrToolFailure = "provider_or_tool_failure";
    public const string ResponseValidationFailed = "response_validation_failed";
    public const string LanguageGuardApplied = "language_guard_applied";
    public const string DifferentIndustries = "different_industries";
    public const string InvalidIndustryMembership = "invalid_industry_membership";
    public const string ResultLimitExceeded = "result_limit_exceeded";
}

public static class AiDialogueOutcomePolicy
{
    public static string DetectReplyLanguage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "en";

        var persianScriptCount = message.Count(character =>
            character is >= '\u0600' and <= '\u06ff' or
            >= '\u0750' and <= '\u077f' or
            >= '\u08a0' and <= '\u08ff');

        return persianScriptCount > 0 ? "fa" : "en";
    }

    public static DialogueOutcomeResult Determine(
        string message,
        DetectedIntent intent,
        bool clarificationRequired,
        string? clarificationMessage,
        bool hasStructuredResult,
        bool hasData,
        bool hasUnresolvedEntity = false,
        bool hasPartialEvidence = false,
        bool hasStaleOrIneligibleData = false)
    {
        var language = DetectReplyLanguage(message);

        if (hasUnresolvedEntity && clarificationRequired)
        {
            return new DialogueOutcomeResult(
                DialogueOutcome.DisambiguationNeeded,
                DialogueOutcomeReasonCodes.EntityAmbiguous,
                language,
                null,
                false);
        }

        if (clarificationRequired)
        {
            return new DialogueOutcomeResult(
                DialogueOutcome.ClarificationNeeded,
                DialogueOutcomeReasonCodes.RequiredInputMissing,
                language,
                clarificationMessage,
                false);
        }

        if (hasUnresolvedEntity)
        {
            return new DialogueOutcomeResult(
                DialogueOutcome.DisambiguationNeeded,
                DialogueOutcomeReasonCodes.EntityNotFound,
                language,
                clarificationMessage,
                false);
        }

        if (hasPartialEvidence)
        {
            return new DialogueOutcomeResult(
                DialogueOutcome.PartialAnswer,
                DialogueOutcomeReasonCodes.PartialEvidence,
                language,
                null,
                false);
        }

        if (hasStructuredResult && !hasData)
        {
            return new DialogueOutcomeResult(
                DialogueOutcome.NoData,
                hasStaleOrIneligibleData
                    ? DialogueOutcomeReasonCodes.DataStaleOrIneligible
                    : DialogueOutcomeReasonCodes.SupportedButNoRows,
                language,
                null,
                false);
        }

        if (!hasStructuredResult && intent == DetectedIntent.Unknown)
        {
            return new DialogueOutcomeResult(
                DialogueOutcome.Unsupported,
                DialogueOutcomeReasonCodes.CapabilityNotRecognized,
                language,
                null,
                false);
        }

        return new DialogueOutcomeResult(
            DialogueOutcome.Answered,
            DialogueOutcomeReasonCodes.None,
            language,
            null,
            false);
    }

    public static DialogueOutcomeResult FromException(string message, Exception exception)
    {
        var language = DetectReplyLanguage(message);
        var (outcome, reason) = exception switch
        {
            OperationCanceledException or TimeoutException =>
                (DialogueOutcome.TemporarilyUnavailable, DialogueOutcomeReasonCodes.ProviderOrToolTimeout),
            AiModelProviderException { Status: AiExecutionStatus.TimedOut } =>
                (DialogueOutcome.TemporarilyUnavailable, DialogueOutcomeReasonCodes.ProviderOrToolTimeout),
            AiModelProviderException { Status: AiExecutionStatus.InvalidStructuredOutput } =>
                (DialogueOutcome.Failed, DialogueOutcomeReasonCodes.ResponseValidationFailed),
            AiModelProviderException { Status: AiExecutionStatus.CapabilityUnavailable or AiExecutionStatus.RuntimeUnavailable } =>
                (DialogueOutcome.TemporarilyUnavailable, DialogueOutcomeReasonCodes.ProviderOrToolFailure),
            AiModelProviderException =>
                (DialogueOutcome.Failed, DialogueOutcomeReasonCodes.ProviderOrToolFailure),
            _ => (DialogueOutcome.Failed, DialogueOutcomeReasonCodes.ProviderOrToolFailure)
        };

        return new DialogueOutcomeResult(outcome, reason, language, null, false);
    }

    public static string ComposeSystemMessage(
        DialogueOutcomeResult outcome,
        string? safeDetail = null)
    {
        var detail = IsCompatibleWithLanguage(safeDetail, outcome.ReplyLanguage)
            ? safeDetail!.Trim()
            : null;

        if (outcome.Outcome == DialogueOutcome.ClarificationNeeded && detail is not null)
            return detail;

        return outcome.ReplyLanguage == "fa"
            ? ComposePersian(outcome, detail)
            : ComposeEnglish(outcome, detail);
    }

    public static bool IsCompatibleWithLanguage(string? value, string language)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var containsPersian = value.Any(character =>
            character is >= '\u0600' and <= '\u06ff' or
            >= '\u0750' and <= '\u077f' or
            >= '\u08a0' and <= '\u08ff');

        return string.Equals(language, "fa", StringComparison.OrdinalIgnoreCase)
            ? containsPersian
            : !containsPersian;
    }

    private static string ComposePersian(DialogueOutcomeResult outcome, string? detail) =>
        outcome.Outcome switch
        {
            DialogueOutcome.ClarificationNeeded when outcome.ReasonCode == DialogueOutcomeReasonCodes.DifferentIndustries => "این دو نماد متعلق به صنایع مختلف هستند. کدام را نسبت به صنعت خودش بررسی کنم؟",
            DialogueOutcome.ClarificationNeeded when outcome.ReasonCode == DialogueOutcomeReasonCodes.InvalidIndustryMembership => "این نماد عضو صنعت انتخاب‌شده نیست. لطفاً نماد یا صنعت درست را مشخص کنید.",
            DialogueOutcome.ClarificationNeeded => detail
                ?? "برای پاسخ دقیق، لطفاً اطلاعات بیشتری درباره نماد، معیار یا بازه موردنظر بنویسید.",
            DialogueOutcome.DisambiguationNeeded =>
                "نام نماد یا شرکت به‌طور قطعی مشخص نشد. لطفاً نام نماد دقیق را وارد کنید.",
            DialogueOutcome.NoData =>
                detail
                ?? "درخواست شما پشتیبانی می‌شود، اما داده قابل استفاده‌ای برای آن در بازه موجود پیدا نشد.",
            DialogueOutcome.Unsupported =>
                "این پرسش را نمی‌توانم به‌صورت قابل اتکا پاسخ بدهم. می‌توانم نمادها را با معیارهای مالی فیلتر کنم، شاخص‌های یک نماد را نشان بدهم، روند فروش ماهانه را نمایش دهم یا تحلیل‌های ثبت‌شده را ارائه کنم.",
            DialogueOutcome.TemporarilyUnavailable =>
                "این قابلیت موقتاً در دسترس نیست. لطفاً کمی بعد دوباره تلاش کنید.",
            DialogueOutcome.Failed =>
                "در پردازش پرسش خطایی رخ داد. لطفاً دوباره تلاش کنید.",
            DialogueOutcome.PartialAnswer =>
                detail ?? "بخشی از اطلاعات درخواست‌شده در دسترس بود و همان بخش نمایش داده شد.",
            _ => detail ?? "پاسخ آماده است."
        };

    private static string ComposeEnglish(DialogueOutcomeResult outcome, string? detail) =>
        outcome.Outcome switch
        {
            DialogueOutcome.ClarificationNeeded => detail
                ?? "Please provide more information about the symbol, metric, or period you want.",
            DialogueOutcome.DisambiguationNeeded =>
                "I could not identify the symbol or company with enough confidence. Please provide the exact symbol.",
            DialogueOutcome.NoData =>
                detail
                ?? "This request is supported, but no usable data was found for the available period.",
            DialogueOutcome.Unsupported =>
                "I cannot answer this question reliably yet. I can screen symbols by financial conditions, show metrics for a symbol, display monthly sales trends, or provide stored analysis posts.",
            DialogueOutcome.TemporarilyUnavailable =>
                "This capability is temporarily unavailable. Please try again shortly.",
            DialogueOutcome.Failed =>
                "The request could not be processed. Please try again.",
            DialogueOutcome.PartialAnswer =>
                detail ?? "Only part of the requested information was available, so I displayed that part.",
            _ => detail ?? "The answer is ready."
        };

    public static DialogueOutcomeResult ApplyLanguageGuard(
        DialogueOutcomeResult outcome,
        string? candidateDetail)
    {
        var guardApplied = !string.IsNullOrWhiteSpace(candidateDetail) &&
                           !IsCompatibleWithLanguage(candidateDetail, outcome.ReplyLanguage);
        return guardApplied
            ? outcome with { LanguageGuardApplied = true }
            : outcome;
    }
}

public sealed record DialogueOutcomeResult(
    DialogueOutcome Outcome,
    string ReasonCode,
    string ReplyLanguage,
    string? SafeDetail,
    bool LanguageGuardApplied);
