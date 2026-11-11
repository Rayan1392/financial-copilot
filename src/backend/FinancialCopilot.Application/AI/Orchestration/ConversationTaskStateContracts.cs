using System.Diagnostics;

namespace FinancialCopilot.Application.AI.Orchestration;

using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Application.AI.Evaluation;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.FinancialData.Semantics.Intent;

public enum PendingDialogueActionKind { Clarification, Disambiguation }

public enum ConversationTaskStateTransitionKind { Answered, ClarificationRequested, ClarificationResolved, ClarificationAbandoned, TaskSwitched, Expired, Replay }

public sealed record ConversationTaskSlot(
    QuerySlotType Type,
    string Value,
    Guid? CanonicalEntityId,
    QueryValueProvenance Provenance,
    decimal Confidence,
    Guid? OriginatingMessageId,
    long OriginatingStateVersion);

public sealed record PendingDialogueAction(
    PendingDialogueActionKind Kind,
    QuerySlotType ExpectedSlot,
    IReadOnlyList<ConversationTaskSlot> Candidates,
    string ReasonCode,
    Guid OriginatingMessageId,
    long OriginatingStateVersion);

public sealed record ConversationTaskState(
    Guid ConversationId,
    Guid TenantId,
    Guid ActorId,
    long Version,
    string? ActiveCapability,
    IReadOnlyList<ConversationTaskSlot> Slots,
    PendingDialogueAction? PendingAction,
    int TurnCount,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt,
    string? LastCorrelationId = null)
{
    public ConversationTaskSlot? FindSlot(QuerySlotType type) =>
        Slots.FirstOrDefault(slot => slot.Type == type);
}

public sealed record ConversationTaskStateScope(Guid ConversationId, Guid TenantId, Guid ActorId);

public sealed record ConversationTaskStateWriteResult(bool Succeeded, ConversationTaskState? State, bool IsReplay = false);

public interface IConversationTaskStateRepository
{
    Task<ConversationTaskState?> FindAsync(ConversationTaskStateScope scope, CancellationToken cancellationToken);
    Task<ConversationTaskStateWriteResult> TryWriteAsync(ConversationTaskState state, long? expectedVersion, CancellationToken cancellationToken);
    Task DeleteAsync(ConversationTaskStateScope scope, CancellationToken cancellationToken);
}

public sealed record ConversationTaskStateOptions(
    int ExpiryMinutes = 20,
    int MaximumTurns = 6,
    decimal MinimumCarryOverConfidence = 0.85m)
{
    public ConversationTaskStateOptions() : this(20, 6, 0.85m) { }

    public const string SectionName = "ConversationTaskState";

    public void Validate()
    {
        if (ExpiryMinutes is < 1 or > 240) throw new InvalidOperationException("Conversation task-state expiry must be between 1 and 240 minutes.");
        if (MaximumTurns is < 1 or > 30) throw new InvalidOperationException("Conversation task-state maximum turns must be between 1 and 30.");
        if (MinimumCarryOverConfidence is < 0m or > 1m) throw new InvalidOperationException("Conversation task-state confidence must be between zero and one.");
    }
}

public sealed record ConversationTaskStateTransition(
    ConversationTaskStateTransitionKind Kind,
    ConversationTaskState? Previous,
    ConversationTaskState? Current,
    string ReasonCode);

public interface IConversationTaskStateService
{
    Task<ConversationTaskState?> GetActiveAsync(ConversationTaskStateScope scope, CancellationToken cancellationToken);
    Task<ConversationTaskStateTransition> RecordAnsweredAsync(ConversationTaskStateScope scope, string capabilityCode, IReadOnlyCollection<ConversationTaskSlot> validatedSlots, Guid messageId, string correlationId, CancellationToken cancellationToken);
    Task<ConversationTaskStateTransition> RecordPendingAsync(ConversationTaskStateScope scope, string? capabilityCode, IReadOnlyCollection<ConversationTaskSlot> validatedSlots, PendingDialogueAction pendingAction, string correlationId, CancellationToken cancellationToken);
    Task<ConversationTaskStateTransition> ResolveFollowUpAsync(ConversationTaskStateScope scope, string? requestedCapability, IReadOnlyCollection<ConversationTaskSlot> explicitSlots, Guid messageId, string correlationId, CancellationToken cancellationToken);
}

public interface IConversationTaskStateTelemetrySink
{
    void Record(ConversationTaskStateTransition transition, ConversationTaskStateScope scope);
}

public sealed record ConversationDialogueGateResult(AiQueryRequest Request, ConversationTaskStateTransition? Transition, IReadOnlyCollection<ConversationTaskSlot> Slots);
public interface IConversationDialogueGate
{
    Task<ConversationDialogueGateResult> PrepareAsync(AiQueryRequest request, Guid conversationId, CancellationToken cancellationToken);
    Task RecordOutcomeAsync(AiQueryRequest request, Guid conversationId, bool clarificationRequired, string? clarificationReason, CancellationToken cancellationToken);
}

public sealed class ConversationDialogueGate(
    IConversationTaskStateService stateService,
    ICapabilityInterpreter interpreter,
    ICanonicalQueryEntityResolver entityResolver,
    ICapabilitySlotValidator slotValidator,
    IDirectMetricRoutingRegistry directMetricRegistry,
    ISemanticRoutingRolloutCoordinator rolloutCoordinator,
    IMessageRepository messageRepository,
    TimeProvider timeProvider,
    ISemanticDialogueEventSink? eventSink = null,
    ISemanticQueryFrameEnricher? frameEnricher = null,
    IIndustryRelativeValuationSemanticResolver? industryRelativeValuationResolver = null,
    HybridCapabilityInterpreter? hybridInterpreter = null,
    ICanonicalQueryProductResolver? productResolver = null,
    ISemanticRoutingLatencySink? latencySink = null,
    ISemanticRoutingCanaryTelemetrySink? canaryTelemetrySink = null,
    Feature154RoutingScope? feature154 = null,
    IEligibleSymbolResolver? eligibleSymbolResolver = null,
    SymbolSnapshotOptions? symbolSnapshotOptions = null,
    IConversationalCapabilityRegistry? capabilityRegistry = null,
    IChatCapabilityAvailability? capabilityAvailability = null) : IConversationDialogueGate
{
    private readonly Feature154RoutingScope _scope = feature154 ?? Feature154RoutingScope.V2;
    private bool _feature154 => _scope.IsActive;

    public async Task<ConversationDialogueGateResult> PrepareAsync(AiQueryRequest request, Guid conversationId, CancellationToken cancellationToken)
    {
        var scope = new ConversationTaskStateScope(conversationId, request.TenantId, request.ActorId);
        var symbolSnapshotInterpretation = await TryInterpretSymbolSnapshotAsync(request, scope, cancellationToken)
            ?? await TryInterpretTechnicalAnalysisAsync(request, scope, cancellationToken);
        if (symbolSnapshotInterpretation is null &&
            await TryPrepareUnavailableTopicAsync(request, scope, cancellationToken) is { } unavailableTopic)
            return unavailableTopic;
        if (symbolSnapshotInterpretation is null &&
            await TryPrepareAssistantHelpAsync(request, scope, cancellationToken) is { } assistantHelp)
            return assistantHelp;
        var hybridResult = hybridInterpreter is null || symbolSnapshotInterpretation is not null
            ? null
            : await hybridInterpreter.InterpretAsync(request.Message, request.TenantId, request.CorrelationId, cancellationToken);
        var interpretation = symbolSnapshotInterpretation ?? hybridResult?.Interpretation ?? interpreter.Interpret(request.Message);
        var active = await stateService.GetActiveAsync(scope, cancellationToken);
        var detectedCapability = request.Context?.InsightEventId is not null
            ? "personalized_insight_explanation"
            : interpretation.CapabilityCandidates.FirstOrDefault()?.CapabilityCode;
        // Feature 154 (V2, spec §4.4): inside an active statement-table task, «همین را برای X» repeats the active
        // report for X (only when no other capability was detected), and a bare baseline/comparison cue with no other
        // subject continues an active balance-sheet report (F154-R-14: anything with its own subject keeps its route).
        if (_feature154 && request.Context?.InsightEventId is null &&
            string.Equals(active?.ActiveCapability, "financial_statement_table", StringComparison.Ordinal) &&
            (FinancialStatementTableIntentRules.LooksLikeRepeatForCompany(request.Message) &&
             detectedCapability is null or "financial_statement_table" ||
             FinancialStatementTableCapabilityExecutor.ParseStatementTypeSlot(active!.FindSlot(QuerySlotType.StatementType)?.Value, out var activeReport) is not null &&
             activeReport == FinancialStatementTableCapabilityExecutor.Feature154ReportMarker &&
             FinancialStatementTableIntentRules.LooksLikeBalanceSheetFollowUpCue(request.Message)))
            detectedCapability = "financial_statement_table";
        var capability = detectedCapability ?? (active?.PendingAction is not null ? active.ActiveCapability : null);
        if (!string.IsNullOrWhiteSpace(request.SuggestedActionId) && request.SuggestedActionId.Length <= 160)
        {
            var selected = await FindSelectedActionAsync(conversationId, request.SuggestedActionId, cancellationToken);
            var selection = selected.GetValueOrDefault();
            var expired = selected is null ||
                !string.Equals(selection.Action.Message.Trim(), request.Message.Trim(), StringComparison.Ordinal) ||
                timeProvider.GetUtcNow() - selection.CreatedAt > TimeSpan.FromMinutes(30);
            eventSink?.Record(new SemanticDialogueEvent(
                expired ? SemanticEventName.SuggestionExpired : SemanticEventName.SuggestionSelected,
                request.CorrelationId,
                selected?.Action.CapabilityCode ?? capability,
                selected?.Action.RegistryVersion ?? interpretation.RegistryVersion,
                expired ? "suggested_action_expired_or_invalid" : "suggested_action_selected",
                request.ExternalUserId?.StartsWith("telegram:", StringComparison.Ordinal) == true ? "telegram" : "web-ai",
                timeProvider.GetUtcNow(),
                request.SuggestedActionId));
            if (expired) request = request with { SuggestedActionId = null };
        }
        eventSink?.Record(new SemanticDialogueEvent(
            SemanticEventName.InterpretationCompleted,
            request.CorrelationId,
            capability,
            interpretation.RegistryVersion,
            capability is null ? DialogueOutcomeReasonCodes.CapabilityNotRecognized : DialogueOutcomeReasonCodes.None,
            request.ExternalUserId?.StartsWith("telegram:", StringComparison.Ordinal) == true ? "telegram" : "web-ai",
            timeProvider.GetUtcNow()));
        var entityStart = Stopwatch.GetTimestamp();
        var entity = await entityResolver.ResolveFromInterpretationAsync(interpretation, cancellationToken);
        if (capability == "financial_statement_table" &&
            (_feature154
                ? FinancialStatementTableIntentRules.LooksLikeV2QuarterlyReportTableQuery(request.Message)
                : FinancialStatementTableIntentRules.LooksLikeQuarterlyReportTableQuery(request.Message)) &&
            entity is not EntityResolutionResult.Resolved &&
            (_feature154
                ? FinancialStatementTableIntentRules.BuildV2QuarterlyQuery(request.Message)
                : FinancialStatementTableIntentRules.BuildQuarterlyQuery(request.Message)).CompanyQuery is { } companyHint)
        {
            // A product mention can consume or truncate a ticker in semantic interpretation.
            // Resolve the quarterly parser's company hint through the canonical catalog.
            var quarterlyEntity = await entityResolver.ResolveMentionAsync(companyHint, cancellationToken);
            if (quarterlyEntity is EntityResolutionResult.Resolved or EntityResolutionResult.Ambiguous)
                entity = quarterlyEntity;
        }
        latencySink?.Record(new SemanticRoutingLatencySample(
            request.CorrelationId,
            EntityResolutionMs: Stopwatch.GetElapsedTime(entityStart).TotalMilliseconds));
        var entityEvent = entity switch
        {
            EntityResolutionResult.Resolved => SemanticEventName.EntityResolved,
            EntityResolutionResult.Ambiguous => SemanticEventName.EntityAmbiguous,
            EntityResolutionResult.NotFound => SemanticEventName.EntityNotFound,
            _ => (SemanticEventName?)null
        };
        if (entityEvent is { } eventName)
            eventSink?.Record(new SemanticDialogueEvent(eventName, request.CorrelationId, capability, interpretation.RegistryVersion,
                entity is EntityResolutionResult.Ambiguous ? DialogueOutcomeReasonCodes.EntityAmbiguous : entity is EntityResolutionResult.NotFound ? DialogueOutcomeReasonCodes.EntityNotFound : DialogueOutcomeReasonCodes.None,
                request.ExternalUserId?.StartsWith("telegram:", StringComparison.Ordinal) == true ? "telegram" : "web-ai", timeProvider.GetUtcNow()));
        var productMention = interpretation.EntityMentions
            .FirstOrDefault(item => string.Equals(item.EntityType, "product", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(item.Scope, "product", StringComparison.OrdinalIgnoreCase))?.Text;
        ProductResolutionResult? productResolution = productMention is null || productResolver is null
            ? null
            : entity is EntityResolutionResult.Resolved resolvedCompany
                ? await productResolver.ResolveAsync(resolvedCompany.Entity.DisplaySymbol, productMention, cancellationToken)
                : new ProductResolutionResult.Missing("Product");
        var validatedFrameSlots = capability is null
            ? Array.Empty<ResolvedQuerySlot>()
            : slotValidator.Validate(capability, interpretation, entity, productResolution).Slots.ToArray();
        if (request.Context?.InsightEventId is Guid insightEventId && capability == "personalized_insight_explanation")
            validatedFrameSlots = validatedFrameSlots
                .Where(slot => slot.Type != QuerySlotType.Insight)
                .Append(new ResolvedQuerySlot(
                    QuerySlotType.Insight,
                    insightEventId.ToString("D"),
                    QueryValueProvenance.UserExplicit,
                    1m,
                    QuerySlotValidationState.Valid,
                    capability))
                .ToArray();
        if (capability is not null && frameEnricher is not null)
            validatedFrameSlots = frameEnricher.Enrich(
                capability,
                interpretation,
                validatedFrameSlots,
                timeProvider.GetUtcNow()).ToArray();
        IndustryRelativeValuationResolution? relativeResolution = null;
        PendingDialogueAction? pendingCompanyAction = null;
        if (industryRelativeValuationResolver is not null && IsIndustryRelativeValuationCapability(capability))
        {
            relativeResolution = await industryRelativeValuationResolver.ResolveAsync(capability!, interpretation, cancellationToken);
            if ((relativeResolution.Status is IndustryRelativeValuationResolutionStatus.Resolved or
                    IndustryRelativeValuationResolutionStatus.DifferentIndustries or
                    IndustryRelativeValuationResolutionStatus.InvalidIndustryMembership) &&
                relativeResolution.CompanyIds is { Count: 2 } &&
                capability == "symbol_vs_industry_relative_valuation")
            {
                capability = "symbol_pair_within_industry";
                validatedFrameSlots = slotValidator.Validate(capability, interpretation, entity, productResolution).Slots.ToArray();
            }
            if (relativeResolution.Status == IndustryRelativeValuationResolutionStatus.Resolved)
            {
                validatedFrameSlots = validatedFrameSlots
                    .Where(slot => slot.Type is not QuerySlotType.Industry and not QuerySlotType.IndustryGroup and not QuerySlotType.CompanyOrSymbol and not QuerySlotType.CompaniesOrSymbols)
                    .Concat(capability == "symbol_pair_within_industry"
                        ? []
                        : new[] { new ResolvedQuerySlot(QuerySlotType.IndustryGroup, relativeResolution.GroupId!.Value.ToString("D"), QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid, capability, relativeResolution.GroupTitle) })
                    .Concat(relativeResolution.CompanyIds is { Count: 1 } ? [new ResolvedQuerySlot(QuerySlotType.CompanyOrSymbol, relativeResolution.CompanyIds[0].ToString("D"), QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid, capability, relativeResolution.Symbols?.FirstOrDefault())] : [])
                    .Concat(relativeResolution.CompanyIds is { Count: > 1 } ? [new ResolvedQuerySlot(QuerySlotType.CompaniesOrSymbols, string.Join(',', relativeResolution.CompanyIds), QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid, capability, string.Join(',', relativeResolution.Symbols ?? []))] : [])
                    .ToArray();
            }
            else
            {
                var companyIssue = relativeResolution.Status is IndustryRelativeValuationResolutionStatus.DifferentIndustries or IndustryRelativeValuationResolutionStatus.InvalidIndustryMembership;
                var expected = companyIssue
                    ? QuerySlotType.CompaniesOrSymbols
                    : relativeResolution.Detail is "Industry" or "IndustryGroup" || capability is "industry_relative_valuation_ranking" or "industry_relative_valuation_summary"
                        ? QuerySlotType.IndustryGroup
                        : QuerySlotType.CompanyOrSymbol;
                var validation = relativeResolution.Status == IndustryRelativeValuationResolutionStatus.Ambiguous
                    ? QuerySlotValidationState.Ambiguous
                    : relativeResolution.Status == IndustryRelativeValuationResolutionStatus.Missing
                        ? QuerySlotValidationState.Missing
                        : QuerySlotValidationState.Invalid;
                var reason = relativeResolution.Status switch
                {
                    IndustryRelativeValuationResolutionStatus.Ambiguous => DialogueOutcomeReasonCodes.EntityAmbiguous,
                    IndustryRelativeValuationResolutionStatus.NotFound => DialogueOutcomeReasonCodes.EntityNotFound,
                    IndustryRelativeValuationResolutionStatus.DifferentIndustries => DialogueOutcomeReasonCodes.DifferentIndustries,
                    IndustryRelativeValuationResolutionStatus.InvalidIndustryMembership => DialogueOutcomeReasonCodes.InvalidIndustryMembership,
                    _ => DialogueOutcomeReasonCodes.RequiredInputMissing
                };
                validatedFrameSlots = validatedFrameSlots
                    .Where(slot => slot.Type != expected)
                    .Append(new ResolvedQuerySlot(expected, null, QueryValueProvenance.UserExplicit, 0m, validation, capability, reason))
                    .ToArray();
                var candidateSlots = (relativeResolution.CandidateIds ?? [])
                    .Select((id, index) => new ConversationTaskSlot(
                        expected,
                        id.ToString("D"),
                        id,
                        QueryValueProvenance.UserExplicit,
                        1m,
                        Guid.Empty,
                        0))
                    .ToArray();
                pendingCompanyAction = new(
                    validation == QuerySlotValidationState.Ambiguous ? PendingDialogueActionKind.Disambiguation : PendingDialogueActionKind.Clarification,
                    expected,
                    candidateSlots,
                    reason,
                    Guid.Empty,
                    0);
            }
        }
        if (pendingCompanyAction is null &&
            await TryBuildMultipleCompanyClarificationAsync(capability, interpretation, cancellationToken) is { } multipleCompanies)
        {
            validatedFrameSlots = validatedFrameSlots
                .Where(slot => slot.Type != QuerySlotType.CompanyOrSymbol)
                .Append(new ResolvedQuerySlot(
                    QuerySlotType.CompanyOrSymbol,
                    null,
                    QueryValueProvenance.UserExplicit,
                    0m,
                    QuerySlotValidationState.Invalid,
                    capability,
                    DialogueOutcomeReasonCodes.MultipleCompaniesNotSupported))
                .ToArray();
            pendingCompanyAction = multipleCompanies;
        }
        var resolvedEntities = capability == "symbol_metric_lookup"
            ? await entityResolver.ResolveAllFromInterpretationAsync(interpretation, cancellationToken)
            : [];
        if (resolvedEntities.Count > 1)
        {
            validatedFrameSlots = validatedFrameSlots
                .Where(slot => slot.Type != QuerySlotType.CompaniesOrSymbols)
                .Append(new ResolvedQuerySlot(
                    QuerySlotType.CompaniesOrSymbols,
                    string.Join(',', resolvedEntities.Select(item => item.Entity.DisplaySymbol)),
                    QueryValueProvenance.UserExplicit,
                    resolvedEntities.Min(item => item.Evidence.Confidence),
                    QuerySlotValidationState.Valid,
                    capability))
                .ToArray();
        }
        var slots = validatedFrameSlots
            .Where(slot => slot.ValidationState == QuerySlotValidationState.Valid && !string.IsNullOrWhiteSpace(slot.Value))
            .Select(slot => new ConversationTaskSlot(
                slot.Type,
                slot.Value!,
                slot.CanonicalEntity?.CanonicalId ??
                    (entity is EntityResolutionResult.Resolved resolved && slot.Type == QuerySlotType.CompanyOrSymbol
                        ? resolved.Entity.CanonicalId
                        : null),
                slot.Provenance,
                slot.Confidence,
                null,
                0)).ToArray();
        var governedMetrics = capability == "symbol_metric_lookup"
            ? directMetricRegistry.ResolveAll(
                request.Message,
                DateOnly.FromDateTime(timeProvider.GetUtcNow().DateTime))
            : [];
        if (capability == "symbol_metric_lookup" && governedMetrics.Count > 0)
        {
            var governedMetric = governedMetrics.FirstOrDefault();
            validatedFrameSlots = validatedFrameSlots
                .Where(slot => slot.Type is not QuerySlotType.Metric and not QuerySlotType.Metrics and not QuerySlotType.Period)
                .Append(new ResolvedQuerySlot(
                    QuerySlotType.Metric,
                    governedMetric?.MetricCode.Value,
                    QueryValueProvenance.UserExplicit,
                    governedMetric is null ? 0m : 1m,
                    governedMetric is null ? QuerySlotValidationState.Invalid : QuerySlotValidationState.Valid,
                    capability,
                    governedMetric is null ? "metric_not_resolved" : null))
                .Concat(governedMetrics.Count > 1
                    ? new[]
                    {
                        new ResolvedQuerySlot(
                            QuerySlotType.Metrics,
                            string.Join(',', governedMetrics.Select(item => item.MetricCode.Value)),
                            QueryValueProvenance.UserExplicit,
                            1m,
                            QuerySlotValidationState.Valid,
                            capability)
                    }
                    : [])
                .Concat(governedMetric?.PeriodSelector is null
                    ? []
                    : new[]
                    {
                        new ResolvedQuerySlot(
                            QuerySlotType.Period,
                            governedMetric.PeriodSelector.Value.ToString(),
                            QueryValueProvenance.UserExplicit,
                            1m,
                            QuerySlotValidationState.Valid,
                            capability)
                    })
                .ToArray();
            slots = slots.Where(slot => slot.Type is not QuerySlotType.Metric and not QuerySlotType.Metrics and not QuerySlotType.Period)
                .Concat(governedMetric is null
                    ? []
                    : new[]
                    {
                        new ConversationTaskSlot(
                            QuerySlotType.Metric,
                            governedMetric.MetricCode.Value,
                            null,
                            QueryValueProvenance.UserExplicit,
                            1m,
                            null,
                            0)
                    })
                .Concat(governedMetrics.Count > 1
                    ? new[]
                    {
                        new ConversationTaskSlot(
                            QuerySlotType.Metrics,
                            string.Join(',', governedMetrics.Select(item => item.MetricCode.Value)),
                            null,
                            QueryValueProvenance.UserExplicit,
                            1m,
                            null,
                            0)
                    }
                    : [])
                .Concat(governedMetric?.PeriodSelector is null
                    ? []
                    : new[]
                    {
                        new ConversationTaskSlot(
                            QuerySlotType.Period,
                            governedMetric.PeriodSelector.Value.ToString(),
                            null,
                            QueryValueProvenance.UserExplicit,
                            1m,
                            null,
                            0)
                    })
                .ToArray();
        }
        else if (detectedCapability == "symbol_metric_lookup" && interpretation.Metrics.Count > 0)
        {
            validatedFrameSlots = validatedFrameSlots
                .Where(slot => slot.Type is not QuerySlotType.Metric and not QuerySlotType.Metrics and not QuerySlotType.Period)
                .Append(new ResolvedQuerySlot(
                    QuerySlotType.Metric,
                    null,
                    QueryValueProvenance.UserExplicit,
                    0m,
                    QuerySlotValidationState.Invalid,
                    capability,
                    "metric_not_resolved"))
                .ToArray();
            slots = slots.Where(slot => slot.Type is not QuerySlotType.Metric and not QuerySlotType.Metrics and not QuerySlotType.Period).ToArray();
        }
        var hasExplicitUnresolvedEntity = pendingCompanyAction is not null || validatedFrameSlots.Any(slot =>
            (slot.Type is QuerySlotType.CompanyOrSymbol or QuerySlotType.CompaniesOrSymbols or QuerySlotType.Product or QuerySlotType.Industry or QuerySlotType.IndustryGroup) &&
            slot.ValidationState is QuerySlotValidationState.Ambiguous or QuerySlotValidationState.Invalid or QuerySlotValidationState.Missing);
        ConversationTaskStateTransition? transition;
        if (pendingCompanyAction is not null)
        {
            // First let Feature 120 apply task-switch semantics, then persist the complete
            // Feature-125 pending action and its canonical candidates.
            var switched = await stateService.ResolveFollowUpAsync(scope, capability, slots, Guid.Empty, request.CorrelationId + ":switch", cancellationToken);
            transition = await stateService.RecordPendingAsync(
                scope,
                capability,
                switched.Current?.Slots ?? slots,
                pendingCompanyAction,
                request.CorrelationId + ":pending",
                cancellationToken);
        }
        else
        {
            transition = hasExplicitUnresolvedEntity
                ? null
                : await stateService.ResolveFollowUpAsync(scope, capability, slots, Guid.Empty, request.CorrelationId, cancellationToken);
        }
        var state = transition?.Current ?? active;
        var effectiveSlots = state?.Slots ?? slots;
        var effectiveCapability = detectedCapability ?? state?.ActiveCapability ?? capability;
        var frameSlotsByType = validatedFrameSlots.ToDictionary(slot => slot.Type);
        // A question about a set of companies is never narrowed to the company of an earlier turn.
        var populationQuestion = ProductSemanticIntentRules.AsksForCompanyPopulation(request.OriginalUserMessage ?? request.Message);
        foreach (var slot in effectiveSlots)
        {
            if (populationQuestion && slot.Provenance == QueryValueProvenance.ConversationInferred
                && slot.Type is QuerySlotType.CompanyOrSymbol or QuerySlotType.CompaniesOrSymbols)
                continue;
            if (frameSlotsByType.TryGetValue(slot.Type, out var current) &&
                current.ValidationState is QuerySlotValidationState.Ambiguous or QuerySlotValidationState.Invalid or QuerySlotValidationState.Unsupported)
                continue;
            frameSlotsByType[slot.Type] = frameSlotsByType.TryGetValue(slot.Type, out var preserved)
                ? preserved with
                {
                    Value = slot.Value,
                    Provenance = slot.Provenance,
                    Confidence = slot.Confidence,
                    ValidationState = QuerySlotValidationState.Valid,
                    CapabilityCode = effectiveCapability
                }
                : new ResolvedQuerySlot(
                    slot.Type,
                    slot.Value,
                    slot.Provenance,
                    slot.Confidence,
                    QuerySlotValidationState.Valid,
                    effectiveCapability);
        }
        var frame = effectiveCapability is null
            ? null
            : new ValidatedQueryFrame(
                effectiveCapability,
                interpretation.RegistryVersion,
                frameSlotsByType.Values.ToArray(),
                interpretation,
                hybridResult?.LegacyCapability ?? interpretation.CapabilityCandidates.FirstOrDefault()?.CapabilityCode,
                hybridResult?.LegacyConfidence ?? interpretation.CapabilityCandidates.FirstOrDefault()?.Confidence,
                hybridResult?.SemanticStatus ?? "Completed");
        var routing = frame is null ? null : rolloutCoordinator.Decide(frame.CapabilityCode, request.ActorId.ToString("N"));
        var modelUnsupported = hybridResult?.ModelProposalUsed == true && hybridResult.SemanticStatus == "Unsupported";
        var unresolvedSlots = validatedFrameSlots.Any(slot => slot.ValidationState is
            QuerySlotValidationState.Missing or QuerySlotValidationState.Ambiguous or
            QuerySlotValidationState.Invalid or QuerySlotValidationState.Unsupported);
        var explicitRollback = routing?.Mode is SemanticRoutingMode.Legacy or SemanticRoutingMode.Rollback;
        var deterministicFallback = hybridResult is { ModelProposalUsed: false } &&
            hybridResult.SemanticStatus is "Timeout" or "ProviderError" or "InvalidStructuredOutput" or "Unavailable";
        var disposition = modelUnsupported ? "Unsupported"
            : unresolvedSlots ? "Clarification"
            : explicitRollback ? "Rollback"
            : deterministicFallback ? "DeterministicFallback"
            : frame is not null ? "SemanticPrimary"
            : "Unsupported";
        var routedRequest = request with
        {
            OriginalUserMessage = request.OriginalUserMessage ?? request.Message,
            SemanticFrame = routing?.ExecuteSemanticRoute == true && !modelUnsupported ? frame : null,
            SemanticShadowFrame = explicitRollback ? frame : null,
            SemanticRoutingDisposition = disposition
        };

        if (canaryTelemetrySink is not null && frame is not null && routing is
            { Mode: SemanticRoutingMode.Canary, ExecuteSemanticRoute: true, InCanaryCohort: true })
        {
            var context = SemanticRoutingTelemetryFactory.FromFrame(
                routedRequest, frame, frame.CapabilityCode, frame.CapabilityCode, routing.Mode);
            var entitySlots = frame.Slots.Where(slot => slot.Type is
                QuerySlotType.CompanyOrSymbol or QuerySlotType.CompaniesOrSymbols or QuerySlotType.Product).ToArray();
            var entityResolution = entitySlots.Length == 0
                ? "NotApplicable"
                : entitySlots.All(slot => slot.ValidationState == QuerySlotValidationState.Valid)
                    ? "Resolved"
                    : "Failed";
            canaryTelemetrySink.Record(new SemanticRoutingCanarySample(
                routedRequest.CorrelationId,
                SemanticRoutingTelemetryFactory.ActorCohortKeyHash(routedRequest.ActorId.ToString("N")),
                "Canary",
                routing.CohortBucket,
                frame.CapabilityCode,
                routing.Mode,
                true,
                context.QueryHash,
                context.SemanticIntent,
                context.SemanticCandidate,
                context.LegacyCapability,
                context.ArbitrationPreferredCandidate,
                context.ArbitrationReason,
                context.ResolvedEntities,
                frame.CapabilityCode,
                SemanticRoutingDisagreementCategory.AGREE,
                "PENDING",
                entityResolution,
                "Started",
                false,
                false,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                false,
                timeProvider.GetUtcNow()));
        }

        return new(
            routedRequest,
            transition,
            effectiveSlots);
    }

    /// <summary>Feature 156: a message that is only an eligible ticker, unless it answers a pending question.</summary>
    private async Task<QueryInterpretation?> TryInterpretSymbolSnapshotAsync(
        AiQueryRequest request,
        ConversationTaskStateScope scope,
        CancellationToken cancellationToken)
    {
        if (symbolSnapshotOptions?.Enabled != true || eligibleSymbolResolver is null || capabilityRegistry is null ||
            request.Context?.InsightEventId is not null)
            return null;

        var key = SymbolSnapshotIntentRules.ExtractCompactKey(request.Message);
        if (key is null)
            return null;

        var eligible = await eligibleSymbolResolver.ResolveAsync(key, cancellationToken);
        if (eligible is null)
            return null;

        var active = await stateService.GetActiveAsync(scope, cancellationToken);
        return active?.PendingAction is not null
            ? null
            : SymbolSnapshotIntentRules.BuildInterpretation(request.Message, eligible.Symbol, capabilityRegistry.Version);
    }

    /// <summary>
    /// The company of a topic question: an exact ticker first; otherwise the deterministic interpreter's mention in the
    /// message without its topic words (Noavaran rows often carry the symbol in TseSymbol/CompanySymbol, not Ticker).
    /// </summary>
    private async Task<EntityResolutionResult> ResolveTopicCompanyAsync(string message, CancellationToken cancellationToken)
    {
        var exact = await entityResolver.ResolveExactTickerFromTextAsync(message, cancellationToken);
        if (exact is not EntityResolutionResult.Missing)
            return exact;

        var remaining = UnavailableTopicCatalog.WithoutTopicWords(message);
        return remaining.Length == 0
            ? exact
            : await entityResolver.ResolveFromInterpretationAsync(interpreter.Interpret(remaining), cancellationToken);
    }

    /// <summary>
    /// BUG-020 (owner, 2026-11-11; V2, NoavaranAmin base provider): while comprehensive analysis is available, a
    /// technical-analysis question («تکنیکال X», «پیوت X», «تحلیل تکنیکال X») about one exact ticker goes to comprehensive
    /// analysis with the technical topic, through the normal frame path. Without an exact ticker, or while comprehensive
    /// analysis is unavailable, the unavailable-topic refusal below still answers. Never during a pending question.
    /// </summary>
    private async Task<QueryInterpretation?> TryInterpretTechnicalAnalysisAsync(
        AiQueryRequest request,
        ConversationTaskStateScope scope,
        CancellationToken cancellationToken)
    {
        if (!_scope.IsActive || capabilityRegistry is null || capabilityAvailability is null ||
            capabilityAvailability.Provider != FinancialDataChatProvider.NoavaranAmin ||
            request.Context?.InsightEventId is not null)
            return null;

        // BUG-022: «تحلیل X» / «تحلیل جامع X» / «تحلیل بنیادی X» (only analysis words and one company) are routed here
        // too, so the model cannot send them elsewhere. Anything else in the message keeps today's routing.
        // A valuation or advice word («تحلیل ارزش ذاتی X») is not a plain analysis request: C3 / OQ-072 keep its route.
        var topic = UnavailableTopicCatalog.Match(request.Message);
        var technical = topic?.ReasonCode == UnavailableTopicCatalog.TechnicalAnalysisReasonCode;
        var analysisCompanyText = topic is null ? UnavailableTopicCatalog.AnalysisRequestCompanyText(request.Message) : null;
        if (!technical && analysisCompanyText is null ||
            !capabilityAvailability.Evaluate(CapabilityExecutionRoutes.ComprehensiveAnalysis).IsAvailable)
            return null;

        var active = await stateService.GetActiveAsync(scope, cancellationToken);
        if (active?.PendingAction is not null)
            return null;

        EntityResolutionResult entity;
        try
        {
            entity = technical
                ? await ResolveTopicCompanyAsync(request.Message, cancellationToken)
                : await entityResolver.ResolveMentionAsync(analysisCompanyText!, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }

        if (entity is not EntityResolutionResult.Resolved resolved)
            return null;
        return technical
            ? UnavailableTopicCatalog.BuildTechnicalAnalysisInterpretation(request.Message, resolved.Entity.DisplaySymbol, capabilityRegistry.Version)
            : UnavailableTopicCatalog.BuildAnalysisRequestInterpretation(request.Message, resolved.Entity.DisplaySymbol, capabilityRegistry.Version);
    }

    /// <summary>
    /// Feature 150 change «unavailable-capability-refusals-and-answer-correctness» (V2 only, NoavaranAmin base provider):
    /// a technical-analysis, valuation, or advice question gets a fixed refusal before any model call, instead of an
    /// unrelated capability. It never answers a pending question and never writes task state.
    /// <list type="bullet">
    /// <item>Off when the base provider is CyclicalWaves: that mode answers the equilibrium range itself (OQ-068).</item>
    /// <item>Gate B C1–C3 / OQ-072: when the deterministic interpreter would pick comprehensive analysis and that
    /// capability is available (for example «تحلیل تکنیکال فولاد» with the CyclicalWaves analysis source), today's route stays.</item>
    /// <item>The company slot is filled only for one exact ticker; otherwise it stays empty and nothing is asked.</item>
    /// <item>Rollout mode Legacy or Rollback for <c>unavailable_topic</c> turns the rule off.</item>
    /// </list>
    /// </summary>
    private async Task<ConversationDialogueGateResult?> TryPrepareUnavailableTopicAsync(
        AiQueryRequest request,
        ConversationTaskStateScope scope,
        CancellationToken cancellationToken)
    {
        if (!_scope.IsActive || capabilityRegistry is null || capabilityAvailability is null ||
            capabilityAvailability.Provider != FinancialDataChatProvider.NoavaranAmin ||
            request.Context?.InsightEventId is not null)
            return null;

        var topic = UnavailableTopicCatalog.Match(request.Message);
        if (topic is null)
            return null;

        if (interpreter.Interpret(request.Message).CapabilityCandidates.FirstOrDefault()?.CapabilityCode ==
                CapabilityExecutionRoutes.ComprehensiveAnalysis &&
            capabilityAvailability.Evaluate(CapabilityExecutionRoutes.ComprehensiveAnalysis).IsAvailable)
            return null;

        if (!rolloutCoordinator.Decide(UnavailableTopicCapability.Code, request.ActorId.ToString("N")).ExecuteSemanticRoute)
            return null;

        var active = await stateService.GetActiveAsync(scope, cancellationToken);
        if (active?.PendingAction is not null)
            return null;

        var interpretation = UnavailableTopicCatalog.BuildInterpretation(request.Message, topic, capabilityRegistry.Version);
        EntityResolutionResult entity;
        try
        {
            entity = await ResolveTopicCompanyAsync(request.Message, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The refusal does not depend on the company; without it the follow-ups show the catalog example.
            entity = new EntityResolutionResult.Missing("CompanyOrSymbol");
        }

        ResolvedQuerySlot[] slots = entity is EntityResolutionResult.Resolved resolved
            ? [new ResolvedQuerySlot(QuerySlotType.CompanyOrSymbol, resolved.Entity.DisplaySymbol, QueryValueProvenance.UserExplicit,
                resolved.Evidence.Confidence, QuerySlotValidationState.Valid, UnavailableTopicCapability.Code, CanonicalEntity: resolved.Entity)]
            : [];
        eventSink?.Record(new SemanticDialogueEvent(
            SemanticEventName.InterpretationCompleted,
            request.CorrelationId,
            UnavailableTopicCapability.Code,
            interpretation.RegistryVersion,
            topic.ReasonCode,
            request.ExternalUserId?.StartsWith("telegram:", StringComparison.Ordinal) == true ? "telegram" : "web-ai",
            timeProvider.GetUtcNow()));
        var frame = new ValidatedQueryFrame(
            UnavailableTopicCapability.Code,
            interpretation.RegistryVersion,
            slots,
            interpretation,
            UnavailableTopicCapability.Code,
            interpretation.Confidence);
        return new ConversationDialogueGateResult(
            request with
            {
                OriginalUserMessage = request.OriginalUserMessage ?? request.Message,
                SemanticFrame = frame,
                SemanticShadowFrame = null,
                SemanticRoutingDisposition = "SemanticPrimary"
            },
            null,
            []);
    }

    /// <summary>
    /// Feature 121 change (V2 only): a whole-message greeting or help question. It never answers a pending question
    /// and never writes conversation task state, so the next turn can still use the earlier company and metric.
    /// </summary>
    private async Task<ConversationDialogueGateResult?> TryPrepareAssistantHelpAsync(
        AiQueryRequest request,
        ConversationTaskStateScope scope,
        CancellationToken cancellationToken)
    {
        if (!_scope.IsActive || capabilityRegistry is null || request.Context?.InsightEventId is not null ||
            !AssistantHelpIntentRules.IsMatch(request.Message))
            return null;

        if (!rolloutCoordinator.Decide(AssistantHelpCapability.Code, request.ActorId.ToString("N")).ExecuteSemanticRoute)
            return null;

        var active = await stateService.GetActiveAsync(scope, cancellationToken);
        if (active?.PendingAction is not null)
            return null;

        var interpretation = AssistantHelpIntentRules.BuildInterpretation(request.Message, capabilityRegistry.Version);
        eventSink?.Record(new SemanticDialogueEvent(
            SemanticEventName.InterpretationCompleted,
            request.CorrelationId,
            AssistantHelpCapability.Code,
            interpretation.RegistryVersion,
            DialogueOutcomeReasonCodes.None,
            request.ExternalUserId?.StartsWith("telegram:", StringComparison.Ordinal) == true ? "telegram" : "web-ai",
            timeProvider.GetUtcNow()));
        var frame = new ValidatedQueryFrame(
            AssistantHelpCapability.Code,
            interpretation.RegistryVersion,
            [],
            interpretation,
            AssistantHelpCapability.Code,
            interpretation.Confidence);
        return new ConversationDialogueGateResult(
            request with
            {
                OriginalUserMessage = request.OriginalUserMessage ?? request.Message,
                SemanticFrame = frame,
                SemanticShadowFrame = null,
                SemanticRoutingDisposition = "SemanticPrimary"
            },
            null,
            []);
    }

    private async Task<(SuggestedAction Action, DateTimeOffset CreatedAt)?> FindSelectedActionAsync(
        Guid conversationId,
        string actionId,
        CancellationToken cancellationToken)
    {
        var messages = await messageRepository.ListByConversationAsync(conversationId, cancellationToken);
        return messages
            .Where(message => message.Role == MessageRole.Assistant && message.AssistantPayload?.SuggestedActions is { Count: > 0 })
            .OrderByDescending(message => message.CreatedAt)
            .SelectMany(message => message.AssistantPayload!.SuggestedActions!
                .Where(action => string.Equals(action.Id, actionId, StringComparison.Ordinal))
                .Select(action => ((SuggestedAction Action, DateTimeOffset CreatedAt)?)(action, message.CreatedAt)))
            .FirstOrDefault();
    }

    /// <summary>
    /// Feature 158 (V2 only): a capability that answers for one company cannot serve two companies named exactly.
    /// Only model-proposed mentions count: deterministic mentions are every non-stop-word token, and a month name
    /// that is also a ticker («دی», «مهر») must not turn an ordinary period question into a clarification.
    /// </summary>
    private async Task<PendingDialogueAction?> TryBuildMultipleCompanyClarificationAsync(
        string? capability,
        QueryInterpretation interpretation,
        CancellationToken cancellationToken)
    {
        if (!_scope.IsActive || capability is null || IsIndustryRelativeValuationCapability(capability) ||
            capabilityRegistry?.Find(capability) is not { } definition)
            return null;

        var slotNames = definition.RequiredSlots.Concat(definition.OptionalSlots).Select(slot => slot.Name).ToArray();
        if (!slotNames.Contains("symbol", StringComparer.Ordinal) || slotNames.Contains("symbols", StringComparer.Ordinal))
            return null;

        var mentions = interpretation.EntityMentions
            .Where(mention => mention.Provenance == QueryValueProvenance.ModelProposed &&
                              SemanticRoutingTelemetryFactory.IsCompanyMention(mention) &&
                              !QueryNormalization.IsEntityDistractor(mention.Text) &&
                              !PersianReportingPeriodNormalizer.IsMonthName(QueryNormalization.Normalize(mention.Text)))
            .OrderBy(mention => mention.Start)
            .ToArray();
        if (mentions.Length < 2)
            return null;

        var companies = await ResolveExactCompaniesFromSeparateSpansAsync(mentions, cancellationToken);
        if (companies.Count < 2)
            return null;

        return new PendingDialogueAction(
            PendingDialogueActionKind.Disambiguation,
            QuerySlotType.CompanyOrSymbol,
            companies.Select(company => new ConversationTaskSlot(
                    QuerySlotType.CompanyOrSymbol,
                    company.DisplaySymbol,
                    company.CanonicalId,
                    QueryValueProvenance.UserExplicit,
                    1m,
                    Guid.Empty,
                    0))
                .ToArray(),
            DialogueOutcomeReasonCodes.MultipleCompaniesNotSupported,
            Guid.Empty,
            0);
    }

    /// <summary>
    /// Exact companies named by separate parts of the message. Adjacent mentions are tried as one phrase first, with
    /// the resolver's own window and adjacency, and the longest span wins, so one multi-word company name or two
    /// overlapping model spans never count as two companies.
    /// </summary>
    private async Task<IReadOnlyList<CanonicalQueryEntity>> ResolveExactCompaniesFromSeparateSpansAsync(
        IReadOnlyList<EntityMention> mentions,
        CancellationToken cancellationToken)
    {
        const int maximumPhraseMentions = 4;
        const int maximumAdjacentGap = 2;

        var spans = new List<(int Start, int End, CanonicalQueryEntity Company)>();
        for (var length = Math.Min(maximumPhraseMentions, mentions.Count); length >= 1; length--)
        for (var start = 0; start + length <= mentions.Count; start++)
        {
            var window = mentions.Skip(start).Take(length).ToArray();
            if (!window.Zip(window.Skip(1), (left, right) => right.Start - (left.Start + left.Length) <= maximumAdjacentGap).All(adjacent => adjacent))
                continue;
            var text = string.Join(' ', window.Select(mention => mention.Text));
            if (await entityResolver.ResolveMentionAsync(text, cancellationToken) is EntityResolutionResult.Resolved match &&
                EntityResolutionMatchKinds.IsExact(match.Evidence.MatchKind))
                spans.Add((window[0].Start, window.Max(mention => mention.Start + mention.Length), match.Entity));
        }

        var selected = new List<(int Start, int End, CanonicalQueryEntity Company)>();
        foreach (var span in spans.OrderByDescending(item => item.End - item.Start).ThenBy(item => item.Start))
        {
            if (!selected.Any(other => span.Start < other.End && other.Start < span.End))
                selected.Add(span);
        }

        return selected
            .OrderBy(span => span.Start)
            .Select(span => span.Company)
            .DistinctBy(company => company.CanonicalId)
            .ToArray();
    }

    private static bool IsIndustryRelativeValuationCapability(string? capability) =>
        capability is "symbol_vs_industry_relative_valuation" or
            "industry_relative_valuation_ranking" or
            "industry_relative_valuation_summary" or
            "symbol_pair_within_industry";

    public async Task RecordOutcomeAsync(AiQueryRequest request, Guid conversationId, bool clarificationRequired, string? clarificationReason, CancellationToken cancellationToken)
    {
        // The help reply is not a task, so it leaves the conversation state as it found it.
        if (request.SemanticFrame?.CapabilityCode == AssistantHelpCapability.Code)
            return;

        var scope = new ConversationTaskStateScope(conversationId, request.TenantId, request.ActorId);
        var interpretation = interpreter.Interpret(request.OriginalUserMessage ?? request.Message);
        var capability = interpretation.CapabilityCandidates.FirstOrDefault()?.CapabilityCode;
        var active = await stateService.GetActiveAsync(scope, cancellationToken);
        var retainedSlots = active?.Slots ?? [];
        if (clarificationRequired)
        {
            // Feature-specific adapters may already have persisted a candidate-bearing
            // Feature 120 pending action. Do not overwrite it with the legacy empty action.
            if (active?.PendingAction is not null &&
                string.Equals(active.PendingAction.ReasonCode, clarificationReason, StringComparison.Ordinal))
                return;
            var productReason = clarificationReason is "product_not_found" or "product_ambiguous";
            var entityReason = clarificationReason is DialogueOutcomeReasonCodes.EntityAmbiguous or DialogueOutcomeReasonCodes.EntityNotFound;
            var expected = productReason
                ? QuerySlotType.Product
                : entityReason || interpretation.MissingSlots.Contains("symbol", StringComparer.Ordinal)
                ? QuerySlotType.CompanyOrSymbol
                : QuerySlotType.Metric;
            var kind = clarificationReason is DialogueOutcomeReasonCodes.EntityAmbiguous or "product_ambiguous"
                ? PendingDialogueActionKind.Disambiguation
                : PendingDialogueActionKind.Clarification;
            await stateService.RecordPendingAsync(scope, capability ?? active?.ActiveCapability, retainedSlots, new(kind, expected, [], clarificationReason ?? "clarification_required", Guid.Empty, 0), request.CorrelationId + ":outcome", cancellationToken);
            return;
        }
        await stateService.RecordAnsweredAsync(scope, capability ?? active?.ActiveCapability ?? "unknown", retainedSlots, Guid.Empty, request.CorrelationId + ":outcome", cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.SuggestedActionId) && request.SuggestedActionId.Length <= 160)
            eventSink?.Record(new SemanticDialogueEvent(
                SemanticEventName.SuggestionResolved,
                request.CorrelationId,
                capability ?? active?.ActiveCapability,
                interpretation.RegistryVersion,
                "suggested_action_resolved",
                request.ExternalUserId?.StartsWith("telegram:", StringComparison.Ordinal) == true ? "telegram" : "web-ai",
                timeProvider.GetUtcNow(),
                request.SuggestedActionId));
    }
}

public sealed class NullConversationTaskStateTelemetrySink : IConversationTaskStateTelemetrySink
{
    public void Record(ConversationTaskStateTransition transition, ConversationTaskStateScope scope) { }
}

public sealed class ConversationTaskStateService(
    IConversationTaskStateRepository repository,
    TimeProvider timeProvider,
    ConversationTaskStateOptions options,
    IConversationTaskStateTelemetrySink? telemetrySink = null) : IConversationTaskStateService
{
    public async Task<ConversationTaskState?> GetActiveAsync(ConversationTaskStateScope scope, CancellationToken cancellationToken)
    {
        var state = await repository.FindAsync(scope, cancellationToken);
        if (state is null) return null;
        var now = timeProvider.GetUtcNow();
        if (state.ExpiresAt > now && state.TurnCount < options.MaximumTurns) return state;
        await repository.DeleteAsync(scope, cancellationToken);
        telemetrySink?.Record(new(ConversationTaskStateTransitionKind.Expired, state, null, "state_expired"), scope);
        return null;
    }

    public Task<ConversationTaskStateTransition> RecordAnsweredAsync(ConversationTaskStateScope scope, string capabilityCode, IReadOnlyCollection<ConversationTaskSlot> validatedSlots, Guid messageId, string correlationId, CancellationToken cancellationToken) =>
        WriteAsync(scope, capabilityCode, validatedSlots, null, messageId, correlationId, ConversationTaskStateTransitionKind.Answered, cancellationToken);

    public Task<ConversationTaskStateTransition> RecordPendingAsync(ConversationTaskStateScope scope, string? capabilityCode, IReadOnlyCollection<ConversationTaskSlot> validatedSlots, PendingDialogueAction pendingAction, string correlationId, CancellationToken cancellationToken) =>
        WriteAsync(scope, capabilityCode, validatedSlots, pendingAction, pendingAction.OriginatingMessageId, correlationId, ConversationTaskStateTransitionKind.ClarificationRequested, cancellationToken);

    public async Task<ConversationTaskStateTransition> ResolveFollowUpAsync(ConversationTaskStateScope scope, string? requestedCapability, IReadOnlyCollection<ConversationTaskSlot> explicitSlots, Guid messageId, string correlationId, CancellationToken cancellationToken)
    {
        var previous = await GetActiveAsync(scope, cancellationToken);
        if (previous?.LastCorrelationId == correlationId)
            return new(ConversationTaskStateTransitionKind.Replay, previous, previous, "correlation_replay");

        var isSwitch = !string.IsNullOrWhiteSpace(requestedCapability) &&
            !string.Equals(requestedCapability, previous?.ActiveCapability, StringComparison.Ordinal);
        var compatibleRefinement = isSwitch &&
            string.Equals(previous?.ActiveCapability, "symbol_metric_lookup", StringComparison.Ordinal) &&
            string.Equals(requestedCapability, "monthly_activity_trend", StringComparison.Ordinal) &&
            explicitSlots.Any(slot => slot.Type == QuerySlotType.Presentation);
        var capability = requestedCapability ?? previous?.ActiveCapability;
        var slots = explicitSlots.ToDictionary(slot => slot.Type);
        if ((!isSwitch || compatibleRefinement) && previous is not null)
        {
            foreach (var slot in previous.Slots.Where(slot => slot.Confidence >= options.MinimumCarryOverConfidence))
            {
                if (!slots.ContainsKey(slot.Type))
                    slots[slot.Type] = slot with { Provenance = QueryValueProvenance.ConversationInferred, OriginatingStateVersion = previous.Version };
            }
        }

        var pending = previous?.PendingAction;
        if (pending is not null && !isSwitch && slots.ContainsKey(pending.ExpectedSlot))
            pending = null;
        var kind = isSwitch ? ConversationTaskStateTransitionKind.TaskSwitched : pending is null && previous?.PendingAction is not null
            ? ConversationTaskStateTransitionKind.ClarificationResolved : ConversationTaskStateTransitionKind.Answered;
        return await WriteAsync(scope, capability, slots.Values.ToArray(), pending, messageId, correlationId, kind, cancellationToken, previous);
    }

    private async Task<ConversationTaskStateTransition> WriteAsync(ConversationTaskStateScope scope, string? capabilityCode, IReadOnlyCollection<ConversationTaskSlot> slots, PendingDialogueAction? pending, Guid messageId, string correlationId, ConversationTaskStateTransitionKind kind, CancellationToken cancellationToken, ConversationTaskState? knownPrevious = null)
    {
        options.Validate();
        var previous = knownPrevious ?? await GetActiveAsync(scope, cancellationToken);
        if (previous?.LastCorrelationId == correlationId)
            return new(ConversationTaskStateTransitionKind.Replay, previous, previous, "correlation_replay");
        var now = timeProvider.GetUtcNow();
        var current = new ConversationTaskState(scope.ConversationId, scope.TenantId, scope.ActorId, (previous?.Version ?? 0) + 1, capabilityCode, slots.ToArray(), pending, (previous?.TurnCount ?? 0) + 1, now, now.AddMinutes(options.ExpiryMinutes), correlationId);
        var written = await repository.TryWriteAsync(current, previous?.Version, cancellationToken);
        if (!written.Succeeded) throw new InvalidOperationException("Conversation task state changed concurrently; retry with the latest version.");
        var transition = new ConversationTaskStateTransition(kind, previous, written.State!, pending?.ReasonCode ?? "state_updated");
        telemetrySink?.Record(transition, scope);
        return transition;
    }
}
