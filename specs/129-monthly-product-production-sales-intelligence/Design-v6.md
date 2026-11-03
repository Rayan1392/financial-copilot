# Feature 129 — Monthly Product Production and Sales Intelligence

**Status:** `READY_FOR_DESIGN_REVIEW`  
**Revision:** v6  
**Date:** 2026-08-25  
**Authority:** this document is the complete normative design for Feature 129. Earlier documents are historical inputs only; no implementation task may require them for a missing contract.

## 1. Executive summary

Feature 129 explains a company’s monthly ProductSales revenue change using immutable provider evidence, deterministic report revision acceptance, complete product-alias ownership versions, governed unit policy, a seven-bucket price/quantity attribution, immutable snapshots, and bounded structured results through the existing AI facade.

The first public release (`R1-Core`) compares two consecutive published Jalali months from `1404/01` onward. It publishes only values whose source, identity, unit, calculation, evidence, security, and operational gates pass. The model may propose typed capability slots; it cannot calculate, select a database route, or execute with unvalidated values.

`R2-History` contains explicitly gated history, YoY, fiscal YTD, contiguous averages, anomaly, inferred-inventory, and backfill work. The optional direct endpoint is disabled unless separately approved.

## 2. Status and revision history

| Revision | Resolution carried forward |
| --- | --- |
| v1 | Initial product-sales intelligence, formulas, API, UX, and six-slice proposal. |
| v2 | Immutable revisions, alias versions, typed semantic slots, publication pointer, and orchestration contracts. |
| v3 | Dedicated Feature 129 dispatch boundary, evidence, replay, and expanded test/impact map. |
| v4 | Detailed architecture baseline; findings remained around retry ownership, manifest ordering, fixture completeness, operational criteria, confirmation states, and trigger inventory. |
| v5 | Added durable retry ownership, canonical manifest rebuild, complete fixture, numeric operations defaults, broker/job state separation, and exact trigger inventory, but compressed the standalone contract and grouped acceptance criteria. |
| v6 | Restores the detailed standalone design, separates mutable operational projections from immutable orchestration history, and defines 78 atomic acceptance criteria. |

## 3. Scope, release gates, and non-goals

### 3.1 Release gates

`R1-Core` includes source/revision/manifest correctness, canonical product ownership, unit and monetary gates, two-period calculation, attribution, immutable snapshot/evidence, durable dispatch/retry, semantic routing, structured AI-facade result, replay, Telegram fallback, investor web UI and all states, security/billing/audit, and core SLOs.

`R2-History` includes YoY, fiscal YTD, 3/12 contiguous averages, up to 24 months of product history, anomalies, inferred inventory signals, historical backfill, and their SLOs. R2 does not block R1 publication.

`Optional-Endpoint` is the direct non-chat read endpoint. It is disabled and excluded from R1 unless a separately authorized release decision enables it.

### 3.2 In scope

ProductSales `OutputType=0`; Jalali months from `1404/01`; latest published versus immediately preceding published month; explicit two-period comparison; immutable observations and revisions; manifest generations; complete alias ownership; monetary/unit policy; seven-bucket attribution; immutable evidence and snapshots; freshness, warnings, coverage, cancellation, contributor metadata; R1 semantic/API/conversation/Telegram/web delivery; R2 history and operations.

### 3.3 Non-goals

No forecasts, investment advice, target prices, inventory claims stated as facts, cross-company physical comparison, raw-provider calls from read paths, LLM arithmetic, unapproved economic-unit conversion, pre-1404 backfill, or automatic merge of ambiguous products.

## 4. Verified repository discovery

The repository is .NET 10, PostgreSQL, EF Core, Clean Architecture, and Microsoft Agent Framework V2. `src/backend/FinancialCopilot.API/appsettings.Development.json` sets `AiOrchestration:Mode` to `MicrosoftAgentFrameworkV2`.

Current authoritative behavior:

| Existing path | Observed behavior | v6 coexistence rule |
| --- | --- | --- |
| `NadpcoApiDataProviderClient.FetchMonthlyReportsAsync` | Requests ProductSales output slots and applies the current monthly boundary. | Add operation outcomes and preserve raw payload; do not change provider authentication semantics. |
| `NadpcoApiMonthlyActivityNormalizer.NormalizeAsync` | Groups by report identity, collapses duplicate `LineItemCode` using `Last()`, deletes current child rows, inserts new IDs, and calls existing calculators. | Feature 129 immutable observations become authority; current rows remain a compatibility projection during cutover. |
| `FinancialDataSyncProcessor.ProcessCoreAsync` | Stores raw payload, normalizes, marks sync completion, and publishes derived-metric recalculation. | Manifest and Feature 129 outbox intent are committed atomically; existing derived-metric publication remains separate. |
| `FeatureComputationProcessor.ProcessAsync` | Persists generic job and publishes completion/failure directly. | Direct scheduling rejects Feature 129 with `FeatureRequiresTransactionalOutbox`; dedicated path owns Feature 129. |
| `RabbitMqFeatureBus.ConsumeAsync` | ACKs a valid message before invoking the handler. | Unchanged for unrelated features; Feature 129 has a dedicated handler-before-ACK consumer. |
| `FeatureComputationConsumerWorker` | Resolves the generic processor through the shared consumer. | Unchanged; new Feature 129 worker/queue is separate. |
| `AiStructuredOutputContract` | Shared root structured-output contract. | Remains root-only; Feature 129 nested proposal is validated locally. |
| `AssistantMessagePayload` | Versioned persisted assistant envelope. | Add payload v3 result kind and backward decoder without changing old meanings. |

## 5. Functional requirements

1. A source row is preserved as an immutable observation, including repeated product codes.
2. Only an accepted ProductSales type-0 revision can provide R1 monthly revenue.
3. A manifest is ready only when mandatory type 0 has an accepted success or valid-empty result; optional outcomes are visible.
4. A snapshot references exactly one accepted manifest generation, alias ownership version, unit policy, and calculation policy.
5. Every product contribution is allocated exactly once to one of `Quantity`, `Price`, `Residual`, `NewProduct`, `DiscontinuedProduct`, `IdentityChange`, or `Unsafe/Unattributable`.
6. Company and product equations reconcile at stored scale or publication is blocked.
7. Historical evidence remains readable after source correction, alias change, policy change, or projection replacement.
8. No retryable failure is ACKed until a future durable delivery is committed.
9. Server values are authoritative in API, conversation, Telegram, and web output; clients perform layout only.

## 6. Non-functional requirements

All history is append-only and protected by database triggers. Mutable operational projections use bounded transitions, `xmin`, lease fencing, and idempotency keys. Monetary output is blocked until the provider unit is confirmed. Secrets, raw payloads, prompts, internal exceptions, and product text are not logged. All user-facing decimal values are serialized as invariant decimal strings or JSON numbers according to the DTO contract, never binary floating-point.

## 7. Provider operation and raw-source contract

For each company/Jalali month/family, the provider operation vector is exactly: `ProductSales:0`, `ProductSales:1`, `ProductSales:2`, `ProductSales:3`, `ProductSales:4`, and `ServiceSales:none`. Type 0 is mandatory; the others are optional. A provider response is `Succeeded`, `ValidEmpty`, `Retrying`, `Failed`, `Blocked`, or `Cancelled`.

Each operation stores request identity, provider, company, period, output type, raw payload ID/checksum, raw row count, normalized row count, reported revenue sum, comparable provider revision, valid publication timestamp, semantic fingerprint, completion time, fixed outcome/reason, and attempt ordinal. `ValidEmpty` is success with zero observations; a null slot caused by provider failure is not valid empty.

Raw JSON is stored before normalization. Numeric lexemes are preserved in canonical UTF-8 JSON. A candidate is blocked as `RawNormalizationMismatch` unless raw count and reported revenue reconcile with immutable observations at `numeric(28,8)`.

## 8. Immutable source observations

`MonthlyReportSourceObservation` is append-only. Its business identity is `(ProviderName, ExternalCompanyId, ExternalReportId, OutputType, JalaliYear, JalaliMonth, RawArrayOrdinal, SourceRowDiscriminator, DuplicateOccurrence)`. It stores canonical row JSON, economic signature, source-fact fingerprint, product/provider identifiers, title, raw unit text, governed unit code, quantity/rate/revenue values, raw payload ID/checksum, accepted revision ID, and completion timestamp.

Raw array ordinal is evidence only. A reorder must not alter semantic fingerprint. Distinct rows with one code remain distinct; exact repeated rows remain separate occurrences and emit `DuplicateFactObserved`.

## 9. Report identity, revisions, receipts, decisions, and accepted pointer

`MonthlyReportLogicalIdentity` is unique on `(ProviderName, ExternalCompanyId, ReportKind, OutputType, JalaliYear, JalaliMonth)`. `MonthlyReportRevision`, `MonthlyReportSourceObservation`, `MonthlyReportReceipt`, and `MonthlyReportDecisionEvent` are immutable. `MonthlyReportAcceptedPointer` is the only mutable accepted-current projection.

Under PostgreSQL `SERIALIZABLE` and `pg_advisory_xact_lock(hashtextextended('f129-report|' || logical-key, 0))`, acceptance precedence is: higher comparable provider revision; then newer valid provider publication timestamp; then no winner for absent/equal metadata with different semantic fingerprints. Receipt time, local completion time, and row ID never select an economic winner. Equal-precedence different facts become `AmbiguousRevisionOrder`; the prior pointer remains current and DataAdmin must record an accept/reject decision. Serialization/deadlock/optimistic conflicts retry three times after rereading state.

## 10. Company-month manifest

### 10.1 Canonical vector

Manifest selection locks `f129-manifest|provider|company|period|family` and locks the current pointer. It reads accepted report-revision state and immutable attempts, then chooses per operation: accepted successful revision; valid-empty accepted revision; otherwise latest immutable attempt only for operational visibility. A retry/failure cannot downgrade accepted success. A type-0 revision that is no longer accepted or an ambiguous revision blocks readiness.

The vector order is fixed: `ProductSales:0`, `ProductSales:1`, `ProductSales:2`, `ProductSales:3`, `ProductSales:4`, `ServiceSales:none`. Each element serializes `OperationKey`, `SelectedAcceptedRevisionId` (or `null`), `SelectedOutcome`, `AcceptedSemanticFingerprint` (or `null`), `MaterialReasonCode` (or `null`), and `MandatoryClassification`.

`ManifestFingerprint` is SHA-256 over UTF-8 canonical JSON with fixed property order, invariant numeric formatting, explicit null markers, and no whitespace. Attempt ordinals, receipt timestamps, generation IDs, and audit-only failure retries are excluded unless they change selected semantic state. Identical semantic vectors produce an audit receipt only. Optional success replacing failure, accepted correction, or a user-visible readiness/quality change creates the next generation. Failure-to-retrying with no accepted fact does not create a financial generation. Same-revision different facts are resolved by report revision logic, never receipt time.

`CompanyMonthIngestionManifestGeneration` is immutable. `CompanyMonthIngestionManifestCurrentPointer` stores the exact generation, accepted type-0 revision, fingerprint, readiness, `xmin`, and update time. Composite scope consistency is enforced by alternate-key FKs and a deferred constraint trigger.

## 11. Canonical products and alias ownership

`CanonicalProduct` identifies a company-scoped economic product. Blank/zero vendor IDs are absent. Matching order is approved ownership member, compatible collision-free provider key, exact economic signature, prior approved signature, then manual review. Text similarity alone cannot merge material revenue. A changed package, grade, unit, domestic/export status, or range creates an identity-review outcome rather than a silent merge.

Alias drafts are administrative and cannot enter calculations. Approval obtains the company/provider advisory lock, loads the prior complete set, applies the draft, validates identity/unit/signature/range, and inserts a new complete immutable `CompanyProductAliasOwnershipVersion` and members. The mutable current pointer/projection is replaced atomically. GiST exclusion with `btree_gist` rejects overlapping approved ownership ranges for the same provider/company/product signature. Merge, split, reversal, retirement, and reactivation create lineage events and a new complete version. Historical snapshots retain their version ID.

## 12. Unit and monetary policy

Raw unit text is retained beside normalized `UnitCode` and dimension. `ProviderMonetaryUnitPolicyVersion`, `UnitConversionPolicyVersion`, and `CalculationPolicyVersion` are immutable. `MonetaryUnitUnconfirmed` blocks public monetary output. `UnitConversionUnapproved` preserves reported monetary contribution but suppresses quantity/rate decomposition and production/sales comparison. v1 performs no physical conversion. Policy approval is DataAdmin-only, audited, and schedules affected eligible periods.

## 13. Calculation definitions and precision

All stored monetary, quantity, and rate calculations use PostgreSQL `numeric(28,8)` inputs and outputs; intermediate values use `numeric(38,16)`, then ToEven rounding to scale 8 at persistence. Canonical fingerprints use invariant decimal text with trailing zero normalization. Presentation rounding is separate.

For a valid continuing product with base `(Qb,Pb,Rb)` and current `(Qc,Pc,Rc)`:

```text
QuantityEffect = (Qc - Qb) * (Pb + Pc) / 2
PriceEffect    = (Pc - Pb) * (Qb + Qc) / 2
Residual       = (Rc - Rb) - QuantityEffect - PriceEffect
Contribution   = QuantityEffect + PriceEffect + Residual
```

Reported revenue `R` is authoritative. `Contribution` is signed. The company equation is `CurrentTotal - BaseTotal = Σ(Quantity + Price + Residual + NewProduct + DiscontinuedProduct + IdentityChange + Unsafe/Unattributable)`. Publication fails with `ReconciliationMismatch` if either product or company equality fails at scale 8.

## 14. Exhaustive attribution decision table

Rules are evaluated in priority order; the first matching row controls attribution.

| Priority | Conditions | Lifecycle | Identity | Quality | Attribution | Bucket | Reason |
| ---: | --- | --- | --- | --- | --- | --- | --- |
| 1 | Mandatory source blocked or accepted revision absent | Unknown | Unknown | Blocked | None | Unsafe/Unattributable | `SourceBlocked` |
| 2 | Comparison month absent | InsufficientHistory | Known | Partial | None | Unsafe/Unattributable | `MissingComparison` |
| 3 | Identity unresolved or manual review | Any | Ambiguous | Review | Signed revenue retained | IdentityChange | `IdentityAmbiguous` |
| 4 | Product exists in neither period | Inactive | Unmatched | Valid | Signed revenue retained | Unsafe/Unattributable | `NoComparableIdentity` |
| 5 | Base revenue absent/zero and current positive | New | Matched | Valid | Current signed revenue | NewProduct | `NewProduct` |
| 6 | Base positive and current absent/zero | Discontinued | Matched | Valid | Negative base revenue | DiscontinuedProduct | `DiscontinuedProduct` |
| 7 | Prior history exists, current reappears after absence | Resumed | Matched | Valid | Signed revenue retained | NewProduct | `ResumedProduct` |
| 8 | Valid compatible units, quantities and positive rates | Continuing | Matched | Valid | Symmetric formula | Quantity/Price/Residual | `Decomposed` |
| 9 | Rounded formula leaves signed remainder | Continuing | Matched | Rounded | Signed remainder | Residual | `StoredScaleResidual` |
| 10 | Base rate missing/invalid | Continuing | Matched | Unsafe | Entire signed revenue change | Unsafe/Unattributable | `MissingBaseRate` |
| 11 | Current rate missing/invalid | Continuing | Matched | Unsafe | Entire signed revenue change | Unsafe/Unattributable | `MissingCurrentRate` |
| 12 | Quantity missing or negative | Continuing | Matched | Unsafe | Entire signed revenue change | Unsafe/Unattributable | `InvalidQuantity` |
| 13 | Zero/negative rate | Continuing | Matched | Unsafe | Entire signed revenue change | Unsafe/Unattributable | `InvalidRate` |
| 14 | Approved compatible conversion exists | Continuing | Matched | Converted | Formula after governed conversion | Quantity/Price/Residual | `ApprovedConversion` |
| 15 | Unit changed without approved conversion | Continuing | Matched | Unsafe | Entire signed revenue change | Unsafe/Unattributable | `UnitConversionUnapproved` |
| 16 | Return/reversal or negative adjustment | Continuing | Matched | Return | Signed revenue retained | Unsafe/Unattributable | `ReturnOrReversal` |
| 17 | Optional output is partial but type 0 accepted | Continuing | Matched | Partial | Type-0 signed revenue retained | Formula or Unsafe | `OptionalPartial` |
| 18 | Company change equals zero at scale 8 | Any | Any | Valid | Product effects retained | N/A | `ZeroCompanyChange` |
| 19 | Absolute company change below policy floor | Any | Any | Immaterial | Product effects retained | N/A | `ImmaterialCompanyChange` |
| 20 | Gross opposing effect mass / net change exceeds policy threshold | Continuing | Matched | Cancellation | Effects retained; driver suppressed | Existing buckets | `HighCancellation` |
| 21 | Coverage below driver threshold | Any | Any | Insufficient | Effects retained; classification null | Existing buckets | `InsufficientClassificationCoverage` |

`MatchCoverage = matched reported revenue / absolute total reported revenue`, `DecompositionCoverage = decomposed reported revenue / absolute total reported revenue`, `ResidualRatio = absolute residual / max(absolute company change, materiality floor)`, `UnmatchedRatio = absolute unmatched revenue / absolute total revenue`, and `CancellationRatio = gross opposing effect mass / max(absolute company change, materiality floor)`. Zero denominators return null and the corresponding fixed reason. Default driver threshold is 60%; cancellation suppression is 3.0; materiality is policy-versioned; breadth is at least two contributing products; concentration uses HHI with exact server-computed values. Mix shift is a composition signal, never additive revenue attribution.

## 15. Company classification and publication policy

Classification uses signed additive effects only after coverage and cancellation guards. `QuantityDriven`, `PriceDriven`, `ResidualDriven`, `Mixed`, `NewDiscontinuedDriven`, and `Unclassified` are closed values. A high-cancellation or insufficient-coverage result is `Unclassified` with its reason. Negative revenue is retained in totals; it does not change lifecycle by itself.

Publication requires accepted type-0 manifest, monetary policy, valid ownership version, policy versions, exact reconciliation, evidence completeness, no blocking quality event, and current-pointer concurrency success. A stale or failed run never displaces the current published snapshot.

## 16. Calculation orchestration

Manifest, alias approval, and policy approval create a deterministic Feature 129 job idempotency key from scope, selected manifest generation, ownership version, unit policy, calculation policy, and algorithm version. The ingestion transaction inserts the Job projection and first Outbox projection, plus corresponding immutable events, before commit. The existing metric recalculation queue is never the Feature 129 retry owner.

## 17. Job, Outbox, retry, RabbitMQ, and consumer contracts

### 17.1 Mutable projections versus immutable history

`Feature129ComputationJob` and `Feature129ComputationOutbox` are mutable operational projections. Their state, lease, retry schedule, current attempt, and current reason are not immutable history and are excluded from `prevent_f129_history_mutation()`.

Append-only history consists of `Feature129ComputationJobStateEvent`, `Feature129ComputationOutboxStateEvent`, `Feature129ComputationAttempt`, `Feature129ComputationDeadLetter`, and `Feature129ComputationRedriveDecision`. Every projection transition appends its event in the same transaction. Events contain event ID, job ID, outbox ID where applicable, attempt number, from/to state, fixed reason, policy version, correlation ID, actor/system identity, occurred time, and idempotency key. These history tables have rejecting UPDATE/DELETE triggers.

Broker confirmation is Outbox publication state only. Calculation completion is Job/result state only. Consumer deduplication uses Job ID, source fingerprint, attempt number, and Message ID evidence; result uniqueness is `(JobId, SourceFingerprint)`.

### 17.2 State and retry policy

Job states are `Requested`, `Running`, `RetryScheduled`, `Completed`, `PermanentlyFailed`, `Cancelled`, and `DeadLettered`. Outbox states are `Pending`, `Leased`, `PublishedAwaitingConfirm`, `Confirmed`, `DeliveryConsumed`, `RetryablePublishFailure`, `PermanentlyFailed`, and `DeadLettered`.

Attempt numbers start at 1 and are unique with `(JobId, AttemptNumber)`. Message ID is stable across republish of the same attempt. Delays are: attempt 1 immediate, attempt 2 30 seconds, attempt 3 2 minutes, attempt 4 10 minutes, attempt 5 30 minutes, attempt 6 2 hours. The outbox dispatcher is the sole durable delivery owner. It selects due `Pending` rows with `FOR UPDATE SKIP LOCKED`, leases with token/expiry and `xmin`, publishes, persists confirm state, and recovers expired leases using bounded predicates.

A retryable handler outcome is ACKable only after one transaction persists failed attempt evidence, appends Job transition event, updates Job to `RetryScheduled`, inserts the next Outbox projection, appends Outbox-created event, stores `AvailableAtUtc`, and commits. Otherwise the dedicated consumer NACKs/requeues. Permanent failure ACKs after terminal evidence/dead-letter commit. Exhaustion creates a dead letter. DataAdmin redrive is audited and creates a new attempt number with lineage. Cancellation is an authorized terminal transition; a queued non-started attempt becomes `Cancelled` without deleting history.

Forbidden transitions include terminal Job to `Running`, `Completed` to retry, `Confirmed` to `Pending` without an authorized recovery transition, and any transition with a stale `xmin` or lease token. Duplicate terminal delivery records an idempotent event and ACKs.

### 17.3 Transition tables

| Projection | From | To | Authority and guard |
| --- | --- | --- | --- |
| Job | `Requested` | `Running` | Consumer locks Job by ID and `xmin`; one current attempt. |
| Job | `Running` | `Completed` | Handler commits immutable result and completion event first. |
| Job | `Running` | `RetryScheduled` | Same transaction inserts next Outbox and both events. |
| Job | `Running` | `PermanentlyFailed` | Fixed permanent reason and attempt evidence committed. |
| Job | `Running` or `RetryScheduled` | `Cancelled` | Authorized cancellation with `xmin`; no deletion. |
| Job | `RetryScheduled` | `Running` | Dispatcher-created delivery is consumed and handler obtains lease. |
| Job | `Running` | `DeadLettered` | Retry exhaustion or poison policy; dead-letter row committed. |
| Outbox | `Pending` | `Leased` | Dispatcher uses `SKIP LOCKED`, due time, lease token, and `xmin`. |
| Outbox | `Leased` | `PublishedAwaitingConfirm` | Same Message ID is published; lease token matches. |
| Outbox | `PublishedAwaitingConfirm` | `Confirmed` | Confirm callback matches Message ID and token. |
| Outbox | `Confirmed` | `DeliveryConsumed` | Dedicated consumer commits handler outcome. |
| Outbox | `Leased` or `PublishedAwaitingConfirm` | `RetryablePublishFailure` | Lease owner records fixed retryable publish reason. |
| Outbox | `RetryablePublishFailure` | `Pending` | Backoff and `AvailableAtUtc` are committed with event. |
| Outbox | Any nonterminal | `DeadLettered` | Poison/permanent policy or authorized terminal handling. |

No transition may skip its event, mutate a terminal projection without a redrive decision, reuse an attempt number, or proceed with a stale `xmin`/lease token. The Job projection is authoritative for execution state; the Outbox projection is authoritative for delivery state; event/history tables are authoritative for audit chronology.

## 18. Immutable snapshot and current-pointer publication

`Feature129Snapshot`, `Feature129SnapshotProductFact`, `Feature129SnapshotEvidence`, `Feature129PublicationEvent`, and `Feature129PolicyVersion` are append-only. `Feature129CurrentSnapshotPointer` is mutable and protected by `xmin`, advisory lock, composite FKs, and a deferred scope-consistency trigger. One current row exists per provider/company/comparison key. A failed child insert, evidence mismatch, pointer conflict, or reconciliation failure rolls back the complete publication and leaves the prior pointer untouched.

## 19. Evidence and historical reproducibility

Every public number maps to evidence containing source observation IDs, report revision IDs, payload IDs/checksums, copied numeric inputs, unit/policy IDs, alias version, manifest generation, calculation policy, reason codes, and source timestamps. Evidence is bounded to 20 groups and 10 facts per group for public transport; internal evidence remains complete and immutable. Historical replay reads the persisted snapshot and narrative, never current projections.

## 20. Persistence contract

All tables below use PostgreSQL and the stated trigger/delete rules. `IMM` means immutable history; `MUT` means mutable operational projection.

| Table | Required columns and types | Keys/indexes/FKs | Delete/update policy |
| --- | --- | --- | --- |
| `MonthlyReportLogicalIdentity` | `Id uuid`, provider/company/report kind/output type/year/month, `AcceptedRevisionId uuid null`, `xmin` | Unique logical business key; accepted FK | MUT pointer; Restrict history |
| `MonthlyReportRevision` | `Id uuid`, logical ID, provider revision, publication time, fingerprint, payload ID/checksum, status, created time | Logical FK; fingerprint index | IMM trigger; Restrict |
| `MonthlyReportSourceObservation` | `Id uuid`, revision ID, ordinal, discriminator, duplicate occurrence, product fields, `numeric(28,8)` Q/P/R, raw JSON/checksum | Revision FK; semantic fingerprint index | IMM trigger; Restrict |
| `MonthlyReportReceipt` | `Id uuid`, logical ID, operation ID, attempt, received time, outcome, reason, payload checksum | `(operation,attempt)` unique | IMM trigger; Restrict |
| `MonthlyReportDecisionEvent` | event ID, logical/revision IDs, decision, actor, reason, correlation, time | revision/event idempotency unique | IMM trigger; Restrict |
| `MonthlyReportAcceptedPointer` | logical ID, accepted revision ID, fingerprint, `xmin`, updated time | One-to-one logical FK; composite consistency trigger | MUT pointer; no history trigger |
| `CompanyMonthIngestionOperationAttempt` | ID, scope, operation key, attempt, outcome, accepted revision, fingerprint, count/sum, reason, times | scope/operation/attempt unique | IMM trigger; Restrict |
| `CompanyMonthIngestionManifestGeneration` | ID, scope, generation, canonical vector JSON, fingerprint, readiness, created time | scope/generation and scope/fingerprint unique | IMM trigger; Restrict |
| `CompanyMonthIngestionManifestCurrentPointer` | scope, generation ID, type-0 revision, fingerprint, readiness, `xmin` | One scope row; composite generation FK | MUT pointer; no history trigger |
| `CanonicalProduct` | ID, company ID, canonical code/title, active flag, created time | company/code unique | Restrict; administrative mutation only |
| `CompanyProductAliasDraft` | ID, scope, proposed members JSON, actor, status, reason, times | draft idempotency unique | MUT admin; no calculation FK |
| `CompanyProductAliasOwnershipVersion` | ID, scope, version number, actor/reason, signature, effective range | scope/version unique; GiST exclusion key | IMM trigger; Restrict |
| `CompanyProductAliasOwnershipVersionMember` | version ID, provider key/signature/product ID, range, unit/dimension | composite PK; version FK | IMM trigger; Restrict |
| `CompanyProductAliasDecisionEvent` | event ID, version ID, lineage type, predecessor/successor IDs, actor/reason/time | event idempotency unique | IMM trigger; Restrict |
| `CompanyProductAliasCurrentPointer` | scope, version ID, `xmin`, updated time | One scope; composite FK | MUT pointer |
| `CompanyProductAliasCurrentProjection` | scope, provider key/signature, canonical product, version ID, range | filtered unique; GiST exclusion | MUT projection |
| `ProviderMonetaryUnitPolicyVersion` | ID, provider/company, unit code, scale, status, actor/time | scope/version unique | IMM trigger; Restrict |
| `UnitConversionPolicyVersion` | ID, source/destination dimensions, factor `numeric(28,8)`, status, actor/time | scope/version unique | IMM trigger; Restrict |
| `CalculationPolicyVersion` | ID, algorithm, precision, materiality, thresholds, status/time | feature/version unique | IMM trigger; Restrict |
| `Feature129ComputationJob` | ID, feature/version, scope IDs, fingerprint, idempotency key, state, current attempt, next time, reason, `xmin` | idempotency unique; source fingerprint index | MUT projection; no history trigger |
| `Feature129ComputationJobStateEvent` | event fields, from/to, attempt, reason, policy, actor, correlation, idempotency | event idempotency unique; Job FK | IMM trigger; Restrict |
| `Feature129ComputationOutbox` | ID, Job ID, attempt, message ID, checksum, state, lease token/expiry, available/confirm times, `xmin` | `(JobId,AttemptNumber)` and idempotency unique | MUT projection; no history trigger |
| `Feature129ComputationOutboxStateEvent` | event fields including Outbox ID and from/to | event idempotency unique; Outbox FK | IMM trigger; Restrict |
| `Feature129ComputationAttempt` | ID, Job ID, attempt, Message ID, handler outcome, reason, input fingerprint, started/completed | `(JobId,AttemptNumber)` unique | IMM trigger; Restrict |
| `Feature129ComputationDeadLetter` | ID, Job/Outbox/attempt, reason, payload checksum, created time | Job/attempt index | IMM trigger; Restrict |
| `Feature129ComputationRedriveDecision` | ID, dead-letter ID, new attempt, actor, reason, policy, time | decision idempotency unique | IMM trigger; Restrict |
| `Feature129Snapshot` | ID, job/source/manifest/alias/policy IDs, base/current periods, totals, status, created time | source fingerprint unique | IMM trigger; Restrict |
| `Feature129SnapshotProductFact` | ID, snapshot/product IDs, Q/P/R values, lifecycle/match/quality, seven effects `numeric(28,8)` | filtered canonical uniqueness; snapshot FK | IMM trigger; Restrict |
| `Feature129SnapshotEvidence` | ID, snapshot/fact ID, source/revision/policy IDs, copied numeric JSON, reason/time | evidence index; FKs | IMM trigger; Restrict |
| `Feature129PublicationEvent` | event ID, snapshot ID, publication state, actor/reason/time | snapshot/event unique | IMM trigger; Restrict |
| `Feature129CurrentSnapshotPointer` | scope key, snapshot ID, `xmin`, updated time | one current row; composite scope FK | MUT pointer |
| `Feature129BackfillCheckpoint` | job/batch/company/period, last committed key, status, updated time, `xmin` | scope unique | MUT checkpoint; audit events immutable |

All money/quantity/rate columns are `numeric(28,8)`; policy factors are `numeric(28,8)`. `btree_gist` is required for mixed equality/range exclusion. Composite FK pairs include `(ProviderName,ExternalCompanyId,JalaliYear,JalaliMonth,Family)` to the same scope key on generation/pointer/job/snapshot. Historical FKs use `ON DELETE RESTRICT`; raw payload links across DbContexts are soft references verified by ID and checksum. Deferred triggers validate pointer-selected version equality. `prevent_f129_history_mutation()` rejects UPDATE/DELETE on every immutable table in the trigger inventory in section 21.

## 21. Exact trigger inventory

The same `BEFORE UPDATE OR DELETE` rejecting trigger is installed on: `MonthlyReportRevision`, `MonthlyReportSourceObservation`, `MonthlyReportReceipt`, `MonthlyReportDecisionEvent`, `CompanyMonthIngestionOperationAttempt`, `CompanyMonthIngestionManifestGeneration`, `CompanyProductAliasOwnershipVersion`, `CompanyProductAliasOwnershipVersionMember`, `CompanyProductAliasDecisionEvent`, `Feature129ComputationJobStateEvent`, `Feature129ComputationOutboxStateEvent`, `Feature129ComputationAttempt`, `Feature129ComputationDeadLetter`, `Feature129ComputationRedriveDecision`, `Feature129Snapshot`, `Feature129SnapshotProductFact`, `Feature129SnapshotEvidence`, `Feature129PublicationEvent`, and `Feature129PolicyVersion`.

Excluded mutable tables are `MonthlyReportLogicalIdentity`, `MonthlyReportAcceptedPointer`, `CompanyMonthIngestionManifestCurrentPointer`, `CompanyProductAliasCurrentPointer`, `CompanyProductAliasCurrentProjection`, `Feature129ComputationJob`, `Feature129ComputationOutbox`, `Feature129CurrentSnapshotPointer`, leases, and `Feature129BackfillCheckpoint`.

## 22. Semantic proposal and local validation

The local validator accepts only this closed root shape:

```json
{
  "schemaVersion": 1,
  "capability": "monthly_product_intelligence",
  "slots": {
    "company": {"symbol":"غاذر","companyId":null,"confidence":0.98,"evidence":[{"start":0,"length":4}]},
    "product": null,
    "currentPeriod":{"jalaliYear":1405,"jalaliMonth":5},
    "comparisonPeriod":{"kind":"previous_published"},
    "analysisFocus":"revenue_attribution",
    "measure":"reported_sales",
    "presentation":"summary_and_products",
    "resultLimit":20
  }
}
```

Allowed slots are `company`, `product`, `currentPeriod`, `comparisonPeriod`, `analysisFocus`, `measure`, `presentation`, and `resultLimit`. Unknown root/nested properties, duplicate conflicting slots, unsupported schema, invalid Jalali periods, limits above 100, confidence outside 0–1, malformed UTF-16 spans, prompt-injection instructions, and unresolved identity are rejected with closed codes: `UnknownProperty`, `UnsupportedSchema`, `InvalidPeriod`, `InvalidLimit`, `InvalidConfidence`, `InvalidEvidenceSpan`, `ConflictingSlot`, `PromptInjection`, and `UnresolvedIdentity`.

Typed values require the fields shown by their kind and forbid all other fields. Company resolution uses symbol/company ID and normalized Persian text; product resolution uses company scope, provider signature, alias version, and exact/approved match. Ambiguous resolution returns bounded clarification and persists no executor input. Evidence spans use UTF-16 offsets and must lie within the original user text.

The root `AiStructuredOutputContract` remains unchanged. The local Feature 129 validator runs after model response receipt and before merge. Deterministic interpreter values outrank model values when both are valid; model paraphrase fills missing slots only. Conflict yields clarification. The same `ValidatedQueryFrame` is used by V1, native V2, and fallback V2. The active MAF V2 workflow carries proposal, validation result, frame, and executor result in versioned internal messages. The executor accepts only the frame and has no model dependency.

Examples accepted: `گزارش فروش و اثر تغییر قیمت و مقدار غاذر در ۱۴۰۵/۰۵ نسبت به ماه قبل چیست؟`, `برای غاذر، ماه جاری را با ماه قبل از نظر فروش محصولات مقایسه کن`, and `سبزیجات ۴۰ گرمی غاذر چقدر از رشد فروش را توضیح می‌دهد؟`.

### 22.1 Slot contract

| Slot | Value kind and required fields | Forbidden/normalization | Resolver, confidence, evidence, task state |
| --- | --- | --- | --- |
| Company/symbol | `CompanyRef`: symbol or company ID, confidence, spans | No free-form company ID; Persian Arabic-letter normalization only | Canonical company resolver; ambiguity clarifies; persists resolved ID and spans. |
| Product | `ProductRef`: company ID, canonical/provider identity, confidence, spans | No product without company scope; no text-only merge | Alias resolver; collision clarifies; persists ownership version. |
| Current period | `JalaliPeriod`: year/month | Month 1–12; no unsupported Gregorian string | Explicit period or latest published default; span persisted. |
| Comparison period/kind | `JalaliPeriod` or `previous_published`/`previous_fiscal` | No arbitrary unsupported kind | Validates accepted/comparable period; missing comparison clarifies. |
| Analysis focus | enum `summary`, `revenue_attribution`, `production_sales`, `contribution` | No unknown enum or model-created capability | Capability registry precedence; unsupported value clarifies. |
| Measure | enum `reported_sales`, `quantity`, `rate`, `production` | No computed measure outside executor | Validates focus and unit policy; persists in frame. |
| Presentation | enum `summary`, `table`, `chart`, `evidence` | No client calculation instruction | Maps to DTO sections; chart is layout only. |
| Result limit | integer 1–100 | No decimal, negative, or unbounded value | Clamped only by policy; otherwise `InvalidLimit`; persisted. |

Each slot has `confidence`, optional provenance (`deterministic`, `model`, `follow_up`), and UTF-16 evidence spans when sourced from text. Task state persists the validated proposal, canonical frame, clarification status, and resolver version; raw model JSON is not an executor input.

## 23. API, structured result, conversation, Telegram, and web

The AI facade remains the R1 entry point. The optional endpoint is `GET /api/features/v1/monthly-products/{companyId}?current=1405-05&comparison=previous` and is disabled unless the Optional-Endpoint flag is enabled.

The Feature 129 result has `schemaVersion`, `feature`, `company`, `periods`, `summary`, `products`, `contribution`, `coverage`, `warnings`, `freshness`, `evidence`, `policyVersionIds`, `sourceRevisionIds`, `state`, and `pagination`. Decimal values are server values; arrays are ordered by explicit server rank then canonical product ID. Top-N overflow is represented by an `Other` member containing member IDs and server-computed amount. States are `Published`, `Partial`, `Stale`, `Processing`, `Unavailable`, `Blocked`, `Empty`, and `Error`, each with a fixed reason code.

Conversation payload v3 wraps this result in `AssistantMessagePayload` without changing versions 1/2. The decoder maps old payloads to their existing result kinds, treats unknown future result kinds as bounded `UnsupportedResult` rather than failing the conversation, and compares replay by schema, immutable IDs, decimal values, enum values, collection order, and evidence IDs—not serializer bytes. The persisted Persian narrative is deterministic and replayed verbatim.

Telegram output is limited to 3,500 UTF-16 characters per message, escapes Markdown metacharacters, splits at section boundaries, includes summary/unit/period/warning/evidence limitation footer, and never fabricates a chart. Web output includes summary cards, Persian narrative, trend chart, contribution table/chart, production-versus-sales panel, product table, evidence drawer, and accessible table equivalent. Loading, partial, stale, processing, unavailable, blocked, empty, and error states are explicit. RTL/mobile/keyboard access and non-color status indicators are required. The client may calculate chart layout offsets only; it may not calculate financial amounts, totals, rankings, or percentages.

### 23.1 Result DTO example

```json
{
  "schemaVersion": 1,
  "feature": "monthly_product_intelligence",
  "state": "Published",
  "company": {"id":"company-1","symbol":"غاذر"},
  "periods": {"base":"1405/04","current":"1405/05","kind":"previous_published"},
  "summary": {"baseRevenue":"450000.00000000","currentRevenue":"570150.00000000","change":"120150.00000000","growthPercent":"26.70000000","driver":"Quantity"},
  "products": [{"id":"product-1","name":"سبزیجات ۴۰ گرمی","reportedChange":"91881.60000000","lifecycle":"Continuing","match":"Approved","quality":"Valid","effects":{"quantity":"95440.80000000","price":"-3559.20000000","residual":"0.00000000"}}],
  "contribution": {"items":[{"productId":"product-1","amount":"91881.60000000","rank":1}],"other":null,"endingTotal":"120150.00000000"},
  "coverage": {"match":"1.00000000","decomposition":"1.00000000","residualRatio":"0.00223387","unmatchedRatio":"0.00000000"},
  "warnings": [],
  "freshness": {"sourceCompletedAtUtc":"2026-08-25T10:00:00Z","ageSeconds":120},
  "evidence": [{"id":"e-1","fact":"summary.currentRevenue","sourceRevisionId":"revision-1","policyVersionId":"policy-1"}],
  "policyVersionIds": ["policy-1"],
  "sourceRevisionIds": ["revision-1","revision-2"],
  "pagination": {"limit":20,"returned":1,"hasMore":false}
}
```

`Partial`, `Stale`, `Processing`, `Unavailable`, `Blocked`, `Empty`, and `Error` responses retain the same envelope and replace numerical sections with `null` where unavailable, plus a fixed `stateReason`. This prevents clients from interpreting omitted values as zero.

### 23.2 Web state matrix

| State | Server payload rule | Required UI |
| --- | --- | --- |
| Loading | No result yet | Skeleton and accessible progress text. |
| Published | Complete R1 result | Summary, tables, evidence, and optional visualizations. |
| Partial | Optional source outcome missing | Published values plus labeled missing coverage/warning. |
| Stale | Previous snapshot is safe but newer job exists | Previous values, timestamp, stale banner, refresh affordance. |
| Processing | Job is nonterminal and no current result | Progress text and no fabricated values. |
| Unavailable | No accepted type-0/source | Explanation and no numerical cards. |
| Blocked | Quality/policy/reconciliation gate failed | Fixed reason, safe limitation, no unsafe number. |
| Empty | Valid accepted empty source | Explicit empty result, not error. |
| Error | Unexpected bounded failure | Retry message and correlation-safe support code. |

## 24. Security, authorization, billing, audit, and observability

The existing authenticated AI facade remains the R1 boundary. Entitlement is checked before model/tool execution; rate limits apply by authenticated principal and capability; billing reserves before execution and commits only after a successful response, with replay charged according to the existing replay policy. DataAdmin is required for revision decisions, alias approval, monetary confirmation, conversion approval, cancellation, redrive, and backfill control. Evidence authorization excludes raw payloads and credentials.

Input limits are 8,000 UTF-16 user characters, 100 result rows, 20 evidence groups, and 10 facts per group. Markdown is sanitized server-side. Audit events store actor, action, target IDs, old/new state, fixed reason, correlation ID, policy version, and UTC time; never raw payload, prompt, secret, or product narrative.

Metrics are fixed low-cardinality names: `f129_manifest_ready_total`, `f129_calculation_duration_ms`, `f129_publication_total`, `f129_retry_total`, `f129_deadletter_total`, `f129_reconciliation_block_total`, `f129_api_duration_ms`, and `f129_backfill_lag_seconds`. Allowed labels are release gate, outcome, reason code, provider, and deployment environment. User ID, company ID, product ID, prompt text, and raw error text are forbidden labels. Alerts: publication blocks >5% over 100 jobs, dead letters >0 for 10 minutes, retry backlog >1,000 for 10 minutes, p95 core retrieval >700 ms for 15 minutes, and reconciliation mismatch >0.

Runbooks cover provider blockage, manifest ambiguity, retry backlog, lease recovery, dead-letter inspection/redrive, stale publication, unit confirmation, alias collision, and rollback. Each runbook identifies DataAdmin authorization and does not permit mutation of history.

## 25. Numeric operations, load, and backfill policies

R1 defaults: snapshot read p95 ≤300 ms for 100 products/24 periods; facade retrieval excluding model p95 ≤700 ms; two-period calculation p95 ≤5 s for 100 products/24 periods; evidence response p95 ≤500 ms for 20 groups/10 facts; dispatcher lease transaction p95 ≤250 ms for 50 rows. Staging results record PostgreSQL version, deployment class, CPU/memory/storage, cache state, dataset generator, concurrency, and window.

R2 backfill starts at `1404/01`, maximum two concurrent calculations, 60 company-month calculations/minute, batches of 50, polls every 5 seconds while active and 15 seconds idle, checkpoints after each committed company-month, resumes from checkpoint, skips identical fingerprints, and processes newest-to-oldest per company. It pauses at CPU or pool utilization ≥80% for 5 minutes, queue depth ≥10,000, failure rate ≥5% over 100 attempts, or provider protection response; resumes after all conditions are below threshold for 10 minutes.

## 26. Normative fixture

All monetary values are million rial. Raw unit text is `هزار عدد`; normalized unit is `ThousandCount`; symbol is `غاذر`; base is `1405/04`; current is `1405/05`.

| Product | Base Q | Base P | Base R | Current Q | Current P | Current R |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| سبزیجات ۴۰ گرمی | 1,000 | 100 | 100,000 | 1,966 | 97.6 | 191,881.6 |
| کنسرو مخلوط | 2,000 | 100 | 200,000 | 1,700 | 100 | 170,000 |
| غذای آماده صادراتی | 1,500 | 100 | 150,000 | 2,000 | 104 | 208,268.4 |

Expected effects are respectively `(95,440.8,-3,559.2,0,91,881.6)`, `(-30,000,0,0,-30,000)`, and `(51,000,7,000,268.4,58,268.4)` for quantity, price, residual, and contribution. Base total is `450,000`; current total is `570,150`; change is `120,150`; growth is `26.7%`; largest positive contributor is `سبزیجات ۴۰ گرمی`; primary aligned driver is `Quantity`.

The fixture test also rejects malformed `غذاآماده`, `سبزیجات ۴۰گرمی`, changed Persian characters, changed product order, changed inputs/effects, Arabic `ي/ك` substitution, ZWNJ removal, missing product ID, incompatible `عدد/تن`, negative return, zero rate, rounding residual, missing month, corrected report, and fiscal-year-end mismatch. These are separate tests, not implicit assertions.

## 27. Named testing strategy

Named tests are mandatory and are the referents used by the AC table.

| Test IDs | Coverage |
| --- | --- |
| `T-ING-01` to `T-ING-04` | Duplicate observations, reorder invariance, replay receipt, raw reconciliation. |
| `T-MAN-01` to `T-MAN-08` | Operation states, type-0 barrier, optional completion, pointer integrity, vector fingerprint, stale rebuild, no-op generation. |
| `T-REV-01` to `T-REV-04` | Correction lineage, precedence, ambiguity, late older payload. |
| `T-ALIAS-01` to `T-ALIAS-12` | Draft isolation, complete version, member/composite checks, overlap, approval atomicity, merge/split/reversal, historical stability. |
| `T-POLICY-01` to `T-POLICY-03` | Monetary confirmation, shadow output, conversion gate. |
| `T-CALC-01` to `T-CALC-12` | Totals, symmetric formulas, lifecycle, unsafe branches, precision, cancellation, coverage, classification. |
| `T-IMM-01` to `T-IMM-05` | Trigger rejection, repository insert-only behavior, Restrict deletes, composite/deferred constraints. |
| `T-PUB-01` to `T-PUB-05` | Pointer concurrency, identical/no-op calculation, rollback, uniqueness, publication event. |
| `T-ORCH-01` to `T-ORCH-14` | Outbox atomicity, direct guard, lease, confirm recovery, ACK/NACK, retry creation, crash points, exhaustion, redrive, cancellation, deduplication. |
| `T-SEM-01` to `T-SEM-12` | Schema closure, spans, slots, merge, resolver, prompt injection, frame parity, MAF messages. |
| `T-API-01` to `T-API-06` | DTO, payload v3, decoder, replay, authorization/billing, optional endpoint. |
| `T-TG-01` to `T-TG-03` | Telegram size, escaping, bounded footer/fallback. |
| `T-UI-01` to `T-UI-10` | Server-value mapping, all states, RTL/mobile, keyboard, accessible table, no client arithmetic. |
| `T-HIST-01` to `T-HIST-08` | YoY, YTD, contiguous averages, history, anomaly, inferred wording, backfill checkpoint/recovery. |
| `T-SEC-01` to `T-SEC-06` | Auth, entitlement, billing, DataAdmin actions, disclosure, audit. |
| `T-PERF-01` to `T-PERF-05` | R1 SLOs, R2 load/backfill, alert thresholds. |
| `T-FIX-01` to `T-FIX-12` | Complete fixture and each negative variant. |
| `T-E2E-01` to `T-E2E-08` | Source-to-UI, correction replay, partial, blocked, retry recovery, semantic Persian query, Telegram, R2 backfill. |

Property-based tests generate valid signed rows and assert product/company reconciliation at stored scale. PostgreSQL integration tests use a real PostgreSQL instance for `numeric`, GiST, deferred triggers, `xmin`, advisory locks, and `ON DELETE RESTRICT`. RabbitMQ tests use a real broker or protocol-compatible test container for ACK/NACK and confirms.

## 28. Atomic acceptance criteria and traceability

The following table has exactly one row per criterion. Each row has one behavior, explicit preconditions, one observable outcome, a section, named test, and slice.

| AC | Release gate | Preconditions | One normative behavior | Expected result/failure code | Design section | Named test(s) | Slice |
| --- | --- | --- | --- | --- | --- | --- | --- |
| AC-01 | R1-Core | Two source rows share a product code but have distinct source-row discriminators. | Persist both rows as separate immutable observations. | Two observations and both revenues are present. | §8 | T-ING-01 | 1 |
| AC-02 | R1-Core | The same facts arrive in a different array order. | Compute the same semantic fingerprint independent of ordinal. | Fingerprints equal; ordinals differ only in evidence. | §8 | T-ING-02 | 1 |
| AC-03 | R1-Core | An identical payload is received again. | Record a receipt without creating an economic revision. | No new revision; immutable receipt exists. | §9 | T-ING-03 | 1 |
| AC-04 | R1-Core | Raw row count or revenue differs from normalized observations. | Block candidate acceptance. | `RawNormalizationMismatch`; no accepted revision. | §7 | T-ING-04 | 1 |
| AC-05 | R1-Core | All six provider operations have outcomes. | Persist one outcome for each operation key. | Six operation records exist with closed outcomes. | §7 | T-MAN-01 | 1 |
| AC-06 | R1-Core | ProductSales type 0 is failed or not accepted. | Prevent calculation-job creation. | No Feature 129 job; readiness is false. | §10 | T-MAN-02 | 1 |
| AC-07 | R1-Core | An optional operation later changes from failure to accepted success. | Create a new manifest generation. | Generation increments and one idempotent job is created. | §10 | T-MAN-03 | 1 |
| AC-08 | R1-Core | A manifest scope is already present. | Permit only one current pointer row for the scope. | Duplicate scope violates unique key. | §10 | T-MAN-04 | 1 |
| AC-09 | R1-Core | Pointer and selected generation have different scope or fingerprint. | Reject the pointer update. | Deferred composite-consistency failure; prior pointer remains. | §10 | T-MAN-05 | 1 |
| AC-10 | R1-Core | Two attempts have the same semantic selected vector. | Avoid a new generation and job. | Audit receipt only; generation/job counts unchanged. | §10 | T-MAN-06 | 1 |
| AC-11 | R1-Core | A serializable retry observes changed accepted revision state. | Rebuild the vector before retrying selection. | Published vector contains reread accepted revision, never stale candidate. | §10 | T-MAN-07 | 1 |
| AC-12 | R1-Core | Optional success completes after type 0. | Publish the new complete readiness vector. | Pointer references new generation with optional success. | §10 | T-MAN-08 | 1 |
| AC-13 | R1-Core | A corrected report has a higher provider revision. | Accept the higher comparable revision. | Accepted pointer changes to the new revision. | §9 | T-REV-01 | 1 |
| AC-14 | R1-Core | Two revisions have equal precedence and different facts. | Refuse automatic economic selection. | `AmbiguousRevisionOrder`; DataAdmin decision required. | §9 | T-REV-02 | 1 |
| AC-15 | R1-Core | An older payload arrives after a newer accepted revision. | Keep the newer accepted pointer. | Older payload is recorded as rejected/late; pointer unchanged. | §9 | T-REV-03 | 1 |
| AC-16 | R1-Core | An alias draft is unapproved. | Exclude it from calculation and lookup projection. | Draft cannot be referenced by a job or snapshot. | §11 | T-ALIAS-01 | 2 |
| AC-17 | R1-Core | An approved ownership version overlaps an existing approved range. | Reject the approval transaction. | GiST exclusion violation; no pointer replacement. | §11 | T-ALIAS-02 | 2 |
| AC-18 | R1-Core | A valid alias draft is approved. | Insert a complete immutable version and all members. | One complete version contains the full approved set. | §11 | T-ALIAS-03 | 2 |
| AC-19 | R1-Core | Current pointer and projection are replaced during approval. | Commit both replacements atomically. | Commit exposes one matching version; rollback exposes the prior one. | §11 | T-ALIAS-04 | 2 |
| AC-20 | R1-Core | A provider key is blank, zero, or reused with incompatible signature. | Treat it as missing/collision, not as a merge key. | Identity is unmatched or review-required. | §11 | T-ALIAS-05 | 2 |
| AC-21 | R1-Core | A member has changed unit/package/grade/range. | Create a new ownership version instead of mutating history. | Prior version remains immutable; new lineage event exists. | §11 | T-ALIAS-06 | 2 |
| AC-22 | R1-Core | A merge, split, reversal, retirement, or reactivation is approved. | Record predecessor/successor lineage. | Decision event and complete new version are persisted. | §11 | T-ALIAS-07 | 2 |
| AC-23 | R1-Core | A published snapshot references an ownership version. | Preserve that version after later alias changes. | Historical snapshot resolves to its original version. | §11 | T-ALIAS-08 | 2 |
| AC-24 | R1-Core | An alias change intersects supported periods. | Schedule exactly those affected comparisons. | Jobs cover the intersection and no unrelated periods. | §11 | T-ALIAS-09 | 2 |
| AC-25 | R1-Core | Provider monetary unit is unconfirmed. | Suppress public monetary output. | `MonetaryUnitUnconfirmed`; snapshot is not public. | §12 | T-POLICY-01 | 2 |
| AC-26 | R1-Core | A conversion is not approved. | Preserve monetary contribution but suppress physical comparison. | `UnitConversionUnapproved`; quantity/rate comparison absent. | §12 | T-POLICY-02 | 2 |
| AC-27 | R1-Core | A DataAdmin confirms a monetary or conversion policy. | Insert an immutable policy version and audit event. | Version becomes selectable only after committed approval. | §12 | T-POLICY-03 | 2 |
| AC-28 | R1-Core | Accepted type-0 observations exist for both periods. | Sum every accepted reported revenue, including negatives. | Base/current totals equal source sums at scale 8. | §13 | T-CALC-01 | 3 |
| AC-29 | R1-Core | A product has valid continuing quantities and rates. | Apply the symmetric quantity and price formulas. | Quantity, price, residual, and contribution match formula values. | §13 | T-CALC-02 | 3 |
| AC-30 | R1-Core | Base revenue is zero/absent and current revenue is positive. | Classify the product as new. | Entire current signed contribution is `NewProduct`. | §14 | T-CALC-03 | 3 |
| AC-31 | R1-Core | Base revenue is positive and current revenue is zero/absent. | Classify the product as discontinued. | Entire negative base contribution is `DiscontinuedProduct`. | §14 | T-CALC-04 | 3 |
| AC-32 | R1-Core | Identity, source, unit, or quantity is unsafe. | Allocate the entire signed contribution once to unsafe. | `Unsafe/Unattributable` with the highest-priority reason. | §14 | T-CALC-05 | 3 |
| AC-33 | R1-Core | Formula values are rounded to storage scale. | Allocate the remaining signed amount to residual. | Product equation reconciles exactly at scale 8. | §13 | T-CALC-06 | 3 |
| AC-34 | R1-Core | A valid governed conversion exists. | Convert only according to the selected immutable policy. | Converted values and policy ID appear in evidence. | §12 | T-CALC-07 | 3 |
| AC-35 | R1-Core | A return, negative quantity, or invalid rate exists. | Retain signed revenue and suppress unsafe decomposition. | Fixed return/invalid reason and unsafe bucket. | §14 | T-CALC-08 | 3 |
| AC-36 | R1-Core | Gross opposing effects exceed cancellation threshold. | Suppress driver classification. | `HighCancellation`; classification is `Unclassified`. | §14 | T-CALC-09 | 3 |
| AC-37 | R1-Core | Company change is zero or below materiality floor. | Return null share/classification with fixed reason. | `ZeroCompanyChange` or `ImmaterialCompanyChange`. | §14 | T-CALC-10 | 3 |
| AC-38 | R1-Core | Product/company equality fails at storage scale. | Block snapshot publication. | `ReconciliationMismatch`; current pointer unchanged. | §15 | T-CALC-11 | 3 |
| AC-39 | R1-Core | Product and company effects are valid and covered. | Compute additive company classification from signed effects. | One closed classification and coverage values are returned. | §15 | T-CALC-12 | 3 |
| AC-40 | R1-Core | A history table receives UPDATE or DELETE. | Reject the mutation in the database. | Trigger raises `Feature129HistoryMutationDenied`. | §21 | T-IMM-01 | 1 |
| AC-41 | R1-Core | A repository attempts to update immutable history. | Reject the operation at repository contract level. | Insert-only repository throws fixed domain error. | §21 | T-IMM-02 | 1 |
| AC-42 | R1-Core | A historical parent is referenced by evidence or snapshot. | Reject parent deletion. | `ON DELETE RESTRICT` prevents deletion. | §20 | T-IMM-03 | 1 |
| AC-43 | R1-Core | A pointer references a version from another scope. | Reject commit through deferred constraint validation. | Composite FK/trigger failure; prior pointer remains. | §20 | T-IMM-04 | 1 |
| AC-44 | R1-Core | A snapshot publication event names another snapshot. | Reject the event insert. | Publication identity constraint fails. | §18 | T-IMM-05 | 3 |
| AC-45 | R1-Core | Two jobs use the same scope/generation/policy inputs. | Collapse them to one idempotent Job projection. | One Job and one initial Outbox exist. | §16 | T-PUB-01 | 1 |
| AC-46 | R1-Core | Two identical calculations execute concurrently. | Publish one snapshot and one no-op event. | Unique source fingerprint and pointer lock prevent duplicate publication. | §18 | T-PUB-02 | 3 |
| AC-47 | R1-Core | A snapshot child/evidence/pointer write fails. | Roll back the complete publication transaction. | Prior current pointer and snapshot remain unchanged. | §18 | T-PUB-03 | 3 |
| AC-48 | R1-Core | Feature 129 is submitted to the existing direct scheduler. | Reject before generic job persistence or publish. | `FeatureRequiresTransactionalOutbox`. | §16 | T-ORCH-01 | 1 |
| AC-49 | R1-Core | Manifest commit and Feature 129 job creation share one ingestion transaction. | Commit them atomically. | Either both exist or neither exists. | §16 | T-ORCH-02 | 1 |
| AC-50 | R1-Core | A due outbox row is available. | Lease it with `SKIP LOCKED`, token, expiry, and `xmin`. | Only one dispatcher owns the lease. | §17 | T-ORCH-03 | 6 |
| AC-51 | R1-Core | A leased message is published but confirm persistence crashes. | Republish the same attempt with the same Message ID. | Duplicate delivery is deduplicable; no new attempt is invented. | §17 | T-ORCH-04 | 6 |
| AC-52 | R1-Core | A lease or confirm wait expires. | Recover it to due Pending through a fenced transition. | Expired row becomes dispatchable once. | §17 | T-ORCH-05 | 6 |
| AC-53 | R1-Core | Handler succeeds and commits result/job completion. | ACK only after commit. | Job `Completed`, result exists, broker ACK succeeds. | §17 | T-ORCH-06 | 3 |
| AC-54 | R1-Core | Handler transaction cannot commit. | NACK/requeue without ACK. | No terminal or retry state is falsely recorded. | §17 | T-ORCH-07 | 3 |
| AC-55 | R1-Core | Handler has a retryable failure and six attempts remain. | Commit failed attempt, Job `RetryScheduled`, next Outbox, event, and availability before ACK. | Future durable delivery exists; current message is ACKed only after commit. | §17 | T-ORCH-08 | 3 |
| AC-56 | R1-Core | Sixth attempt fails retryably. | Persist exhaustion and dead-letter evidence. | Job is `DeadLettered`; no seventh automatic attempt. | §17 | T-ORCH-09 | 6 |
| AC-57 | R1-Core | A terminal Job message is delivered again. | Record duplicate consumption idempotently. | No second result; message is ACKed. | §17 | T-ORCH-10 | 3 |
| AC-58 | R1-Core | DataAdmin requests a redrive. | Create an audited new attempt with lineage. | Redrive decision and new Outbox exist; old history unchanged. | §17 | T-ORCH-11 | 6 |
| AC-59 | R1-Core | A cancellation is authorized before execution starts. | Transition Job and Outbox to cancellation state without deletion. | `Cancelled` state and immutable event exist. | §17 | T-ORCH-12 | 6 |
| AC-60 | R1-Core | A model proposal contains an unknown field or unsupported schema. | Reject locally before merge/execution. | Fixed validation code; no executor call. | §22 | T-SEM-01 | 4 |
| AC-61 | R1-Core | A proposal contains invalid period, limit, confidence, or span. | Reject the invalid slot. | Fixed validation code identifies the invalid field. | §22 | T-SEM-02 | 4 |
| AC-62 | R1-Core | Deterministic and model proposals contain the same slot with different values. | Apply precedence and return clarification on unresolved conflict. | One validated frame or bounded clarification; never two values. | §22 | T-SEM-03 | 4 |
| AC-63 | R1-Core | Company/product text is a Persian paraphrase with varied word order. | Resolve it through canonical resolver and typed frame. | Correct canonical IDs and evidence spans are persisted. | §22 | T-SEM-04 | 4 |
| AC-64 | R1-Core | V1, native V2, and fallback V2 receive the same validated frame. | Execute the same capability and inputs. | Semantic result values and IDs are equal. | §22 | T-SEM-05 | 4 |
| AC-65 | R1-Core | A prompt contains injection instructions or unsupported capability. | Isolate/reject it as bounded clarification. | No raw prompt instruction reaches executor or database route. | §22 | T-SEM-06 | 4 |
| AC-66 | R1-Core | A valid frame requests a two-period comparison. | Executor returns typed Feature 129 result v3. | Result contains periods, facts, effects, coverage, warnings, evidence, and policy IDs. | §23 | T-API-01 | 4 |
| AC-67 | R1-Core | A persisted v1 or v2 conversation is replayed after newer publication. | Decode old payload and preserve old numeric semantics. | Existing payload remains readable and immutable values are unchanged. | §23 | T-API-02 | 4 |
| AC-68 | R1-Core | A v3 conversation is replayed. | Compare semantics, not serializer bytes. | Replay equality holds for decimals, IDs, enums, order, and evidence. | §23 | T-API-03 | 4 |
| AC-69 | R1-Core | A Telegram result exceeds 3,500 UTF-16 characters. | Split escaped bounded messages at section boundaries. | All messages obey limit and include limitation footer. | §23 | T-TG-01 | 4 |
| AC-70 | R1-Core | Telegram receives a chart-capable result. | Render textual summary/table only. | No fabricated chart or client calculation is emitted. | §23 | T-TG-02 | 4 |
| AC-71 | R1-Core | Web receives a published result. | Render server values in summary, table, evidence, and optional chart layout. | UI values equal DTO values; client arithmetic is absent. | §23 | T-UI-01 | 5 |
| AC-72 | R1-Core | Web receives each published/partial/stale/processing/unavailable/blocked/empty/error state. | Render the matching state with accessible non-color status. | State-specific view and accessible table fallback appear. | §23 | T-UI-02 | 5 |
| AC-73 | R2-History | Two accepted comparable fiscal periods exist. | Compute YoY and fiscal YTD only from accepted revisions/output type rules. | Invalid fiscal comparison is suppressed with fixed reason. | §25 | T-HIST-01 | 6 |
| AC-74 | R2-History | Three or twelve contiguous published months exist. | Compute the requested average only for a complete window. | Missing month returns `PartialWindow`; no fabricated average. | §25 | T-HIST-02 | 6 |
| AC-75 | R2-History | At least six comparable periods and materiality policy exist. | Compute anomaly only with robust z-score threshold `|z| ≥ 3.5`. | Anomaly is labeled inferred and evidence includes periods/policy. | §25 | T-HIST-03 | 6 |
| AC-76 | Optional-Endpoint | Optional flag, auth, entitlement, rate, and ETag support are enabled. | Serve the direct endpoint with strong ETag/304 behavior. | Unauthorized/over-limit requests fail; matching ETag returns 304. | §23 | T-API-04 | 6 |
| AC-77 | R1-Core | The complete §26 fixture and every named negative variant are loaded. | Calculate and compare all fixture inputs, effects, totals, spelling, order, and server values. | `T-FIX` assertions pass; every mutation is rejected or quality-gated. | §26 | T-FIX-01, T-FIX-02, T-FIX-03, T-FIX-04, T-FIX-05, T-FIX-06, T-FIX-07, T-FIX-08, T-FIX-09, T-FIX-10, T-FIX-11, T-FIX-12 | 3 |
| AC-78 | R1-Core | Design-v6 is reviewed with earlier documents unchanged and no implementation change. | Pass the design gate only when standalone coverage, traceability, scope, and file impact are complete. | `T-TRACE-01`; status may become `APPROVED`. | §43 | T-TRACE-01 | Design gate |

## 29. Vertical slices

### Slice 1 — Source, revisions, manifest, and immutable foundation

**Goal:** establish immutable source authority, report acceptance, manifest generation, policies, trigger foundation, and direct-dispatch rejection. **ACs:** AC-01 through AC-15, AC-40 through AC-49. **Backend/database:** add ingestion rows, revisions, observations, receipts, decisions, manifest services, pointers, constraints, trigger function, and transaction boundaries. **AI/orchestration:** no public semantic route; register Feature 129 dispatch policy. **Frontend:** none. **Tests:** ingestion, revision, manifest, trigger, and atomic outbox tests. **Dependencies:** existing raw store, company resolver, PostgreSQL, existing scheduler. **Flags:** `F129ImmutableShadow`, `F129TransactionalDispatch`. **Rollout:** shadow-write and compare without publication. **Backfill:** none. **Observability:** raw mismatch, ambiguity, manifest readiness, and shadow divergence metrics. **DoD:** no public snapshot and no direct Feature 129 publish path.

### Slice 2 — Canonical ownership and unit policy

**Goal:** establish complete alias versions and governed units. **ACs:** AC-16 through AC-27. **Backend/database:** drafts, approval service, versions/members/lineage, current pointer/projection, GiST exclusion, monetary/conversion policies. **AI/orchestration:** jobs are created only for affected periods after approval commit. **Frontend:** DataAdmin screens only. **Tests:** alias concurrency, rollback, lineage, and policy gates. **Dependencies:** Slice 1. **Flags:** `F129AliasOwnership`, `F129UnitGate`. **Rollout:** canary companies and shadow matching. **Backfill:** schedule only intersecting periods. **Observability:** collision, overlap, policy-block, and affected-job metrics. **DoD:** every eligible snapshot has stable ownership and unit policy IDs.

### Slice 3 — Calculator, evidence, snapshot, and durable retry

**Goal:** calculate and publish stable two-period results with durable dispatch. **ACs:** AC-28 through AC-59, AC-77. **Backend/database:** calculator, state table, snapshot/evidence writer, job/outbox projections and history, dispatcher, dedicated message contract, consumer, dead-letter/redrive. **AI/orchestration:** result executor contract only. **Frontend:** fixture/debug view only. **Tests:** property reconciliation, every attribution branch, PostgreSQL publication, crash-point retry, ACK/NACK, and fixture tests. **Dependencies:** slices 1–2. **Flags:** `F129PublicSnapshot`, enabled only after durable retry tests pass. **Rollout:** no public output before stable source identity and complete alias ownership. **Backfill:** per accepted manifest. **Observability:** SLO, retry, dead-letter, reconciliation, and publication alerts. **DoD:** retry correctness is complete before dispatch enablement; R1 snapshot is immutable.

### Slice 4 — Semantic/API/conversation/Telegram

**Goal:** expose typed validated capability through active MAF V2, V1, fallback, facade, persistence, and Telegram. **ACs:** AC-60 through AC-70. **Backend/AI:** validator, merge, resolver, frame, workflow messages, executor, DTOs, payload decoder, billing/auth integration. **Database:** conversation payload version handling and audit only. **Frontend:** DTO/view-model contract. **Tests:** schema, Persian paraphrase, conflict, parity, replay, API auth/billing, Telegram. **Dependencies:** Slice 3. **Flags:** `F129SemanticCapability`, `F129Telegram`. **Rollout:** internal principals then canary. **Backfill:** none. **Observability:** validation rejection, clarification, executor duration, billing, and replay metrics. **DoD:** one frame and one result contract across all orchestration paths.

### Slice 5 — Investor web experience

**Goal:** deliver R1 investor-facing UI and all states. **ACs:** AC-71 and AC-72. **Backend:** finalized DTO pagination/evidence/state mapping. **Database:** none beyond existing immutable snapshot. **AI:** no new model behavior. **Frontend:** RTL summary, narrative, trend, contribution, product, production/sales, evidence drawer, responsive/accessibility components. **Tests:** view-model, components, state matrix, keyboard, screen-reader table, no-arithmetic checks. **Dependencies:** Slice 4. **Flags:** `F129InvestorUI`. **Rollout:** internal, canary companies, then R1. **Backfill:** only reads published snapshots. **Observability:** client error and state distribution metrics without user/product labels. **DoD:** R1-Core ends only after Slice 5.

### Slice 6 — History and operations

**Goal:** deliver R2 history and operational hardening. **ACs:** AC-50 through AC-52, AC-56, AC-58, AC-59, AC-73 through AC-75, and AC-76. **Backend/database:** history queries, anomaly/inferred signals, backfill checkpoint/coordinator, runbooks/alerts. **AI/frontend:** history DTOs and optional views. **Tests:** history, backfill, restart, load, alerts, optional endpoint. **Dependencies:** R1 and accepted policy versions. **Flags:** `F129History`, `F129OptionalEndpoint`. **Rollout:** R2 is separately approved; Optional-Endpoint remains disabled by default. **Backfill:** starts at 1404/01 under section 25 controls. **Observability:** backlog, lag, pause/resume, and SLO metrics. **DoD:** R2 criteria do not gate R1; endpoint criteria are evaluated only when explicitly enabled.

## 30. Four-category file-impact map

### 30.1 Existing files to modify in future implementation

| Exact path | Planned change |
| --- | --- |
| `src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/NadpcoApiDataProviderClient.cs` | Preserve raw operation envelopes and explicit output-operation metadata. |
| `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiMonthlyActivityNormalizer.cs` | Dual-write immutable observations and compatibility projection; remove Feature 129 dependence on collapse. |
| `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/FinancialDataSyncProcessor.cs` | Include manifest and atomic Feature 129 job/outbox intent while retaining existing derived-metric publication. |
| `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/FinancialIngestionRows.cs` | Add EF rows for tables in §20. |
| `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/FinancialIngestionConfigurations.cs` | Add exact keys, precision, indexes, FKs, checks, and `xmin` mappings. |
| `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/FinancialIngestionDbContext.cs` | Register new entities and transaction/query filters. |
| `src/backend/FinancialCopilot.Infrastructure/Financial/Features/FeatureComputationProcessor.cs` | Enforce direct Feature 129 rejection before persistence. |
| `src/backend/FinancialCopilot.Infrastructure/ServiceCollectionExtensions.cs` | Register policies, repositories, dedicated dispatcher/consumer, validator, and executor. |
| `src/backend/FinancialCopilot.Worker/Program.cs` | Register dedicated Feature 129 worker and dispatcher. |
| `src/backend/FinancialCopilot.Application/Conversations/ConversationContracts.cs` | Add payload v3 result kind and backward decoder contract. |
| `src/backend/FinancialCopilot.Application/AI/ModelProviders/AiModelContracts.cs` | Preserve shared root contract; no nested Feature 129 bypass. |
| `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/FinancialCopilotWorkflowDefinition.cs` | Carry validated proposal/frame/executor messages in MAF V2. |
| `src/backend/FinancialCopilot.Infrastructure/Authentication/TelegramAssistantResponseRenderer.cs` | Add bounded Feature 129 rendering/fallback. |
| `src/frontend/src/functions/chat.functions.ts` | Add discriminated payload v3 mapping. |
| `src/frontend/src/components/chat/message-list.tsx` | Render Feature 129 states and evidence. |

### 30.2 Existing files inspected but intentionally unchanged

`src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/MetricRecalculationProcessor.cs`; shared provider structured-output adapters; existing unrelated feature consumer behavior and queue; historical migrations; existing old conversation payload meanings; current Product Revenue Mix and Monthly Trend calculators during compatibility period.

### 30.3 Proposed new files

`Feature129Rows.cs`, `Feature129Configurations.cs`, `Feature129ManifestService.cs`, `Feature129RevisionService.cs`, `Feature129AliasOwnershipService.cs`, `Feature129PolicyService.cs`, `Feature129Calculator.cs`, `Feature129SnapshotWriter.cs`, `Feature129JobRepository.cs`, `Feature129OutboxRepository.cs`, `Feature129OutboxDispatcher.cs`, `Feature129MessageContracts.cs`, `Feature129Publisher.cs`, `Feature129Consumer.cs`, `Feature129ConsumerWorker.cs`, `MonthlyProductSemanticProposal.cs`, `MonthlyProductSemanticProposalValidator.cs`, `MonthlyProductQueryFrame.cs`, `Feature129Executor.cs`, `Feature129DtoContracts.cs`, `Feature129AuthorizationAudit.cs`, migration/model-snapshot files, frontend Feature 129 components/view-models, and all named tests in §27. Exact directory placement follows the existing Application/Infrastructure/Worker/API/frontend project boundaries.

### 30.4 Optional later-slice files

History/anomaly/inferred-inventory repositories and calculators, backfill coordinator/checkpoint worker, optional direct controller/authorization/ETag DTOs, history frontend views, and corresponding R2 tests are separate from R1 files.

## 31. Runtime coexistence, cutover, and rollback

During shadow period, the normalizer dual-writes immutable observations and the current compatibility projection. Existing Product Revenue Mix and Monthly Trend calculators continue unchanged; Feature 129 calculates shadow snapshots and reports reconciliation divergence. `FinancialDataSyncProcessor` continues publishing the existing derived-metric event, but a dataset guard prevents that event from invoking Feature 129. The direct scheduler rejects Feature 129, while unrelated features retain direct behavior.

Canary rollout selects named companies, verifies canonical counts, source sums, manifest readiness, alias completeness, and shadow/current result equality. Public publication is enabled only after trigger, concurrency, retry crash-point, and SLO gates pass. Rollback disables public Feature 129 and new backfill dispatch; it never deletes immutable history or rewrites old projections. Existing calculators remain active until a separately approved cutover after reconciliation telemetry is stable. MAF V2 remains active; V1/fallback parity is tested and retained.

## 32. Dependencies, decisions, open questions, and safe gates

Dependencies are NADPCO/raw storage, ingestion DbContext, company resolver, PostgreSQL `btree_gist`, RabbitMQ, existing feature registration, semantic registry/task state, AI facade/conversation/billing/auth, Telegram renderer, frontend structured chat, and deployment observability.

Safe gates: provider monetary unit confirmation; approved conversion dictionary; policy version values for freshness/materiality/anomaly; DataAdmin approval of R2 activation; separate approval of Optional-Endpoint. Until a gate is decided, the safe behavior is block or suppress the affected output, never infer or convert.

## 33. Design-v5 review resolution matrix

| Finding | Concrete resolution | Design-v6 section | ACs | Tests | Slice | Status |
| --- | --- | --- | --- | --- | --- | --- |
| V5-AC-01: grouped 78 identifiers | One atomic row for every identifier with preconditions, outcome, section, test, and slice. | §28 | AC-01 through AC-78 | T-TRACE-01 plus row tests | 1–6 | Resolved |
| V5-M-01: compressed standalone contracts | Restored source, schema, formulas, state, semantic, API, UX, security, testing, slices, and impact sections. | §§7–30 | AC-01 through AC-78 | T-TRACE-01 | Resolved |
| V5-M-02: mutable Job/Outbox called immutable | Projections are mutable; append-only events/attempt/dead-letter/redrive history is trigger-protected. | §§17, 20, 21 | AC-40, AC-41, AC-45, AC-50 through AC-59 | T-IMM-01, T-ORCH-01 through T-ORCH-12 | 1, 3, 6 | Resolved |
| V5-M-03: retry delivery ambiguity | One outbox owner, six-attempt delays, lease recovery, stable Message ID, transactional retry creation before ACK, and crash rules. | §17 | AC-50 through AC-59 | T-ORCH-03 through T-ORCH-14 | 3, 6 | Resolved |
| V5-M-04: manifest retry noise | Canonical vector excludes audit-only ordinal/timestamp noise and changes generation only for semantic/readiness changes. | §10 | AC-08 through AC-12 | T-MAN-04 through T-MAN-08 | 1 | Resolved |
| V5-M-05: incomplete persistence model | Every required table has columns/types/keys/FKs/delete/mutability/trigger contract; exact composite scope and precision are stated. | §20 | AC-08, AC-09, AC-17, AC-19, AC-40 through AC-47 | T-IMM-01 through T-IMM-05, T-PUB-01 through T-PUB-03 | 1–3 | Resolved |
| V5-M-06: attribution state compression | Ordered exhaustive decision table, closed reasons, equations, coverage, cancellation, thresholds, and numeric fixture are provided. | §§13, 14, 26 | AC-28 through AC-39, AC-77 | T-CALC-01 through T-CALC-12, T-FIX-01 through T-FIX-12 | 2, 3 |
| V5-M-07: semantic/API/UI assertion-only contract | Closed proposal/slot/frame/DTO/state/decoder/Telegram/web contracts and integration points are explicit. | §§22, 23 | AC-60 through AC-72 | T-SEM-01 through T-SEM-06, T-API-01 through T-API-04, T-TG-01/02, T-UI-01/02 | 4, 5 |
| V5-M-08: R1/R2 scope ambiguity | AC table uses `R1-Core`, `R2-History`, and `Optional-Endpoint`; slices and rollout preserve independent gates. | §§3, 28, 29, 31 | AC-73 through AC-76 | T-HIST-01 through T-HIST-03, T-API-04 | 5, 6 |

Previous review findings remain represented by the v6 contracts: duplicate-row preservation, exhaustive unsafe attribution, immutable evidence, accepted revision ordering, alias exclusion/versioning, publication pointer integrity, semantic frame validation, payload replay, operational SLOs, and exact trigger inventory.

## 34. Final readiness checklist

- [x] Standalone architecture, data model, formulas, state machines, API, UX, security, tests, slices, and impact map are present.
- [x] Mutable Job/Outbox projections are explicitly not immutable history.
- [x] Job/Outbox immutable events, attempts, dead letters, and redrive decisions are trigger-protected.
- [x] Retryable handler failure creates the future delivery before ACK in one transaction.
- [x] Manifest fingerprint excludes audit-only attempt noise and rebuilds under lock.
- [x] Complete Persian fixture and negative variants are included.
- [x] Exactly 78 atomic acceptance criteria are individually mapped.
- [x] R1, R2, and Optional-Endpoint gates are consistent.
- [x] Existing runtime coexistence and rollback are defined.

**Final status:** `READY_FOR_DESIGN_REVIEW`
