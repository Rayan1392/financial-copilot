namespace FinancialCopilot.Application.AI.Orchestration;

using FinancialCopilot.Application.AI.Evaluation;

public enum SuggestedActionKind { FillSlot, ChooseEntity, Retry, RunRelatedCapability, ShowCapabilityHelp, RephraseExample }

public sealed record SuggestedAction(
    string Id,
    SuggestedActionKind Kind,
    string LocalizedLabel,
    string Message,
    string CapabilityCode,
    IReadOnlyDictionary<string, string> PresetSlots,
    string RelevanceReason,
    int RegistryVersion);

public sealed record CapabilityHelpSummary(string CapabilityCode, string LocalizedLabel, string Example, int RegistryVersion);
public sealed record CapabilityGuidanceRequest(
    string Message,
    string ReplyLanguage,
    DialogueOutcome Outcome,
    string OutcomeReasonCode,
    QueryInterpretation? Interpretation = null,
    IReadOnlyCollection<EntityResolutionCandidate>? EntityCandidates = null,
    IReadOnlyCollection<string>? ActorAvailableCapabilities = null,
    string? CorrelationId = null,
    string Channel = "web-ai",
    string? Symbol = null,
    string? AttemptedCapability = null);

/// <summary>
/// The symbol a non-answered question was about, and the capability that was tried, so the guidance can offer
/// follow-ups for that same symbol. Prefers the validated symbol slot of the semantic frame; otherwise uses the
/// symbol the routed intent itself extracted from the message (the same text its answer names).
/// </summary>
public static class CapabilityGuidanceSubject
{
    public static (string? Symbol, string? AttemptedCapability) From(
        ValidatedQueryFrame? frame,
        DetectedIntent intent,
        string message)
    {
        var symbol = frame?.Slots
            .FirstOrDefault(slot =>
                slot.Type == QuerySlotType.CompanyOrSymbol &&
                slot.ValidationState == QuerySlotValidationState.Valid &&
                slot.CanonicalEntity is not null)
            ?.CanonicalEntity!.DisplaySymbol;
        symbol ??= intent switch
        {
            DetectedIntent.MonthlyActivityTrend => MonthlyActivityTrendIntentRules.ExtractCompanySymbol(message),
            DetectedIntent.FinancialStatementTableLookup when FinancialStatementTableIntentRules.LooksLikeQuarterlyReportTableQuery(message) =>
                FinancialStatementTableIntentRules.BuildQuarterlyQuery(message).CompanyQuery,
            _ => null
        };
        var attempted = intent switch
        {
            DetectedIntent.MonthlyActivityTrend => "monthly_activity_trend",
            DetectedIntent.FinancialStatementTableLookup => "financial_statement_table",
            _ => frame?.CapabilityCode
        };
        return (string.IsNullOrWhiteSpace(symbol) ? null : symbol.Trim(), attempted);
    }
}

public interface ICapabilityGuidanceService
{
    IReadOnlyList<SuggestedAction> Suggest(CapabilityGuidanceRequest request);
    IReadOnlyList<CapabilityHelpSummary> StarterPrompts(string language, IReadOnlyCollection<string>? actorAvailableCapabilities = null);
}

public sealed class CapabilityGuidanceService(
    IConversationalCapabilityRegistry registry,
    ISemanticDialogueEventSink? eventSink = null,
    TimeProvider? timeProvider = null) : ICapabilityGuidanceService
{
    private const int MaximumActions = 4;

    public IReadOnlyList<SuggestedAction> Suggest(CapabilityGuidanceRequest request)
    {
        if (request.Outcome == DialogueOutcome.Answered) return [];
        var enabled = Enabled(request.ActorAvailableCapabilities).ToArray();
        var target = request.Interpretation?.CapabilityCandidates.FirstOrDefault()?.CapabilityCode;
        var candidates = request.EntityCandidates?.Take(MaximumActions).Select(candidate => Action(
            $"entity:{candidate.Entity.CanonicalId:N}", SuggestedActionKind.ChooseEntity, candidate.Entity.DisplaySymbol,
            candidate.Entity.DisplaySymbol, target ?? "comprehensive_analysis", new Dictionary<string, string> { ["symbol"] = candidate.Entity.DisplaySymbol }, "entity_ambiguity", request.ReplyLanguage)).ToList() ?? [];
        if (candidates.Count > 0) return RecordPresented(candidates, request);

        if (request.Outcome is DialogueOutcome.ClarificationNeeded or DialogueOutcome.DisambiguationNeeded && target is not null && enabled.Any(item => item.Code == target))
        {
            var definition = enabled.Single(item => item.Code == target);
            var example = Example(definition, request.ReplyLanguage);
            return RecordPresented([Action($"fill:{target}", SuggestedActionKind.FillSlot, Label(request.ReplyLanguage, "Complete this request", "تکمیل درخواست"), example, target, new Dictionary<string, string>(), request.OutcomeReasonCode, request.ReplyLanguage)], request);
        }
        if (request.Outcome == DialogueOutcome.TemporarilyUnavailable && target is not null && enabled.Any(item => item.Code == target))
            return RecordPresented([Action($"retry:{target}", SuggestedActionKind.Retry, Label(request.ReplyLanguage, "Try again", "تلاش دوباره"), request.Message, target, new Dictionary<string, string>(), "temporary_failure", request.ReplyLanguage)], request);

        var followUps = SymbolFollowUps(request, enabled);
        if (followUps.Count > 0) return RecordPresented(followUps, request);

        return RecordPresented(enabled.Take(MaximumActions).Select(definition => Action($"help:{definition.Code}", SuggestedActionKind.ShowCapabilityHelp,
            Label(request.ReplyLanguage, "Try: ", "مثال: ") + Example(definition, request.ReplyLanguage), Example(definition, request.ReplyLanguage), definition.Code, new Dictionary<string, string>(), "capability_help", request.ReplyLanguage)).ToArray(), request);
    }

    public IReadOnlyList<CapabilityHelpSummary> StarterPrompts(string language, IReadOnlyCollection<string>? actorAvailableCapabilities = null) =>
        Enabled(actorAvailableCapabilities)
            .Where(definition => definition.SuggestionPolicy.IncludeInGuidance)
            .Select(definition => new CapabilityHelpSummary(definition.Code, Alias(definition, language), Example(definition, language), registry.Version))
            .ToArray();

    /// <summary>
    /// Follow-ups for a question that was not answered: the monthly sales trend and the quarterly report, which work
    /// for listed companies. They name the symbol the user asked about; without one they show the catalog's example
    /// symbol as an example. The capability that just found no data for that symbol is not offered again.
    /// </summary>
    private IReadOnlyList<SuggestedAction> SymbolFollowUps(CapabilityGuidanceRequest request, IReadOnlyCollection<CapabilityDefinition> enabled)
    {
        var symbol = request.Symbol;
        var actions = new List<SuggestedAction>();
        foreach (var (code, english, persian) in FollowUpTemplates)
        {
            if (!enabled.Any(item => item.Code == code)) continue;
            if (symbol is not null && request.Outcome == DialogueOutcome.NoData && request.AttemptedCapability == code) continue;
            var message = string.Format(request.ReplyLanguage == "fa" ? persian : english, symbol ?? ExampleSymbol);
            actions.Add(symbol is null
                ? Action($"help:{code}", SuggestedActionKind.ShowCapabilityHelp, Label(request.ReplyLanguage, "Try: ", "مثال: ") + message,
                    message, code, new Dictionary<string, string>(), "capability_help", request.ReplyLanguage)
                : Action($"related:{code}", SuggestedActionKind.RunRelatedCapability, message,
                    message, code, new Dictionary<string, string> { ["symbol"] = symbol }, "same_symbol_follow_up", request.ReplyLanguage));
        }
        return actions;
    }

    private const string ExampleSymbol = "فولاد";

    private static readonly (string Code, string English, string Persian)[] FollowUpTemplates =
    [
        ("monthly_activity_trend", "chart monthly sales for {0}", "روند فروش {0}"),
        ("financial_statement_table", "show {0} quarterly income statement", "گزارش فصلی {0}")
    ];

    private IEnumerable<CapabilityDefinition> Enabled(IReadOnlyCollection<string>? available) => registry.GetEnabled().Where(definition => available is null || available.Contains(definition.Code, StringComparer.Ordinal));
    private SuggestedAction Action(string id, SuggestedActionKind kind, string label, string message, string capability, IReadOnlyDictionary<string, string> slots, string reason, string language)
    {
        var boundedSlots = slots.Take(4).ToDictionary(
            item => Bound(item.Key, 40), item => Bound(item.Value, 120), StringComparer.Ordinal);
        return new(Bound(id, 160), kind, Bound(label, 160), Bound(message, 500),
            Bound(capability, 100), boundedSlots, Bound(reason, 100), registry.Version);
    }
    private static string Example(CapabilityDefinition definition, string language) => definition.Examples.FirstOrDefault(example => example.Language == language)?.Text ?? definition.Examples[0].Text;
    private static string Alias(CapabilityDefinition definition, string language) => definition.Aliases.FirstOrDefault(alias => alias.Language == language)?.Value ?? definition.Code;
    private static string Label(string language, string english, string persian) => language == "fa" ? persian : english;
    private static string Bound(string value, int maximumLength) => value.Length <= maximumLength ? value : value[..maximumLength];

    private IReadOnlyList<SuggestedAction> RecordPresented(IReadOnlyList<SuggestedAction> actions, CapabilityGuidanceRequest request)
    {
        if (eventSink is null || string.IsNullOrWhiteSpace(request.CorrelationId)) return actions;
        foreach (var action in actions)
            eventSink.Record(new SemanticDialogueEvent(
                SemanticEventName.SuggestionPresented,
                request.CorrelationId,
                action.CapabilityCode,
                action.RegistryVersion,
                action.RelevanceReason,
                request.Channel,
                (timeProvider ?? TimeProvider.System).GetUtcNow(),
                action.Id));
        return actions;
    }
}
