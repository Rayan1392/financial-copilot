# Feature 132 — AI Financial Statement Value Search

## User Story

As a user of the existing AI chat, I want to identify a symbol or company from one or more exact values in its latest persisted income statement without already knowing the symbol.

## Acceptance Criteria

1. An exact-value identification question is routed through the existing `DetectedIntent`, `SemanticRouteMapping`, `InitialConversationalCapabilityCatalog`, slot schema, semantic-frame enrichment, and `SemanticCapabilityDispatcher` integration, and that registration is covered by a test.
2. The single existing query-facing numeric normalization path accepts the application-supported Latin, Persian, and Arabic digits, thousands separators, and decimal separator, produces invariant `decimal` values only, rejects malformed input deterministically, and never accepts more than 20 clues.
3. A valid single numeric clue invokes `IFinancialStatementValueSearchService` with `IncomeStatement` and the configured provider.
4. Multiple numeric clues are sent as separate clues and Feature 131 must match them in one latest statement.
5. A metric code, persisted source title, or governed alias refines a numeric clue; none of these is sufficient without a numeric clue.
6. `CompanyOrSymbol` is not required. Missing or unresolved company identity does not block this capability; a request with no numeric clue is clarified/unsupported without invoking Feature 131.
7. The typed/common result preserves resolution status, symbol, company name, statement type, period metadata, publication date, exact decimal value, metric code, source title, and provider/external evidence returned by Feature 131.
8. No-match and unresolved-identity results render distinctly and deterministically; the AI never invents a symbol or company.
9. V1 `AiQueryOrchestrationService` and active MAF V2 `FinancialCopilotWorkflowDefinition` use the same Feature 132 executor and the same `IFinancialStatementValueSearchService` path, producing equivalent requests and result semantics.
10. Authentication, reservation exactly once, one execution, finalization exactly once, tenant/actor isolation, telemetry, and error handling use the existing AI facade lifecycle; Feature 131 does not bill.
11. No provider API is called during AI query execution.
12. No new public route, database schema, migration, repository, semantic subsystem, or duplicate Feature 131 query logic is added.

## Routing examples

| User question | Expected behavior |
|---|---|
| `نمادی را پیدا کن با درآمد 3300508` | `financial_statement_value_search`; one exact clue; unknown symbol allowed. |
| `Which company has revenue 3,300,508?` | Same capability; shared numeric normalization; Feature 131 invocation. |
| `سهام با درآمد بالای 3300508` | `stock_screening`; threshold precedence wins. |
| `P/E شغدیر` | `symbol_metric_lookup`; known-symbol metric precedence wins. |
| `تحلیل شغدیر` | Existing comprehensive-analysis route. |
| `روند فروش ماهانه کهمدا را نشان بده` | Existing monthly trend route. |
| `نمادی با درآمد پیدا کن` | Clarification/unsupported; no Feature 131 call. |
| `کدام شرکت سود 2580407 و درآمد 3300508 دارد؟` | Two clues; one latest same-statement result or deterministic no-match/unresolved result. |
