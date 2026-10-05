# Feature 137 — Implementation Tasks

These tasks are limited to deterministic follow-up enrichment after a usable
V2 company-level monthly sales trend result. They do not add a recommendation
engine, change Feature 136 behavior, or modify the frozen V1 capability set.

## Task 1 — Define the bounded candidate contract

- **Goal:** Define the application contract consumed by the Feature 137 selector.
- **Implementation scope:** Extend the existing monthly product comparison read boundary with a company-scoped suggestion-candidate read operation; do not create a second product data model.
- **Dependencies:** `MonthlyReports`, `MonthlyReportLineItems`, `IMonthlyProductComparisonReadRepository`, Feature 136 contracts.
- **Exact behavior:** Return one common anchor period and a bounded candidate universe containing `ProductKey`, Feature-136-compatible canonical `DisplayTitle`, unit/provider identity, company scope, anchor `SalesAmount`, and whether any valid non-null `SalesAmount` observation exists.
- **Acceptance criteria:** The contract is company-scoped and exposes enough data for identity, title, parser safety, queryability, and history eligibility without invoking Feature 136 per candidate.
- **Tests:** Contract tests assert the candidate fields and empty-result behavior.

## Task 2 — Extract the shared Feature 136 identity/title helper

- **Goal:** Prevent Feature 137 from duplicating product identity or title rules.
- **Implementation scope:** Extract the current Feature 136 `ProductKey`, candidate construction, grouping, canonical display-title selection, and matching semantics into a shared internal helper; update Feature 136 to call it without changing behavior.
- **Dependencies:** `MonthlyProductTrendQueryUseCase`, `MonthlyProductTrendCalculator`, `MonthlyProductComparisonNormalizer`.
- **Exact behavior:** Preserve provider-code, positive-provider-ID, and normalized-title-plus-unit key precedence. For each key, select the same deterministic display title/unit currently selected by Feature 136, not the latest-period title. Preserve `Resolved`, `NotFound`, and `Ambiguous` semantics.
- **Acceptance criteria:** Existing Feature 136 tests remain green and Feature 137 consumes the same helper rather than a second identity implementation.
- **Tests:** Stable-key, provider identity, title/unit fallback, duplicate-key, and renamed-product tests compare Feature 136 and Feature 137 candidate output.

## Task 3 — Implement the bounded accepted-data query and common anchor

- **Goal:** Read eligible product data without N+1 queries or repeated period scans.
- **Implementation scope:** Implement the repository operation over `MonthlyReports` joined to `MonthlyReportLineItems`.
- **Dependencies:** Task 1; accepted-report schema and indexes.
- **Exact behavior:** Filter `ExternalCompanyId`, `ReportType == "ProductSales"`, `OutputType == 0`, and `IsAccepted`. Restrict periods to those not later than the company trend `LatestReportYear/LatestReportMonth`. Select the newest period with usable line items and at least one non-null `SalesAmount`; exclude a header-only report. Return anchor rows plus the selected columns for the same accepted company-scoped candidate universe used by Feature 136's default resolver, in one set-based read.
- **Acceptance criteria:** Service rows, null/other output types, unaccepted revisions, other companies, later periods, and header-only periods never enter selection.
- **Tests:** Repository tests cover accepted predicates, common anchor selection, later-period exclusion, header-without-line-items, and no-anchor behavior.

## Task 4 — Implement parser-safe/query-safe eligibility

- **Goal:** Prove each returned action can round-trip through the existing product-trend parser and shared resolver.
- **Implementation scope:** Add deterministic in-memory validation to the selector; do not call `MonthlyProductTrendQueryUseCase` per candidate.
- **Dependencies:** Tasks 2–3; `MonthlyProductTrendIntentRules`.
- **Exact behavior:** Build `روند فروش {DisplayTitle} {CanonicalCompanySymbol}`. Require `LooksLikeMonthlyProductTrendQuery` to pass, require `BuildQuery` to produce the exact normalized company and product slots, and require the shared Feature-136-compatible matcher to resolve the product to exactly one `ProductKey` in the bounded universe. Reject stop-word-stripped, empty, changed, ambiguous, or otherwise non-round-trippable titles.
- **Acceptance criteria:** No action is created for a parser-unsafe title, duplicate normalized title, missing title, ambiguous identity, or candidate without a valid non-null `SalesAmount` observation.
- **Tests:** Persian/Arabic normalization, multi-token titles, stop-word titles, one-character/empty parsed slots, duplicate titles, ambiguous matches, and generated-query round trips.

## Task 5 — Aggregate and rank anchor-period candidates

- **Goal:** Produce the deterministic top-three candidate set.
- **Implementation scope:** Implement the selector over the Task 3 anchor rows and Task 2 canonical candidates.
- **Dependencies:** Tasks 2–4.
- **Exact behavior:** Group anchor observations by `ProductKey`; checked-sum non-null `SalesAmount`; keep zero as valid; exclude null aggregate values. Sort by `SalesAmount DESC`, canonical normalized `DisplayTitle ASC` using ordinal comparison, then `ProductKey ASC` using ordinal comparison; take three.
- **Acceptance criteria:** Ranking uses one common company anchor period and never uses independently latest periods per product.
- **Tests:** Zero, one, three, more-than-three, null amount, zero amount, positive-vs-zero, sales ties, title ties, final-key ties, and repeated-run order equality.

## Task 6 — Build bounded canonical `SuggestedAction` values

- **Goal:** Construct executable structured actions from selected candidates.
- **Implementation scope:** Implement the action builder using the existing `SuggestedAction` contract.
- **Dependencies:** Task 5; `CapabilityGuidanceContracts`; `IConversationalCapabilityRegistry`.
- **Exact behavior:** Use `RunRelatedCapability`, capability `monthly_product_trend`, canonical `DisplayTitle`, and company symbol `TseSymbol ?? Ticker ?? CompanySymbol`. Use the same query for `LocalizedLabel` and `Message`; set company/product preset slots and reason `monthly_sales_product_follow_up`. If the canonical symbol is absent, return zero actions.
- **Acceptance criteria:** At most three actions are returned; titles and symbols are source-backed; no company display-name fallback or LLM-generated text is used.
- **Tests:** Label/message equality, preset slots, long-title bounds, Unicode titles, missing symbol, and maximum-count tests.

## Task 7 — Version and identify actions using the existing registry

- **Goal:** Make action identity stable without adding a versioning subsystem.
- **Implementation scope:** Use `IConversationalCapabilityRegistry.Version` as `SuggestedAction.RegistryVersion` and the Feature 137 action version.
- **Dependencies:** Task 6; existing capability registry behavior.
- **Exact behavior:** Derive a bounded SHA-256-based ID from `feature137`, `monthly_product_trend`, registry version, canonical external company ID, and `ProductKey`. Do not use raw Persian text. A registry/policy change that changes action semantics must increment the existing registry version and intentionally changes IDs.
- **Acceptance criteria:** Identical canonical inputs produce identical IDs; changed company/product/version inputs produce different IDs; all IDs satisfy existing length bounds.
- **Tests:** Stable-ID, changed-version, changed-company, changed-product, collision-format, and bound tests.

## Task 8 — Attach the selector to the V2 company-trend seam

- **Goal:** Activate Feature 137 only after a successful usable company trend result.
- **Implementation scope:** Update `FinancialCopilotWorkflowDefinition` result computation and workflow messages.
- **Dependencies:** Tasks 1–7; `MonthlyActivityTrendResponse`.
- **Exact behavior:** When detected intent is `MonthlyActivityTrend`, the typed result is usable, and the dialogue outcome is successful, invoke the selector. Put the resulting collection on `ResultsComputedMessage` and set `Feature137SuggestionsApplied = true`. Use an explicit empty collection when no candidates exist. Do not invoke for product trend, product comparison, revenue mix, failed, clarification, no-data, or unrelated results.
- **Acceptance criteria:** Activation depends on the resolved typed capability/result, not exact Persian wording; company text, chart, billing, outcome, and calculations remain unchanged.
- **Tests:** V2 routing matrix covering equivalent company-trend phrasings, product trend, comparison, revenue mix, semantic routing, failure, clarification, and no-data paths.

## Task 9 — Make `MessagePersistenceFunction` accept authoritative actions

- **Goal:** Prevent generic guidance from dropping or overwriting Feature 137 actions.
- **Implementation scope:** Add the deterministic action collection and `Feature137SuggestionsApplied` marker to the persistence input path.
- **Dependencies:** Task 8; `MessagePersistenceFunction`; `CapabilityGuidanceService`.
- **Exact behavior:** For `Feature137SuggestionsApplied = true`, persist the supplied one-to-three actions or explicit empty collection and skip `CapabilityGuidanceService.Suggest`. For false, retain the existing generic guidance behavior unchanged.
- **Acceptance criteria:** A successful company trend cannot have Feature 137 actions replaced by generic guidance; zero eligible products persists zero Feature 137 actions and remains successful.
- **Tests:** Persistence-function tests for one-to-three actions, empty applied set, failed/clarification responses, and unrelated capabilities.

## Task 10 — Propagate actions through persistence and API results

- **Goal:** Preserve the authoritative action collection end to end.
- **Implementation scope:** Carry the marker and collection through `PersistenceCompletedMessage`, `AssistantMessagePayload`, `AiQueryResponse`, and existing `AiFacadeController` mapping.
- **Dependencies:** Tasks 8–9; existing conversation and API contracts.
- **Exact behavior:** Persist the existing structured `SuggestedActions` field; return it through the existing API response; preserve backward decoding of old payloads and nullable generic-guidance behavior outside Feature 137.
- **Acceptance criteria:** The API returns the same action IDs/messages as the selector, and an explicit empty Feature 137 set is not converted to generic fallback actions.
- **Tests:** Workflow-message, conversation serialization/reload, API JSON, old-payload decode, and action-ID correlation tests.

## Task 11 — Preserve V1 and existing generic guidance behavior

- **Goal:** Enforce the V2-only boundary without changing V1 guidance.
- **Implementation scope:** Add V2-only wiring and regression coverage; do not add a V1 selector, parser, route, DTO, or response branch.
- **Dependencies:** Tasks 8–10; `specs/POLICY-V1-FREEZE.md`.
- **Exact behavior:** V1 receives no Feature 137 product follow-up actions. Existing V1 generic `CapabilityGuidanceService` behavior remains unchanged.
- **Acceptance criteria:** Switching to V1 leaves existing company trend output and generic guidance behavior unchanged while omitting only Feature 137 product actions.
- **Tests:** V1 regression and V1/V2 capability-difference tests.

## Task 12 — Verify existing web click and Telegram rendering

- **Goal:** Reuse existing structured action consumers.
- **Implementation scope:** Verify `chat.functions.ts`, `message-list.tsx`, `SuggestedActionId` request handling, and `TelegramAssistantResponseRenderer`.
- **Dependencies:** Tasks 6 and 10.
- **Exact behavior:** Web displays `LocalizedLabel`, submits exact `Message` plus `Id`, and does not parse prose. Telegram renders the same bounded structured action metadata and does not recalculate candidates.
- **Acceptance criteria:** Existing company chart and answer rendering remain unchanged; clicked actions enter normal V2 product routing.
- **Tests:** Frontend click/mapping test, API click-correlation test, and Telegram renderer regression.

## Task 13 — Add observability and non-critical fallback behavior

- **Goal:** Measure selection quality without changing the company answer.
- **Implementation scope:** Add bounded metrics/traces around the selector and V2 seam.
- **Dependencies:** Tasks 3–8; existing activity/telemetry infrastructure.
- **Exact behavior:** Record invocation, duration, anchor period, candidate/eligible/returned counts, exclusion reason counts, zero-action reason, and action attribution. Hash or redact product keys/external IDs in ordinary logs. Read failure, timeout, cancellation after the company result, or malformed candidate returns zero actions and preserves the company response.
- **Acceptance criteria:** Selector failure is observable and non-critical; no raw user prompt, provider call, or sensitive identifier is required in ordinary logs.
- **Tests:** Telemetry assertions for no anchor, ambiguity, parser rejection, read failure, success, and zero-action fallback.

## Task 14 — Complete focused identity, ranking, anchor, and performance tests

- **Goal:** Prove deterministic behavior and bounded execution.
- **Implementation scope:** Add unit/repository/integration coverage for the selector and read contract.
- **Dependencies:** Tasks 1–7 and 13.
- **Exact behavior:** Cover stable identity, renamed title, duplicate display titles, parser-unsafe titles, common anchor, later-period exclusion, header-only period, no anchor, all ranking tie-breaks, null/zero sales, and unsupported/service-only companies.
- **Acceptance criteria:** Tests prove no N+1, no per-candidate Feature 136 execution, no provider calls, no independently latest product periods, and repeated-run deterministic output.
- **Tests:** Mock repository call-count assertions, query-shape tests, selector matrix, and relational repository tests.

## Task 15 — Run end-to-end regression and rollout verification

- **Goal:** Confirm compatibility and safe rollout.
- **Implementation scope:** Run focused .NET/frontend tests, existing Feature 076/077/078/129/136 regressions, and the API validation sequence.
- **Dependencies:** Tasks 8–14.
- **Exact behavior:** With V2 active, submit a company trend query, verify unchanged chart/text plus zero-to-three actions, click one action, and verify typed product trend routing. Repeat with no product data, ambiguous titles, parser-unsafe titles, service-only, and unresolved companies. If a supported V2 rollout flag exists, verify flag-off leaves company trend operational.
- **Acceptance criteria:** All 22 ACs pass; no production migration or V1 change is required; Feature 136 production behavior is unchanged.
- **Tests:** Full targeted suite followed by the repository’s normal .NET and frontend validation commands.
