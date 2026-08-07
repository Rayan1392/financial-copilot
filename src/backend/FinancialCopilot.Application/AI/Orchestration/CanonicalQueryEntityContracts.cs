namespace FinancialCopilot.Application.AI.Orchestration;

public sealed record CanonicalQueryEntity(
    Guid CanonicalId,
    string DisplaySymbol,
    string CompanyName,
    string EntityType,
    string IdentityProvenance);

public sealed record EntityResolutionEvidence(
    string MatchKind,
    decimal Confidence,
    QueryValueProvenance Provenance = QueryValueProvenance.UserExplicit);

public sealed record EntityResolutionCandidate(
    CanonicalQueryEntity Entity,
    decimal Confidence,
    string MatchKind);

public abstract record EntityResolutionResult
{
    public sealed record Resolved(CanonicalQueryEntity Entity, EntityResolutionEvidence Evidence) : EntityResolutionResult;

    public sealed record Ambiguous(IReadOnlyList<EntityResolutionCandidate> Candidates) : EntityResolutionResult;

    public sealed record NotFound(string NormalizedMention) : EntityResolutionResult;

    public sealed record Missing(string EntityType) : EntityResolutionResult;
}

public interface ICanonicalQueryEntityResolver
{
    Task<EntityResolutionResult> ResolveMentionAsync(string? mention, CancellationToken cancellationToken = default);

    Task<EntityResolutionResult> ResolveFromInterpretationAsync(
        QueryInterpretation interpretation,
        CancellationToken cancellationToken = default);
}

public enum QuerySlotType
{
    CompanyOrSymbol,
    Metric,
    Period,
    ComparisonBaseline,
    Threshold,
    StatementType,
    AnalysisTopic,
    Presentation,
    ResultLimit,
    Sort
}

public enum QuerySlotValidationState
{
    Valid,
    Missing,
    Ambiguous,
    Unsupported,
    Invalid
}

public sealed record ResolvedQuerySlot(
    QuerySlotType Type,
    string? Value,
    QueryValueProvenance Provenance,
    decimal Confidence,
    QuerySlotValidationState ValidationState,
    string? CapabilityCode = null,
    string? Detail = null);

public sealed record SlotValidationResult(
    IReadOnlyList<ResolvedQuerySlot> Slots,
    ResolvedQuerySlot? NextClarificationSlot,
    IReadOnlyList<string> UnsupportedSlots);

public interface ICapabilitySlotValidator
{
    SlotValidationResult Validate(string capabilityCode, QueryInterpretation interpretation, EntityResolutionResult entityResolution);
}

public sealed class CapabilitySlotValidator(IConversationalCapabilityRegistry registry) : ICapabilitySlotValidator
{
    private static readonly IReadOnlyDictionary<string, QuerySlotType> SlotTypes = new Dictionary<string, QuerySlotType>(StringComparer.Ordinal)
    {
        ["symbol"] = QuerySlotType.CompanyOrSymbol,
        ["metric"] = QuerySlotType.Metric,
        ["period"] = QuerySlotType.Period,
        ["comparison"] = QuerySlotType.ComparisonBaseline,
        ["threshold"] = QuerySlotType.Threshold,
        ["statementType"] = QuerySlotType.StatementType,
        ["topic"] = QuerySlotType.AnalysisTopic,
        ["presentation"] = QuerySlotType.Presentation,
        ["limit"] = QuerySlotType.ResultLimit,
        ["sort"] = QuerySlotType.Sort
    };

    public SlotValidationResult Validate(string capabilityCode, QueryInterpretation interpretation, EntityResolutionResult entityResolution)
    {
        var definition = registry.Find(capabilityCode)
            ?? throw new InvalidOperationException($"Unknown capability '{capabilityCode}'.");
        var slots = new List<ResolvedQuerySlot>();

        foreach (var definitionSlot in definition.RequiredSlots.Concat(definition.OptionalSlots))
        {
            if (!SlotTypes.TryGetValue(definitionSlot.Name, out var type))
                continue;

            slots.Add(ResolveSlot(type, definitionSlot.Required, capabilityCode, interpretation, entityResolution));
        }

        var priority = definition.RequiredSlots
            .Select(slot => SlotTypes.TryGetValue(slot.Name, out var type) ? type : (QuerySlotType?)null)
            .Where(type => type is not null)
            .Select(type => slots.Single(slot => slot.Type == type))
            .FirstOrDefault(slot => slot.ValidationState is QuerySlotValidationState.Missing or QuerySlotValidationState.Ambiguous);

        return new SlotValidationResult(
            slots,
            priority,
            slots.Where(slot => slot.ValidationState == QuerySlotValidationState.Unsupported)
                .Select(slot => slot.Type.ToString()).ToArray());
    }

    private static ResolvedQuerySlot ResolveSlot(
        QuerySlotType type,
        bool required,
        string capabilityCode,
        QueryInterpretation interpretation,
        EntityResolutionResult entityResolution) =>
        type switch
        {
            QuerySlotType.CompanyOrSymbol => entityResolution switch
            {
                EntityResolutionResult.Resolved resolved => new(type, resolved.Entity.DisplaySymbol, QueryValueProvenance.UserExplicit, resolved.Evidence.Confidence, QuerySlotValidationState.Valid, capabilityCode),
                EntityResolutionResult.Ambiguous => new(type, null, QueryValueProvenance.UserExplicit, 0m, QuerySlotValidationState.Ambiguous, capabilityCode),
                EntityResolutionResult.NotFound notFound => new(type, notFound.NormalizedMention, QueryValueProvenance.UserExplicit, 0m, QuerySlotValidationState.Invalid, capabilityCode, "entity_not_found"),
                _ => new(type, null, QueryValueProvenance.UserExplicit, 0m, required ? QuerySlotValidationState.Missing : QuerySlotValidationState.Valid, capabilityCode)
            },
            QuerySlotType.Metric when interpretation.Metrics.FirstOrDefault() is { } metric => new(type, metric.MetricCode, metric.Provenance, interpretation.Confidence, QuerySlotValidationState.Valid, capabilityCode),
            QuerySlotType.Metric => new(type, null, QueryValueProvenance.UserExplicit, 0m, required ? QuerySlotValidationState.Missing : QuerySlotValidationState.Valid, capabilityCode),
            QuerySlotType.Period when interpretation.Period is { } period => new(type, period.Value, period.Provenance, interpretation.Confidence, QuerySlotValidationState.Valid, capabilityCode),
            QuerySlotType.ComparisonBaseline when interpretation.Comparison is { } comparison => new(type, comparison.Value, comparison.Provenance, interpretation.Confidence, QuerySlotValidationState.Valid, capabilityCode),
            QuerySlotType.Presentation when interpretation.Presentation is { } presentation => new(type, presentation.Kind.ToString(), presentation.Provenance, interpretation.Confidence, QuerySlotValidationState.Valid, capabilityCode),
            _ => new(type, null, QueryValueProvenance.PolicyDefaulted, 1m, required ? QuerySlotValidationState.Missing : QuerySlotValidationState.Valid, capabilityCode)
        };
}

public sealed record CanonicalEntityResolutionOptions(bool Enabled = false, int MaxCandidates = 5, decimal FuzzyCandidateThreshold = 0.72m);

public interface ICanonicalCompanyRouteAdapter
{
    Task<EntityResolutionResult?> ResolveForRouteAsync(
        string capabilityCode,
        QueryInterpretation interpretation,
        CancellationToken cancellationToken = default);
}

public sealed class CanonicalCompanyRouteAdapter(
    ICanonicalQueryEntityResolver resolver,
    CanonicalEntityResolutionOptions options) : ICanonicalCompanyRouteAdapter
{
    public async Task<EntityResolutionResult?> ResolveForRouteAsync(
        string capabilityCode,
        QueryInterpretation interpretation,
        CancellationToken cancellationToken = default)
    {
        if (!options.Enabled)
            return null;

        return await resolver.ResolveFromInterpretationAsync(interpretation, cancellationToken);
    }
}

public static class EntityResolutionOutcomeMapper
{
    public static DialogueOutcomeResult ToOutcome(string message, EntityResolutionResult result, bool resolvedHasData = true) =>
        result switch
        {
            EntityResolutionResult.Missing => new(
                DialogueOutcome.ClarificationNeeded,
                DialogueOutcomeReasonCodes.RequiredInputMissing,
                AiDialogueOutcomePolicy.DetectReplyLanguage(message), null, false),
            EntityResolutionResult.Ambiguous => new(
                DialogueOutcome.DisambiguationNeeded,
                DialogueOutcomeReasonCodes.EntityAmbiguous,
                AiDialogueOutcomePolicy.DetectReplyLanguage(message), null, false),
            EntityResolutionResult.NotFound => new(
                DialogueOutcome.DisambiguationNeeded,
                DialogueOutcomeReasonCodes.EntityNotFound,
                AiDialogueOutcomePolicy.DetectReplyLanguage(message), null, false),
            EntityResolutionResult.Resolved when !resolvedHasData => new(
                DialogueOutcome.NoData,
                DialogueOutcomeReasonCodes.SupportedButNoRows,
                AiDialogueOutcomePolicy.DetectReplyLanguage(message), null, false),
            _ => new(DialogueOutcome.Answered, DialogueOutcomeReasonCodes.None, AiDialogueOutcomePolicy.DetectReplyLanguage(message), null, false)
        };
}
