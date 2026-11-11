namespace FinancialCopilot.Application.AI.Orchestration;

public static class UnavailableTopicCapability
{
    public const string Code = "unavailable_topic";
}

/// <summary>One topic the assistant cannot answer yet, with the reason code that selects its fixed reply.</summary>
public sealed record UnavailableTopic(
    string Topic,
    string ReasonCode,
    IReadOnlyList<string[]> Phrases,
    IReadOnlyList<UnavailableTopicPairRule> PairRules);

/// <summary>Both the <see cref="Phrase"/> and one of the <see cref="AnyWord"/> words must appear.</summary>
public sealed record UnavailableTopicPairRule(string[] Phrase, IReadOnlyCollection<string> AnyWord);

/// <summary>
/// Feature 150 change «unavailable-capability-refusals-and-answer-correctness» (R1–R3): questions about technical
/// analysis, valuation, and investment advice, which no capability answers. Matching is on whole words of
/// <see cref="QueryNormalization.Normalize"/> output (ZWNJ becomes a space, Arabic letters become Persian), so
/// «پیوت‌های» matches «پیوت» and «قیمت فولاد» never matches «قیمت تعادلی». Only the reported phrases are listed (OQ-069).
/// When a later feature answers a topic, delete its row and its reply.
/// </summary>
public static class UnavailableTopicCatalog
{
    public const string TechnicalAnalysisReasonCode = "technical_analysis_unavailable";
    public const string ValuationReasonCode = "valuation_unavailable";
    public const string InvestmentAdviceReasonCode = "investment_advice_unavailable";

    public static readonly IReadOnlyList<UnavailableTopic> Topics =
    [
        new("valuation", ValuationReasonCode,
            [["قیمت", "تعادلی"], ["ارزش", "ذاتی"], ["equilibrium", "price"], ["intrinsic", "value"]],
            []),
        new("investment_advice", InvestmentAdviceReasonCode,
            [["ارزش", "خرید"]],
            [new(["این", "قیمت"], new HashSet<string>(StringComparer.Ordinal) { "خوبه", "مناسب" })]),
        new("technical_analysis", TechnicalAnalysisReasonCode,
            [["تکنیکال"], ["پیوت"], ["technical", "analysis"], ["pivot"]],
            [])
    ];

    /// <summary>The first matching topic in catalog order, or null.</summary>
    public static UnavailableTopic? Match(string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > MaxMessageLength)
            return null;

        var words = QueryNormalization.Normalize(message).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return null;

        return Topics.FirstOrDefault(topic =>
            topic.Phrases.Any(phrase => ContainsSequence(words, phrase)) ||
            topic.PairRules.Any(rule => ContainsSequence(words, rule.Phrase) && words.Any(rule.AnyWord.Contains)));
    }

    public static QueryInterpretation BuildInterpretation(string message, UnavailableTopic topic, int registryVersion)
    {
        const decimal confidence = 1m;
        var evidence = new InterpretationEvidence(UnavailableTopicCapability.Code, $"unavailable-topic:{topic.Topic}", QueryValueProvenance.UserExplicit);
        return new QueryInterpretation(
            message,
            QueryNormalization.Normalize(message),
            AiDialogueOutcomePolicy.DetectReplyLanguage(message),
            [new CapabilityCandidate(UnavailableTopicCapability.Code, registryVersion, confidence, [evidence])],
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

    /// <summary>
    /// BUG-020 (owner, 2026-11-11): while comprehensive analysis is available, a technical question about one exact ticker
    /// asks TahlilApp for its technical analysis instead of being refused. The frame enricher adds the technical topic.
    /// </summary>
    public static QueryInterpretation BuildTechnicalAnalysisInterpretation(string message, string symbol, int registryVersion) =>
        BuildComprehensiveAnalysisInterpretation(message, symbol, registryVersion, "technical-analysis-topic");

    /// <summary>
    /// BUG-022 (owner, 2026-11-11): «تحلیل X», «تحلیل جامع X» and «تحلیل بنیادی X» take the comprehensive-analysis route
    /// deterministically, so the model's proposal cannot send one of them elsewhere.
    /// </summary>
    public static QueryInterpretation BuildAnalysisRequestInterpretation(string message, string symbol, int registryVersion) =>
        BuildComprehensiveAnalysisInterpretation(message, symbol, registryVersion, "analysis-request");

    /// <summary>
    /// BUG-022: the company text of a message made only of analysis words («تحلیل», «تحلیل جامع», «تحلیل بنیادی», the
    /// technical words, the plural «ها»/«های») and one company. Null when «تحلیل» is missing or nothing else remains.
    /// Whether the rest is exactly one company is decided by the caller's entity resolution.
    /// </summary>
    public static string? AnalysisRequestCompanyText(string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > MaxMessageLength)
            return null;

        var words = QueryNormalization.Normalize(message).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!words.Contains(AnalysisWord, StringComparer.Ordinal))
            return null;

        var rest = words.Where(word => !AnalysisRequestWords.Contains(word)).ToArray();
        return rest.Length == 0 ? null : string.Join(' ', rest);
    }

    private const string AnalysisWord = "تحلیل";

    private static readonly HashSet<string> AnalysisRequestWords =
        new(StringComparer.Ordinal) { AnalysisWord, "جامع", "بنیادی", "تکنیکال", "پیوت", "ها", "های" };

    private static QueryInterpretation BuildComprehensiveAnalysisInterpretation(
        string message, string symbol, int registryVersion, string evidenceValue)
    {
        const decimal confidence = 1m;
        const string code = CapabilityExecutionRoutes.ComprehensiveAnalysis;
        var evidence = new InterpretationEvidence(code, evidenceValue, QueryValueProvenance.UserExplicit);
        var start = Math.Max(0, message.IndexOf(symbol, StringComparison.Ordinal));
        return new QueryInterpretation(
            message,
            QueryNormalization.Normalize(message),
            AiDialogueOutcomePolicy.DetectReplyLanguage(message),
            [new CapabilityCandidate(code, registryVersion, confidence, [evidence])],
            [new EntityMention(symbol, start, symbol.Length)],
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

    /// <summary>The message without the catalog's topic words (and the plural «ها»/«های»), for company resolution.</summary>
    public static string WithoutTopicWords(string message)
    {
        var topicWords = Topics
            .SelectMany(topic => topic.Phrases.SelectMany(phrase => phrase).Concat(topic.PairRules.SelectMany(rule => rule.Phrase.Concat(rule.AnyWord))))
            .Concat(["ها", "های", "تحلیل"])
            .ToHashSet(StringComparer.Ordinal);
        return string.Join(' ', QueryNormalization.Normalize(message)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => !topicWords.Contains(word)));
    }

    // The same bound the interpreters apply to a chat message.
    private const int MaxMessageLength = 4000;

    private static bool ContainsSequence(string[] words, string[] phrase)
    {
        for (var start = 0; start + phrase.Length <= words.Length; start++)
        {
            var matched = true;
            for (var offset = 0; offset < phrase.Length && matched; offset++)
                matched = string.Equals(words[start + offset], phrase[offset], StringComparison.Ordinal);
            if (matched)
                return true;
        }

        return false;
    }
}
