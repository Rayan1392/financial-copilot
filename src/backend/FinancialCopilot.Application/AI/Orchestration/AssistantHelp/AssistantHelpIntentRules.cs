namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>
/// Decides whether a whole message is a greeting, a request to introduce the assistant, or a question about what can
/// be asked. Matching is exact on a compact key, so a greeting joined to a real question never matches.
/// </summary>
public static class AssistantHelpIntentRules
{
    public const int MaxMessageLength = 80;

    private static readonly HashSet<string> Greetings = Keys(
        "سلام", "سلام علیکم", "درود", "درود بر شما", "وقت بخیر", "وقت به خیر", "روز بخیر", "صبح بخیر", "ظهر بخیر", "عصر بخیر",
        "سلام وقت بخیر", "سلام صبح بخیر", "سلام روز بخیر", "سلام خسته نباشید", "خسته نباشید", "سلام خوبی", "سلام چطوری",
        "سلام ساپیو", "سلام دستیار", "hi", "hello", "hey");

    private static readonly HashSet<string> Introductions = Keys(
        "خودت را معرفی کن", "خودت رو معرفی کن", "خودتو معرفی کن",
        "خودت را معرفی کن و بگو چه کارهایی بلدی", "خودت رو معرفی کن و بگو چه کارهایی بلدی", "خودتو معرفی کن و بگو چه کارهایی بلدی",
        "خودت را معرفی کن و بگو چه کارهایی میتوانی انجام بدهی",
        "خودت را معرفی کن و بگو چیکار میکنی", "خودت رو معرفی کن و بگو چیکار میکنی", "خودتو معرفی کن و بگو چیکار میکنی",
        "خودت را معرفی کن و بگو چیکار میتونی بکنی", "خودت رو معرفی کن و بگو چیکار میتونی بکنی", "خودتو معرفی کن و بگو چیکار میتونی بکنی",
        "کی هستی", "تو کی هستی", "تو چی هستی", "شما کی هستید",
        "چیکار میکنی", "چکار میکنی", "چه کار میکنی", "چیکار میتونی بکنی", "چکار میتونی بکنی", "چه کاری میتونی بکنی",
        "چه کارهایی میتونی بکنی", "چه کارهایی میتوانی انجام بدهی", "چه کارهایی بلدی", "چه کارایی بلدی", "چی بلدی", "چیکار بلدی",
        "who are you", "introduce yourself", "what can you do", "what do you do");

    private static readonly HashSet<string> FollowUps = Keys(
        "چی میتونم بپرسم", "چی میتوانم بپرسم", "چی میشه پرسید", "چی میشه بپرسم", "چی بپرسم", "چی رو بپرسم", "چی را بپرسم",
        "چه چیزی میتونم بپرسم", "چه چیزی میتوانم بپرسم", "چه سوالی میتونم بپرسم", "چه سوالی میتوانم بپرسم", "چه سوالی بپرسم",
        "چه سوالایی میتونم بپرسم", "چه سوالهایی میتونم بپرسم", "چه سوالاتی میتونم بپرسم", "چه سوالاتی میشه پرسید",
        "از تو چی میتونم بپرسم", "ازت چی میتونم بپرسم", "ازت چی بپرسم", "از شما چی میتونم بپرسم", "از شما چه سوالی میتونم بپرسم",
        "درباره چی میتونم بپرسم", "راجع به چی میتونم بپرسم", "راهنما", "راهنمایی", "help",
        "what can i ask", "what can i ask you", "what should i ask");

    private static readonly HashSet<string> LeadingWords = Words("سلام", "درود", "خب", "خوب", "پس", "باشه", "ببخشید", "لطفا", "لطفاً");

    // Only the introduction pattern tolerates this adverb.
    private static readonly HashSet<string> IntroductionAdverbs = Words("کامل", "کاملا", "کاملاً");

    public static bool IsMatch(string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > MaxMessageLength)
            return false;

        var words = Tokens(message);
        if (Greetings.Contains(Compact(words)))
            return true;

        var remaining = words.SkipWhile(LeadingWords.Contains).ToArray();
        if (remaining.Length == 0)
            return false;

        return FollowUps.Contains(Compact(remaining)) ||
               Introductions.Contains(Compact(remaining.Where(word => !IntroductionAdverbs.Contains(word))));
    }

    public static QueryInterpretation BuildInterpretation(string message, int registryVersion)
    {
        const decimal confidence = 1m;
        var evidence = new InterpretationEvidence(AssistantHelpCapability.Code, "whole-message-help-phrase", QueryValueProvenance.UserExplicit);
        return new QueryInterpretation(
            message,
            QueryNormalization.Normalize(message),
            AiDialogueOutcomePolicy.DetectReplyLanguage(message),
            [new CapabilityCandidate(AssistantHelpCapability.Code, registryVersion, confidence, [evidence])],
            [],
            [],
            null,
            null,
            null,
            [],
            [],
            confidence,
            [evidence],
            registryVersion,
            InterpretationConfidencePolicy.Band(confidence));
    }

    private static string[] Tokens(string text) =>
        QueryNormalization.Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static string Compact(IEnumerable<string> words) =>
        string.Concat(words).Replace('ؤ', 'و');

    private static HashSet<string> Keys(params string[] phrases) =>
        phrases.Select(phrase => Compact(Tokens(phrase))).ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> Words(params string[] words) =>
        words.Select(QueryNormalization.Normalize).ToHashSet(StringComparer.Ordinal);
}
