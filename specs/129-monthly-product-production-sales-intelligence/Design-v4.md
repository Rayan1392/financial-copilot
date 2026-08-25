# Feature 129 — Monthly Product Production and Sales Intelligence

## 1. Document status and purpose

**Status:** `READY_FOR_DESIGN_REVIEW`  
**Revision:** v4  
**Repository discovery date:** 2026-08-24  
**Scope:** Design only. This document authorizes no application code, migration execution, provider call, test change, configuration change, or production-data change.

Feature 129 explains why a company's reported monthly product revenue changed. It ingests immutable source observations, deterministically accepts report revisions, compiles complete product ownership versions, calculates a seven-bucket price/quantity attribution, publishes immutable snapshots, and serves bounded structured results through the shared AI facade. The model may propose a capability and typed slot candidates; it cannot calculate, choose a database route, or invoke an executor with unvalidated values.

## 2. Design-v3 Review Resolution Matrix

| Finding | Resolution | Design-v4 sections | ACs | Tests | Slice | Status |
| --- | --- | --- | --- | --- | --- | --- |
| V3-B-01 | Draft edits compile into one complete immutable ownership version. The current pointer and lookup projection contain rows from exactly one version; snapshots reference that version. | 11–13, 21 | AC-16–AC-27 | T-ALIAS-01–08 | 2–3 | Resolved |
| V3-M-01 | A closed dispatch policy rejects Feature 129 at the direct scheduler before persistence; a dedicated queue/dispatcher/consumer ACKs after terminal persistence. | 18–20, 37 | AC-48–AC-57 | T-ORCH-01–10 | 1, 3, 6 | Resolved |
| V3-M-02 | A concrete accepted manifest-generation pointer selects one immutable generation per provider/company/period/family and is included in job fingerprints. | 9–10, 20–21 | AC-08–AC-15, AC-58–AC-63 | T-MAN-01–08 | 1, 3 | Resolved |
| V3-M-03 | PostgreSQL immutable-history triggers, insert-only repositories, composite/deferred integrity constraints, `ON DELETE RESTRICT`, and cross-context soft-reference rules are selected. | 22–24, 38 | AC-28–AC-47 | T-IMM-01–08, T-PUB-01–08 | 1–3 | Resolved |
| V3-M-04 | Shared provider structured-output contracts remain root-level; a Feature-129-owned strict local validator is authoritative for nested proposals. | 29–31, 38 | AC-64–AC-72 | T-SEM-01–09 | 4 | Resolved |
| V3-N-01 | The impact map names migration/model-snapshot, dispatch, local-validator, dedicated transport, authorization/audit, and test touchpoints. | 38 | AC-78 | T-TRACE-01 | 1–6 | Resolved |
| V3-N-02 | The documentation-only diff condition is removed from runtime acceptance criteria and retained only in the document readiness checklist. | 36, 39 | AC-78 | T-TRACE-01 | Design gate | Resolved |
| V3-T-01 | Monetary-unit and conversion decisions have fixed publication gates `MonetaryUnitUnconfirmed` and `UnitConversionUnapproved`. | 25, 36 | AC-73–AC-77 | T-POLICY-01–03 | 2–4 | Resolved |

### 2.1 Previous review history

| Prior findings | Retained resolution |
| --- | --- |
| B-01 | Every provider row is an immutable observation; repeated codes and identical occurrences are retained and reconciled. |
| B-02 | A mutually exclusive state machine allocates every signed product contribution to one of seven buckets. |
| B-03 | Evidence copies observed values and references immutable report revisions/observations; it never depends on replaceable line items. |
| B-04 | Immutable report revisions, deterministic precedence, accepted pointer locking, ambiguity events, and late-older rejection are defined. |
| M-01 | A per-operation company-month manifest plus accepted current-generation pointer gates type 0. |
| M-02 | Feature 129 uses dedicated transactional outbox dispatch and a specialized handler; metric recalculation remains separate. |
| M-03–M-04 | Complete ownership versions, members, events, pointer, projection, compatibility gates, and lineage make matching reproducible. |
| M-05 | Immutable snapshot facts/events, current pointer, triggers, composite checks, `xmin`, and serializable publication are concrete. |
| M-06 | Lifecycle, identity, sign, quality, attribution availability, aligned driver mass, and cancellation are independent. |
| M-07 | Typed proposal values pass local strict validation, deterministic merge, canonical resolution, task state, and one V1/V2 frame. |
| M-08 | Result schema, payload v3, semantic replay equality, bounded evidence, Telegram fallback, and contribution transport are defined. |
| M-09 | Identity and revision foundations precede publication; supported backfill begins at 1404/01. |
| M-10 | Normative behavior is mapped to named ACs/tests/slices; design-only checks are separate from runtime ACs. |
| N-01 | Decimal precision, ToEven rounding, canonical fingerprinting, tolerance, and denominator reason codes are fixed. |
| T-01 | Core correctness is separated from later history, anomaly, visual, and optional endpoint work. |

## 3. Scope and non-goals

### 3.1 First-release scope

- ProductSales `OutputType=0` monthly facts from 1404/01 onward.
- Latest published month versus immediately previous Jalali month, plus explicit two-period comparisons when both accepted generations are available.
- Immutable raw-derived observations, report revisions, operation manifests, accepted manifest pointer, complete product ownership versions, reviewed unit policy, deterministic calculation, immutable evidence, atomic snapshot publication, semantic/API integration, conversation replay, Telegram text fallback, and accessible web contribution tables.
- Company/product totals, guarded changes, concentration, seven effect buckets, lifecycle/sign/identity/quality dimensions, contributors, coverage, cancellation ratio, freshness, warnings, and selected-product production-versus-sales visualization only when units are compatible.

### 3.2 Later slices

YoY, fiscal YTD, prior-year equivalent YTD, contiguous 3/12-month averages, longer history, robust anomalies, inferred inventory signals, plotted waterfall, image export, and an optional direct read endpoint. Dead-letter recovery and outbox operations are not deferrable once Feature 129 dispatch is enabled.

### 3.3 Non-goals

Forecasts, inventory-balance claims, target prices, investment advice, cross-company physical quantities, unapproved economic-unit conversion, provider calls or raw aggregation from an HTTP request, LLM arithmetic, and pre-1404 backfill without a separately approved archive source.

## 4. Repository-verified current state

| Area | Verified implementation | Consequence |
| --- | --- | --- |
| NADPCO | ProductSales 0–4 are requested independently; failures may be represented as null/empty slots. | Persist operation outcomes and distinguish valid empty from failure. |
| Raw payloads | `ProviderRawPayloads` are in `FinancialProviderDbContext`, separate from `FinancialIngestionDbContext`, with checksum uniqueness. | Use immutable ID/checksum soft references, not false cross-context FKs. |
| Normalizer | Current normalizer groups by line code, takes the last row, replaces children, and uses array position in fallback identity. | Feature 129 authority is revision/observation storage; compatibility rows are not historical evidence. |
| Company identity | Company uniqueness is provider plus external company identity. | New pointer keys include provider and external identity. |
| Boundary | Provider and monthly backfill coordination begin at 1404. | Feature 129 rejects months before 1404/01. |
| Feature scheduler | `FeatureRecalculationScheduler.ScheduleAsync` persists then directly publishes. | A dispatch policy guard must reject Feature 129 before either action. |
| Feature consumer | Current feature bus ACKs before invoking the handler. | Feature 129 uses a dedicated queue/consumer that ACKs only after handler persistence. |
| Feature jobs | Existing `FeatureComputationJobRow` has a unique idempotency key but no outbox/lease state. | Feature 129 adds dedicated job/outbox rows and does not alter unrelated direct behavior. |
| Semantic proposal | `AiStructuredOutputContract` validates only required root properties; no nested schema transport exists. | Keep shared provider contract root-level; Feature 129 owns strict local validation. |
| Dialogue | Deterministic interpreter is registered; hybrid/no-op proposal infrastructure exists. | Feature 129's proposal provider is governed and locally validated before frame creation. |
| Conversation | Payloads are serialized objects and history deserializes them; raw bytes are not retained. | Replay is canonical semantic equality, with persisted deterministic narrative. |
| Frontend | Chat transport has interfaces and existing structured trend rendering, but no Feature 129 discriminated result. | Add Feature 129 Zod/result mapping and server-value-only views. |

## 5. Architectural invariants

```text
raw payload
→ immutable operation attempt and manifest generation
→ accepted report revision and source observations
→ accepted current manifest-generation pointer
→ mutable draft alias changes
→ complete immutable ownership version/member set
→ current ownership-version pointer/projection
→ Feature 129 job + outbox in the same transaction
→ dedicated dispatcher → dedicated RabbitMQ queue → dedicated consumer
→ idempotent Feature 129 handler
→ immutable snapshot/items/evidence/events
→ current snapshot pointer
→ shared validated semantic frame → V1/V2/API/conversation/UI
```

1. No draft, mutable projection, current pointer, or latest-policy lookup is a calculation input.
2. Every accepted source row and every historical financial fact is immutable.
3. One snapshot references one complete immutable `AliasOwnershipVersionId`.
4. One accepted manifest pointer exists for each provider/company/period/dataset-family key.
5. Feature 129 has no path through the existing direct-publish queue.
6. Only valid typed slots reach an executor; the LLM cannot select a route or formula.
7. A failed/stale run never displaces the current published snapshot.

## 6. Functional and non-functional requirements

Resolve company through the canonical resolver; default to the current snapshot pointer and previous Jalali month; never skip a missing month; use only accepted ProductSales type-0 revisions for monthly values; expose the base/current product union and explicit unmatched items; return guarded percentages, effects, states, evidence, freshness, warnings, contributors, and limitations; and never call NADPCO from a read path.

Identical immutable source IDs, ownership version, policy versions, and calculation version produce identical values, ordering, warnings, reason codes, narrative, and fingerprint. Repository reads target p95 ≤300 ms and structured facade retrieval p95 ≤700 ms excluding model latency, with ≤100 products and ≤24 periods. Raw payloads, credentials, internal errors, and unvalidated semantic values remain server-side.

## 7. Immutable source observations

Each accepted operation payload parses every array element into `MonthlyReportSourceObservation`:

- fixed-order `CanonicalRowJson` with raw numeric lexemes retained;
- `EconomicSignature` over normalized identity/title/domestic-export/category/package/grade/unit, excluding ordinal/measures;
- `SourceFactFingerprint` over all facts and measures;
- deterministic `DuplicateOccurrence` among identical facts;
- `SourceRowDiscriminator` over provider, logical report, fact fingerprint, and occurrence;
- `RawArrayOrdinal` for evidence only.

Distinct rows sharing a code remain distinct. Identical rows remain separate occurrences and raise `DuplicateFactObserved`. Replay is a payload/semantic-multiset replay, not row deletion.

`RawRevenueTotal`, normalized revenue, and row count must match at `numeric(28,8)` or the candidate is `Blocked(RawNormalizationMismatch)` and cannot be accepted.

## 8. Immutable report revisions and acceptance

`MonthlyReportLogicalIdentity` is unique by provider, external company, report kind, output type, and Jalali month. It contains mutable `AcceptedRevisionId`, `xmin`, and update time. `MonthlyReportRevision`, source observations, receipt rows, and status/decision events are insert-only.

Under `Serializable` plus `pg_advisory_xact_lock(hashtextextended('f129-report|' || LogicalIdentityId,0))`, replay records a receipt; higher comparable provider revision wins; otherwise newer valid provider publication time wins; receipt time never outranks valid publication metadata; metadata-less conflicts become `Blocked(AmbiguousRevisionOrder)`; DataAdmin decisions append events. `40001`, `40P01`, and pointer concurrency conflicts retry at most three times. Compatibility `MonthlyReports`/`MonthlyReportLineItems` remain current projections for older features and are never evidence.

## 9. Manifest generations and accepted current pointer

### 9.1 Immutable generations

`CompanyMonthIngestionManifestGeneration` is immutable and has:

| Field | Contract |
| --- | --- |
| `Id` | GUID PK. |
| `ProviderName` | Required bounded provider code. |
| `ExternalCompanyId` | Required provider-scoped identity. |
| `JalaliYear`, `JalaliMonth` | Required year/month; month 1–12. |
| `DatasetFamily` | Required closed code, currently `ProductSalesMonthly`. |
| `GenerationNumber` | Monotone per business key. |
| `ManifestFingerprint` | Required SHA-256 unique per business key. |
| `AcceptedType0RevisionId` | Required for a core-ready generation; FK restrict. |
| `CompletenessState` | `CoreReady`, `PartialOptional`, `Blocked`, `Unavailable`. |
| `OperationSummaryJson` | Bounded immutable summary of operation outcomes. |
| `CreatedAtUtc` | Immutable receipt time. |

Alternate key `(ProviderName, ExternalCompanyId, JalaliYear, JalaliMonth, DatasetFamily, Id)` supports composite FKs. Unique `(ProviderName, ExternalCompanyId, JalaliYear, JalaliMonth, DatasetFamily, ManifestFingerprint)` prevents duplicate generations. Operation and attempt rows are immutable, keyed by generation/operation and generation/operation/attempt number.

ProductSales 0 is mandatory; valid empty is accepted only with a validated empty revision. Types 1–4 and ServiceSales are recorded separately; optional failure gives `PartialOptional`, while type-0 failure is `Blocked`.

### 9.2 Accepted current pointer

`CompanyMonthIngestionManifestCurrentPointer` is mutable and has:

| Field | Contract |
| --- | --- |
| `Id` | GUID PK. |
| `ProviderName`, `ExternalCompanyId` | Required provider/company scope. |
| `JalaliYear`, `JalaliMonth` | Required period. |
| `DatasetFamily` | Required report-family code. |
| `CurrentManifestGenerationId` | Required FK restrict to one immutable generation. |
| `AcceptedType0RevisionId` | Required composite FK to the generation's accepted revision. |
| `ManifestFingerprint` | Copied exact fingerprint. |
| `CoreReady` | Projection boolean; true only for accepted type-0 generation. |
| `UpdatedAtUtc` | Update timestamp. |
| `xmin` | Npgsql concurrency token. |

Business key uniqueness is `(ProviderName, ExternalCompanyId, JalaliYear, JalaliMonth, DatasetFamily)`. Alternate key `(ProviderName, ExternalCompanyId, JalaliYear, JalaliMonth, DatasetFamily, CurrentManifestGenerationId)` is referenced by generation-consistency FKs/triggers. A deferred constraint trigger verifies that pointer generation, accepted revision, fingerprint, provider, company, period, and family agree.

### 9.3 Selection transaction

1. Acquire `pg_advisory_xact_lock(hashtextextended('f129-manifest|' || ProviderName || '|' || ExternalCompanyId || '|' || JalaliYear || '-' || JalaliMonth || '|' || DatasetFamily,0))`.
2. Insert immutable operation attempts and candidate generation.
3. Evaluate all outcomes, accepted report revisions, raw reconciliation, and type-0 readiness.
4. Lock the current pointer with `SELECT ... FOR UPDATE`; if absent, create it only for a core-ready generation.
5. Reject a candidate whose accepted revision is no longer current or whose fingerprint is older/equal without a changed optional section. A later valid optional success may replace a partial generation without changing the type-0 revision.
6. Update `CurrentManifestGenerationId`, accepted type-0 revision, fingerprint, readiness, and `xmin` only when the candidate is the deterministic accepted generation.
7. Insert/reuse the Feature 129 job and outbox using the exact generation ID and fingerprint.
8. Commit atomically.

Concurrent equal fingerprints converge to one pointer/job/outbox. Concurrent different candidates serialize under the lock; the loser revalidates and becomes stale, with no second distinct job for the same accepted fingerprint. A failed optional retry remains explicitly partial; a late success creates a new generation and schedules recalculation. A failed type-0 retry cannot make the pointer ready. Pointer update conflicts retry three times with bounded jitter.

## 10. Canonical products and unit policy

Canonical products are company-scoped. Blank/zero vendor IDs are absent; positive IDs require compatible economic signatures covering unit dimension, domestic/export, category, package, grade, and quality. Matching order is approved ownership member, compatible collision-free vendor ID, exact economic signature, prior approved signature, then manual review. Text similarity alone cannot merge material revenue.

Raw unit text remains alongside governed `UnitCode`, `Dimension`, and immutable `ProviderUnitPolicyVersion`. Version 1 allows no physical conversion. A reviewed policy may permit exact same-dimension conversions. Unsupported conversion leaves monetary contribution available but suppresses quantity/rate and production/sales comparisons.

## 11. Complete immutable alias ownership versions

### 11.1 Drafts, versions, members, events, and pointer

`CompanyProductAliasChangeDraft` is a mutable administrative workspace. It contains draft ID, company/provider, base `AliasOwnershipVersionId`, proposed add/change/remove/range/merge/split/reversal operations, actor, reason, draft version, `xmin`, and status (`Draft`, `Submitted`, `Rejected`, `Compiled`). Draft rows are never calculator inputs and are not referenced by snapshots.

`CompanyProductAliasOwnershipVersion` is immutable and represents the **complete** ownership mapping for one company/provider over a declared supported effective-period domain:

| Field | Contract |
| --- | --- |
| `Id` | GUID PK, `AliasOwnershipVersionId`. |
| `ExternalCompanyId`, `ProviderName` | Required scope. |
| `SupportedMonthRange` | Non-empty `int4range`, bounded by supported history. |
| `VersionNumber` | Unique per company/provider. |
| `ParentVersionId` | Nullable self-FK restrict. |
| `ContentFingerprint` | Required unique per company/provider. |
| `AlgorithmVersion`, `CreatedAtUtc` | Immutable provenance. |

`CompanyProductAliasOwnershipVersionMember` is immutable and has member ID PK, version ID FK restrict, canonical product ID FK restrict, provider alias key, economic signature, effective month range, unit/dimension/package attributes, evidence, and method/confidence. Composite alternate key `(OwnershipVersionId, MemberId)` supports FKs. Membership uniqueness is `(OwnershipVersionId, ProviderName, ProviderAliasKey, EffectiveMonthRange)` and a GiST exclusion constraint rejects overlapping ranges for the same version/provider/alias:

```sql
EXCLUDE USING gist
("OwnershipVersionId" WITH =,
 "ProviderName" WITH =,
 "ProviderAliasKey" WITH =,
 "EffectiveMonthRange" WITH &&)
```

The Feature 129 foundation migration runs `CREATE EXTENSION IF NOT EXISTS btree_gist;` before creating this constraint; extension creation is part of migration preflight and is not delegated to application startup.

The version/member company/provider values are enforced with composite FKs and a deferred consistency trigger. Every approved version independently reproduces the full mapping for its supported domain; it is never assembled from several revisions at calculation time.

`CompanyProductAliasOwnershipEvent` is immutable append-only history for `DraftCompiled`, `Approved`, `Rejected`, `Replaced`, `Merge`, `Split`, `Reversal`, `Retirement`, and `Reactivation`. It has event ID PK, company/provider/version FKs, event type, actor/reason/time, and unique `(ProviderName, ExternalCompanyId, DecisionIdempotencyKey)`.

`CompanyProductAliasOwnershipCurrentPointer` is mutable and has pointer ID PK, provider/company key, current `AliasOwnershipVersionId` composite FK, updated time, and `xmin`. Unique `(ProviderName, ExternalCompanyId)` allows exactly one approved complete version per company/provider.

### 11.2 Current lookup projection

`CurrentApprovedCompanyProductAliasOwnership` is an optional denormalized query projection. It has ID PK, provider/company, alias key, effective range, canonical product ID, `AliasOwnershipVersionId`, `MembershipId`, timestamps, and `xmin`. Every row has a composite FK to `(ProviderName, ExternalCompanyId, AliasOwnershipVersionId)` on the current pointer and to `(AliasOwnershipVersionId, MembershipId)` on the immutable member. A deferred consistency trigger verifies that the projection version equals the pointer-selected version and the member's company/provider/range/product.

The projection has the cross-company/provider/range exclusion constraint:

```sql
EXCLUDE USING gist
("ExternalCompanyId" WITH =,
 "ProviderName" WITH =,
 "ProviderAliasKey" WITH =,
 "EffectiveMonthRange" WITH &&)
```

It is replaced entirely for one company/provider inside the approval transaction. It can never contain rows from multiple ownership versions. Superseded versions/members remain in immutable history but cannot enter the current projection after pointer replacement.

### 11.3 Draft compilation and approval transaction

1. Acquire company/provider advisory lock `hashtextextended('f129-alias|' || ProviderName || '|' || ExternalCompanyId,0)`.
2. `SELECT ... FOR UPDATE` the current ownership pointer.
3. Load the previous complete ownership version and the submitted draft.
4. Apply draft changes in memory to the previous full member set; materialize every unchanged and changed effective membership into a new complete candidate version.
5. Validate company/provider consistency, product compatibility, collisions, non-empty ranges, supported domain coverage, GiST overlap, lineage, permissions, and draft idempotency.
6. Insert the immutable version header and all immutable members.
7. Append approval and merge/split/reversal/retirement/reactivation events.
8. Delete/insert only the mutable current lookup projection rows for this company/provider, with all new rows referencing the candidate version; do not mutate or delete historical versions/members.
9. Update `CompanyProductAliasOwnershipCurrentPointer` with `xmin`.
10. Insert affected-period Feature 129 jobs/outbox rows keyed by the new `AliasOwnershipVersionId`.
11. Commit atomically.

Any validation, exclusion, trigger, event, pointer, or outbox failure rolls back the complete transaction. No old version/member is mutated or deleted. A superseded version cannot block later ownership because it is absent from the current projection. A draft correction compiles another complete version; it never edits the previous one.

Affected months include alias addition, range change, merge, split, reversal, retirement, reactivation, and any corrected draft. For every changed member/range, schedule all supported company-months whose source observation range intersects, plus snapshots where the month is current or comparison period. Historical snapshots continue to reference their original complete `AliasOwnershipVersionId`.

## 12. Calculation definitions

For product `i`, base `0`, current `1`: sales quantity `Q`, valid rate `P`, reported revenue `R`, production `G`, and `S_t=ΣR_i,t`.

```text
Contribution_i = R_i,1 - R_i,0
CompanyChange D = S_1 - S_0 = ΣContribution_i
PercentChange(x) = 100 × (x1-x0)/x0 only when x0 > 0
RevenueShare_i,t = 100 × R_i,t/S_t only when S_t > 0
```

For safe continuing products:

```text
QuantityEffect = (Q1-Q0) × (P0+P1)/2
PriceEffect = (P1-P0) × (Q0+Q1)/2
ResidualEffect = Contribution - persisted QuantityEffect - persisted PriceEffect
```

`ContributionShare` is null with `ZeroCompanyChange` for `D=0` and with `ImmaterialCompanyChange` below `max(1.00000000, 0.5%×max(|S0|,|S1|))`. Effects use PostgreSQL `numeric(28,8)`, ratios `numeric(20,10)`, percentages `numeric(18,6)`, .NET checked `decimal`, and `MidpointRounding.ToEven`. Effects round once to scale 8; residual is the balancing difference. Fingerprints use fixed-scale invariant decimals, explicit nulls, enum codes, immutable IDs, and stable ordinal order.

## 13. Exhaustive attribution and classification

Independent dimensions are lifecycle (`New`, `Resumed`, `ContinuouslyActive`, `Inactive`, `Discontinued`, `HistoryInsufficient`), identity (`Matched`, `Unmatched`, `Ambiguous`, `ManualReview`), sign (`PositiveSale`, `ZeroActivity`, `ReturnOrReversal`, `NegativeAdjustment`), quality (`Valid`, `Warning`, `Partial`, `Blocking`), and availability (`Decomposed`, `UnattributedComparable`, `Unmatched`, `Unavailable`).

Ordered paths are blocking/no snapshot; unsafe identity → `UnmatchedEffect`; inactive → zero; safe activation → `ActivationEffect`; safe discontinuation → `DiscontinuationEffect`; valid continuing → quantity+price+residual; and monetary-but-unsafe quantity/rate/unit/sign → `UnattributedComparableEffect`. Thus every product and company satisfies:

```text
Contribution = QuantityEffect + PriceEffect + ActivationEffect
             + DiscontinuationEffect + ResidualEffect
             + UnattributedComparableEffect + UnmatchedEffect
```

Driver classification uses aligned signed mass, match/decomposition/residual coverage, materiality, and cancellation ratio. Opposing effects exceeding 35% cancellation produce `NotReliablyClassifiable`. Revenue-share turnover is non-additive `MixShift`, never an attribution bucket.

## 14. Monetary-unit and conversion gates

`ProviderUnitPolicyVersion` is immutable and has provider/tenant scope, monetary unit code, scale/factor, confirmation evidence, contract fixture ID, reconciled sample ID, status, version, and creation time. Its history is trigger-protected.

Fixed outcomes:

- `MonetaryUnitUnconfirmed`: the provider/tenant monetary unit lacks a confirmed contract fixture and reconciled sample.
- `UnitConversionUnapproved`: a physical-unit conversion is required but absent from the approved policy.

Public monetary analysis is blocked until the monetary unit policy is confirmed. Internal shadow calculations may run only with `PublicEligible=false`, explicit unit-unconfirmed quality, and no public/API/conversation result. No unapproved physical conversion occurs. Monetary contribution may remain available when physical conversion is unavailable, but quantity/rate and production/sales comparison are suppressed. Confirmation creates a new immutable provider-unit policy version and schedules eligible recalculation.

## 15. Data quality and publication policy

Publication requires core-ready current and comparison manifest pointers, exact accepted revision IDs, one complete alias ownership version, confirmed monetary-unit policy, approved conversion policy for any physical comparison, exact stored-scale equations, immutable evidence, and no blocking source/identity/overflow issue. A failed run leaves the prior current pointer. Freshness states are `Fresh`, `Stale`, `Partial`, `Processing`, `Unavailable`, `Blocked`, and `MonetaryUnitUnconfirmed`.

## 16. Immutable report/source evidence

Evidence copies snapshot/item/insight ID, report revision/observation, raw payload ID/checksum soft reference, source discriminator, numeric lexeme/value, units, company/period/output type, provider times, `AliasOwnershipVersionId`/member, policy IDs, formula/rule, and calculated value. It is bounded publicly and never resolves through current projection or latest policy. Missing raw soft reference creates `RawPayloadReferenceMissing` audit/quality evidence and cannot silently alter facts.

## 17. Backend architecture

Application owns formulas, policies, contracts, validators, frame construction, and interfaces. Infrastructure owns provider parsing, EF rows/configuration, PostgreSQL triggers/locks, dedicated outbox/dispatcher/RabbitMQ, repositories, cache, and render adapters.

The first public analysis cannot be published before source/revision, manifest pointer, complete ownership version, unit policy, evidence, and publication foundations are enabled.

## 18. Feature dispatch policy and direct-path guard

Introduce a closed `FeatureDispatchMode` with `DirectPublish` and `TransactionalOutbox` in the feature definition contract/row. The registered feature definition for `monthly_product_activity_analysis` declares `TransactionalOutbox`.

`FeatureRecalculationScheduler.ScheduleAsync` resolves the feature definition and checks the mode **before** job lookup, persistence, or publish. If mode is `TransactionalOutbox`, it throws fixed code `FeatureRequiresTransactionalOutbox`; no direct job or message is created. This is an intentional modification to `FeatureComputationProcessor.cs`; unrelated feature definitions retain `DirectPublish` behavior.

Feature 129 has exactly this path:

```text
Financial ingestion or alias approval transaction
→ Feature129ComputationJob
→ Feature129ComputationOutbox
→ leased Feature129 dispatcher
→ routing key `financialcopilot.feature129.monthly-product-analysis.v1`
→ queue `financialcopilot.feature129.monthly-product-analysis.v1`
→ dedicated Feature129 consumer
→ idempotent Feature129 handler persistence
→ ACK
```

It never publishes to the existing direct feature queue.

## 19. Feature 129 job/outbox schema and transaction

`Feature129ComputationJob` has GUID PK, feature/version, provider/company/current/comparison periods, exact manifest-generation IDs, accepted report revision IDs, `AliasOwnershipVersionId`, unit/calculation policy IDs, immutable idempotency key, status (`Requested`, `Running`, `Completed`, `RetryableFailed`, `PermanentlyFailed`, `Cancelled`), timestamps, terminal reason, and `xmin`.

`Feature129ComputationOutbox` has GUID PK, unique `OutboxIdempotencyKey`, Job FK restrict, schema version, dedicated routing key/queue, serialized `MonthlyProductAnalysisDispatchMessage`, state (`Pending`, `Leased`, `PublishedAwaitingConfirm`, `Confirmed`, `RetryableFailed`, `DeadLettered`), attempt count, next attempt, lease owner/token/expiry, broker message ID, publish/confirm/failure timestamps, reason, and `xmin`.

The request writer runs inside the ingestion or alias approval `FinancialIngestionDbContext` transaction. It inserts/reuses a job and exactly one outbox row with:

```text
JobIdempotencyKey = SHA256(feature/version, provider/company, current/comparison period,
  current manifest-generation IDs, accepted report revision IDs,
  AliasOwnershipVersionId, unit policy, calculation policy)
OutboxIdempotencyKey = "f129-request|" + JobIdempotencyKey
MessageId = OutboxIdempotencyKey
```

The committed outbox is the only publication authority. If the transaction rolls back, no dispatcher can see a request.

## 20. Dedicated dispatcher, consumer, and recovery

The dispatcher selects `Pending`/expired eligible rows with `FOR UPDATE SKIP LOCKED`, assigns a lease/fencing token, commits, publishes a persistent message, waits for RabbitMQ publisher confirm, and conditionally marks `Confirmed` using lease token and `xmin`. It never holds a database transaction during broker I/O.

If a process crashes after publish before confirm persistence, the expired row is republished with the same MessageId; the consumer deduplicates by job/idempotency/fingerprint and produces one result. The dedicated consumer deserializes the versioned message, validates routing key/schema, runs the handler, and ACKs only after a persisted terminal result (`Completed`, `RetryableFailed`, `PermanentlyFailed`, or `Cancelled`) is committed. A transient handler failure persists `RetryableFailed`/next attempt and then ACKs because durable retry owns recovery; an unpersistable failure NACKs/requeues or dead-letters according to a bounded policy. Permanent failure is persisted before ACK. Duplicate delivery after commit is an idempotent no-op.

Expired leases are recovered by the dispatcher. Dead-letter redrive requires DataAdmin authorization, a reason, a new lease, and the same idempotency key. Cancellation persists `Cancelled` only when safe; shutdown before ACK causes redelivery. Dedicated consumer tests prove ACK never precedes handler persistence, direct scheduler rejection, routing-key isolation, duplicate recovery, and direct/outbox mutual exclusion.

## 21. Immutable snapshot facts, events, and current pointer

`CompanyMonthlyProductAnalysisSnapshot`, items, signals, evidence, publication events, report/source facts, manifest generations/attempts, ownership versions/members/events, and persisted policy versions are immutable. Snapshot facts have no `Status`, `IsCurrent`, `Superseded`, or mutable publication field.

`CompanyMonthlyProductAnalysisPublicationEvent` has event ID PK, snapshot ID FK restrict, event type (`Calculated`, `ValidationPassed`, `Published`, `PublicationRejected`, `ReplacedAsCurrent`), reason, actor/run/correlation, event time, and unique publication idempotency key.

`CompanyMonthlyProductAnalysisCurrentPointer` has GUID PK, provider/company identity, current month, comparison month, comparison kind, policy family, current snapshot FK restrict, publication-event FK restrict, updated time, and `xmin`. Unique business key is `(ProviderName, ExternalCompanyId, CurrentMonthOrdinal, ComparisonMonthOrdinal, ComparisonKind, AnalysisPolicyFamily)`. Composite alternate keys on snapshot and event permit the pointer to prove same scope. A deferred trigger verifies the event belongs to the same snapshot and has event type `Published`.

Publication transaction:

1. Calculate outside the transaction from exact immutable IDs.
2. Begin `Serializable`; acquire `f129-snapshot|provider|company|current|comparison|kind|policyFamily` advisory lock.
3. Re-read current manifest pointers, accepted revisions, complete alias ownership version, unit policy, and calculation policy.
4. If any input differs, record `StaleInput` and enqueue the new exact fingerprint; do not publish.
5. Insert immutable snapshot facts/items/evidence and `Calculated`/`ValidationPassed` events. A complete audit-worthy policy rejection may commit a non-current snapshot with `PublicationRejected`; structural failure rolls back all facts/events.
6. Append `Published` and `ReplacedAsCurrent` events as appropriate; never update old snapshots/events.
7. Insert/update the pointer using original `xmin` and composite consistency.
8. Commit; then invalidate cache and emit completion.

Identical concurrent work creates one snapshot/current pointer and one no-op. Different work revalidates exact inputs; stale writers cannot move the pointer. Query/cache/history use pointer snapshot ID, schema version, and fingerprint.

## 22. PostgreSQL immutability and relational integrity

### 22.1 Database enforcement

The Feature 129 foundation migration creates:

```sql
CREATE OR REPLACE FUNCTION prevent_f129_history_mutation()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION 'feature129_history_immutable: %', TG_TABLE_NAME;
END $$;
```

`BEFORE UPDATE OR DELETE` triggers using this function apply to source observations, report revisions, manifest generations/operation attempts, ownership versions/members/events, snapshot facts/items/evidence/publication events, and persisted unit/calculation-policy versions. Ordinary application roles have no bypass privilege. Any separately reviewed archive operation exports/verifies immutable facts and is not an ordinary delete.

### 22.2 Application enforcement

Immutable repositories expose insert-only methods; they have no update/delete methods. Mutable pointer/projection/job/lease/run repositories are separate. EF configurations use required FKs, alternate keys, precision, and immutable navigation ownership. DataAdmin decisions require authorization, actor, reason, idempotency key, and audit event. Integration tests attempt SQL update/delete and repository mutation and expect fixed failure.

### 22.3 Composite integrity and delete behavior

All historical and Evidence FKs use `ON DELETE RESTRICT`. Composite/deferred constraints prove:

- current snapshot pointer scope equals snapshot scope and policy family;
- pointer publication event has the same snapshot and `Published` type;
- current manifest pointer scope, generation, accepted revision, and fingerprint agree;
- ownership pointer scope equals complete ownership version scope;
- ownership member scope/version/product agree;
- current alias projection rows reference exactly the pointer-selected version and immutable member;
- evidence snapshot/item and source revision/observation identifiers agree;
- approval/publication/outbox/job decision keys are unique/idempotent.

Regular composite FKs/checks enforce ordinary scope. Deferred constraint triggers enforce cross-row predicates that PostgreSQL regular FKs cannot express, especially pointer-selected projection version and event type. Current pointers/projections may be replaced but never orphan historical rows. Snapshot/policy/history retention is explicit and cannot use ordinary delete.

Raw provider references are cross-DbContext immutable soft references containing payload ID and checksum. A missing reference creates an audit/quality reason and never changes copied evidence values.

## 23. Evidence and historical replay

Every public number has bounded evidence; internal evidence copies report revision/observation, raw soft reference, source row, units, company/period, ownership version/member, policy IDs, formula/rule, and calculated value. Historical snapshots and conversations retain exact complete ownership version, manifest generation, report revisions, observation IDs, and policies. Current pointers and projections are never consulted during replay.

## 24. API/result and frontend contract

`MonthlyProductAnalysisResult` is owned by the application with schema `monthly-product-analysis/1`, discriminated status, company/period/comparison, money-unit descriptor, exact snapshot/manifest/ownership IDs, summary, product items, effect totals, quality, warnings, bounded evidence, and server-calculated contribution view model. Live API, V1, V2, conversation, Telegram, and frontend map it without arithmetic.

The frontend adds a discriminated Zod schema and renders summary/table/evidence/selected-product views from server values. It may calculate pixels for a chart, never financial amounts, cumulative totals, Other, effects, or conversions. Telegram renders bounded text with unit, period, quality, evidence, and operational-analysis limitation.

## 25. Typed semantic proposal and local validation

### 25.1 Contracts

```csharp
enum SemanticSlotValueKind {
  Text, CanonicalEntityReference, JalaliPeriod,
  ComparisonKind, AnalysisFocus, Measure, Presentation, Integer
}

record SemanticSlotProposal(
  QuerySlotType SlotType, string SlotName, SemanticSlotValueKind ValueKind,
  string RawText, string? NormalizedValue, Guid? CanonicalEntityId,
  JalaliPeriodValue? JalaliPeriod, string? ComparisonKind,
  string? AnalysisFocus, string? Measure, string? Presentation,
  int? IntegerValue, decimal Confidence, SemanticSlotProvenance Provenance,
  EvidenceSpan EvidenceSpan, SemanticResolverStatus ResolverStatus,
  SemanticSlotValidationStatus ValidationStatus, string? RejectionOrAmbiguityReason);
```

The closed slot names are `symbol`, `product`, `products`, `period`, `comparison`, `analysisFocus`, `measure`, `presentation`, and `limit`. Closed values cover canonical entity reference, text candidate, Jalali year/month 1300–1600/month 1–12, `PreviousMonth`, `SameMonthPreviousYear`, `ExplicitPeriod`, focus (`Summary`, `Contributors`, `PriceQuantity`, `ProductionSales`, `Mix`, `PotentialInventory`), measures (`Revenue`, `ProductionQuantity`, `SalesQuantity`, `Rate`), presentation, and integer limit 1–100.

### 25.2 Local strict validator

`MonthlyProductSemanticProposalValidator` is Feature-129-owned and registered in DI. The model request continues to use the existing root-level `AiStructuredOutputContract` (`schema name`, required root properties) and unchanged provider adapters. After the response arrives, the local validator parses schema version `monthly-product-semantic-proposal/1` and rejects unknown root/nested properties, unknown capability/slot/value kinds, wrong one-of shapes, invalid period/comparison/enum/limit/confidence, duplicate conflicting slots, excessive counts/lengths, invalid spans, and injection-like unsupported content. It returns only typed proposal objects and fixed reason codes.

Evidence spans are zero-based UTF-16 code-unit offsets into the exact original .NET request string. A span that is out of bounds or does not match the proposed raw text is discarded and warned; it is never trusted for routing. The slot value is independently validated. Invalid JSON/schema/version/provider timeout/refusal falls back to deterministic interpretation or bounded clarification and never reaches an executor.

### 25.3 Merge and validated frame

Current-turn explicit deterministic values outrank model proposals, which outrank valid carryover, which outrank defaults. Equal-precedence conflicts clarify. Company resolves before company-scoped product; model IDs are never trusted. `QueryInterpretation` contains typed candidates and normalized selections; `ValidatedQueryFrame` contains only valid canonical IDs, periods, enums, and provenance. `ConversationTaskState` persists the same validated closed envelope with ownership-version/period provenance, confidence, origin, state version, and expiry. V1/native V2/fallback V2 use one gate, frame builder, and executor. No rejected raw value is active state.

## 26. Semantic routing and executor

Precedence remains scanner threshold, direct metric, statements/disclosures/valuation, monthly trend without product causality, single-period revenue mix, Feature 129 for explicit comparison/contributor/price-quantity/production-sales/cause, and comprehensive-analysis fallback. `MonthlyProductActivityAnalysisCapabilityExecutor` accepts only `ValidatedQueryFrame`, maps it to a query, and reads published snapshots. It cannot invoke provider access or a calculator.

## 27. Conversation persistence and semantic replay

Payload v3 embeds the complete bounded result, schema/version, exact immutable IDs/decimals/enums/units/periods/order/warnings/evidence, and the final versioned deterministic Persian narrative. History decodes embedded content and never queries current state.

Semantic equality requires same schema version, exact financial decimal values, immutable snapshot/manifest/report/observation/ownership/member/policy IDs, enum/reason codes, units/periods, ordered products/effects/Other, warnings/evidence, and exact persisted narrative. JSON whitespace/property order/serializer metadata are excluded. v1/v2 decoders remain compatible; unknown future result kinds degrade safely. Tests replay after report correction, alias merge/split/reversal, policy update, new pointer, serializer update, and decoder upgrade.

## 28. Security, authorization, observability

Current authentication, tenant/actor ownership, billing, rate limits, and entitlement checks remain. Alias drafts, approvals, revision decisions, rebuilds, dead-letter redrive, and backfill require DataAdmin or reviewed narrower permissions with immutable audit records. Metrics use fixed low-cardinality labels; logs carry bounded IDs/counts, never raw payload/query/product text or credentials.

## 29. Acceptance criteria

AC-01–AC-77 are runtime/design behavior requirements; AC-78 is a document readiness gate only. Every runtime AC maps to a named test and implementation slice.

| AC | Objective criterion | Test | Slice |
| --- | --- | --- | --- |
| AC-01 | Distinct source rows sharing a code persist as distinct observations and both enter totals. | T-ING-01 | 1 |
| AC-02 | Reordering preserves semantic multiset/discriminator identity; ordinal is evidence only. | T-ING-02 | 1 |
| AC-03 | Replay creates no economic revision and appends a receipt. | T-ING-03 | 1 |
| AC-04 | Raw row count/revenue equals normalized observations or acceptance blocks. | T-ING-04 | 1 |
| AC-05 | Manifest records every ProductSales 0–4/ServiceSales outcome and valid empty versus failure. | T-MAN-01 | 1 |
| AC-06 | Type-0 incompleteness cannot make a core-ready pointer or job. | T-MAN-02 | 1 |
| AC-07 | Optional late success creates a new generation and exact idempotent request. | T-MAN-03 | 1 |
| AC-08 | One current manifest pointer exists for provider/company/period/family. | T-MAN-04 | 1 |
| AC-09 | Manifest pointer FK proves generation/revision/fingerprint/scope consistency. | T-MAN-05 | 1 |
| AC-10 | Concurrent manifest generations select one deterministic current generation. | T-MAN-06 | 1 |
| AC-11 | Stale manifest writer cannot move the pointer or schedule a distinct stale job. | T-MAN-07 | 1, 3 |
| AC-12 | Late optional completion preserves accepted type-0 identity and updates readiness deterministically. | T-MAN-08 | 1 |
| AC-13 | Corrected reports create immutable revisions/observations. | T-REV-01 | 1 |
| AC-14 | Revision precedence selects higher comparable revision/newer valid publication. | T-REV-02 | 1 |
| AC-15 | Late older/equal ambiguous payload cannot replace the accepted pointer. | T-REV-03–04 | 1 |
| AC-16 | Draft changes never enter calculations or snapshots. | T-ALIAS-01 | 2 |
| AC-17 | Draft compilation materializes a complete ownership version containing unchanged and changed mappings. | T-ALIAS-02 | 2 |
| AC-18 | Exactly one current complete ownership version exists per company/provider. | T-ALIAS-03 | 2 |
| AC-19 | Every snapshot references one complete immutable `AliasOwnershipVersionId`. | T-ALIAS-04 | 3 |
| AC-20 | Current lookup projection contains rows from only the pointer-selected ownership version. | T-ALIAS-05 | 2 |
| AC-21 | Ownership version/member composite keys and company/provider/product consistency are enforced. | T-ALIAS-06 | 2 |
| AC-22 | Ownership-version effective-range and provider-alias overlaps are rejected by GiST constraints. | T-ALIAS-07 | 2 |
| AC-23 | Concurrent approval atomically replaces pointer/projection and creates affected-period outbox rows. | T-ALIAS-08 | 2 |
| AC-24 | Approval failure rolls back version/projection/pointer/events/outbox together. | T-ALIAS-09 | 2 |
| AC-25 | Merge/split/reversal/retirement/reactivation create complete versions and never rewrite history. | T-ALIAS-10 | 2 |
| AC-26 | Superseded versions cannot re-enter current lookup or block later ownership. | T-ALIAS-11 | 2 |
| AC-27 | Historical snapshot remains mapped to its original complete ownership version after changes. | T-ALIAS-12 | 3, 4 |
| AC-28 | Monthly revenue sums all accepted type-0 observations and excludes other types/services. | T-CALC-01 | 3 |
| AC-29 | Safe continuing products use symmetric effects and balancing residual. | T-CALC-02 | 3 |
| AC-30 | Activation/discontinuation/unsafe identity/unit/rate/sign paths allocate contribution exactly once. | T-CALC-03–04 | 3 |
| AC-31 | Product/company equations reconcile at stored scale or publication blocks. | T-CALC-05 | 3 |
| AC-32 | Unit incompatibility preserves monetary contribution and suppresses physical comparison. | T-CALC-06 | 3 |
| AC-33 | Decimal precision, ToEven rounding, tolerance, and zero/immaterial reasons match policy. | T-DEC-01–02 | 3 |
| AC-34 | Cancellation prevents misleading driver classification. | T-CLASS-01 | 3 |
| AC-35 | Every public numeric fact has bounded copied immutable evidence. | T-EVID-01 | 3 |
| AC-36 | Evidence retains old revision/ownership/policy values after correction/reversal. | T-EVID-02 | 3, 4 |
| AC-37 | Cross-DbContext raw references verify ID/checksum and never rewrite copied evidence. | T-EVID-03 | 1, 3 |
| AC-38 | Immutable tables reject UPDATE and DELETE through PostgreSQL triggers. | T-IMM-01 | 1–3 |
| AC-39 | Immutable repositories expose insert-only APIs and no mutation method. | T-IMM-02 | 1–3 |
| AC-40 | All historical FKs use `ON DELETE RESTRICT`; archive is separate and audited. | T-IMM-03 | 1–3 |
| AC-41 | Snapshot pointer composite integrity prevents scope mismatch. | T-PUB-01 | 3 |
| AC-42 | Publication event must belong to the same snapshot and be `Published`. | T-PUB-02 | 3 |
| AC-43 | Manifest pointer composite integrity prevents generation/revision mismatch. | T-MAN-05 | 1 |
| AC-44 | Current alias pointer/projection/member composite integrity prevents mixed versions. | T-ALIAS-05–06 | 2 |
| AC-45 | Approval/publication/outbox decision idempotency keys are unique. | T-IMM-04 | 1–3 |
| AC-46 | Snapshot facts/items/evidence/events never mutate after insertion. | T-IMM-05 | 3 |
| AC-47 | Exactly one current snapshot pointer exists per complete business key. | T-PUB-03 | 3 |
| AC-48 | Feature definition declares `TransactionalOutbox` and direct scheduler rejects it before persistence. | T-ORCH-01 | 1 |
| AC-49 | Direct scheduler rejection uses `FeatureRequiresTransactionalOutbox`. | T-ORCH-02 | 1 |
| AC-50 | Job/outbox insertion is atomic with ingestion/alias transaction. | T-ORCH-03 | 1–2 |
| AC-51 | Outbox leasing uses `SKIP LOCKED`, lease token, expiry, and `xmin`. | T-ORCH-04 | 1 |
| AC-52 | Publisher confirms and conditional state update recover undispatched/expired rows. | T-ORCH-05 | 1, 6 |
| AC-53 | Crash after publish before confirm produces one idempotent calculation. | T-ORCH-06 | 3 |
| AC-54 | Dedicated queue/routing key rejects direct-queue messages and unrelated consumers do not process Feature 129. | T-ORCH-07 | 1 |
| AC-55 | Dedicated consumer ACK occurs only after persisted terminal/retryable state. | T-ORCH-08 | 3 |
| AC-56 | Duplicate delivery produces one terminal job/snapshot. | T-ORCH-09 | 3 |
| AC-57 | Retry, NACK/requeue, dead-letter, authorized redrive, cancellation, and recovery have fixed outcomes. | T-ORCH-10 | 3, 6 |
| AC-58 | Root-level provider structured contract remains unchanged for unrelated capabilities. | T-SEM-01 | 4 |
| AC-59 | Local validator rejects unknown schema/root/nested properties. | T-SEM-02 | 4 |
| AC-60 | Local validator enforces closed slots/value kinds/one-of shapes/enums/counts/lengths. | T-SEM-03 | 4 |
| AC-61 | Local validator rejects malformed periods, limits, confidence, and unsupported schema versions. | T-SEM-04 | 4 |
| AC-62 | Evidence spans use UTF-16 offsets; invalid/mismatched spans are discarded and cannot route. | T-SEM-05 | 4 |
| AC-63 | Deterministic/model merge precedence and conflict clarification are objective. | T-SEM-06 | 4 |
| AC-64 | Canonical company/product resolution replaces raw/model IDs and ambiguity asks bounded clarification. | T-SEM-07 | 4 |
| AC-65 | Only validated slots enter `ValidatedQueryFrame`, task state, and the executor. | T-SEM-08 | 4 |
| AC-66 | V1/native V2/fallback V2 consume the same validated values and produce identical numbers/reasons. | T-SEM-09 | 4 |
| AC-67 | Live/history payload v3 preserves exact result schema, values, IDs, enums, units, order, warnings, and evidence. | T-CONV-01 | 4 |
| AC-68 | Replay remains semantically equal after report, ownership, policy, pointer, serializer, and decoder changes. | T-CONV-02 | 4 |
| AC-69 | Persisted deterministic Persian narrative replays exactly; v1/v2/unknown kinds decode safely. | T-CONV-03 | 4 |
| AC-70 | Optional direct endpoint, if enabled, enforces auth/rate/entitlement and ETag semantics. | T-API-01 | 6 |
| AC-71 | Web/Telegram use server values and expose accessible evidence/limitations without client arithmetic. | T-UI-01–02 | 4–5 |
| AC-72 | 1404/01 boundary, restartable backfill, and optional history policies are enforced. | T-BF-01–04, T-HIST-01–03 | 6 |
| AC-73 | Unconfirmed provider monetary unit yields `MonetaryUnitUnconfirmed` and blocks public output. | T-POLICY-01 | 2–4 |
| AC-74 | Internal unit-unconfirmed shadow results are non-public and clearly marked. | T-POLICY-02 | 3 |
| AC-75 | Unapproved physical conversion yields `UnitConversionUnapproved` and suppresses quantity/rate comparison. | T-POLICY-03 | 2–3 |
| AC-76 | Security/non-disclosure and bounded performance targets pass. | T-SEC-01, T-PERF-01 | 4–6 |
| AC-77 | Exact fixture uses `غاذر` and `سبزیجات ۴۰ گرمی`; malformed variants are absent. | T-FIX-01 | 1–6 |
| AC-78 | Design readiness checklist verifies prior documents unchanged, impact categories, and AC/test/slice mapping. | T-TRACE-01 | Design gate |

## 30. Testing strategy

- **Ingestion/revision:** repeated codes, identical occurrences, reorder, replay, raw mismatch, scale, precedence, late older, equal conflict, and concurrent acceptance.
- **Manifest:** all outcome states, pointer uniqueness/scope FKs, optional late completion, stale generation, concurrent generation selection, and exact job generation IDs.
- **Alias:** draft isolation, complete-set compilation, version/member composite consistency, GiST overlap, concurrent approval, rollback, merge/split/reversal/range/retirement/reactivation, projection single-version invariant, and historical replay.
- **Calculator:** `غاذر` fixture, property-based seven-bucket equation, negative/zero/overflow/rounding/tolerance/cancellation/unit gates.
- **Immutability/integrity:** SQL UPDATE/DELETE trigger rejection, repository API mutation absence, `ON DELETE RESTRICT`, composite/deferred pointer/event/version checks, event/outbox idempotency, and cross-context raw checksum verification.
- **Publication:** immutable snapshot row hash, pointer uniqueness/FK, `xmin`, serializable retry, stale writer, identical/different concurrency, rollback, cache identity, and old-pointer preservation.
- **Dispatch:** direct scheduler rejection, mode registry, atomic job/outbox, lease contention, confirm, crash-after-publish, dedicated routing, ACK ordering, duplicate, retry/NACK/dead-letter/redrive/cancel/recovery.
- **Semantic:** local nested validator schema/rejection matrix, UTF-16 spans, merge precedence/conflicts, resolver ambiguity, task-state carryover, prompt injection, and V1/V2 parity.
- **Conversation/API/UI:** semantic comparator after all input changes, narrative replay, decoder/schema compatibility, live/history mapping, Telegram, Zod, accessibility, RTL/mobile, and no provider calls.
- **Policy/operations:** monetary unit confirmation gate, unapproved conversion, 1404 boundary, restartable backfill, performance, credentials/raw-payload non-disclosure, dashboards, and runbooks.

## 31. Vertical slices

### Slice 1 — Immutable source, manifest pointer, durable dispatch

Implement source/revision/manifest generations, accepted manifest pointer/selection, Feature dispatch policy and direct-scheduler guard, Feature 129 job/outbox writer, dedicated message/dispatcher/queue/consumer, immutable trigger foundation, and no user-facing result. Existing unrelated direct scheduler/consumer behavior remains unchanged.

### Slice 2 — Complete ownership and policy foundation

Implement mutable drafts, complete version compilation, immutable version/members/events, ownership pointer, current lookup projection, GiST/composite/deferred constraints, approval/reversal transaction, affected-period outbox scheduling, provider monetary policy, and no snapshot publication before a complete ownership version exists.

### Slice 3 — Calculator and immutable publication

Implement seven-bucket calculator, quality/classification, immutable snapshot/events/evidence, current snapshot pointer, triggers/composite integrity, `MonetaryUnitUnconfirmed` shadow/public gate, and internal shadow publication. The first public result requires confirmed unit policy.

### Slice 4 — Typed semantic/API/conversation

Implement root-level proposal request, local strict validator/schema resource, typed merge/resolution, shared V1/V2 frame, task state, payload v3 semantic replay, billing, Telegram, and public capability enablement only after unit confirmation.

### Slice 5 — Investor-facing UI

Implement server-value summary, contribution/product/evidence tables, selected-product chart, accessibility, RTL/mobile, and optional waterfall layout. No financial recomputation.

### Slice 6 — History and operational hardening

Implement optional history/anomaly/inferred signals, bounded 1404 backfill, optional endpoint, SLO/load tests, dashboards, runbooks, and dead-letter/redrive operations. Dead-letter recovery must be production-ready before dispatch enablement, even if UI/history remains deferred.

## 32. Dependencies and remaining business decisions

Dependencies are NADPCO/raw storage, Financial ingestion DbContext, canonical company resolver, derived-feature contracts, RabbitMQ, semantic registry/task state, AI facade/conversation/billing/auth, frontend structured chat, and Telegram renderer.

Remaining choices may stay open because safe gates are now deterministic:

1. Confirm NADPCO monetary unit per tenant/provider by contract fixture and reconciled sample; until then public output is blocked by `MonetaryUnitUnconfirmed`.
2. Approve a conversion dictionary; until then no conversion is performed and `UnitConversionUnapproved` suppresses physical comparisons.
3. Select operational freshness/materiality thresholds; v1 immutable defaults apply and later choices create a policy version/recalculation.
4. Decide whether ServiceSales becomes a separate future feature; it remains excluded here.

## 33. File-impact map

### 33.1 Existing files to modify

| Existing path | Planned impact |
| --- | --- |
| `FinancialIngestionContracts.cs`, `FinancialDataSyncProcessor.cs`, `NadpcoApiDataProviderClient.cs`, `ProviderRawPayloadPersistence.cs`, `NadpcoApiMonthlyActivityNormalizer.cs`, `MonthlyActivityBackfillCoordinator.cs` | Operation outcomes, immutable revision/observation ingestion, manifest-generation transaction, 1404 boundary, and exact raw linkage. |
| `FinancialIngestionRows.cs`, `FinancialIngestionConfigurations.cs`, `FinancialIngestionDbContext.cs` | Register Feature 129 rows, constraints, precision, `xmin`, and trigger-owned tables. |
| `FeatureModels.cs`, `DerivedFeatureContracts.cs`, `FeatureComputationProcessor.cs`, `PersistedFeatureServices.cs`, `ServiceCollectionExtensions.cs` | Closed dispatch policy, direct scheduler guard, feature definition, repositories, handler/validator/policy registration. |
| `FinancialCopilot.Worker/Program.cs` | Register dedicated dispatcher and Feature 129 consumer worker. |
| `FinancialCopilot.Infrastructure/Financial/Ingestion/Messaging/RabbitMqFeatureMessaging.cs` only if shared declarations are needed | Preserve unrelated bus; Feature 129 queue declarations must not share direct queue behavior. |
| `CanonicalQueryEntityContracts.cs`, `CapabilityInterpretationGovernance.cs`, `ConversationalCapabilityContracts.cs`, `ConversationTaskStateContracts.cs`, `SemanticCapabilityExecutionContracts.cs`, `SemanticCapabilityExecutors.cs` | Typed slot proposals/values, merge, canonical resolution, task state, frame, and executor. |
| `AiOrchestrationContracts.cs`, `AiQueryOrchestrationService.cs`, V2 workflow messages/definition/runner, `MessagePersistenceFunction.cs` | Shared result, V1/V2 parity, payload v3, deterministic narrative, billing/consistency. |
| `ConversationContracts.cs`, `ConversationRepositories.cs`, `AiFacadeContracts.cs`, `AiFacadeController.cs` | Result mapping, backward decoder, semantic replay mapper, live/history behavior. |
| `TelegramAssistantResponseRenderer.cs`, `chat.functions.ts`, `message-list.tsx` | Telegram fallback, frontend discriminated Zod/result mapping, UI states. |

### 33.2 Existing files inspected but intentionally unchanged

| Existing path | Reason |
| --- | --- |
| `MetricRecalculationProcessor.cs` | Feature 129 is not metric-registry recalculation. |
| `AiModelContracts.cs`, `AiModelProviderServices.cs`, and existing provider adapters | Local Feature-129 validator is authoritative for nested schema; shared root contract remains unchanged. |
| Existing unrelated direct-feature consumer behavior and queue | Feature 129 uses a dedicated queue/consumer and must not alter unrelated ACK semantics. |
| Historical migrations | Never edit historical migration files; the new migration is proposed separately. |

### 33.3 Proposed new files

- EF migration `AddFeature129Foundation` and updated generated `FinancialIngestionDbContextModelSnapshot`.
- Complete ownership version/member/draft/event/current-pointer rows and configurations.
- Manifest-generation/current-pointer rows and configurations.
- Feature 129 job/outbox rows/configurations/repositories/contracts.
- Dedicated `Feature129Dispatcher`, dispatch message contract, publisher, consumer, queue declarations, and consumer worker.
- PostgreSQL immutable-history trigger SQL migration and deferred consistency trigger SQL.
- `MonthlyProductSemanticProposalValidator`, typed proposal contracts, schema/prompt resource, governance adapter, and local validator tests.
- Monetary/unit policy rows/contracts and authorization/audit handlers.
- Monthly product calculator/handler/snapshot writer/query repository and API/result contracts.
- Integration, PostgreSQL concurrency, RabbitMQ recovery, semantic, conversation, frontend, and policy tests named in section 30.

### 33.4 Optional later-slice files

Optional direct endpoint/controller/permission, advanced history/anomaly/inferred-inventory services, waterfall/image-export UI, and extended history projections. None is required for first-release correctness or durable dispatch.

## 34. Document readiness checklist

- [x] Design-v3 findings V3-B-01, V3-M-01, V3-M-02, V3-M-03, V3-M-04, V3-N-01, V3-N-02, and V3-T-01 have concrete schema/workflow/constraint/AC/test/slice/impact resolutions.
- [x] One complete immutable ownership version reproduces every mapping used by one snapshot.
- [x] Current alias lookup contains rows from exactly one pointer-selected version.
- [x] One accepted current manifest generation exists for each provider/company/period/family key.
- [x] Direct Feature 129 scheduling is rejected before persistence; only the dedicated outbox route is accepted.
- [x] Dedicated consumer ACK is after persisted handler state.
- [x] Immutable tables reject UPDATE/DELETE through the selected trigger mechanism.
- [x] Composite pointer/event/version consistency is enforced by FKs/checks/deferred triggers.
- [x] Cross-DbContext raw links are explicitly verified soft references.
- [x] Nested semantic proposals are strictly validated locally; shared provider adapters remain unchanged.
- [x] Runtime ACs contain no documentation-only diff condition; design checks are here.
- [x] `MonetaryUnitUnconfirmed` blocks public output and `UnitConversionUnapproved` suppresses physical comparison.
- [x] Every runtime AC maps to a named test and slice.
- [x] `غاذر`/`سبزیجات ۴۰ گرمی` fixture reconciles to 120,150 and 26.7%.
- [x] `MetricRecalculationProcessor.cs` remains intentionally unchanged.
- [x] Prior documents, code, migrations, tests, configuration, and production data are unchanged by this design task.

**Final status:** `READY_FOR_DESIGN_REVIEW`
