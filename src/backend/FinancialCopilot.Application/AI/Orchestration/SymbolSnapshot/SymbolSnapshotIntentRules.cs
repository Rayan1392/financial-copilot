namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>Decides whether a message can be a bare ticker. Whether the ticker exists is the resolver's job.</summary>
public static class SymbolSnapshotIntentRules
{
    public const int MaxMessageLength = 40;

    private static readonly HashSet<string> LeadingFillers = new(StringComparer.Ordinal) { "نماد", "سهم", "سهام" };

    /// <summary>Returns the compact key to look up, or null when the message cannot be a bare ticker.</summary>
    public static string? ExtractCompactKey(string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > MaxMessageLength)
            return null;

        var words = QueryNormalization.Normalize(message).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return null;

        // A lone «نماد» stays a candidate: it is also an eligible ticker.
        var tickerWords = words.Length > 1 && LeadingFillers.Contains(words[0]) ? words[1..] : words;
        return string.Concat(tickerWords);
    }

    public static string CompactKey(string? symbol) =>
        QueryNormalization.Normalize(symbol).Replace(" ", string.Empty, StringComparison.Ordinal);

    public static QueryInterpretation BuildInterpretation(string message, string symbol, int registryVersion)
    {
        const decimal confidence = 1m;
        var evidence = new InterpretationEvidence(SymbolSnapshotCapability.Code, "bare-ticker", QueryValueProvenance.UserExplicit);
        return new QueryInterpretation(
            message,
            QueryNormalization.Normalize(message),
            "fa",
            [new CapabilityCandidate(SymbolSnapshotCapability.Code, registryVersion, confidence, [evidence])],
            [new EntityMention(symbol, 0, symbol.Length)],
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
}
