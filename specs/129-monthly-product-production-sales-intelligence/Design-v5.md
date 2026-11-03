# Feature 129 — Monthly Product Production and Sales Intelligence

## 1. Status, purpose, and review resolution

**Status:** `READY_FOR_DESIGN_REVIEW`  
**Revision:** v5  
**Date:** 2026-08-24  
**Scope:** design/specification only. This document authorizes no implementation, migration execution, test/configuration change, provider call, or production-data change.

This standalone revision resolves every finding in `Design-v4-review.md`: V4-M-01 durable retry ownership, V4-M-02 deterministic manifest selection, V4-M-03 the complete normative fixture, V4-M-04 objective operational criteria, V4-N-01 publisher-confirm versus handler-result states, and V4-N-02 the complete immutable-trigger inventory. Earlier design and review documents remain historical and unchanged.

Feature 129 explains a company's monthly ProductSales revenue change using immutable source evidence, deterministic report/revision acceptance, complete alias ownership, a seven-bucket price/quantity calculation, immutable snapshots, and bounded structured results through the existing AI facade. The model may propose typed capability slots; it cannot calculate, select a database route, or execute with unvalidated values.

### 1.1 Repository facts used by this design

The indexed repository confirms:

| Current component | Authoritative observed behavior | v5 consequence |
| --- | --- | --- |
| `FeatureComputationProcessor.ProcessAsync` | Persists a job and publishes completion/failure directly. | Feature 129 is rejected by the direct scheduler before persistence and uses its own outbox. |
| `RabbitMqFeatureBus.ConsumeAsync` | ACKs before invoking the handler. | The shared consumer is unchanged; Feature 129 has a dedicated consumer that ACKs after a database commit. |
| `FeatureComputationConsumerWorker.ExecuteAsync` | Resolves the existing processor through the shared consumer. | A separate worker/queue is required for Feature 129. |
| `FinancialDataSyncProcessor.ProcessCoreAsync` | Persists raw payload, normalizes, completes the sync, then publishes derived recalculation. | Feature 129 manifest/job/outbox intent is atomic with the authoritative ingestion/alias transaction. |
| `MetricRecalculationProcessor.ProcessPendingAsync` | Drains the existing metric-registry queue. | It remains separate and is not a Feature 129 retry owner. |
| `AiStructuredOutputContract` and conversation contracts | Shared root contract and persisted assistant payload are existing compatibility surfaces. | Nested Feature 129 proposals are strictly validated locally; shared root behavior remains unchanged. |

## 2. Scope and invariants

### 2.1 First release

ProductSales `OutputType=0`, from Jalali `1404/01` onward; latest versus immediately preceding published Jalali month; explicit two-period comparisons; immutable observations and report revisions; manifest generations; complete immutable product ownership versions; governed monetary/unit policy; deterministic seven-bucket attribution; immutable evidence and snapshots; server-side API/facade/conversation/Telegram/web rendering; freshness, warnings, coverage, cancellation, and contributor metadata.

Later work may add YoY, fiscal YTD, 3/12-month averages, longer history, anomalies, inferred inventory signals, waterfall export, and an optional direct read endpoint. Durable retry, dead-letter recovery, and outbox operations are not deferred after dispatch enablement.

### 2.2 Non-goals

No forecasts, investment advice, target prices, inventory claims, cross-company physical comparison, raw provider request from a read path, LLM arithmetic, unapproved economic-unit conversion, or pre-1404 backfill.

The following invariants are normative:

1. Raw payload, operation attempts, observations, accepted revisions, manifests, ownership versions, policies, jobs, outbox attempts, snapshots, evidence, and events are immutable history.
2. Mutable pointers/projections are never calculation evidence and always point to a complete immutable version.
3. One accepted manifest pointer exists per provider/company/month/family key.
4. One snapshot references one complete `AliasOwnershipVersionId`, one accepted manifest generation, and one calculation-policy version.
5. Feature 129 has no path through the existing direct-publish queue.
6. Only locally validated and canonically resolved typed values enter V1, native V2, fallback V2, or an executor.
7. A stale or failed run never displaces the current published snapshot.

## 3. Source, revision, and manifest model

### 3.1 Immutable source observations

Each provider array element becomes a `MonthlyReportSourceObservation` with provider/company/report identity, `RawArrayOrdinal`, canonical raw JSON with numeric lexemes, `EconomicSignature`, `SourceFactFingerprint`, duplicate occurrence, source-row discriminator, reported revenue, raw payload ID/checksum, and completion timestamp. Distinct rows sharing a code remain distinct; identical rows remain separate occurrences and produce a `DuplicateFactObserved` quality event. Raw row count and revenue must reconcile with normalized rows at `numeric(28,8)` or the candidate is blocked as `RawNormalizationMismatch`.

### 3.2 Report revisions

`MonthlyReportLogicalIdentity` is unique by provider, external company, report kind, output type, and Jalali month. It has a mutable accepted-revision pointer and `xmin`; `MonthlyReportRevision`, observations, receipt rows, and decision/status events are insert-only.

Under `Serializable` and `pg_advisory_xact_lock('f129-report|' + logical identity)`, precedence is: higher comparable provider revision; then newer valid provider publication timestamp; then no winner when metadata is absent or equal with different semantic fingerprints. Receipt time never decides an economic winner. Equal-precedence different facts are `AmbiguousRevisionOrder`; the existing accepted revision remains current and a DataAdmin decision is required. Retries for `40001`, `40P01`, and optimistic conflicts are bounded to three.

### 3.3 Operation attempts and canonical manifest generation

For every operation key persist immutable:

| Field | Requirement |
| --- | --- |
| Operation key / attempt ordinal | Unique operation identity and monotonically allocated ordinal under the logical-identity lock. |
| Outcome | `Succeeded`, `ValidEmpty`, `Retrying`, `Failed`, `Blocked`, or `Cancelled`. |
| Accepted report revision ID | Nullable until accepted; never inferred from receipt order. |
| Semantic payload fingerprint | SHA-256 of normalized accepted facts. |
| Comparable provider revision / valid publication timestamp | Copied provider metadata, nullable when absent. |
| Raw checksum / row count / completion time | Immutable reconciliation and evidence fields. |
| Fixed error code / optional-mandatory classification | Closed codes, no free-form routing state. |

The operation key is `ProductSales:0`, `ProductSales:1`, `ProductSales:2`, `ProductSales:3`, `ProductSales:4`, or `ServiceSales:none`. ProductSales type 0 is mandatory; a valid empty accepted revision is success, not failure. Type 1–4 and ServiceSales are optional.

`CompanyMonthIngestionManifestGeneration` is immutable and contains provider/company/month/family, monotonic generation number, accepted type-0 revision, complete operation vector, readiness, fingerprint, and creation metadata. `CompanyMonthIngestionManifestCurrentPointer` is mutable and unique by provider/company/month/family; it stores the exact generation, revision, fingerprint, readiness, `xmin`, and updated timestamp. Composite FKs/deferred triggers enforce that scope, generation, revision, and fingerprint agree.

### 3.4 Total deterministic selection algorithm

Under `pg_advisory_xact_lock(hashtextextended('f129-manifest|' || provider || '|' || company || '|' || period || '|' || family, 0))`:

1. Lock the current pointer with `FOR UPDATE`.
2. Read authoritative accepted report-revision state and all immutable operation attempts.
3. For each operation choose, in order: its currently accepted successful revision; a validated empty accepted revision; otherwise its latest immutable attempt ordinal. A retry/failure never downgrades an accepted success.
4. Reject any candidate whose type-0 revision is no longer accepted or whose equal-precedence conflicting payload is `AmbiguousRevisionOrder`.
5. Serialize the fixed vector in this exact order: `ProductSales:0`, `ProductSales:1`, `ProductSales:2`, `ProductSales:3`, `ProductSales:4`, `ServiceSales:none`. Each element contains selected outcome, revision ID, semantic fingerprint, selected attempt ordinal, and fixed reason.
6. Compute `ManifestFingerprint = SHA-256(UTF-8, culture-invariant canonical JSON/vector serialization)` with fixed property order, invariant numeric formatting, and no whitespace.
7. If the fingerprint equals the pointer fingerprint, record a replay/no-op and create neither a generation nor a job.
8. If it differs, allocate `GenerationNumber = current + 1` (or `1`), insert the immutable generation, update the pointer with `xmin`, and insert/reuse a job and its exact outbox attempt in the same transaction.
9. If a serializable or `xmin` retry occurs, reread accepted revisions and rebuild the vector; never retry a stale candidate.
10. Receipt time, operation completion time, and generation ID are not economic tie-breakers. Generation ID only identifies the already-selected immutable vector.

This is a total algorithm. A same-revision/different-fingerprint conflict is decided by report-revision acceptance or remains blocked; Manifest selection never chooses between conflicting facts.

## 4. Ownership, units, and calculation

### 4.1 Complete ownership versions

Drafts are administrative only. Approval takes the company/provider advisory lock, loads the prior complete set, applies the draft, validates product identity/unit/signature/range, and inserts a complete immutable `CompanyProductAliasOwnershipVersion` plus immutable members and decision event. The current pointer and lookup projection are replaced atomically and contain rows from exactly one version. Version/member keys, `btree_gist` range exclusion, deferred composite consistency, `ON DELETE RESTRICT`, `xmin`, and audit actor/reason are required. Merge, split, reversal, retirement, reactivation, and changed package/range each create another complete version; historical snapshots retain their original version.

Matching order is approved member, compatible collision-free provider key, exact economic signature, prior approved signature, then manual review. Text similarity alone cannot merge material revenue. Raw unit text is retained beside governed `UnitCode`/dimension and immutable `ProviderUnitPolicyVersion`. v1 performs no physical conversion. `MonetaryUnitUnconfirmed` blocks public monetary output; `UnitConversionUnapproved` preserves monetary contribution but suppresses quantity/rate and production/sales comparison.

### 4.2 Seven-bucket formula

For a continuing product with base `(Qb, Pb, Rb)` and current `(Qc, Pc, Rc)`, use the symmetric midpoint decomposition, stored-scale decimal arithmetic, and ToEven rounding:

```text
QuantityEffect = (Qc - Qb) × (Pb + Pc) / 2
PriceEffect    = (Pc - Pb) × (Qb + Qc) / 2
Residual       = (Rc - Rb) - QuantityEffect - PriceEffect
Contribution   = QuantityEffect + PriceEffect + Residual
```

The mutually exclusive seven buckets are `Quantity`, `Price`, `Residual`, `NewProduct`, `DiscontinuedProduct`, `IdentityChange`, and `Unsafe/Unattributable`. Lifecycle, identity, unit, sign, quality, cancellation, and attribution availability are separate dimensions. Every signed product revenue change is allocated exactly once, or to `Unsafe/Unattributable`; company and product equations must reconcile at stored scale or publication is blocked. Zero denominator, negative/return, missing/ambiguous identity, incompatible units, and cancellation have fixed reason codes.

### 4.3 Immutable publication

`Feature129Snapshot`, product facts, effects, evidence, publication events, and current pointer are insert-only except the pointer. Evidence copies all numeric inputs, selected policies, source revision IDs/checksums, ownership version, manifest generation, and reason codes. Current-pointer composite consistency, one-current-row business-key uniqueness, publication-event-to-snapshot identity, and immutable SQL triggers are mandatory. A failed publication transaction leaves the previous pointer untouched.

## 5. Durable job, outbox, dispatch, and recovery

### 5.1 Separate logical job from delivery attempt

`Feature129ComputationJob` represents one logical calculation request:

| Field | Contract |
| --- | --- |
| Job ID / Feature and version | Immutable identity and algorithm version. |
| Source fingerprint / Manifest generation ID | Exact accepted inputs. |
| Alias ownership version ID / calculation policy ID / unit policy ID | Exact reproducibility context. |
| Job idempotency key | Unique, deterministic from scope, generation, and policies. |
| Execution state | `Requested`, `Running`, `RetryScheduled`, `Completed`, `PermanentlyFailed`, `Cancelled`, `DeadLettered`. |
| Current attempt / maximum attempts | Starts at 0; maximum is 6. |
| Next attempt time / last fixed outcome-reason | Durable scheduling and closed reason code. |
| Created/updated timestamps / `xmin` | Concurrency and audit. |

`Feature129ComputationOutbox` is one immutable logical delivery attempt. It has unique `(JobId, AttemptNumber)`, unique `OutboxIdempotencyKey`, Message ID, payload checksum, lease token/expiry, `AvailableAtUtc`, publish-confirm timestamp, and publication state: `Pending`, `Leased`, `PublishedAwaitingConfirm`, `Confirmed`, `DeliveryConsumed`, `RetryablePublishFailure`, `PermanentlyFailed`, or `DeadLettered`.

Broker confirmation means only that RabbitMQ accepted that delivery attempt. It does not mean calculation success and never controls calculator idempotency. Consumer deduplication uses Job ID, job idempotency key, source fingerprint, and attempt number/Message ID as delivery evidence. Result uniqueness is by Job/source fingerprint.

### 5.2 One durable retry owner

The Feature129 outbox and its existing-style leased dispatcher are the sole durable owners of all Feature 129 delivery attempts, including retryable handler failures. No unspecified external retry mechanism, timer, or direct scheduler is allowed.

The dispatcher selects `State = Pending AND AvailableAtUtc <= now` with `FOR UPDATE SKIP LOCKED`, sets a lease token and expiry using `xmin`, publishes with the same Message ID on republish, and persists publisher-confirm state conditionally. v1 backoff is deterministic and has no jitter:

| Attempt | 1 | 2 | 3 | 4 | 5 | 6 |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Delay | immediate | 30 seconds | 2 minutes | 10 minutes | 30 minutes | 2 hours |

Failure classes are `RetryableInfrastructureFailure`, `RetryableSerializationFailure`, `RetryableLockConflict`, `RetryableDatabaseAvailability`, `PermanentValidationFailure`, `PermanentUnsupportedSchema`, `PermanentSourceBlocked`, and `Cancelled`. Policy changes affect only newly scheduled attempts unless an authorized redrive creates a new policy-bound decision.

### 5.3 Retryable handler transaction and broker actions

The dedicated consumer receives a message and the handler performs the following in one Financial ingestion transaction:

1. Lock the Job by ID and `xmin`; if terminal, record duplicate consumption and return idempotently.
2. Verify the Job is not terminal and persist the current attempt's fixed outcome/reason.
3. For a retryable failure, increment `CurrentAttemptNumber`; if attempts remain, set Job `RetryScheduled`, calculate `NextAttemptAtUtc`, insert the next outbox row with `(JobId, NextAttemptNumber)`, deterministic idempotency key, `Pending`, and `AvailableAtUtc`; otherwise set `DeadLettered` or `PermanentlyFailed` per the fixed policy and insert the dead-letter record.
4. For success, persist the immutable result/snapshot and set Job `Completed`.
5. Commit before broker action. ACK only after the commit succeeds.

If commit fails, ACK is forbidden; the current message is NACK/requeued under bounded broker-redelivery policy. ACK is therefore impossible without either a committed terminal result or a committed future retry outbox row.

| Handler outcome | Required committed database state | Broker action |
| --- | --- | --- |
| Completed | Terminal result and Job `Completed` | ACK |
| Retryable failure | Future `Pending` retry outbox row | ACK |
| Permanent failure | Terminal failure and dead-letter record | ACK |
| Cancelled | Job `Cancelled` | ACK |
| Duplicate terminal delivery | Idempotent duplicate-consumption record | ACK |
| Cannot persist outcome | No safe durable state | NACK/requeue |
| Invalid schema | Dead-letter record when possible | Reject/dead-letter |

### 5.4 State transitions and crash rules

Outbox transitions are `Pending → Leased → PublishedAwaitingConfirm → Confirmed → DeliveryConsumed`; `Leased` or `PublishedAwaitingConfirm → RetryablePublishFailure → Pending`; any nonterminal state may become `DeadLettered`; terminal states do not return to `Pending` except authorized redrive creating a new attempt number. Job transitions are `Requested → Running → Completed`, `Running → RetryScheduled → Running`, `Running → PermanentlyFailed`, and `Running/RetryScheduled → Cancelled`; exhaustion may become `DeadLettered`.

Crash before failure commit leaves an unacknowledged message for redelivery. Crash after retry-row commit before ACK causes duplicate delivery, but `(JobId, AttemptNumber)` makes retry creation a no-op. Crash after broker publish before confirm persistence republishes the same attempt and Message ID. Duplicate calculation is suppressed by Job/source uniqueness. Dead-letter redrive is DataAdmin-only, requires reason/audit, retains lineage/source fingerprint, and creates a new attempt number.

## 6. Semantic, API, conversation, and UI contract

Feature 129 owns a closed `MonthlyProductSemanticProposal` schema with version, slots, value kinds, one-of value shapes, Jalali period bounds, limit/confidence/count/length limits, UTF-16 evidence spans, and fixed rejection codes. Unknown root/nested properties, malformed spans, prompt injection, unsupported schema versions, duplicate conflicts, and unresolved canonical company/product identity are rejected or converted to bounded clarification. Shared `AiStructuredOutputContract` remains root-only and unchanged.

Deterministic interpreter/model candidates are merged by fixed precedence, then canonical resolver output creates one `ValidatedQueryFrame`. V1 and both V2 paths consume the same frame. The executor accepts only that frame and returns payload v3 with exact values, IDs, units, enums, order, warnings, evidence, freshness, and limitations. Conversation persistence stores canonical semantic payload and deterministic Persian narrative; replay compares semantics, not serializer bytes. Web and Telegram render server values and evidence; client code performs no financial calculation. Protected endpoint behavior retains existing auth, entitlement, rate-limit, billing, and backward decoder contracts.

## 7. Immutability, security, and observability

The migration must install `prevent_f129_history_mutation()` for every history table listed in §8.1. History repositories are insert-only; mutable pointer/projection/lease repositories expose only concurrency-safe state transitions. Historical FKs use `ON DELETE RESTRICT`; raw payload links across DbContexts are ID/checksum-verified soft references. Raw payloads, credentials, model prompts, internal exceptions, and product text are never logged or returned. Metrics use bounded low-cardinality labels and logs contain IDs, counts, states, and fixed codes only. DataAdmin redrive, report decisions, alias approvals, monetary-unit confirmation, conversion approval, and cancellation are audited.

## 8. Exact database trigger inventory and impact map

### 8.1 Normative immutable-trigger inventory

The migration task must install the same `BEFORE UPDATE OR DELETE` rejecting trigger on each exact table below; there is no wildcard interpretation:

| Table | Immutable content |
| --- | --- |
| `MonthlyReportRevision` | Accepted report revision history. |
| `MonthlyReportSourceObservation` | Source rows and fingerprints. |
| `MonthlyReportReceipt` | Provider receipt attempts. |
| `MonthlyReportDecisionEvent` | Acceptance/ambiguity/status events. |
| `CompanyMonthIngestionOperationAttempt` | Manifest operation attempts. |
| `CompanyMonthIngestionManifestGeneration` | Canonical generation vector. |
| `CompanyProductAliasOwnershipVersion` | Complete ownership version. |
| `CompanyProductAliasOwnershipVersionMember` | Version membership. |
| `CompanyProductAliasDecisionEvent` | Approval/reversal/merge/split events. |
| `Feature129ComputationAttempt` | Handler attempt outcome evidence. |
| `Feature129ComputationDeadLetter` | Dead-letter evidence. |
| `Feature129Snapshot` | Published calculation snapshot. |
| `Feature129SnapshotProductFact` | Product values/effects. |
| `Feature129SnapshotEvidence` | Copied source/policy/effect evidence. |
| `Feature129PublicationEvent` | Publication event history. |
| `Feature129PolicyVersion` | Calculation/unit/materiality policy history. |

Mutable tables explicitly excluded from this trigger are `CompanyMonthIngestionManifestCurrentPointer`, `CompanyProductAliasCurrentPointer`, current lookup projection, `Feature129ComputationJob`, `Feature129ComputationOutbox`, and lease rows; their updates are constrained by FKs, checks, unique indexes, `xmin`, and state-transition code.

### 8.2 Planned implementation impact

Modify only through future implementation tasks: ingestion contracts/processor/normalizer and raw-link verification; ingestion rows/configuration/context and one new migration/model snapshot; Feature definitions, dedicated job/outbox repositories, dispatcher, publisher, queue, consumer, worker, handler, calculator, snapshot repository; local semantic validator and frame/executor adapters; AI facade/conversation/Telegram/frontend result mapping; auth/audit/observability/runbooks. `MetricRecalculationProcessor.cs`, shared provider adapters, existing unrelated direct queue/consumer behavior, historical migrations, and production data remain unchanged.

## 9. Normative standalone fixture

All monetary values are million rial. Unit is normalized `ThousandCount`; raw unit text is `هزار عدد`. Symbol is `غاذر`. Base and current are consecutive Jalali months: base `1405/04`, current `1405/05`.

### 9.1 Inputs

| Product | Unit | Base Q | Base P | Base R | Current Q | Current P | Current R |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| سبزیجات ۴۰ گرمی | هزار عدد | 1,000 | 100 | 100,000 | 1,966 | 97.6 | 191,881.6 |
| کنسرو مخلوط | هزار عدد | 2,000 | 100 | 200,000 | 1,700 | 100 | 170,000 |
| غذای آماده صادراتی | هزار عدد | 1,500 | 100 | 150,000 | 2,000 | 104 | 208,268.4 |

### 9.2 Expected effects

| Product | Quantity effect | Price effect | Residual | Contribution |
| --- | ---: | ---: | ---: | ---: |
| سبزیجات ۴۰ گرمی | 95,440.8 | -3,559.2 | 0 | 91,881.6 |
| کنسرو مخلوط | -30,000 | 0 | 0 | -30,000 |
| غذای آماده صادراتی | 51,000 | 7,000 | 268.4 | 58,268.4 |
| Total | 116,440.8 | 3,440.8 | 268.4 | 120,150 |

```text
Base total = 450,000
Current total = 570,150
Change = 120,150
Growth = 26.7%
Largest positive contributor = سبزیجات ۴۰ گرمی
Primary aligned driver = Quantity
```

The fixture test must calculate and assert, at stored scale, all of the following symmetric formulas:

```text
سبزیجات ۴۰ گرمی: (1,966 - 1,000) × (100 + 97.6) / 2 = 95,440.8;
                (97.6 - 100) × (1,000 + 1,966) / 2 = -3,559.2;
                residual = 191,881.6 - 100,000 - 95,440.8 - (-3,559.2) = 0;
کنسرو مخلوط:     (1,700 - 2,000) × (100 + 100) / 2 = -30,000; price = 0; residual = 0;
غذای آماده صادراتی: (2,000 - 1,500) × (100 + 104) / 2 = 51,000;
                   (104 - 100) × (1,500 + 2,000) / 2 = 7,000;
                   residual = 208,268.4 - 150,000 - 51,000 - 7,000 = 268.4;
```

The normative negative assertions reject malformed `غذاآماده`, `سبزیجات ۴۰گرمی`, changed Persian characters, changed product order, changed inputs/effects, or client-side recalculation replacing server values. The fixture also tests Arabic `ي/ك`, ZWNJ, missing product ID, incompatible `عدد`/`تن`, negative return, zero rate, rounding residual, missing month, corrected report, and fiscal-year-end variants as separate negative/quality cases.

## 10. Objective operations and history defaults

### 10.1 v1 SLO/load criteria

The following are initial configurable defaults, validated in staging before production sign-off. PostgreSQL version, representative deployment class, CPU/memory/storage class, warm/cold cache, dataset generator, concurrency, and measurement window must be recorded with every load run; these are not universal hardware guarantees.

| Operation | Bound | Target |
| --- | --- | --- |
| Published-snapshot repository read | ≤100 products, ≤24 periods | p95 ≤300 ms |
| Structured API/facade retrieval excluding model | same bound | p95 ≤700 ms |
| One two-period calculation | ≤100 products/period, ≤24 history periods | p95 ≤5 s |
| Evidence response | ≤20 groups, ≤10 facts/group | p95 ≤500 ms |
| Dispatcher lease transaction | ≤50 outbox rows | p95 ≤250 ms |

### 10.2 Backfill and history defaults

Backfill begins at `1404/01`, permits at most 2 concurrent calculations, sustains at most 60 company-month calculations/minute, dispatches batches of 50, polls every 5 seconds while work exists and 15 seconds while idle, checkpoints after each committed company-month, resumes from durable checkpoint, skips identical fingerprints, and processes newest-to-oldest within a company. Pause defaults are database CPU ≥80% for 5 minutes, connection-pool utilization ≥80% for 5 minutes, queue depth ≥10,000, failure rate ≥5% over 100 attempts, or provider protection response; resume requires all conditions below threshold for 10 minutes. Backfill does not call NADPCO unless separately authorized.

Maximum returned history is 24 months. A three-month or twelve-month average requires exactly 3 or 12 contiguous published months; missing months are `PartialWindow`. Anomaly detection requires at least 6 comparable periods, prefers 12, and uses robust z-score `|z| ≥ 3.5` plus the versioned materiality floor. YTD requires accepted OutputType 1 for both fiscal periods. Tuning changes do not recalculate facts; financial/materiality/anomaly policy changes create a new version and job.

## 11. Acceptance criteria, tests, and slices

Every runtime criterion is objective and mapped below. AC-78 is the design gate only.

| AC | Normative acceptance criterion | Test | Slice |
| --- | --- | --- | --- |
| AC-01–04 | Distinct/repeated observations, ordinal semantics, replay receipt, and raw/normalized reconciliation are exact. | T-ING-01–04 | 1 |
| AC-05–08 | All operation outcomes, mandatory type-0 readiness, optional late success, and one pointer key are exact. | T-MAN-01–04 | 1 |
| AC-09–12 | Composite pointer integrity, total selection, stale retry rebuild, and optional completion are exact. | T-MAN-05–08 | 1 |
| AC-13–15 | Revision corrections, precedence, equal ambiguity, and late older rejection are exact. | T-REV-01–04 | 1 |
| AC-16–27 | Draft isolation, complete ownership versions, range/member consistency, atomic approval, lifecycle changes, and historical stability are exact. | T-ALIAS-01–12 | 2–4 |
| AC-28–37 | Reported type-0 totals, seven-bucket formulas, lifecycle/unsafe paths, scale, units, rounding, cancellation, and copied evidence are exact. | T-CALC-01–06, T-DEC-01–02, T-CLASS-01, T-EVID-01–03 | 3 |
| AC-38–47 | Every §8.1 trigger rejects UPDATE/DELETE; repositories are insert-only; FKs, composite checks, events, pointers, and idempotency are exact. | T-IMM-01–05, T-PUB-01–03 | 1–3 |
| AC-48–50 | Feature 129 declares transactional outbox, direct scheduler rejects with `FeatureRequiresTransactionalOutbox`, and job/outbox intent is atomic. | T-ORCH-01–03 | 1–2 |
| AC-51–54 | Lease fencing, confirm recovery, same Message ID republish, and dedicated routing isolation are exact. | T-ORCH-04–07 | 1, 6 |
| AC-55–57 | ACK/NACK table, retry row delivery, bounded exhaustion, dead-letter/redrive, cancellation, and duplicate terminal handling are exact. | T-ORCH-08–12 | 3, 6 |
| AC-58–66 | Shared root contract is unchanged; local nested validation, UTF-16 spans, merge/resolution, one frame, and V1/V2 parity are exact. | T-SEM-01–09 | 4 |
| AC-67–71 | Payload v3, semantic replay, narrative replay, protected API behavior, and server-value-only web/Telegram output are exact. | T-CONV-01–03, T-API-01, T-UI-01–02 | 4–5 |
| AC-72 | Backfill/history numeric bounds, checkpoint/restart, contiguous windows, and fixed anomaly thresholds are enforced. | T-BF-01–04, T-HIST-01–03 | 6 |
| AC-73–75 | Monetary/unit gates produce exact fixed reasons and suppress unsafe public/physical outputs. | T-POLICY-01–03 | 2–4 |
| AC-76 | Security/non-disclosure and §10 SLO/load results pass on the declared staging class and bounds. | T-SEC-01, T-PERF-01 | 4–6 |
| AC-77 | The exact §9 fixture, formulas, totals, spelling/order, negative variants, and server-authoritative values pass. | T-FIX-01, T-CALC-FIXTURE-01 | 1–6 |
| AC-78 | This document is standalone, earlier files are unchanged, impact/test/slice mapping is complete, and no implementation change is included. | T-TRACE-01 | Design gate |

### 11.1 Required concurrency and recovery tests

Test identical retries; same revision metadata with different fingerprints; concurrent type-0 correction and optional completion; optional success after optional failure; failure after accepted optional success; two optional operations concurrently; pointer `xmin` conflict; serializable retry rebuilding the vector; job/outbox failure rolling back pointer; same fingerprint producing no generation/job; lease contention; publish-before-confirm crash; handler retry-row commit-before-ACK crash; transaction-commit failure NACK; duplicate terminal delivery; dead-letter exhaustion; authorized redrive; and a persisted retry being delivered again after `AvailableAtUtc`.

### 11.2 Vertical slices

**Slice 1 — source, revisions, manifest, and dispatch foundation:** immutable rows, total manifest algorithm, pointer, policy guard, job/outbox, dedicated dispatcher/queue, and trigger foundation.

**Slice 2 — ownership and unit policy:** complete versions/members/events, projection/pointer, range/composite/deferred constraints, approval transaction, monetary confirmation, conversion gate, and affected-period outbox.

**Slice 3 — calculator and publication:** seven-bucket calculator, complete fixture, immutable snapshot/evidence/events, pointer, dedicated consumer transaction, retry scheduling, ACK/NACK, exhaustion, and dead-letter.

**Slice 4 — semantic/API/conversation:** local strict validator, typed merge/resolution, shared frame, payload v3, replay, billing/auth, Telegram fallback, and API mapping.

**Slice 5 — investor-facing UI:** server-value summary, product/effect/evidence tables, accessible RTL/mobile views, and optional chart with no client arithmetic.

**Slice 6 — operations and history:** SLO/load tests, bounded backfill/checkpoints, dashboards, runbooks, dead-letter inspection/redrive, and optional history/anomaly/endpoint work. Durable retry correctness is already complete in Slice 3.

## 12. Readiness checklist and remaining decisions

- [x] V4-M-01 has one named durable retry owner, transactional retry-row creation, backoff, lease/fencing, crash rules, and ACK/NACK outcomes.
- [x] V4-M-02 has a total canonical vector algorithm, report-revision ambiguity rule, generation allocation, stale retry rebuild, and concurrency tests.
- [x] V4-M-03 has the complete standalone fixture with exact inputs, outputs, formulas, totals, units, periods, and negative assertions.
- [x] V4-M-04 has numeric SLO, load bounds, backfill limits, pause thresholds, history windows, and versioning rules.
- [x] V4-N-01 separates broker confirmation, outbox publication, Job execution, result terminality, and consumer deduplication.
- [x] V4-N-02 enumerates every immutable table and every excluded mutable pointer/lease/projection table.
- [x] All runtime ACs map to tests and slices.
- [x] `MetricRecalculationProcessor.cs` and unrelated direct consumer behavior remain unchanged by this design task.

Remaining business decisions are limited to confirming the provider monetary unit, approving any exact conversion dictionary, selecting later freshness/materiality thresholds (v1 defaults apply until versioned), and deciding whether ServiceSales becomes a future feature. None weakens the publication gates or durable retry contract.

**Final status:** `READY_FOR_DESIGN_REVIEW`
