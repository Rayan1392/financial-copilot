# Feature 132 — AI Financial Statement Value Search Design

## Objective and boundary

Feature 132 connects the existing AI query facade to Feature 131 for natural-language identification of a company or symbol from one or more exact values in its latest persisted financial statement.

```text
natural-language request
  -> existing AI facade
  -> existing semantic interpretation and frame enrichment
  -> Feature 132 capability executor
  -> IFinancialStatementValueSearchService (Feature 131)
  -> typed, evidence-faithful response
```

Feature 132 owns only interpretation, bounded routing, request construction, and presentation. Feature 131 remains the sole owner of persisted financial-statement queries, exact decimal matching, latest-statement selection, same-statement enforcement, company resolution, governed metric/source-item enforcement, canonical evidence grouping, and match/no-match/unresolved semantics.

Feature 132 adds no database access, EF query, repository, provider call, migration, public API, screener, fuzzy matching, financial reasoning, investment analysis, or new semantic subsystem.

## Existing extension points and registration

The capability is registered in the existing semantic system; no parallel registry is permitted.

- Add `financial_statement_value_search` to `DetectedIntent` and map it in `SemanticRouteMapping`.
- Add the capability definition to `InitialConversationalCapabilityCatalog` using the existing `CapabilityDefinition`, localized aliases/examples, `RequiredSlots`, `OptionalSlots`, execution route, output type, data requirements, and precedence metadata.
- Use the existing slot schema (`QuerySlotSchema`/`SlotDefinition`) and `ISemanticQueryFrameEnricher` for Feature 132 slots. The enrichment output is part of the validated semantic frame; executors do not parse raw text locally.
- Register one `FinancialStatementValueSearchCapabilityExecutor` through the existing `IConversationalCapabilityExecutor` registration in `ServiceCollectionExtensions`.
- `SemanticCapabilityDispatcher` validates the catalog definition and dispatches the registered executor. `SemanticExecutionCoordinator` remains the common execution and billing boundary.
- V1 `AiQueryOrchestrationService` and active MAF V2 `FinancialCopilotWorkflowDefinition` consume the same semantic execution result and dispatcher path. Neither workflow branch calls Feature 131 directly.

The exact capability definition is:

```text
Code: financial_statement_value_search
Route: financial_statement_value_search
OutputType: financial_statement_value_search
PrecedenceGroup: exact-value-identification
Required slots: NumericClues
Optional slots: MetricCode, SourceTitle, GovernedAlias, StatementType
```

`CompanyOrSymbol` is deliberately not a required slot. Existing `ConversationDialogueGate` and canonical entity resolution continue to operate globally, but a missing or unresolved company must not invalidate this capability.

## Validated interpretation and numeric normalization

The common executor accepts only a validated interpretation containing at least one numeric clue. The executor constructs the existing Feature 131 request:

```csharp
new FinancialStatementValueSearchRequest(
    ProviderName: configuredProvider,
    StatementType: FinancialStatementType.IncomeStatement,
    Clues: validatedClues)
```

There is one authoritative query-facing numeric normalization path. It reuses the existing `QueryNormalization` conventions for Unicode normalization and Latin, Persian, and Arabic digit normalization, and the existing decimal parsing convention is extended/used in that same path where required. It must preserve a supported decimal separator, remove supported thousands separators deterministically, and return `decimal` using invariant semantics. It must never use `double` or `float`, round, or apply tolerance. The ingestion-only `FundPortfolioValueNormalizer` is not a second AI-query parser.

The implementation must document and test the formats actually supported by that shared path: Latin/Persian/Arabic digits, supported thousands separators, and the supported decimal separator. Malformed, ambiguous, overflowed, or otherwise invalid numeric input produces deterministic validation/clarification and does not invoke Feature 131. The maximum clue count is 20, matching Feature 131; Feature 132 may reject earlier but can never pass more than 20 clues.

Optional `MetricCode`, persisted `SourceTitle`, and governed `GovernedAlias` refine a numeric clue. A title, metric, or alias without a valid numeric clue is clarification/validation, not a search.

## Deterministic routing precedence

Routing is semantic and uses the existing capability interpretation infrastructure; it is not a rigid phrase grammar or a new phrase-rule engine.

Feature 132 matches:

```text
company/symbol identification intent
+ one or more exact numeric financial-statement clues
+ no required known symbol
```

Examples include “find a symbol with revenue 3300508”, “which company has net profit 2580407?”, and equivalent Persian, English, or mixed wording. Non-template wording is covered by the semantic capability examples and interpretation tests.

The following precedence is deterministic:

1. Threshold/filter semantics route to `stock_screening`, even when a number is present. This includes greater than, less than, above, below, between, growth, `بیشتر از`, `کمتر از`, `بالای`, and `زیر` when they express screening.
2. A known-symbol metric request routes to `symbol_metric_lookup` (for example, `P/E شغدیر` or “revenue of Dateras”; Feature 132 is not used).
3. Analysis, comprehensive analysis, monthly trend/chart, and comparison semantics retain their existing routes.
4. Exact-value company identification with no required known symbol routes to `financial_statement_value_search`.
5. Identification wording with no numeric clue routes to existing clarification/unsupported behavior and never invokes Feature 131.

An unresolved or absent company entity does not block Feature 132. The required information is only at least one valid numeric clue; company/symbol, metric code, source title, and governed alias are optional. This does not bypass or weaken `ConversationDialogueGate` globally and does not change unrelated capabilities.

## Shared V1 / MAF V2 execution path

`FinancialStatementValueSearchCapabilityExecutor` is the single Feature 132 application adapter. It owns validated interpretation → `FinancialStatementValueSearchRequest` construction → one `IFinancialStatementValueSearchService.SearchAsync` invocation → one common `CapabilityExecutionResult` payload.

```text
AiQueryOrchestrationService (V1 semantic route)       \\
                                                        -> SemanticExecutionCoordinator
FinancialCopilotWorkflowDefinition (MAF V2 route)    /       -> SemanticCapabilityDispatcher
                                                               -> FinancialStatementValueSearchCapabilityExecutor
                                                               -> IFinancialStatementValueSearchService
                                                               -> FinancialStatementValueSearchResult
```

There is no V1-specific or V2-specific Feature 131 request mapping, search logic, or direct service call.

## Typed response and rendering contract

The executor payload preserves the existing typed `FinancialStatementValueSearchResult` rather than returning only unconstrained generated text. Its authoritative fields are retained for every match:

- `Outcome` and `FinancialStatementCompanyResolutionStatus`;
- `Symbol` and `CompanyName`, nullable when identity is unresolved;
- `StatementType`, `PeriodType`, `PeriodStart`, `PeriodEnd`, `PublishedAt`, and synchronization metadata;
- exact `decimal` evidence value;
- `MetricCode`, `SourceTitle`, requested clue, and Feature 131 source-item/duplicate-evidence identifiers;
- provider/external evidence identifiers where present.

Resolved matches render the typed facts without changing numeric values or source titles. Unresolved matches preserve unresolved status and do not invent a symbol or company. `NoMatch` remains deterministic and states that no eligible latest statement matched all clues. Multiple clues are shown only as the canonical same-statement evidence returned by Feature 131. The LLM may add headings or translate labels, but may not alter authoritative evidence.

## Billing and operational controls

Authentication, tenant/actor isolation, rate limits, telemetry, correlation, timeout, and failure handling remain on existing facade infrastructure.

Billing uses the existing `IBillingFacadeHook` lifecycle through `SemanticExecutionCoordinator`:

1. Existing facade authentication and authorization run first.
2. Exactly one `TryReserveAsync(BillingReservationRequest)` occurs for the AI operation.
3. The executor invokes Feature 132 once, and Feature 131 performs no billing.
4. Exactly one `FinalizeAsync` occurs for the completed, clarified, failed, or cancelled outcome as defined by the existing coordinator lifecycle; abandoned work uses the existing release path.

The V1 outer orchestration must not reserve when a semantic frame will be executed by `SemanticExecutionCoordinator`; MAF V2 must use that same coordinator and must not create a second reservation/finalization cycle. No Feature 132 billing abstraction is introduced.

The read-only query path does not call a provider API. Existing telemetry records capability, clue count, validation outcome, Feature 131 outcome, latency, and correlation identifiers without logging raw financial payloads or credentials.
