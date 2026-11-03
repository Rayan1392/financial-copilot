# Feature 137 — Design Review

## 1. Verdict

**NEED_CHANGES**

Feature 137 is narrowly scoped and uses the correct existing product-sales and
response abstractions, but three major implementation contracts are not yet
closed against the current codebase. The feature should not be approved for
implementation until the action persistence seam, product identity/title
contract, and bounded action-query validation are made explicit.

## 2. Finding counts

| Severity | Count |
|---|---:|
| Blocker | 0 |
| Major | 3 |
| Minor | 2 |
| Note | 1 |

## 3. Architecture verification

The repository architecture matches the intended V2-only shape:

- `MonthlyActivityTrendQueryUseCase` resolves the company and returns a typed
  `MonthlyActivityTrendResponse` from persisted company snapshots.
- `FinancialCopilotWorkflowDefinition` has separate deterministic branches for
  company monthly activity, product trend, product comparison, and revenue mix.
- The V2 workflow already carries `SuggestedActions` through workflow messages,
  conversation payloads, API mapping, frontend mapping, and Telegram rendering.
- Development configuration selects `MicrosoftAgentFrameworkV2`; the V1
  orchestration service remains a separate rollback path.

The required integration seam is after a usable typed company-trend result is
available and before persistence. Activation must inspect the typed
`MonthlyActivityTrendResult`, not re-match one Persian phrase.

### Major M-01 — action eligibility cannot currently be validated within the specified query shape

**Spec sections:** Design §7 rule 5, Design §15, Design §20, Tasks 1.3 and 4.3.

Feature 137 requires every generated title query to resolve through
`MonthlyProductTrendQueryUseCase`, while also prohibiting repeated product-trend
execution and product-by-product N+1 work. The current Feature 136 use case does
not provide a bounded single-candidate resolver:

- it calls `GetAvailablePeriodsAsync`;
- then calls `GetPeriodAsync` once for every available period; and
- only then builds the cross-period candidate set and applies title matching.

This is visible in
`src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/MonthlyProductTrendQueryUseCase.cs:19-42`.
Calling it once per candidate would violate Design §15. Reusing only the
anchor-period rows does not prove the same cross-period identity and ambiguity
semantics as Feature 136. The design also does not define how parser-unsafe
titles are excluded, even though `MonthlyProductTrendIntentRules` removes
stop-words and extracts the final token as the company.

**Why implementation is ambiguous:** an implementer cannot simultaneously
prove AC-5/AC-9, preserve Feature 136 resolution behavior, and meet the
bounded-query requirement from the currently specified interfaces.

**Minimal correction:** define one shared, bounded identity-resolution contract
for the selector and Feature 136, or define a repository query that returns the
bounded company candidate universe plus the exact canonical title/key used by
Feature 136. The contract must explicitly reject titles that cannot round-trip
through `MonthlyProductTrendIntentRules` and must not invoke the full product
trend use case once per candidate.

### Major M-02 — renamed-product title semantics conflict with Feature 136

**Spec sections:** Design §§7–9 and §13 (“Renamed product”), AC-3, AC-5, and
Tasks 1.3, 2.3, and 5.1.

Feature 137 says to keep the latest anchor-period title for a stable provider
identity and use that title in the action query. Feature 136 currently groups
candidate observations by `ProductKey` and selects the lexicographically first
`DisplayTitle`/unit across all loaded periods:

`MonthlyProductTrendQueryUseCase.cs:29-38`.

Therefore, a title renamed in the latest period can be emitted by Feature 137
but not be the canonical title exposed to the Feature 136 resolver. The action
can become `NotFound` or resolve differently, contrary to the “every suggested
product is queryable” requirement.

**Why implementation would fail:** the specification simultaneously requires a
latest title and reuse of a resolver whose current canonical display title is
not latest-period based.

**Minimal correction:** choose one canonical display-title policy and state it
as shared behavior. The least invasive option is for Feature 137 to use the
exact Feature 136 resolver-selected title and revise the renamed-product rule
accordingly. If “latest title” is mandatory, Feature 136 must first expose a
shared latest-title resolver and receive its own regression coverage; that is a
dependency change, not a self-contained Feature 137 change.

### Major M-03 — current V2 persistence drops feature-specific actions

**Spec sections:** Design §§4, 10, 11, and 20; Tasks 3.1–3.3.

The transport types exist, but the current implementation does not yet have the
required ownership path:

- `MessagePersistenceFunction.PersistAsync` has no feature-action parameter.
- It always calls `ICapabilityGuidanceService.Suggest`.
- `CapabilityGuidanceService.Suggest` returns no actions for an answered
  dialogue.
- `ExecuteResultComputationStep` currently emits no `SuggestedActions`, and
  `ExecutePersistenceStepAsync` passes no generated actions to persistence.

Evidence: `MessagePersistenceFunction.cs:56-66`,
`CapabilityGuidanceContracts.cs:41`, and
`FinancialCopilotWorkflowDefinition.cs:879-895` plus `932-955`.

**Why implementation would fail:** a selector can be added and the existing
DTOs can serialize actions, but the normal successful company-trend response
will still persist and return an empty action collection unless the persistence
function is changed.

**Minimal correction:** specify that the V2 result-computation step constructs
the deterministic Feature 137 actions, passes them through
`ResultsComputedMessage` and `PersistenceCompletedMessage`, and supplies them
to `MessagePersistenceFunction`. Define precedence explicitly: Feature 137
actions are preserved for a successful `MonthlyActivityTrend` result; generic
guidance remains available for other outcomes/capabilities; no generic
guidance call may overwrite the deterministic collection.

### Minor M-04 — canonical company-symbol precedence is not named

**Spec sections:** Design §§6 and 9; AC-9.

`ResolvedCompany` exposes `TseSymbol`, `Ticker`, and `CompanySymbol`, while the
existing Feature 136 result uses a specific fallback chain for its company
symbol. Feature 137 only says `ResolvedCompanySymbol`, so an implementation
could put a company name or a different symbol variant into the generated
query. This is straightforward to correct but should be explicit for stable
round-tripping.

**Minimal correction:** mandate the same canonical symbol precedence used by
Feature 136 (`TseSymbol ?? Ticker ?? CompanySymbol`, subject to the existing
normalization rules) for the action message and preset slots; return zero
actions if no canonical symbol is available.

### Minor M-05 — V1 acceptance wording is broader than the existing contract

**Spec section:** Tasks §4.2.

The current V1 persistence path already uses `CapabilityGuidanceService` for
generic clarification/help actions. Saying that V1 “returns no fabricated or
LLM-generated suggestions” is therefore ambiguous if read as a statement about
all `SuggestedAction` values.

**Minimal correction:** scope the criterion to “V1 returns no Feature 137
product follow-up actions”; retain existing generic guidance behavior.

### Note N-01 — action-version source should be named

**Spec section:** Design §9.

The existing action contract exposes `RegistryVersion`, while the design asks
for IDs stable by an “action version.” The implementation can use a fixed
selector-policy version or the capability registry version, but the chosen
source should be recorded so ID stability is testable across registry changes.

## 4. Feature 136 dependency verification

The following dependency claims are confirmed:

- Accepted source filtering is correct: the repository uses
  `ReportType == "ProductSales"`, `OutputType == 0`, and `IsAccepted` in both
  period discovery and line-item reads.
- Company ownership is available through `ICompanyResolverService` and
  `ResolvedCompany.ExternalCompanyId`.
- Product identity is company/provider scoped and uses provider product code,
  positive provider ID, then normalized title plus unit through
  `MonthlyProductTrendCalculator.ProductKey`.
- Feature 136 treats one period containing a non-null `SalesAmount` as enough;
  missing older periods are gaps. Feature 137 correctly reuses this minimum
  rather than inventing a history count.
- Zero sales is a valid observed value in the current model and must remain
  eligible when it ranks within the top three.

The dependency is therefore valid, but the shared title/resolution policy and
bounded resolver identified in M-01/M-02 must be made explicit before use.

## 5. SuggestedAction reuse verification

**Valid: YES.**

`SuggestedAction` already contains the required kind, label, executable
message, capability code, preset slots, relevance reason, and registry version.
`SuggestedActionHttpResponse`, `AssistantMessagePayload.SuggestedActions`,
the API mapper, web click handler, action ID correlation, and Telegram renderer
already support the transport. No parallel `SuggestedPrompt` abstraction is
needed.

The reuse is additive and serialization-compatible. The missing part is not a
new contract; it is the explicit V2 ownership and persistence wiring in M-03.

## 6. Ranking algorithm assessment

**Valid: YES, subject to the corrections above.**

Ranking by checked aggregate `SalesAmount` in a single anchor period is
consistent with the source units, the company sales trigger, and the Feature
136 product observation model. The design correctly excludes null values,
retains zero values, groups by canonical `ProductKey`, and limits output to
three. It does not introduce a recommendation score or LLM choice.

## 7. Exact latest-period interpretation

The review chooses **A — one common company period for all products**.

Precisely, the anchor is the newest accepted `ProductSales` report period with
`OutputType == 0` and usable product rows that is not later than the company
trend response’s `LatestReportYear/LatestReportMonth`. All candidates are
aggregated and ranked within that same anchor. Each product must not use its
own independently latest period, because that would compare products from
different reporting dates and could surface stale or non-comparable follow-ups.

If no qualifying product-sales period exists at or before the company period,
the company response succeeds with zero actions.

## 8. Deterministic tie-breaking assessment

**Defined: YES.**

The design specifies:

1. `SalesAmount DESC`;
2. normalized canonical title ASC using ordinal comparison;
3. stable `ProductKey` ASC using ordinal comparison.

The title selected for a `ProductKey` must first be aligned with the shared
Feature 136 policy (M-02); otherwise the comparator is deterministic but may be
deterministically wrong for a renamed product.

## 9. V2-only decision assessment

**Valid: YES.**

The repository has an explicit `AiOrchestrationMode` with V1 and
`MicrosoftAgentFrameworkV2`, development configuration selects V2, and the V1
service remains a separate path. Adding Feature 137 only to the typed V2
company-trend result does not require V1 parity and does not require a new V1
route, parser, intent, DTO, or branch.

One wording correction is still required: Tasks §4.2 says V1 “returns no
fabricated or LLM-generated suggestions.” Existing V1 guidance can already
return generic clarification/help `SuggestedAction` values. That statement
must be scoped to “no Feature 137 product follow-up actions.”

## 10. Performance/query-shape assessment

The intended shape is acceptable: one company scope, one common anchor period,
bounded candidate fields, set-based aggregation, and no provider call. The
current read repository can support a first implementation with period discovery
plus one anchor-period read, but the selector must not call
`MonthlyProductTrendQueryUseCase` once per product. That would multiply the
existing per-period loop and violate Design §15.

The corrected design must state how cross-period identity/queryability is
validated in one bounded operation, as required by M-01. It should also define
that accepted revisions are filtered before aggregation and that a report with
no qualifying line items cannot become an anchor merely because its header
exists.

## 11. Acceptance-criteria review

| AC | Assessment |
|---|---|
| AC-1 | Testable after the V2 persistence seam is specified. |
| AC-2 | Testable and bounded to three. |
| AC-3 | Needs the shared title/display identity correction in M-02. |
| AC-4 | Testable; backend-only candidate construction is clear. |
| AC-5 | Needs the bounded resolver and parser-safe eligibility contract in M-01. |
| AC-6 | Consistent with Feature 136’s one-valid-sales-value minimum. |
| AC-7 | Testable; tie-break sequence is specified. |
| AC-8 | Testable and consistent with the selected common-period interpretation. |
| AC-9 | Needs an explicit canonical company-symbol precedence and shared product-title policy. |
| AC-10 | Testable; zero actions is a normal success. |
| AC-11 | Testable; company chart fields are additive-safe. |
| AC-12 | Testable regression protection. |
| AC-13 | Testable regression protection. |
| AC-14 | Supported by the existing structured action transport. |
| AC-15 | Testable through typed `MonthlyActivityTrendResult` activation. |
| AC-16 | Testable; the selector must not be called from phrase matching. |
| AC-17 | Testable; no direct LLM/database path is required. |
| AC-18 | Testable; V1 remains unchanged and V2 gains additive metadata. |
| AC-19 | Testable and consistent with the maximum of three. |
| AC-20 | Testable after canonical-key/title selection is shared. |
| AC-21 | Testable with service-only, unresolved, and no-line-item cases. |
| AC-22 | Compatible with the existing optional API field. |

No AC is inherently infeasible, but AC-3, AC-5, and AC-9 depend on resolving
M-01/M-02 and AC-1 depends on resolving M-03.

## 12. Tasks completeness review

Tasks.md covers the major work areas: repository predicates, identity reuse,
ranking, action construction, V2 transport, V1 regression, click-path tests,
frontend/Telegram verification, observability, and rollout.

It is not fully complete as an executable plan because:

- Tasks 1.3/4.3 do not define the bounded implementation of “would resolve”
  eligibility (M-01).
- Tasks 1.3/5.1 mention renamed products but do not choose whether the latest
  title or Feature 136’s current canonical title wins (M-02).
- Task 3.1 names `MessagePersistenceFunction` but does not state how generated
  actions bypass/override its current generic guidance generation (M-03).
- Task 4.2 needs the V1 wording correction described above.

## 13. Required changes

1. Add a bounded, shared product identity/queryability contract and parser-safe
   eligibility rule; prohibit per-candidate Feature 136 execution.
2. Align Feature 137’s canonical display-title policy with Feature 136,
   especially for provider-identity renames, or explicitly revise Feature 136
   first as a dependency.
3. Specify and test the V2 action ownership/persistence path, including
   precedence over generic `CapabilityGuidanceService` output.
4. Specify the canonical company-symbol field/precedence used in generated
   labels, messages, and preset slots.
5. Narrow Tasks §4.2 to the absence of Feature 137 actions in V1.

## 14. Final assessment

- Feature 136 reuse valid: **YES**
- SuggestedAction reuse valid: **YES**
- Ranking algorithm valid: **YES**
- Latest-period interpretation: **A — common accepted company period**
- Deterministic tie-break defined: **YES**
- V2-only decision valid: **YES**
- Implementation-ready: **NO**

FEATURE_137_NEEDS_CHANGES

## Final Re-review

This is a targeted re-review of the six findings above. The historical review
findings and assessments are unchanged.

| Check | Status | Verification |
|---|---|---|
| Previous verdict | NEED_CHANGES | Historical verdict retained. |
| Current verdict | READY | All previous findings are resolved in the corrected specification; no new blocker or major was identified. |
| M-01 | RESOLVED | Design §§4, 7, 15 and Tasks 1–4 define one company-scoped set-based candidate read, shared Feature-136-compatible identity/title matching, parser-safe round-trip validation, no per-candidate `MonthlyProductTrendQueryUseCase`, no N+1 loop, and no provider/API call. |
| M-02 | RESOLVED | Design §§3, 7–9 and Task 2 require the exact shared Feature-136-compatible canonical `DisplayTitle`; renamed products do not use a latest-period title policy. |
| M-03 | RESOLVED | Design §11 and Tasks 8–10 define `selector -> ResultsComputedMessage -> MessagePersistenceFunction -> AssistantMessagePayload/conversation persistence -> PersistenceCompletedMessage -> API/controller -> existing clients`. Successful Feature 137 actions are authoritative, generic guidance is skipped, explicit empty actions remain empty, and unrelated guidance is unchanged. |
| M-04 | RESOLVED | Design §§4, 9, 22 and Task 6 require `TseSymbol ?? Ticker ?? CompanySymbol`; display name is not a fallback. |
| M-05 | RESOLVED | Design §11, AC-18/AC-22, and Task 11 limit the V1 statement to no Feature 137 product follow-up actions while preserving existing generic V1 guidance. |
| N-01 | RESOLVED | Design §9 and Task 7 use the existing `IConversationalCapabilityRegistry.Version` as `SuggestedAction.RegistryVersion`; bounded IDs are derived from canonical versioned inputs and remain stable until those inputs or the registry version changes. |
| New blocker count | 0 | Narrow regression sanity check found none. |
| New major count | 0 | No material inconsistency was found in Feature 136 reuse, action reuse, anchor/ranking, zero-action behavior, V2-only scope, acceptance criteria, or task executability. |
| Implementation-ready | YES | The corrected specification is implementation-ready. Production implementation remains pending and is covered by Tasks 1–15. |

### Narrow regression sanity check

- Feature 136 reuse remains behavior-preserving: the existing `ProductKey`,
  canonical title selection, ambiguity behavior, accepted-data predicates, and
  one-valid-observation minimum are named as shared semantics.
- Existing `SuggestedAction` and its API, conversation, web, and Telegram
  transport are reused; no parallel action contract is introduced.
- The anchor is one newest qualifying accepted `ProductSales`/`OutputType = 0`
  period not later than the company trend period. Header-only periods are not
  anchors, and all candidates use that common period.
- Ranking is deterministic: aggregate `SalesAmount DESC`, canonical normalized
  title ordinal ascending, then `ProductKey` ordinal ascending, maximum three.
- A successful V2 company trend persists one-to-three actions or an explicit
  empty Feature 137 action set. Non-applicable and unrelated paths retain
  existing guidance behavior.
- Feature 137 remains V2-only; the 22 acceptance criteria and 15 implementation
  tasks are internally consistent and executable.

The current repository implementation still contains the pre-Feature-137
generic persistence call and the existing Feature 136 per-period query loop.
Those are the implementation seams the corrected design and Tasks 1–10
explicitly address; they are not new review findings because this re-review
was limited to specification readiness and production-code changes were
out of scope.
