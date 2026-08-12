# Feature 125 — Slice 5 Review

## Verdict

APPROVED

Slice 5 remediation is complete. Slice 6 was not started.

## Remediation verification

### 1. Canonical resolution and Feature 119 reuse

Feature 125 no longer owns canonical company or industry matching. The former Feature-125
resolver was replaced by `IndustryRelativeValuationSemanticAdapter`:

`src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Adapters/IndustryRelativeValuationSemanticResolver.cs`

The adapter consumes `ICanonicalQueryEntityResolver` and the canonical industry-resolution
authority, then performs only Feature 125 composition and membership validation. Canonical
industry resolution is implemented alongside the Feature 119 resolver and registered through the
existing resolver service:

- `CanonicalQueryEntityContracts.cs`: `ICanonicalQueryIndustryResolver` and typed outcomes.
- `CanonicalQueryEntityResolver.cs`: canonical industry resolution with exact, ambiguous,
  variant, not-found, and missing outcomes.
- `ServiceCollectionExtensions.cs`: adapter and canonical resolver registrations.

Covered outcomes include symbol-only, industry-only, symbol-plus-industry, same-industry pair,
different-industry pair, ambiguity, missing, not-found, and membership mismatch. Different
industries never reach the read executor.

### 2. Feature 120 clarification and replay

`ConversationDialogueGate` now maps all Feature 125 unresolved outcomes to validated slots and
persists a Feature 120 `PendingDialogueAction`. Candidate canonical IDs are stored as
`ConversationTaskSlot` candidates. The pending action records expected slot, reason, capability,
and state version; task-switch handling is delegated to the existing
`ConversationTaskStateService` optimistic state transition.

Existing replay and stale-version protections remain authoritative. The gate avoids overwriting a
candidate-bearing Feature 125 pending action during outcome recording. One-turn candidate
selection clears the pending action through `ResolveFollowUpAsync`; task switches do not carry
stale slots.

Relevant code:

- `ConversationTaskStateContracts.cs`
- `CanonicalQueryEntityContracts.cs`
- `ConversationTaskStateServiceTests.cs`

### 3. Executor boundary

The executor accepts canonical IDs and configured limits only. It calls only
`IIndustryRelativeValuationReadRepository`; it has no provider, calculation, SQL, or raw
persistence dependency. The repository selects only `Published` and `IsSelectedCurrent` rows.
Same-industry membership is enforced by the adapter and the repository rejects requested members
not present in the selected snapshot.

Limit behavior now uses `IndustryRelativeValuation:DefaultResultLimit` and
`IndustryRelativeValuation:MaximumResultLimit`:

- absent limit → configured default;
- `1..maximum` → accepted;
- above maximum or below one → `ClarificationRequired` with
  `result_limit_exceeded`;
- invalid default/maximum configuration → startup validation failure.

### 4. Read model

The read contract now exposes:

- rank and rank version;
- total members and total ranked members;
- P/E, P/S, and equilibrium percentages;
- benchmark values and benchmark quality/counts;
- classification and persisted quality/reason values;
- outlier state and reason;
- calculation and publication timestamps;
- source observation IDs, source versions, source timestamps, persisted timestamps, and
  watermarks;
- barrier status and readiness status;
- insufficient benchmark reason.

Presentation consumes these persisted projections and does not recompute quality, rank, average,
or classification semantics.

### 5. Presentation

`IndustryRelativeValuationPresentation` now provides deterministic Persian and English templates
for all four capabilities:

- symbol versus industry comparison;
- industry ranking;
- industry summary;
- symbol pair comparison.

Templates include rank/total, metric percentages, benchmark comparison, persisted classification,
unavailable metric explanations, outlier explanations, benchmark insufficiency, freshness and
publication context. They contain no buy/sell recommendation or unsupported investment advice.

### 6. Tests added and verified

Added or extended coverage for:

- Feature 119 canonical industry ambiguity/not-found outcomes;
- Feature 125 adapter reuse, candidate IDs, industry-only resolution, same-industry derivation,
  and different-industry rejection;
- Feature 120 candidate persistence and candidate selection;
- replay, task-switch, optimistic-version, and stale-state behavior through the existing task
  state suite;
- published/read-only executor behavior;
- read limits: default, maximum, above-maximum rejection, and configuration validation;
- Persian and English presentation;
- unavailable metrics, outliers, and insufficient benchmarks;
- all four capability registrations and semantic governance coverage.

Verification results:

```text
Feature 125 unit filter: 50 passed, 0 failed
Semantic governance tests: 8 passed, 0 failed
Semantic/API integration filter: 12 passed, 0 failed
FinancialCopilot.API Release build: 0 warnings, 0 errors
```

The full unit suite still has one unrelated pre-existing flaky authentication test failure in
`CyclicalWavesAuthHandlerTests.Response401_TriggersReloginAndRetry`; it is outside Slice 5.

### 7. Slice 6 boundary

No Slice 6 implementation was added. Slice 6 remains only as planned T32–T40 work in the feature
planning documents. No operational, deployment, migration-review, or Slice 6 handoff work was
started.
