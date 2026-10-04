# Feature 137 — Implementation Tasks

The tasks below are intentionally limited to deterministic follow-up enrichment after the existing company monthly sales trend. They do not authorize changes to unrelated capabilities or to the frozen V1 path.

## Slice 1 — Domain/data discovery and reusable query

### 1.1 Confirm the company-trend integration seam

- Goal: Identify the exact V2 point after a successful `MonthlyActivityTrend` result and before final persistence.
- Files/areas likely affected: `FinancialCopilotWorkflowDefinition`, workflow messages, `MessagePersistenceFunction`.
- Dependencies: Existing company trend result and V2 active-mode configuration.
- Implementation notes: Preserve the existing company text, chart payload, intent, billing, and outcome. The enrichment must be non-critical.
- Acceptance criteria: The selector is invoked only for a usable company-level monthly trend result and never for a product trend, comparison, revenue mix, or generic answer.
- Tests: V2 workflow unit/integration test proving the seam and non-company branches.

### 1.2 Define the reusable candidate read contract

- Goal: Read current company-scoped product candidates from the same accepted product-sales read model used by Feature 136.
- Files/areas likely affected: `MonthlyProductComparisonContracts`, `EfCoreMonthlyProductComparisonRepository`, related DI registration.
- Dependencies: `MonthlyReports`, `MonthlyReportLineItems`, `IMonthlyProductComparisonReadRepository`.
- Implementation notes: Reuse `ReportType = ProductSales`, `OutputType = 0`, `IsAccepted`, company, period, provider identity, title, unit, and sales-value semantics. Prefer a bounded anchor-period query over per-product calls.
- Acceptance criteria: No service rows, null/other output types, unaccepted revisions, or cross-company rows enter candidate selection.
- Tests: Repository tests for source predicates, anchor period selection, and accepted revision behavior.

### 1.3 Share product identity and eligibility semantics

- Goal: Ensure suggestions and direct product trend queries use the same canonical product key, normalizer, ambiguity behavior, and minimum-history rule.
- Files/areas likely affected: `MonthlyProductTrendContracts`, `MonthlyProductTrendQueryUseCase`, shared application helper if required.
- Dependencies: Feature 136 identity and typed resolution rules.
- Implementation notes: Do not duplicate title normalization or invent a separate product resolver. One valid sales-value observation remains the minimum; the anchor period supplies recency.
- Acceptance criteria: Every generated action would resolve to the existing product trend capability or is excluded before being returned.
- Tests: Shared identity tests for provider code, provider ID, title-plus-unit fallback, renamed products, and ambiguous matches.

## Slice 2 — Suggested prompt generation

### 2.1 Implement the deterministic follow-up selector

- Goal: Build a focused application service that returns up to three `SuggestedAction` values from structured product data.
- Files/areas likely affected: New application contract/service under AI orchestration or financial-data ingestion; infrastructure implementation; DI registration.
- Dependencies: Tasks 1.2 and 1.3, `ResolvedCompany`, `MonthlyActivityTrendResponse`.
- Implementation notes: Resolve/use the canonical company symbol and external ID. Read the anchor period, group by `ProductKey`, require a non-null sales amount, and return zero on no data.
- Acceptance criteria: The selector never calls an LLM and never creates a product name from user prose.
- Tests: Unit tests for zero, one, three, and more-than-three candidates.

### 2.2 Implement ranking and tie handling

- Goal: Rank eligible products by recent aggregated `SalesAmount` and make every tie deterministic.
- Files/areas likely affected: Follow-up selector and candidate model.
- Dependencies: Task 2.1.
- Implementation notes: Sort descending by checked aggregate sales value, then ordinal normalized title, then ordinal `ProductKey`; take three. Keep zero sales as a valid low-ranked value.
- Acceptance criteria: Repeated runs over the same accepted source revision return byte-equivalent action order and no duplicates.
- Tests: Tie, positive-versus-zero, duplicate-title, and stable-order tests.

### 2.3 Build bounded canonical actions

- Goal: Turn selected candidates into executable existing actions.
- Files/areas likely affected: Follow-up action builder; `CapabilityGuidanceContracts` only if a small shared factory is needed.
- Dependencies: Task 2.2 and `SuggestedAction` bounds.
- Implementation notes: Use `RunRelatedCapability`, capability `monthly_product_trend`, canonical title/symbol in both label and message, preset company/product slots, a bounded relevance reason, and stable versioned IDs.
- Acceptance criteria: Each action is a complete normal-language product trend query and is safe for the existing web click handler.
- Tests: Persian Unicode title, long title, ID stability, message/label, and preset-slot tests.

### 2.4 Add selector observability without changing the answer

- Goal: Make zero-action reasons and selection quality measurable.
- Files/areas likely affected: Existing telemetry/activity infrastructure and selector.
- Dependencies: Task 2.1.
- Implementation notes: Record bounded candidate/eligible/returned counts, anchor period, exclusion reasons, duration, and action attribution. Do not log raw sensitive payloads.
- Acceptance criteria: Selector failures are observable and do not fail the parent company trend response.
- Tests: Telemetry assertions for no data, ambiguity, provider/read failure, and successful selection.

## Slice 3 — Response contract integration

### 3.1 Attach actions to the V2 result path

- Goal: Carry deterministic actions from the selector through V2 workflow result computation and persistence.
- Files/areas likely affected: `FinancialCopilotWorkflowDefinition`, `FinancialCopilotWorkflowMessages`, `MessagePersistenceFunction`, `ConversationContracts`.
- Dependencies: Slice 2.
- Implementation notes: Reuse `SuggestedActions`; do not add `suggestedPrompts` or serialize actions into answer prose. Keep actions optional and empty when there are no eligible products.
- Acceptance criteria: A successful company trend response contains the existing typed chart and the structured action collection.
- Tests: Workflow message and persistence round-trip tests.

### 3.2 Preserve API compatibility

- Goal: Expose actions through the existing API mapping without changing existing fields or meanings.
- Files/areas likely affected: `AiFacadeContracts`, `AiFacadeController`, serialization tests.
- Dependencies: Task 3.1.
- Implementation notes: Reuse `SuggestedActionHttpResponse`; preserve `SuggestedActionId` request handling and existing nullable behavior.
- Acceptance criteria: Existing consumers can ignore actions, and no company trend field changes type or value.
- Tests: API JSON contract tests with actions, no actions, and old persisted payloads.

### 3.3 Verify frontend/Telegram consumption

- Goal: Render the existing structured actions at the end of the company trend response.
- Files/areas likely affected: `chat.functions.ts`, `message-list.tsx`, Telegram renderer only where needed for existing action fallback.
- Dependencies: Task 3.2.
- Implementation notes: Display label, submit message plus action ID, and leave the company chart unchanged. Do not parse assistant text.
- Acceptance criteria: Web actions are clickable and submit the exact generated product query; Telegram uses the same metadata and bounded label.
- Tests: Frontend click test, persisted-message mapping test, and Telegram rendering regression.

## Slice 4 — V1 / MAF V2 integration

### 4.1 Enforce capability-scoped activation

- Goal: Apply suggestions to company-level monthly sales trend semantics rather than one exact phrase.
- Files/areas likely affected: V2 result computation and semantic capability integration.
- Dependencies: Tasks 1.1 and 3.1.
- Implementation notes: The selector is triggered by the resolved `MonthlyActivityTrend` result and must be skipped when a validated product slot selects `MonthlyProductTrend`.
- Acceptance criteria: Equivalent company trend phrasings receive equivalent action behavior; no phrase-only activation is introduced.
- Tests: Routing matrix for trend, monthly sales, chart, status, revenue, product trend, comparison, and revenue-mix queries.

### 4.2 Verify the frozen V1 boundary

- Goal: Prove V1 remains unchanged while V2 gains the additive enrichment.
- Files/areas likely affected: V1 orchestration tests and feature policy documentation.
- Dependencies: Repository V1 freeze policy.
- Implementation notes: Do not add a V1 selector, parser, route, intent, DTO, migration, or branch. Document the intentional absence under V1 rollback.
- Acceptance criteria: V1 company trend values/chart remain unchanged and V1 returns no fabricated or LLM-generated suggestions.
- Tests: V1 regression test plus V2 parity/difference assertion.

### 4.3 Validate the follow-up query through the normal path

- Goal: Confirm a returned action is executable by the existing product trend capability.
- Files/areas likely affected: V2 API integration tests and seeded product-sales data.
- Dependencies: Feature 136 product trend support and Tasks 2.3/3.2.
- Implementation notes: Use a real manufacturing/mining symbol from fixtures or development data, preferably `کچاد` when available; never hard-code it in production code.
- Acceptance criteria: A generated message such as `روند فروش گندله کچاد` resolves to `MonthlyProductTrend` and returns typed product data or its legitimate typed no-data outcome.
- Tests: End-to-end API request/response test with action click metadata.

## Slice 5 — Tests and regression

### 5.1 Complete selector unit matrix

- Goal: Cover all deterministic selection and data-quality rules.
- Files/areas likely affected: New follow-up selector test file.
- Dependencies: Slice 2.
- Implementation notes: Include 0/1/3/>3, ordering, ties, duplicates, sparse data, zero sales, missing recent month, Unicode variants, renamed products, historical-only products, and unsupported service companies.
- Acceptance criteria: The unit suite proves no fabricated product and no non-deterministic ordering.
- Tests: All cases listed in the implementation notes, including repeated-run equality.

### 5.2 Complete V2 integration and transport tests

- Goal: Verify company trend enrichment, persistence, API mapping, frontend mapping, and product follow-up execution.
- Files/areas likely affected: V2 endpoint tests, conversation serialization tests, frontend tests.
- Dependencies: Slices 3 and 4.
- Implementation notes: Assert existing company chart fields are unchanged and actions are structured rather than embedded-only prose.
- Acceptance criteria: Actions survive persistence/reload and remain clickable with their IDs.
- Tests: Company trend success, no eligible products, invalid company, old payload decode, V2 action click, product trend route.

### 5.3 Run regression suite

- Goal: Protect existing financial and rendering behavior.
- Files/areas likely affected: Existing company trend, product trend, semantic routing, API serialization, and chart tests.
- Dependencies: Tasks 5.1 and 5.2.
- Implementation notes: Do not weaken existing Feature 076/077/078/129/136 assertions to make the new feature pass.
- Acceptance criteria: Existing company sales calculations, product calculations, charts, routing precedence, and V1 behavior remain green.
- Tests: Targeted tests followed by the repository’s normal .NET/frontend validation commands.

## Slice 6 — Frontend contract documentation and rollout

### 6.1 Document the client contract

- Goal: Give frontend and channel clients a stable explanation of action semantics.
- Files/areas likely affected: Feature 137 docs and existing API contract documentation if maintained separately.
- Dependencies: Task 3.2.
- Implementation notes: Document label versus message, action ID, `monthly_product_trend` capability, empty behavior, and no-prose-parsing rule.
- Acceptance criteria: A client can render and submit actions without knowing database fields or parsing Persian answer text.
- Tests: Documentation review against serialized API examples.

### 6.2 Development-data validation and observability review

- Goal: Validate the feature with a real represented manufacturing/mining company and inspect data-quality diagnostics.
- Files/areas likely affected: Development/fixture validation scripts or test setup; no production hard-code.
- Dependencies: Task 4.3 and current accepted product-sales data.
- Implementation notes: Prefer `کچاد` when present. Record anchor period, selected titles, action count, and the returned product result; redact raw identifiers in ordinary logs.
- Acceptance criteria: Company chart remains correct, up to three real products appear, and at least one generated query reaches the product trend route.
- Tests: Repeat with no product breakdown and an invalid symbol.

### 6.3 Rollout and rollback checklist

- Goal: Enable the V2 enrichment safely and make rollback non-destructive.
- Files/areas likely affected: Existing V2 capability/feature configuration and operational runbook.
- Dependencies: All prior slices and review of Feature 136 data readiness.
- Implementation notes: If an existing capability flag is available, start disabled in development, then enable for a controlled V2 cohort. Disabling the enrichment must leave company trend answers operational.
- Acceptance criteria: Rollout, monitoring, and rollback require no database migration and no V1 change.
- Tests: Flag-off/flag-on integration tests and rollback smoke test.
