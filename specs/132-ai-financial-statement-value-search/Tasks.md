# Feature 132 — Implementation Tasks

## Slice 1 — AI Contract and Intent

### Task 1.1 — Register the value-search capability

Extend the existing `DetectedIntent`, `SemanticRouteMapping`, `InitialConversationalCapabilityCatalog`, `CapabilityDefinition`, `SlotDefinition`/`QuerySlotSchema`, semantic-frame enrichment, and `SemanticCapabilityDispatcher` path for `financial_statement_value_search`. Register the executor through the existing `IConversationalCapabilityExecutor` mechanism. Do not create a parallel registry, classifier, grammar, or phrase-rule engine.

The capability has no required `CompanyOrSymbol` slot. `NumericClues` is the only required information; metric code, source title, governed alias, statement type, and company identity are optional. Existing `ConversationDialogueGate` remains global and unchanged for other capabilities.

### Task 1.2 — Define the schema-constrained interpretation

Use one authoritative query-facing numeric normalization owner, reusing the existing `QueryNormalization` conventions and one decimal conversion path. Document and test the actual supported Latin/Persian/Arabic digit, thousands-separator, and decimal-separator forms. Produce `decimal`, never `double` or `float`; do not round or tolerate approximate values. Reject malformed input deterministically and enforce a maximum of 20 clues, aligned with Feature 131.

### Task 1.3 — Preserve routing precedence and unknown-symbol behavior

Implement/test semantic precedence for non-template exact-value identification versus threshold scanner semantics, known-symbol metric lookup, comprehensive analysis, and monthly trend/chart/comparison. Threshold terms such as greater than, less than, above, below, between, growth, `بیشتر از`, `کمتر از`, `بالای`, and `زیر` must retain scanner routing when they express screening. No-number identification must clarify without invoking Feature 131. An absent or unresolved company must not reject Feature 132.

## Slice 2 — Existing Service Integration

### Task 2.1 — Add the common Feature 132 executor/adapter

Implement one `FinancialStatementValueSearchCapabilityExecutor` following the existing `IConversationalCapabilityExecutor` convention. It owns validated-frame interpretation, `FinancialStatementValueSearchRequest` construction, one `IFinancialStatementValueSearchService.SearchAsync` call, and the common typed `CapabilityExecutionResult`. It must not contain EF queries, repository access, provider calls, or duplicate Feature 131 semantics.

### Task 2.2 — Integrate V1 and MAF V2 through the same executor

Route both `AiQueryOrchestrationService`/V1 and active MAF V2 `FinancialCopilotWorkflowDefinition` through `SemanticExecutionCoordinator` → `SemanticCapabilityDispatcher` → the same executor. Prohibit direct Feature 131 calls, duplicate request mapping, and duplicate search semantics in either workflow branch.

### Task 2.3 — Verify billing exactly once through existing platform controls

Use the existing `IBillingFacadeHook` and coordinator lifecycle. Verify authentication precedes billing, exactly one reservation occurs, Feature 132 executes once, Feature 131 performs no billing, and exactly one finalization (or existing release path for abandoned work) occurs. Confirm V1/V2 do not create independent billing cycles. Reuse existing tenant/actor isolation, rate limits, telemetry, correlation, timeout, and failure controls.

## Slice 3 — Response and Verification

### Task 3.1 — Define typed/common result and deterministic rendering

Preserve the existing typed `FinancialStatementValueSearchResult` facts: outcome, resolution status, nullable symbol/company, statement and period metadata, publication date, exact decimal evidence, metric code, source title, and provider/external evidence. Render resolved, unresolved, no-match, multiple-clue, and validation outcomes deterministically without inventing identity or changing Feature 131 facts.

### Task 3.2 — Add focused routing and contract tests

Cover non-template wording, exact-value identification, threshold scanner collision, known-symbol metric collision, unknown-symbol execution, no-number clarification, Persian/Arabic/Latin numeric normalization, separators and decimals supported by the shared path, malformed values, max clue count 20, typed evidence preservation, and V1/V2 interpretation parity. Verify only the common executor invokes `IFinancialStatementValueSearchService`.

### Task 3.3 — Keep AI-facade integration coverage

Using the existing AI-facade integration infrastructure, verify the complete `POST /api/ai/v1/query` path for single value, multiple same-statement values, same-line metric/title constraints, no-match, unresolved identity, exact decimal behavior, and billing-once behavior. Cover both V1 and active MAF V2 modes where the existing infrastructure supports them.

### Task 3.4 — Keep regression and scope verification

Run focused tests, existing semantic/routing tests, Feature 131 tests, AI-facade integration tests, and architecture/scope checks. Confirm no database logic, repository, schema/migration, provider call, public route, new semantic subsystem, generic financial search, or duplicate Feature 131 implementation was introduced.

## Completion Gate

- Tasks 1.1–3.4 complete.
- All 12 acceptance criteria verified.
- The common executor is the only Feature 132 caller of `IFinancialStatementValueSearchService`.
- Feature 131 tests remain green and authoritative evidence is preserved.
- Billing-once behavior is verified through existing platform controls.
- No out-of-scope architecture is introduced.
