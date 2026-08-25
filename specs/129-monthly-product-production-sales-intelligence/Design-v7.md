# Feature 129 — Monthly Product Production and Sales Intelligence

**Status:** `READY_FOR_DESIGN_REVIEW`  
**Revision:** v7  
**Date:** 2026-08-25  
**Normative authority:** this document is standalone. Earlier designs and reviews are historical regression evidence only.

## 1. Executive summary

Feature 129 explains a company’s monthly ProductSales revenue change from immutable provider evidence. R1 compares two consecutive published Jalali months from `1404/01`, preserves every source observation, accepts report revisions deterministically, selects a canonical company-month manifest, uses a complete immutable product-ownership version, applies governed monetary/unit policy, calculates a seven-bucket signed attribution, publishes an immutable snapshot/evidence set, and exposes the result through the existing AI facade, conversation persistence, Telegram, and web UI.

The model may propose typed capability slots. Only a locally validated and canonically resolved frame reaches an executor. The client never calculates financial values. R2 history is separately gated. The optional direct endpoint is disabled unless explicitly approved.

## 2. Status and revision history

| Revision | Result |
| --- | --- |
| v1 | Initial product, formula, API, UX, and slice design. |
| v2 | Added immutable revisions, alias versions, typed semantic slots, and snapshot pointers. |
| v3 | Added dedicated dispatch, evidence, replay, and expanded impact/test contracts. |
| v4 | Detailed baseline; identified retry, manifest ordering, fixture, operations, confirmation, and trigger gaps. |
| v5 | Added retry owner, canonical manifest algorithm, fixture, SLO defaults, state separation, and trigger inventory; compressed ACs/contracts. |
| v6 | Restored architecture and 78 AC rows; review found cancellation/state, schema, test, path, scheduler, matrix, and semantic-flow gaps. |
| v7 | Resolves all v6 findings with total states, exact schema/test contracts, corrected paths, field flow, and one primary slice per AC. |

## 3. Scope and release gates

`R1-Core` contains source/revision/manifest correctness, canonical ownership, unit/monetary gates, two-period attribution, immutable snapshot/evidence, durable dispatch/retry/cancellation, semantic routing, structured AI-facade result, replay, Telegram fallback, web UI, security/billing/audit, and core SLOs.

`R2-History` contains YoY, fiscal YTD, 3/12 contiguous averages, 24-month history, anomalies, inferred inventory signals, historical backfill, and R2 SLOs. R2 does not gate R1.

`Optional-Endpoint` is the disabled direct endpoint. It is enabled only by an explicit release decision after auth/entitlement/rate/ETag tests pass.

Non-goals are forecasts, investment advice, target prices, factual inventory claims, cross-company physical comparison, raw-provider calls from reads, LLM arithmetic, unapproved conversion, and pre-1404 backfill.

## 4. Verified repository discovery

The repository is .NET 10, PostgreSQL, EF Core, Clean Architecture, and Microsoft Agent Framework V2. `src/backend/FinancialCopilot.API/appsettings.Development.json` sets `AiOrchestration:Mode` to `MicrosoftAgentFrameworkV2`.

| Existing implementation | Authoritative behavior | v7 integration |
| --- | --- | --- |
| `src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/NadpcoApiDataProviderClient.cs` | Fetches monthly provider envelopes and applies the current boundary. | Preserve provider/auth behavior; add operation metadata/raw evidence at ingestion boundary. |
| `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiMonthlyActivityNormalizer.cs` | Groups by line code, takes last duplicate, deletes current children, inserts new IDs, invokes existing calculators. | Dual-write immutable observations and retain compatibility projection during shadow period. |
| `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/FinancialDataSyncProcessor.cs` | Stores raw payload, normalizes, completes sync, publishes existing derived-metric request. | Manifest/job/outbox are atomic; existing derived-metric publication remains separate and cannot invoke F129. |
| `src/backend/FinancialCopilot.Infrastructure/Financial/Features/FeatureComputationProcessor.cs` | `FeatureRecalculationScheduler.ScheduleAsync` stores generic job then calls `PublishRequestedAsync`; processor computes and publishes completion/failure. | Guard `ScheduleAsync` before either side effect for F129; unrelated features remain unchanged. |
| `src/backend/FinancialCopilot.Infrastructure/Financial/Features/Messaging/RabbitMqFeatureMessaging.cs` | Shared consumer ACKs before handler. | Do not use it for F129; add dedicated handler-before-ACK consumer/queue. |
| `src/backend/FinancialCopilot.Worker/FeatureComputationConsumerWorker.cs` | Hosts shared consumer/processor. | Add separate F129 worker and registrations. |
| `src/backend/FinancialCopilot.Application/AI/ModelProviders/AiModelContracts.cs` | `AiStructuredOutputContract` is shared root contract; JSON validator validates model structured output. | Keep root unchanged; local F129 validator owns nested proposal. |
| `src/backend/FinancialCopilot.Application/Conversations/ConversationContracts.cs` | `AssistantMessagePayload` is versioned persisted envelope at lines 36–51. | Add a discriminated F129 v3 result field and decoder branch. |
| `src/backend/FinancialCopilot.Infrastructure/Conversations/Persistence/ConversationRepositories.cs` | `DeserializePayload` is the persisted decoder. | Add backward-compatible F129 decoder and semantic replay. |
| `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Functions/MessagePersistenceFunction.cs` | Persists MAF V2 messages. | Persist canonical semantic payload and deterministic narrative. |
| `src/backend/FinancialCopilot.Infrastructure/Authentication/TelegramAssistantResponseRenderer.cs` | Telegram response rendering. | Add bounded F129 summary/evidence/limitations. |
| `src/frontend/src/lib/chat.functions.ts` | Existing frontend chat API mapping. | Add F129 discriminated DTO/view-model mapping. |
| `src/frontend/src/components/app/message-list.tsx` | Existing chat message rendering. | Add F129 state/result component. |
| `src/frontend/src/components/app/__tests__/message-list.test.tsx` | Existing message-list tests. | Add server-value/no-arithmetic and state tests. |

## 5. Functional and non-functional requirements

Source rows, revisions, accepted manifests, ownership versions, policies, snapshots, evidence, events, attempts, dead letters, and redrive decisions are immutable. Current pointers, projections, Job, Outbox, leases, retry fields, and checkpoints are mutable operational state. History uses rejecting triggers and `ON DELETE RESTRICT`; mutable state uses bounded transitions, `xmin`, lease fencing, and idempotency.

R1 reports only accepted ProductSales type 0. Raw row count and revenue reconcile at `numeric(28,8)`. A stale/failed run cannot replace the current snapshot. Monetary output is blocked until policy confirmation. Secrets, payloads, prompts, product text, and raw exceptions are never logged.

## 6. Provider operation and raw source

For each provider/company/Jalali month/family, operations are exactly `ProductSales:0`, `ProductSales:1`, `ProductSales:2`, `ProductSales:3`, `ProductSales:4`, and `ServiceSales:none`. Type 0 is mandatory; others are optional. Outcomes are `Succeeded`, `ValidEmpty`, `Retrying`, `Failed`, `Blocked`, or `Cancelled`.

Every attempt stores operation key, attempt number, request identity, provider/company/period, raw payload ID/checksum, raw row count, normalized row count, revenue sum, comparable provider revision, valid publication time, semantic fingerprint, completion time, outcome, and closed reason. Raw JSON is canonical UTF-8 with numeric lexemes preserved. `RawNormalizationMismatch` blocks acceptance.

## 7. Immutable observations

`MonthlyReportSourceObservation` is append-only and identified by revision ID, raw ordinal, source-row discriminator, and duplicate occurrence. It stores provider/company/report/output/period identity, product/provider IDs, title, raw unit, governed unit, quantity/rate/revenue `numeric(28,8)`, economic signature, source-fact fingerprint, raw payload ID/checksum, and completion time. Ordinal is evidence only; reorder does not change semantic fingerprint. Distinct repeated product codes remain distinct observations.

## 8. Reports, revisions, receipts, decisions, accepted pointer

`MonthlyReportLogicalIdentity` has unique `(ProviderName, ExternalCompanyId, ReportKind, OutputType, JalaliYear, JalaliMonth)`. Revisions, observations, receipts, and decision events are append-only; `MonthlyReportAcceptedPointer` is mutable.

Acceptance runs at `SERIALIZABLE` under `pg_advisory_xact_lock(hashtextextended('f129-report|' || logical-key,0))`. Precedence is higher comparable provider revision, then newer valid provider publication time. Missing/equal precedence with different semantic facts is `AmbiguousRevisionOrder`; receipt time, completion time, and row ID never select an economic winner. A late older payload is recorded but cannot replace the pointer. Serialization/deadlock/`xmin` conflicts retry three times after rereading accepted state.

## 9. Manifest determinism

The manifest lock is `pg_advisory_xact_lock(hashtextextended('f129-manifest|' || provider || '|' || company || '|' || period || '|' || family,0))`. The selector locks the pointer, rereads accepted revisions and immutable attempts, and chooses accepted success, accepted valid-empty, or latest operational attempt only when no accepted result exists. A failed/retrying attempt never downgrades accepted success.

The vector order is fixed: `ProductSales:0`, `ProductSales:1`, `ProductSales:2`, `ProductSales:3`, `ProductSales:4`, `ServiceSales:none`. Each element contains exactly `OperationKey`, `SelectedAcceptedRevisionId` nullable, `SelectedOutcome`, `AcceptedSemanticFingerprint` nullable, `MaterialReasonCode` nullable, and `MandatoryClassification`.

`ManifestFingerprint` is SHA-256 of UTF-8 canonical JSON, fixed property order, explicit null markers, invariant decimal text, no whitespace. Receipt time, generation ID, attempt ordinal, and audit-only retry noise are excluded. Identical semantic vector is audit-only; optional success replacing failure, accepted correction, or user-visible readiness/quality change creates one new generation/job. Failure-to-retrying without semantic/readiness change creates no financial generation. Same-revision different facts are resolved only by revision acceptance.

## 10. Product identity and alias ownership

Blank/zero provider IDs are absent. Matching order is approved ownership, compatible collision-free provider key, exact economic signature, prior approved signature, then manual review. Text similarity alone cannot merge material revenue.

Alias drafts are administrative and cannot enter calculations. Approval obtains advisory lock `f129-alias|provider|company`, reads the prior complete set, validates signatures/unit/range, inserts a complete immutable ownership version/members/lineage event, then replaces the current pointer/projection atomically. `daterange` uses inclusive lower/exclusive upper bounds `[start,end)`, with `end` nullable only for open-ended current ownership.

The current projection has only approved rows. The exact exclusion constraint is:

```sql
EXCLUDE USING gist
(ProviderName WITH =, ExternalCompanyId WITH =, ProviderProductSignature WITH =, EffectiveRange WITH &&)
WHERE (ApprovalState = 'Approved' AND IsCurrent = true)
```

`btree_gist` is required. Superseded versions set no current projection row; their historical rows remain immutable. `xmin` protects current pointer updates; the advisory lock serializes approval. Split, merge, retirement, reactivation, reversal, and concurrent overlap outcomes are separately tested.

## 11. Unit and monetary policies

Raw unit text remains beside `UnitCode` and dimension. Immutable `ProviderMonetaryUnitPolicyVersion`, `UnitConversionPolicyVersion`, and `CalculationPolicyVersion` are required. `MonetaryUnitUnconfirmed` blocks public monetary output. `UnitConversionUnapproved` preserves reported money but suppresses quantity/rate and physical comparison. No v1 physical conversion occurs without an approved factor.

## 12. Numeric definitions and formulas

Money, quantity, and rate columns are `numeric(28,8)`; intermediates use `numeric(38,16)` and ToEven round to scale 8 at persistence. Canonical decimal serialization is invariant with normalized trailing zeros.

```text
QuantityEffect = (Qc - Qb) * (Pb + Pc) / 2
PriceEffect    = (Pc - Pb) * (Qb + Qc) / 2
Residual       = (Rc - Rb) - QuantityEffect - PriceEffect
Contribution   = QuantityEffect + PriceEffect + Residual
```

Company equation: `CurrentTotal - BaseTotal = Σ(Quantity + Price + Residual + NewProduct + DiscontinuedProduct + IdentityChange + Unsafe/Unattributable)`. Any scale-8 mismatch blocks publication.

## 13. Closed enumeration and policy registry

Lifecycle: `Unknown`, `Inactive`, `New`, `Resumed`, `Continuing`, `Discontinued`. Identity: `Matched`, `Unmatched`, `Ambiguous`, `ManualReview`. Quality: `Valid`, `Partial`, `Rounded`, `Return`, `Unsafe`, `Blocked`, `Stale`. Buckets: `Quantity`, `Price`, `Residual`, `NewProduct`, `DiscontinuedProduct`, `IdentityChange`, `Unsafe/Unattributable`.

Reasons, in deterministic priority order, are: `SourceBlocked`, `MissingComparison`, `IdentityAmbiguous`, `NoComparableIdentity`, `NewProduct`, `DiscontinuedProduct`, `ResumedProduct`, `MissingBaseRate`, `MissingCurrentRate`, `InvalidQuantity`, `InvalidRate`, `UnitConversionUnapproved`, `ReturnOrReversal`, `OptionalPartial`, `StoredScaleResidual`, `HighCancellation`, `ZeroCompanyChange`, `ImmaterialCompanyChange`, `InsufficientClassificationCoverage`, `ReconciliationMismatch`, `MonetaryUnitUnconfirmed`, `RawNormalizationMismatch`, `AmbiguousRevisionOrder`, `FeatureRequiresTransactionalOutbox`, `StaleXmin`, `StaleLeaseToken`, `InvalidSchema`, `PermanentFailure`, `RetryExhausted`, and `Cancelled`.

Classification is `QuantityDriven`, `PriceDriven`, `ResidualDriven`, `Mixed`, `NewDiscontinuedDriven`, or `Unclassified`. Defaults are driver threshold 60%, cancellation ratio 3.0, materiality `0.01%` of absolute base revenue with minimum `1.00000000`, breadth 2 products, HHI concentration threshold `0.25`, freshness 24 hours, anomaly `|z| >= 3.5`, classification coverage 60%. Every default is a versioned policy. Unknown provider monetary unit blocks; unknown later R2 thresholds suppress that R2 output.

## 14. Exhaustive attribution and classification

Rules are evaluated in priority order: blocked source; missing comparison; ambiguous identity; no comparable identity; new; discontinued; resumed; continuing valid; rounded residual; missing/invalid base/current rate; missing/invalid quantity; approved conversion; unit change without conversion; return/reversal; optional partial; zero/immaterial company change; high cancellation; insufficient coverage. Each branch allocates the entire signed contribution once to the bucket named by the rule and stores the reason. Continuing valid uses the formulas; unsafe branches allocate the entire signed revenue change to `Unsafe/Unattributable`; new/discontinued allocate current/negative base revenue; identity ambiguity uses `IdentityChange`.

`MatchCoverage = matched revenue / absolute total revenue`; `DecompositionCoverage = decomposed revenue / absolute total revenue`; `ResidualRatio = abs(residual)/max(abs(change),floor)`; `UnmatchedRatio = abs(unmatched)/abs(total)`; `CancellationRatio = gross opposing effect mass/max(abs(change),floor)`. Zero denominators return null with `ZeroCompanyChange`. HHI, breadth, driver threshold, and high-cancellation guard are policy-versioned. Mix shift is a non-additive signal only.

## 15. Orchestration

Feature 129 Job idempotency is SHA-256 of feature/version, provider/company/period, manifest generation/fingerprint, ownership version, unit/calculation policies, and algorithm version. Ingestion commits manifest pointer, mutable Job projection, initial Outbox projection, and immutable creation events in one transaction. Existing derived-metric publication remains separate and is guarded from F129.

## 16. Job state machine

Job states are `Requested`, `Running`, `RetryScheduled`, `Completed`, `PermanentlyFailed`, `Cancelled`, and `DeadLettered`. Job is authoritative for execution/result state. Every mutation appends `Feature129ComputationJobStateEvent` in the same transaction. Terminal states cannot return active except audited redrive creates a new attempt lineage.

## 17. Outbox state machine

Outbox states are `Pending`, `Leased`, `PublishedAwaitingConfirm`, `Confirmed`, `DeliveryConsumed`, `RetryablePublishFailure`, `PermanentlyFailed`, `DeadLettered`, and `Cancelled`. Outbox is authoritative for delivery state. Every transition appends `Feature129ComputationOutboxStateEvent` in the same transaction.

| From | To | Authority/guard | Projection/event | Broker/next delivery |
| --- | --- | --- | --- | --- |
| Pending | Leased | Dispatcher, due predicate, `FOR UPDATE SKIP LOCKED`, `xmin` | Lease token/expiry + event | Publish permitted. |
| Leased | PublishedAwaitingConfirm | Same lease token; publish stable Message ID | State/event | Await confirm; no second attempt. |
| PublishedAwaitingConfirm | Confirmed | Confirm matches Message ID/token | State/event | Broker accepted; consumer delivery permitted. |
| Confirmed | DeliveryConsumed | Dedicated consumer commits outcome | State/event | No redelivery after committed consumption. |
| Leased | RetryablePublishFailure | Lease owner records infra failure | State/event + due time | Returns to Pending after backoff. |
| PublishedAwaitingConfirm | RetryablePublishFailure | Confirm timeout/recovery owner | State/event | Same attempt/Message ID republished. |
| RetryablePublishFailure | Pending | Dispatcher recovery transaction | State/event + `AvailableAtUtc` | Delivery permitted. |
| Pending | Cancelled | Authorized cancellation, `xmin` | Projection/event | Dispatcher excludes; no delivery. |
| RetryablePublishFailure | Cancelled | Authorized cancellation, `xmin` | Projection/event | No future delivery. |
| Leased | Cancelled | Fenced lease owner or verified expiry | Projection/event | No publish if prevented. |
| PublishedAwaitingConfirm | DeliveryConsumed | Consumer sees cancelled Job and commits cancellation outcome | Attempt/event | ACK after commit; no calculation. |
| Confirmed | DeliveryConsumed | Consumer sees cancelled Job and commits cancellation outcome | Attempt/event | ACK after commit; no calculation. |
| Any nonterminal | PermanentlyFailed | Dispatcher/consumer permanent policy | Projection/event | ACK after commit; no retry. |
| Any nonterminal | DeadLettered | Poison/exhaustion policy | Dead-letter/event | ACK/reject according to committed dead-letter rule. |

Forbidden: terminal Outbox to active; reuse attempt number; stale `xmin`/lease mutation; cancellation by unauthorized actor; redrive by mutation. Redrive creates a new Job/Outbox attempt lineage and preserves old state.

## 18. Cancellation and races

Cancellation is DataAdmin-authorized, idempotent by `(JobId,DecisionId)`, and updates Job to `Cancelled` with an immutable event. Pending/retry-failure Outbox can become `Cancelled`. Leased Outbox can become `Cancelled` only by the lease owner with matching token or after verified expiry. Published/confirmed messages cannot be recalled: the consumer locks Job, observes `Cancelled`, inserts `Feature129ComputationAttempt` with `CancelledBeforeCalculation`, appends `DeliveryConsumed`, and ACKs only after commit. A cancelled Job never invokes the calculator. Dispatcher excludes cancelled rows and terminal-cancelled Jobs. Duplicate delivery records duplicate-cancellation evidence and ACKs. Redrive requires a new audited DataAdmin decision and new attempt number. Unauthorized cancellation returns `UnauthorizedCancellation` with no mutation.

## 19. Retry, ACK/NACK, dead letter, and redrive

Attempt numbering starts at 1; automatic attempts are exactly 1–6. Attempt 1 is immediate. Attempts 2–6 are delayed 30 seconds, 2 minutes, 10 minutes, 30 minutes, and 2 hours respectively. “Retry budget remains” means `CurrentAttemptNumber < 6` after the failed attempt is committed. Attempt 6 retryable failure creates no seventh Outbox; it commits Job `DeadLettered`, Outbox `DeadLettered`, attempt evidence, and dead-letter row.

The same Outbox attempt republishes with the same Message ID. A retry creates a new Outbox attempt number and new Message ID with idempotency key `(JobId,AttemptNumber)`. Handler retry transaction writes failed attempt, Job event/projection, next Outbox, Outbox-created event, and due time before ACK. Commit failure means NACK/requeue. Crash after commit before ACK causes duplicate delivery; Job/source fingerprint and attempt/message evidence make it a no-op. Publish-before-confirm crash republishes same message. Deserialization poison is dead-lettered when durable; otherwise NACK/requeue. Permanent publication/handler failure is terminal and ACKed only after its evidence commits.

## 20. Snapshot, pointer, evidence, reproducibility

Snapshots, product facts, evidence, publication events, and policy versions are append-only. Current snapshot pointer is mutable and protected by advisory lock, composite FKs, `xmin`, and deferred pointer/version trigger. Evidence copies numeric inputs, source observation/revision IDs, checksums, alias/policy/manifest IDs, reasons, and timestamps. Public evidence is limited to 20 groups/10 facts; internal evidence is complete. Historical replay reads the stored snapshot/narrative, never current projections.

## 21. Exact PostgreSQL schema contract

All UUIDs are `uuid`/`.NET Guid`, timestamps `timestamptz`/`DateTimeOffset`, bounded text `varchar`, JSON `jsonb`, hash `char(64)`, booleans `boolean`, integers `integer`, and money/quantity/rate `numeric(28,8)`.

| Table | Columns: PostgreSQL type, nullability, default | PK/business/alternate keys | FK/delete/mutability/evidence |
| --- | --- | --- | --- |
| `MonthlyReportLogicalIdentity` | `Id uuid NOT NULL`; provider/company/kind/output/year/month `varchar/varchar/varchar/smallint/integer/smallint NOT NULL`; accepted revision `uuid NULL`; `xmin` | PK Id; unique provider/company/kind/output/year/month | revision FK Restrict; mutable pointer; copied IDs |
| `MonthlyReportRevision` | Id, logical ID, provider revision `varchar NULL`, publication `timestamptz NULL`, fingerprint/checksum `char(64) NOT NULL`, status/reason `varchar NOT NULL`, created `timestamptz NOT NULL DEFAULT now()` | PK; unique logical/fingerprint | logical FK Restrict; immutable; copied |
| `MonthlyReportSourceObservation` | Id/revision/payload UUID NOT NULL; ordinal/duplicate `integer NOT NULL`; discriminator/signature/fingerprint `char/varchar NOT NULL`; product/title/unit `varchar NULL`; Q/P/R `numeric(28,8) NULL`; raw `jsonb NOT NULL`; completed `timestamptz NOT NULL` | PK; unique revision/ordinal/discriminator/duplicate | revision FK Restrict; immutable; all numeric/source fields copied |
| `MonthlyReportReceipt` | Id/logical/operation UUID NOT NULL; attempt `integer`; outcome/reason `varchar`; received `timestamptz`; checksum `char(64)` | PK; unique operation/attempt | logical FK Restrict; immutable; audit |
| `MonthlyReportDecisionEvent` | Id/logical/revision/actor UUID; decision/reason/correlation `varchar`; occurred `timestamptz` | PK; unique idempotency key | FKs Restrict; immutable |
| `MonthlyReportAcceptedPointer` | logical/revision UUID; fingerprint `char(64)`; `xmin`; updated `timestamptz` | PK logical; alternate logical/revision | FKs Restrict; mutable; no history trigger |
| `CompanyMonthIngestionOperationAttempt` | Id UUID; scope fields; operation key `varchar`; attempt `integer`; outcome/reason `varchar`; revision UUID NULL; fingerprint `char(64) NULL`; count `integer`; sum `numeric(28,8)`; times | PK; unique scope/operation/attempt | revision FK Restrict; immutable |
| `CompanyMonthIngestionManifestGeneration` | Id UUID; scope; generation `integer`; vector `jsonb`; fingerprint `char(64)`; readiness `varchar`; created `timestamptz` | PK; unique scope/generation and scope/fingerprint | observations/revisions Restrict; immutable |
| `CompanyMonthIngestionManifestCurrentPointer` | scope; generation UUID; type0 revision UUID; fingerprint; readiness; `xmin`; updated | PK scope; composite alternate scope/generation | FKs Restrict; mutable |
| `CanonicalProduct` | Id/company UUID; code/title `varchar`; active `boolean`; created | PK; unique company/code | company Restrict; administrative mutable |
| `CompanyProductAliasDraft` | Id/scope/actor UUID; proposed `jsonb`; status/reason; created/updated | PK; idempotency unique | scope Restrict; mutable admin |
| `CompanyProductAliasOwnershipVersion` | Id/scope; version integer; signature `varchar`; effective `daterange`; approval state; actor/reason/time | PK; unique scope/version; GiST exclusion on approved current | company Restrict; immutable |
| `CompanyProductAliasOwnershipVersionMember` | version/product UUID; provider signature/unit/category `varchar`; effective `daterange`; current flag | composite PK; signature/range index | version/product Restrict; immutable |
| `CompanyProductAliasDecisionEvent` | Id/version/actor UUID; predecessor/successor UUID NULL; lineage/reason/time | PK; idempotency unique | FKs Restrict; immutable |
| `CompanyProductAliasCurrentPointer` | scope/version UUID; `xmin`; updated | PK scope; composite version FK | Restrict; mutable |
| `CompanyProductAliasCurrentProjection` | scope/signature/product/version; `daterange`; approval/current flags | unique scope/signature/product; exact GiST exclusion in §10 | FKs Restrict; mutable projection |
| `ProviderMonetaryUnitPolicyVersion` | Id/scope/version; unit/dimension; status; actor/time | PK; unique scope/version | Restrict; immutable; evidence |
| `UnitConversionPolicyVersion` | Id/scope/version; source/destination dimensions; factor `numeric(28,8)`; status/time | PK; unique scope/version | Restrict; immutable; evidence |
| `CalculationPolicyVersion` | Id/feature/version; scale/threshold JSON; status/time | PK; unique feature/version | Restrict; immutable; evidence |
| `Feature129ComputationJob` | Id/scope/input IDs; source fingerprint; idempotency `char(64)`; state/current attempt/next time/reason; `xmin`; times | PK; unique idempotency | manifest/alias/policy FKs Restrict; mutable projection |
| `Feature129ComputationJobStateEvent` | Id/job/actor; attempt; from/to/reason/policy/correlation/idempotency; occurred | PK; event idempotency unique | Job FK Restrict; immutable |
| `Feature129ComputationOutbox` | Id/job; attempt; Message ID UUID; payload checksum; state; lease token UUID NULL/expiry; available/confirm times; `xmin` | PK; unique job/attempt, idempotency, Message ID | Job Restrict; mutable projection |
| `Feature129ComputationOutboxStateEvent` | Id/job/outbox/actor; attempt; from/to/reason/policy/correlation/idempotency; occurred | PK; event idempotency unique | FKs Restrict; immutable |
| `Feature129ComputationAttempt` | Id/job/outbox; attempt/message; input fingerprint; outcome/reason; started/completed | PK; unique job/attempt | FKs Restrict; immutable |
| `Feature129ComputationDeadLetter` | Id/job/outbox/attempt; reason; checksum; payload; created | PK; job/attempt index | FKs Restrict; immutable |
| `Feature129ComputationRedriveDecision` | Id/dead letter/actor; old/new attempt; reason/policy/time | PK; idempotency unique | FKs Restrict; immutable |
| `Feature129Snapshot` | Id/job/manifest/alias/policy; scope/periods; totals `numeric(28,8)`; state/reason/time | PK; unique scope/input fingerprint | FKs Restrict; immutable; public evidence |
| `Feature129SnapshotProductFact` | Id/snapshot/product UUID NULL; source values/effects all `numeric(28,8)`; lifecycle/identity/quality/bucket/reason/rank | PK; partial unique snapshot/product where product IS NOT NULL; partial unique snapshot/unmatched fingerprint where product IS NULL | snapshot/product Restrict; immutable |
| `Feature129SnapshotEvidence` | Id/snapshot/fact; source/revision/policy UUID; copied values `jsonb`; reason/time | PK; fact index | FKs Restrict; immutable |
| `Feature129PublicationEvent` | Id/snapshot/actor; state/reason/correlation/time | PK; unique snapshot/state/idempotency | FKs Restrict; immutable |
| `Feature129CurrentSnapshotPointer` | scope/snapshot UUID; `xmin`; updated | PK scope; composite scope/snapshot FK | FKs Restrict; mutable |
| `Feature129BackfillCheckpoint` | scope; last period/key; status; updated; `xmin` | PK scope | mutable operational checkpoint |

All historical FKs use `ON DELETE RESTRICT`. Scope alternate key is `(ProviderName,ExternalCompanyId,JalaliYear,JalaliMonth,Family)` and every pointer/job/generation FK uses those exact columns plus selected ID. `btree_gist` supports equality in the exclusion constraint. Deferred constraint triggers validate pointer-selected scope/version/fingerprint equality at commit. EF maps `xmin` with `Property<uint>("xmin").IsRowVersion().IsConcurrencyToken()`; every update includes ID, expected `xmin`, expected lease token, and nonterminal state predicate.

## 22. Trigger inventory and constraints

`prevent_f129_history_mutation()` rejects UPDATE/DELETE on every immutable table in §21: report revisions/observations/receipts/decision events, operation attempts, manifest generations, alias versions/members/events, Job/Outbox events, attempts/dead letters/redrive, snapshots/facts/evidence/publication events, and policies. Mutable exclusions are logical/pointer/projection tables, Job, Outbox, leases, current snapshot pointer, and checkpoint. Checks enforce closed enums, attempt 1–6, scale, nonnegative limits, and valid ranges. Partial uniqueness and exact exclusion are in §§10 and 21.

## 23. Semantic typed-slot contract

`MonthlyProductSemanticProposal` is closed JSON: `schemaVersion=1`, capability `monthly_product_intelligence`, and slots `company`, `product`, `currentPeriod`, `comparisonPeriod`, `analysisFocus`, `measure`, `presentation`, `resultLimit`. Company/product refs include canonical/provider ID nullable, normalized text, confidence 0–1, provenance, and UTF-16 spans. Periods require Jalali year/month 1–12. Comparison kind is `previous_published` or `previous_fiscal`. Focus is `summary`, `revenue_attribution`, `production_sales`, or `contribution`; measure is `reported_sales`, `quantity`, `rate`, or `production`; presentation is `summary`, `table`, `chart`, or `evidence`; limit is integer 1–100.

Unknown properties, unsupported schema, invalid period/limit/confidence/span, conflicting slot, prompt injection, and unresolved identity have fixed codes. Root shared `AiStructuredOutputContract` remains unchanged; `JsonStructuredOutputValidator` validates transport, then the local validator validates F129 nested data. Deterministic valid values outrank model values; conflicts clarify. Only `ValidatedQueryFrame` reaches executor.

## 24. Actual semantic field flow

| Stage | Existing/new path/type | Input → output | Validation/provenance/failure |
| --- | --- | --- | --- |
| User message | AI facade/workflow request | text → raw user text | auth/input bound |
| Deterministic interpretation | existing orchestration interpreter | text → candidate slots | deterministic provenance |
| Model output | `AiStructuredOutputContract`/`AiModelRequest.StructuredOutput` | JSON → root contract | shared validator |
| Local proposal | new `MonthlyProductSemanticProposal` | root nested → typed proposal | closed properties/codes |
| Slots | new typed refs/period/enums | proposal → slot values | confidence/spans/provenance |
| Merge | new `F129ProposalMerger` | deterministic+model → one proposal | precedence/conflict clarification |
| Resolvers | new company/product/period resolvers | refs → canonical IDs/frame values | alias version; ambiguity |
| Interpretation | existing `QueryInterpretation` adapter | frame slots → interpretation | no slot loss; nullable only where allowed |
| Task state | existing conversation task-state integration | interpretation → persisted validated state | versioned frame; raw model excluded |
| MAF V2 | `FinancialCopilotWorkflowDefinition` and workflow messages | frame → executor command | schema/version guard |
| Executor registry | new F129 capability registration | command → F129 executor | only validated frame |
| Result | new F129 result DTO | snapshot → typed result | server values/evidence |
| Payload | `AssistantMessagePayload` | result → versioned field | payload v3 discriminator |
| Persistence | `MessagePersistenceFunction` | payload/narrative → message row | deterministic narrative |
| Decoder | `ConversationRepositories.DeserializePayload` | bytes → v1/v2/v3 envelope | unknown future kind safe unsupported |
| Replay | new semantic comparator | persisted result → same result | IDs/decimals/enums/order exact |
| Telegram | `TelegramAssistantResponseRenderer` | DTO → escaped bounded text | 3500 UTF-16 limit |
| Web | `src/frontend/src/lib/chat.functions.ts`, `src/frontend/src/components/app/message-list.tsx` | DTO → view model/UI | no financial arithmetic |

V1, native MAF V2, and fallback V2 all consume the same frame; a parity test compares canonical IDs, periods, policy IDs, source IDs, decimal values, enums, and ordered evidence. No slot disappears; missing optional slot remains explicit null with provenance.

## 25. API, payload, replay, Telegram, and web contracts

The R1 AI facade returns `MonthlyProductFeatureResultV3` with `schemaVersion`, `feature`, `state`, `stateReason`, `company`, `periods`, `summary`, `products`, `contribution`, `coverage`, `warnings`, `freshness`, `evidence`, `policyVersionIds`, `sourceRevisionIds`, and `pagination`. Decimal values are invariant decimal strings; IDs are immutable strings; enum values are closed. Collections sort by server rank then canonical ID. `Other` contains member IDs and server-computed amount. States are `Published`, `Partial`, `Stale`, `Processing`, `Unavailable`, `Blocked`, `Empty`, and `Error`.

`AssistantMessagePayload` adds a F129 result discriminator/version while preserving v1/v2 fields. Decoder branches are: v1/v2 existing fields unchanged; v3 F129 result parsed by the new DTO; unknown future kind maps to `UnsupportedResult` and remains replay-safe. Semantic replay compares schema, IDs, decimal values, enums, ordering, and evidence IDs, not serializer bytes. The persisted Persian narrative is deterministic and read from the stored message.

The optional endpoint is `GET /api/features/v1/monthly-products/{companyId}?current=YYYY-MM&comparison=previous`, disabled unless `F129OptionalEndpoint=true`; it requires auth, entitlement, rate limit, billing, and strong ETag. Telegram is capped at 3,500 UTF-16 characters, escapes Markdown, splits at section boundaries, includes unit/period/warning/evidence limitation footer, and never fabricates charts. Web states are Loading, Published, Partial, Stale, Processing, Unavailable, Blocked, Empty, Error; it includes summary, narrative, tables, evidence drawer, RTL/mobile, keyboard access, non-color status, and accessible table equivalent. Clients perform layout only.

## 26. Security, authorization, billing, audit, observability

The authenticated AI facade performs entitlement and rate checks before execution. Billing reserves before execution and commits after successful response; replay uses existing replay charge policy. DataAdmin is required for revision decisions, alias approval, monetary/conversion approval, cancellation, redrive, and backfill controls. Evidence output excludes raw payloads/credentials. Input is capped at 8,000 UTF-16 characters, 100 rows, 20 evidence groups, and 10 facts/group; markdown is sanitized.

Audit events contain actor/action/target IDs, old/new state, reason, policy, correlation, and UTC time. Metrics are `f129_manifest_ready_total`, `f129_calculation_duration_ms`, `f129_publication_total`, `f129_retry_total`, `f129_deadletter_total`, `f129_reconciliation_block_total`, `f129_api_duration_ms`, and `f129_backfill_lag_seconds`. Allowed labels are release gate/outcome/reason/provider/environment; user/company/product/prompt/raw error labels are forbidden. Alerts are publication block >5% of 100 jobs, dead letters >0 for 10 minutes, retry backlog >1,000 for 10 minutes, p95 retrieval >700 ms for 15 minutes, and any reconciliation mismatch. Runbooks cover ambiguity, backlog, lease recovery, dead letter/redrive, stale publication, unit gate, alias collision, and rollback.

## 27. History, backfill, load, and SLO policies

R1 defaults are snapshot p95 ≤300 ms for 100 products/24 periods, facade retrieval excluding model p95 ≤700 ms, two-period calculation p95 ≤5 s, evidence p95 ≤500 ms for 20 groups/10 facts, and dispatcher lease p95 ≤250 ms for 50 rows. R2 history requires accepted revisions: YoY/fiscal YTD uses valid fiscal periods/output type rules; 3/12 averages require contiguous published months and return `PartialWindow` otherwise; anomaly requires at least six periods and `|z|>=3.5` plus materiality floor; inferred inventory uses inferred wording only.

Backfill starts at 1404/01, max two concurrent calculations, 60 company-month/minute, batches 50, polls 5 seconds active/15 seconds idle, checkpoints after each company-month, resumes durably, skips identical fingerprints, and pauses at CPU/pool ≥80% for 5 minutes, queue ≥10,000, failures ≥5%/100 attempts, or provider protection. Resume requires ten minutes below thresholds.

## 28. Complete Persian fixture

All money is million rial; raw unit `هزار عدد`; normalized `ThousandCount`; symbol `غاذر`; base `1405/04`; current `1405/05`.

| Product | Base Q | Base P | Base R | Current Q | Current P | Current R |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| سبزیجات ۴۰ گرمی | 1000 | 100 | 100000 | 1966 | 97.6 | 191881.6 |
| کنسرو مخلوط | 2000 | 100 | 200000 | 1700 | 100 | 170000 |
| غذای آماده صادراتی | 1500 | 100 | 150000 | 2000 | 104 | 208268.4 |

Expected effects are `(95440.8,-3559.2,0,91881.6)`, `(-30000,0,0,-30000)`, and `(51000,7000,268.4,58268.4)` for quantity, price, residual, contribution. Base total `450000`; current `570150`; change `120150`; growth `26.7%`; largest positive contributor `سبزیجات ۴۰ گرمی`; driver `Quantity`. Separate tests reject changed spelling/order, Arabic `ي/ك`, removed ZWNJ, missing ID, incompatible `عدد/تن`, negative return, zero rate, rounding residual, missing month, corrected report, and fiscal-year-end mismatch.

## 29. Individual named-test registry

Every test token used by an AC or matrix resolves to exactly one row. `T-AC-01` through `T-AC-78` are individual tests, not ranges. `T-ORCH-13` and `T-ORCH-14` are additional named crash/race tests.

| ID | Name | Level | Setup/operation | Expected state/result | AC | Gate | Slice | Proposed path |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T-AC-01 | Preserve repeated source rows | DB integration | Ingest two same-code distinct rows | Two immutable observations and both revenues | AC-01 | R1-Core | 1 | `tests/Feature129IngestionTests.cs` |
| T-AC-02 | Reorder semantic fingerprint | Unit | Reorder identical payload rows | Same fingerprint, ordinal-only evidence difference | AC-02 | R1-Core | 1 | `tests/Feature129IngestionTests.cs` |
| T-AC-03 | Replay receipt no revision | DB integration | Submit identical payload twice | Receipt appended; no revision | AC-03 | R1-Core | 1 | `tests/Feature129RevisionTests.cs` |
| T-AC-04 | Raw normalization mismatch block | DB integration | Alter row count/revenue | `RawNormalizationMismatch`; no acceptance | AC-04 | R1-Core | 1 | `tests/Feature129IngestionTests.cs` |
| T-AC-05 | Six operation outcomes | DB integration | Submit all operation outcomes | Six attempt rows | AC-05 | R1-Core | 1 | `tests/Feature129ManifestTests.cs` |
| T-AC-06 | Type0 readiness barrier | Integration | Fail type0, optional present | No Job; readiness false | AC-06 | R1-Core | 1 | `tests/Feature129ManifestTests.cs` |
| T-AC-07 | Optional success generation | Integration | Optional changes failure→success | Generation/job increment once | AC-07 | R1-Core | 1 | `tests/Feature129ManifestTests.cs` |
| T-AC-08 | Current pointer uniqueness | DB integration | Insert two scope pointers | Unique violation | AC-08 | R1-Core | 1 | `tests/Feature129ManifestTests.cs` |
| T-AC-09 | Pointer composite rejection | DB integration | Cross-scope generation pointer | Deferred constraint failure | AC-09 | R1-Core | 1 | `tests/Feature129ManifestTests.cs` |
| T-AC-10 | Manifest semantic no-op | Integration | Repeat same vector with new retry | Audit only, no generation/job | AC-10 | R1-Core | 1 | `tests/Feature129ManifestTests.cs` |
| T-AC-11 | Serializable stale rebuild | DB integration | Concurrent accepted revision update | Retry rereads and selects new revision | AC-11 | R1-Core | 1 | `tests/Feature129ManifestTests.cs` |
| T-AC-12 | Optional completion readiness | Integration | Complete optional after type0 | New ready generation/job | AC-12 | R1-Core | 1 | `tests/Feature129ManifestTests.cs` |
| T-AC-13 | Higher revision wins | DB integration | Submit higher provider revision | Accepted pointer changes | AC-13 | R1-Core | 1 | `tests/Feature129RevisionTests.cs` |
| T-AC-14 | Equal revision ambiguity | DB integration | Equal metadata, different facts | `AmbiguousRevisionOrder` | AC-14 | R1-Core | 1 | `tests/Feature129RevisionTests.cs` |
| T-AC-15 | Late older rejection | DB integration | Older payload after newer | Pointer unchanged, rejection event | AC-15 | R1-Core | 1 | `tests/Feature129RevisionTests.cs` |
| T-AC-16 | Draft isolation | DB integration | Use unapproved draft | No calculation reference | AC-16 | R1-Core | 2 | `tests/Feature129AliasTests.cs` |
| T-AC-17 | Alias overlap exclusion | PostgreSQL integration | Concurrent overlapping approval | GiST exclusion failure | AC-17 | R1-Core | 2 | `tests/Feature129AliasTests.cs` |
| T-AC-18 | Complete ownership version | DB integration | Approve valid draft | Complete immutable members | AC-18 | R1-Core | 2 | `tests/Feature129AliasTests.cs` |
| T-AC-19 | Alias atomic replacement | DB integration | Fail after pointer replacement | Whole transaction rolls back | AC-19 | R1-Core | 2 | `tests/Feature129AliasTests.cs` |
| T-AC-20 | Invalid provider key | Unit | Blank/zero/collision signature | Unmatched/manual review | AC-20 | R1-Core | 2 | `tests/Feature129AliasTests.cs` |
| T-AC-21 | Changed identity version | DB integration | Change package/unit/range | New version, old immutable | AC-21 | R1-Core | 2 | `tests/Feature129AliasTests.cs` |
| T-AC-22 | Lineage event | DB integration | Merge/split/reversal | Predecessor/successor event | AC-22 | R1-Core | 2 | `tests/Feature129AliasTests.cs` |
| T-AC-23 | Historical alias stability | DB integration | Change after snapshot | Snapshot old version | AC-23 | R1-Core | 2 | `tests/Feature129AliasTests.cs` |
| T-AC-24 | Affected periods | Integration | Approve range change | Only intersecting jobs | AC-24 | R1-Core | 2 | `tests/Feature129AliasTests.cs` |
| T-AC-25 | Monetary unit block | Integration | Unconfirmed provider unit | Public output blocked | AC-25 | R1-Core | 2 | `tests/Feature129PolicyTests.cs` |
| T-AC-26 | Conversion block | Integration | Unapproved conversion | Money retained; physical suppressed | AC-26 | R1-Core | 2 | `tests/Feature129PolicyTests.cs` |
| T-AC-27 | Policy approval | DB integration | DataAdmin approves policy | Immutable policy/audit | AC-27 | R1-Core | 2 | `tests/Feature129PolicyTests.cs` |
| T-AC-28 | Signed reported total | Unit | Include negative rows | Totals equal source sum | AC-28 | R1-Core | 3 | `tests/Feature129CalculatorTests.cs` |
| T-AC-29 | Symmetric formula | Unit | Valid continuing fixture | Exact effects | AC-29 | R1-Core | 3 | `tests/Feature129CalculatorTests.cs` |
| T-AC-30 | New product bucket | Unit | Base zero/current positive | Entire current in NewProduct | AC-30 | R1-Core | 3 | `tests/Feature129CalculatorTests.cs` |
| T-AC-31 | Discontinued bucket | Unit | Base positive/current zero | Entire base change in DiscontinuedProduct | AC-31 | R1-Core | 3 | `tests/Feature129CalculatorTests.cs` |
| T-AC-32 | Reason precedence | Unit | Multiple unsafe conditions | Highest closed reason and unsafe bucket | AC-32 | R1-Core | 3 | `tests/Feature129CalculatorTests.cs` |
| T-AC-33 | Stored residual | Unit | Scale rounding | Exact product reconciliation | AC-33 | R1-Core | 3 | `tests/Feature129CalculatorTests.cs` |
| T-AC-34 | Approved conversion | Unit | Approved factor | Converted effects/evidence policy | AC-34 | R1-Core | 3 | `tests/Feature129CalculatorTests.cs` |
| T-AC-35 | Return/invalid input | Unit | Negative quantity/invalid rate | Unsafe signed contribution | AC-35 | R1-Core | 3 | `tests/Feature129CalculatorTests.cs` |
| T-AC-36 | Cancellation guard | Unit | Opposing effects ratio >3 | Unclassified HighCancellation | AC-36 | R1-Core | 3 | `tests/Feature129CalculatorTests.cs` |
| T-AC-37 | Zero/immaterial denominator | Unit | Zero/below-floor change | Null share/classification reason | AC-37 | R1-Core | 3 | `tests/Feature129CalculatorTests.cs` |
| T-AC-38 | Reconciliation publication block | Integration | Alter effect child | No pointer replacement | AC-38 | R1-Core | 3 | `tests/Feature129PublicationTests.cs` |
| T-AC-39 | Classification gates | Unit | Valid covered effects | Closed classification/coverage | AC-39 | R1-Core | 3 | `tests/Feature129CalculatorTests.cs` |
| T-AC-40 | History trigger | PostgreSQL integration | UPDATE/DELETE history | `Feature129HistoryMutationDenied` | AC-40 | R1-Core | 1 | `tests/Feature129ImmutabilityTests.cs` |
| T-AC-41 | Repository history error | Unit | Call update/delete repository | `Feature129ImmutableHistoryViolation` | AC-41 | R1-Core | 1 | `tests/Feature129ImmutabilityTests.cs` |
| T-AC-42 | Restrict delete | PostgreSQL integration | Delete referenced source | FK restrict failure | AC-42 | R1-Core | 1 | `tests/Feature129ImmutabilityTests.cs` |
| T-AC-43 | Exact composite scope | PostgreSQL integration | Cross-scope pointer | Deferred exact-key failure | AC-43 | R1-Core | 1 | `tests/Feature129ImmutabilityTests.cs` |
| T-AC-44 | Publication event identity | DB integration | Event names other snapshot | Constraint failure | AC-44 | R1-Core | 3 | `tests/Feature129PublicationTests.cs` |
| T-AC-45 | Job idempotency | Integration | Same scope inputs twice | One Job/Outbox | AC-45 | R1-Core | 1 | `tests/Feature129OrchestrationTests.cs` |
| T-AC-46 | Concurrent same calculation | PostgreSQL integration | Two consumers same input | One snapshot/no-op | AC-46 | R1-Core | 3 | `tests/Feature129PublicationTests.cs` |
| T-AC-47 | Publication rollback | DB integration | Fail child/evidence insert | Prior pointer unchanged | AC-47 | R1-Core | 3 | `tests/Feature129PublicationTests.cs` |
| T-AC-48 | Scheduler guard | Unit/integration | Schedule F129 through generic scheduler | Fixed error, no side effects | AC-48 | R1-Core | 1 | `tests/Feature129SchedulerGuardTests.cs` |
| T-AC-49 | Atomic manifest Job | DB integration | Fail transaction midway | Both or neither | AC-49 | R1-Core | 1 | `tests/Feature129OrchestrationTests.cs` |
| T-AC-50 | Dispatcher lease fencing | DB integration | Two dispatchers due row | One lease owner | AC-50 | R1-Core | 1 | `tests/Feature129OutboxTests.cs` |
| T-AC-51 | Publish-before-confirm crash | Messaging integration | Crash after broker publish | Same attempt/Message ID republish | AC-51 | R1-Core | 1 | `tests/Feature129OutboxTests.cs` |
| T-AC-52 | Lease/confirm/consumer race | Messaging integration | Expiry races with delivery | One fenced outcome, no loss/duplicate result | AC-52 | R1-Core | 1 | `tests/Feature129OutboxTests.cs` |
| T-AC-53 | Success ACK ordering | Messaging integration | Successful handler | Commit then ACK | AC-53 | R1-Core | 3 | `tests/Feature129ConsumerTests.cs` |
| T-AC-54 | Commit failure NACK | Messaging integration | DB commit failure | NACK/requeue, no false terminal | AC-54 | R1-Core | 3 | `tests/Feature129ConsumerTests.cs` |
| T-AC-55 | Retry pre-ACK transaction | Messaging integration | Retryable handler failure | Next Outbox committed before ACK | AC-55 | R1-Core | 3 | `tests/Feature129ConsumerTests.cs` |
| T-AC-56 | Sixth attempt exhaustion | Messaging integration | Attempt 6 retryable failure | DeadLettered, no attempt 7 | AC-56 | R1-Core | 1 | `tests/Feature129RetryTests.cs` |
| T-AC-57 | Duplicate terminal delivery | Messaging integration | Redeliver terminal message | Duplicate event, no second result, ACK | AC-57 | R1-Core | 3 | `tests/Feature129ConsumerTests.cs` |
| T-AC-58 | Authorized redrive | DB integration | DataAdmin redrive dead letter | New lineage/attempt | AC-58 | R1-Core | 1 | `tests/Feature129RetryTests.cs` |
| T-AC-59 | Preventable cancellation | DB integration | Cancel Pending/Retryable Outbox | Job/Outbox Cancelled, no delivery | AC-59 | R1-Core | 1 | `tests/Feature129CancellationTests.cs` |
| T-AC-60 | Strict proposal schema | Contract | Unknown property/schema | Fixed rejection, no executor | AC-60 | R1-Core | 4 | `tests/Feature129SemanticTests.cs` |
| T-AC-61 | Slot value validation | Unit | Invalid period/limit/span | Field-specific rejection | AC-61 | R1-Core | 4 | `tests/Feature129SemanticTests.cs` |
| T-AC-62 | Merge conflict | Unit | Deterministic/model conflict | Precedence or clarification | AC-62 | R1-Core | 4 | `tests/Feature129SemanticTests.cs` |
| T-AC-63 | Persian resolution | Contract | Varied Persian request | Canonical IDs/spans | AC-63 | R1-Core | 4 | `tests/Feature129SemanticTests.cs` |
| T-AC-64 | V1/V2 frame parity | End-to-end | Same frame through three paths | Equal canonical result | AC-64 | R1-Core | 4 | `tests/Feature129SemanticTests.cs` |
| T-AC-65 | Prompt isolation | Contract | Injection/unsupported capability | Clarification, no route | AC-65 | R1-Core | 4 | `tests/Feature129SemanticTests.cs` |
| T-AC-66 | Result DTO v3 | Contract | Valid frame | Complete typed result | AC-66 | R1-Core | 4 | `tests/Feature129ApiTests.cs` |
| T-AC-67 | Legacy decoder | Contract | Persisted v1/v2 payload | Existing meaning preserved | AC-67 | R1-Core | 4 | `tests/Feature129ReplayTests.cs` |
| T-AC-68 | Semantic replay | Contract | Replay v3 payload | Equal decimals/IDs/order/evidence | AC-68 | R1-Core | 4 | `tests/Feature129ReplayTests.cs` |
| T-AC-69 | Telegram split | Contract | >3500 UTF-16 response | Escaped bounded sections | AC-69 | R1-Core | 4 | `tests/Feature129TelegramTests.cs` |
| T-AC-70 | Telegram no chart | Component | Chart-capable result | Summary/table only | AC-70 | R1-Core | 4 | `tests/Feature129TelegramTests.cs` |
| T-AC-71 | Web server values | Frontend component | Published DTO | UI equals DTO; no arithmetic | AC-71 | R1-Core | 5 | `src/frontend/src/components/app/__tests__/message-list.test.tsx` |
| T-AC-72 | Web state matrix | Frontend component | All result states | Correct accessible state | AC-72 | R1-Core | 5 | `src/frontend/src/components/app/__tests__/message-list.test.tsx` |
| T-AC-73 | R2 YoY/YTD | Integration | Accepted fiscal periods | Correct/suppressed result | AC-73 | R2-History | 6 | `tests/Feature129HistoryTests.cs` |
| T-AC-74 | R2 contiguous average | Integration | Complete/incomplete window | Average or PartialWindow | AC-74 | R2-History | 6 | `tests/Feature129HistoryTests.cs` |
| T-AC-75 | R2 anomaly | Integration | ≥6 periods | z-score/materiality result | AC-75 | R2-History | 6 | `tests/Feature129HistoryTests.cs` |
| T-AC-76 | Optional endpoint | API integration | Flag/auth/ETag enabled | Auth/rate/304 behavior | AC-76 | Optional-Endpoint | 6 | `tests/Feature129EndpointTests.cs` |
| T-AC-77 | Complete Persian fixture | Unit/integration | §28 fixture and negatives | All exact assertions | AC-77 | R1-Core | 3 | `tests/Feature129FixtureTests.cs` |
| T-AC-78 | Cancelled published delivery | Messaging integration | Published/confirmed message, Job cancelled | Consumer records cancellation, no calculation, commit then ACK | AC-78 | R1-Core | 1 | `tests/Feature129CancellationTests.cs` |
| T-ORCH-13 | Cancelled published delivery crash | Messaging integration | Crash before cancellation-consumption commit | NACK/requeue; next delivery commits then ACKs | AC-78 | R1-Core | 1 | `tests/Feature129CancellationTests.cs` |
| T-ORCH-14 | Unauthorized cancellation | Integration | Non-DataAdmin cancellation | `UnauthorizedCancellation`; no mutation | AC-59 | R1-Core | 1 | `tests/Feature129CancellationTests.cs` |

## 30. Atomic acceptance criteria

The following table contains exactly 78 rows. Each row has one primary slice owner and one individually defined test.

| AC | Gate | Preconditions | One normative behavior | Observable result/failure | Section | Test | Primary slice |
| --- | --- | --- | --- | --- | --- | --- | --- |
| AC-01 | R1-Core | Two distinct same-code rows | Persist both observations | Two rows and both revenues | §7 | T-AC-01 | 1 |
| AC-02 | R1-Core | Reordered same facts | Fingerprint ignores ordinal | Equal fingerprints | §7 | T-AC-02 | 1 |
| AC-03 | R1-Core | Exact replay | Append receipt only | No revision | §8 | T-AC-03 | 1 |
| AC-04 | R1-Core | Count/sum mismatch | Block acceptance | `RawNormalizationMismatch` | §6 | T-AC-04 | 1 |
| AC-05 | R1-Core | Six operations received | Persist each outcome | Six operation rows | §6 | T-AC-05 | 1 |
| AC-06 | R1-Core | Type0 not accepted | Prevent Job | No Job; readiness false | §9 | T-AC-06 | 1 |
| AC-07 | R1-Core | Optional failure→success | Create generation | One new generation/job | §9 | T-AC-07 | 1 |
| AC-08 | R1-Core | Existing scope pointer | Enforce one current row | Unique violation | §9 | T-AC-08 | 1 |
| AC-09 | R1-Core | Cross-scope pointer | Reject commit | Deferred FK/trigger failure | §9 | T-AC-09 | 1 |
| AC-10 | R1-Core | Same semantic vector | Avoid financial generation | Audit only | §9 | T-AC-10 | 1 |
| AC-11 | R1-Core | Serializable state changed | Rebuild before retry | New accepted revision selected | §9 | T-AC-11 | 1 |
| AC-12 | R1-Core | Optional completes | Publish readiness | New ready generation | §9 | T-AC-12 | 1 |
| AC-13 | R1-Core | Higher provider revision | Accept it | Pointer changes | §8 | T-AC-13 | 1 |
| AC-14 | R1-Core | Equal metadata/different facts | Require decision | `AmbiguousRevisionOrder` | §8 | T-AC-14 | 1 |
| AC-15 | R1-Core | Late older payload | Preserve newer pointer | Rejection event | §8 | T-AC-15 | 1 |
| AC-16 | R1-Core | Draft unapproved | Exclude from calculation | No Job/snapshot reference | §10 | T-AC-16 | 2 |
| AC-17 | R1-Core | Approved range overlap | Reject approval | GiST exclusion failure | §10 | T-AC-17 | 2 |
| AC-18 | R1-Core | Valid draft | Insert complete version | Complete members | §10 | T-AC-18 | 2 |
| AC-19 | R1-Core | Approval replacement fails | Roll back atomically | Prior pointer/projection | §10 | T-AC-19 | 2 |
| AC-20 | R1-Core | Blank/zero/collision key | Treat as unresolved | Unmatched/manual review | §10 | T-AC-20 | 2 |
| AC-21 | R1-Core | Identity signature changed | Insert new version | Old immutable | §10 | T-AC-21 | 2 |
| AC-22 | R1-Core | Merge/split/etc approved | Persist lineage | Decision event | §10 | T-AC-22 | 2 |
| AC-23 | R1-Core | Historical snapshot exists | Preserve old ownership | Original version resolves | §10 | T-AC-23 | 2 |
| AC-24 | R1-Core | Alias range changed | Schedule intersection only | Exact affected Jobs | §10 | T-AC-24 | 2 |
| AC-25 | R1-Core | Monetary unit unconfirmed | Block public money | `MonetaryUnitUnconfirmed` | §11 | T-AC-25 | 2 |
| AC-26 | R1-Core | Conversion unapproved | Suppress physical comparison | `UnitConversionUnapproved` | §11 | T-AC-26 | 2 |
| AC-27 | R1-Core | DataAdmin approves policy | Insert immutable policy | Version/audit committed | §11 | T-AC-27 | 2 |
| AC-28 | R1-Core | Accepted type0 both periods | Sum signed revenue | Exact totals | §12 | T-AC-28 | 3 |
| AC-29 | R1-Core | Valid continuing product | Apply formulas | Exact effects | §12 | T-AC-29 | 3 |
| AC-30 | R1-Core | Base zero/current positive | Use NewProduct | Entire current contribution | §14 | T-AC-30 | 3 |
| AC-31 | R1-Core | Base positive/current zero | Use DiscontinuedProduct | Entire negative base | §14 | T-AC-31 | 3 |
| AC-32 | R1-Core | Multiple unsafe conditions | Apply reason priority | Closed reason + unsafe | §13 | T-AC-32 | 3 |
| AC-33 | R1-Core | Storage rounding | Allocate residual | Scale-8 equality | §12 | T-AC-33 | 3 |
| AC-34 | R1-Core | Approved conversion | Apply factor | Policy/effects evidence | §12 | T-AC-34 | 3 |
| AC-35 | R1-Core | Return/invalid input | Retain signed unsafe | Fixed reason | §14 | T-AC-35 | 3 |
| AC-36 | R1-Core | Cancellation ratio >3 | Suppress driver | `HighCancellation` | §14 | T-AC-36 | 3 |
| AC-37 | R1-Core | Zero/below-floor change | Null share/classification | Fixed denominator reason | §14 | T-AC-37 | 3 |
| AC-38 | R1-Core | Equation mismatch | Block publication | `ReconciliationMismatch` | §14 | T-AC-38 | 3 |
| AC-39 | R1-Core | Covered valid effects | Classify from closed enum | Classification/coverage result | §14 | T-AC-39 | 3 |
| AC-40 | R1-Core | History UPDATE/DELETE | Reject in DB | `Feature129HistoryMutationDenied` | §22 | T-AC-40 | 1 |
| AC-41 | R1-Core | History repository mutation | Reject at repository | `Feature129ImmutableHistoryViolation` | §22 | T-AC-41 | 1 |
| AC-42 | R1-Core | Referenced history delete | Enforce Restrict | FK failure | §21 | T-AC-42 | 1 |
| AC-43 | R1-Core | Scope key mismatch | Reject exact composite FK | Deferred failure | §21 | T-AC-43 | 1 |
| AC-44 | R1-Core | Wrong publication event snapshot | Reject event | Constraint failure | §20 | T-AC-44 | 3 |
| AC-45 | R1-Core | Same Job input key | Idempotently collapse | One Job/Outbox | §15 | T-AC-45 | 1 |
| AC-46 | R1-Core | Concurrent same calculation | Publish once | One snapshot | §20 | T-AC-46 | 3 |
| AC-47 | R1-Core | Child/evidence failure | Roll back publication | Prior pointer | §20 | T-AC-47 | 3 |
| AC-48 | R1-Core | Call `ScheduleAsync` with F129 | Guard before persistence/publish | `FeatureRequiresTransactionalOutbox` | §34 | T-AC-48 | 1 |
| AC-49 | R1-Core | Manifest/job same transaction | Commit atomically | Both or neither | §15 | T-AC-49 | 1 |
| AC-50 | R1-Core | Due Pending Outbox | Lease fenced | One owner | §17 | T-AC-50 | 1 |
| AC-51 | R1-Core | Publish before confirm crash | Republish same attempt/Message ID | No new attempt | §19 | T-AC-51 | 1 |
| AC-52 | R1-Core | Lease/confirm/consumer race | Apply fenced winner | No loss/duplicate result | §19 | T-AC-52 | 1 |
| AC-53 | R1-Core | Successful handler commit | ACK after commit | Completed + ACK | §19 | T-AC-53 | 3 |
| AC-54 | R1-Core | Commit failure | NACK/requeue | No false terminal | §19 | T-AC-54 | 3 |
| AC-55 | R1-Core | Attempt n<6 retryable failure | Commit future delivery before ACK | RetryScheduled + next Outbox | §19 | T-AC-55 | 3 |
| AC-56 | R1-Core | Attempt 6 retryable failure | Exhaust automatically | DeadLettered; no attempt 7 | §19 | T-AC-56 | 1 |
| AC-57 | R1-Core | Duplicate terminal delivery | Deduplicate | No second result; ACK | §19 | T-AC-57 | 3 |
| AC-58 | R1-Core | Authorized redrive | Create new lineage | New attempt/outbox | §19 | T-AC-58 | 1 |
| AC-59 | R1-Core | Authorized cancellation before publish | Cancel preventable Job/Outbox | `Cancelled`; dispatcher excludes | §18 | T-AC-59 | 1 |
| AC-60 | R1-Core | Unknown proposal property | Reject locally | Fixed validation code | §23 | T-AC-60 | 4 |
| AC-61 | R1-Core | Invalid slot value | Reject slot | Field code | §23 | T-AC-61 | 4 |
| AC-62 | R1-Core | Model/deterministic conflict | Precedence/clarification | One frame or clarification | §24 | T-AC-62 | 4 |
| AC-63 | R1-Core | Persian varied query | Resolve canonical refs | IDs/spans | §24 | T-AC-63 | 4 |
| AC-64 | R1-Core | Same frame V1/native/fallback | Execute parity | Equal semantic result | §24 | T-AC-64 | 4 |
| AC-65 | R1-Core | Injection/unsupported capability | Isolate | No executor route | §23 | T-AC-65 | 4 |
| AC-66 | R1-Core | Valid frame | Return v3 DTO | Complete typed result | §25 | T-AC-66 | 4 |
| AC-67 | R1-Core | v1/v2 persisted payload | Decode legacy branch | Meaning preserved | §25 | T-AC-67 | 4 |
| AC-68 | R1-Core | v3 replay | Compare semantics | Exact IDs/decimals/order | §25 | T-AC-68 | 4 |
| AC-69 | R1-Core | Telegram >3500 UTF-16 | Split escaped sections | Bounded messages/footer | §25 | T-AC-69 | 4 |
| AC-70 | R1-Core | Telegram chart result | Render text/table | No chart/arithmetic | §25 | T-AC-70 | 4 |
| AC-71 | R1-Core | Published web DTO | Render server values | No client arithmetic | §25 | T-AC-71 | 5 |
| AC-72 | R1-Core | All web states | Render state matrix | Accessible state/table | §25 | T-AC-72 | 5 |
| AC-73 | R2-History | Accepted fiscal periods | Compute valid YoY/YTD | Invalid suppressed | §27 | T-AC-73 | 6 |
| AC-74 | R2-History | Complete/incomplete window | Average only complete | `PartialWindow` otherwise | §27 | T-AC-74 | 6 |
| AC-75 | R2-History | ≥6 comparable periods | Compute anomaly | z-score/materiality evidence | §27 | T-AC-75 | 6 |
| AC-76 | Optional-Endpoint | Flag/auth/ETag enabled | Serve direct endpoint | Auth/rate/304 contract | §25 | T-AC-76 | 6 |
| AC-77 | R1-Core | Fixture loaded | Assert all inputs/negatives | Exact fixture results | §28 | T-AC-77 | 3 |
| AC-78 | R1-Core | Published/confirmed message; Job already cancelled | Consumer records cancellation, skips calculator, then ACKs after commit | `CancelledBeforeCalculation`, DeliveryConsumed | §18 | T-AC-78 | 1 |

## 31. Traceability validation

The validation tool parses the §30 table, requires exactly 78 rows, IDs 1–78 once, gates 74/3/1, one test token, one primary slice, and section heading existence. It parses §29 and requires each `T-AC-*`, `T-ORCH-13`, and `T-ORCH-14` exactly once. It rejects test ranges as AC references and verifies every resolution-matrix row has eight cells.

## 32. Vertical slices

Slice 1 owns AC-01–AC-15, AC-40–AC-45, AC-48–AC-52, AC-56, AC-58–AC-59, and AC-78: immutable source/revision/manifest, schema/trigger foundation, scheduler guard, dispatcher, cancellation, lease/retry/dead-letter lineage, and dedicated consumer. Durable correctness is complete before dispatch enablement.

Slice 2 owns AC-16–AC-27: alias versions/projection, GiST approval, unit and monetary policies.

Slice 3 owns AC-28–AC-39 and AC-44–AC-47, AC-53–AC-55, AC-57, AC-77: calculator, exhaustive attribution, snapshot/evidence publication, handler transaction, and fixture.

Slice 4 owns AC-60–AC-70: semantic validator/flow, MAF V2/V1/fallback parity, result DTO, payload decoder, replay, Telegram.

Slice 5 owns AC-71–AC-72: corrected frontend paths, view model, investor UI, accessibility, and all states.

Slice 6 owns AC-73–AC-76: R2 history/backfill/anomaly and separately enabled Optional-Endpoint. Core ACK/retry/cancellation is not deferred to Slice 6.

Each AC has exactly one primary owner above; dependencies are not additional ownership. R1 ends after Slice 5. R2 and Optional-Endpoint are independently gated.

## 33. Four-category file-impact map

### 33.1 Existing files to modify

| Path | Project/namespace/symbol | Change | Slice | AC/tests |
| --- | --- | --- | --- | --- |
| `src/backend/FinancialCopilot.Infrastructure/Financial/Features/FeatureComputationProcessor.cs` | Infrastructure; `FeatureRecalculationScheduler.ScheduleAsync` | Pre-persistence F129 dispatch guard; retain other feature behavior | 1 | AC-48/T-AC-48 |
| `src/backend/FinancialCopilot.Application/FinancialData/Features/DerivedFeatureContracts.cs` | Application; `IFeatureRecalculationScheduler` | Preserve interface, expose dispatch metadata contract | 1 | AC-48 |
| `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/FinancialDataSyncProcessor.cs` | Infrastructure; `ProcessCoreAsync` | Atomic F129 manifest/job/outbox while existing derived event remains separate | 1 | AC-49 |
| `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiMonthlyActivityNormalizer.cs` | Infrastructure; `NormalizeAsync` | Immutable observation dual-write/compatibility projection | 1 | AC-01–AC-04 |
| `src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/NadpcoApiDataProviderClient.cs` | Infrastructure; monthly fetch | Operation metadata/raw envelope | 1 | AC-05 |
| `src/backend/FinancialCopilot.Infrastructure/ServiceCollectionExtensions.cs` | Infrastructure; registration | F129 repositories/publisher/dispatcher/consumer/semantic executor | 1/4 | AC-48/AC-64 |
| `src/backend/FinancialCopilot.Worker/Program.cs` | Worker; host registration | Dedicated F129 worker/dispatcher | 1 | AC-50–AC-59/AC-78 |
| `src/backend/FinancialCopilot.Application/AI/ModelProviders/AiModelContracts.cs` | Application; root contract | Keep root; add adapter integration only | 4 | AC-60 |
| `src/backend/FinancialCopilot.Infrastructure/AI/ModelProviders/AiModelProviderServices.cs` | Infrastructure; `JsonStructuredOutputValidator` | Invoke local F129 validator after root validation | 4 | AC-60–AC-62 |
| `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs` | Infrastructure; MAF workflow | Carry validated frame/result messages | 4 | AC-64/AC-66 |
| `src/backend/FinancialCopilot.Application/Conversations/ConversationContracts.cs` | Application; `AssistantMessagePayload` | F129 v3 discriminator/field | 4 | AC-67–AC-68 |
| `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Functions/MessagePersistenceFunction.cs` | Infrastructure; persistence function | Canonical payload/narrative | 4 | AC-68 |
| `src/backend/FinancialCopilot.Infrastructure/Conversations/Persistence/ConversationRepositories.cs` | Infrastructure; `DeserializePayload` | v1/v2/v3 decoder/replay | 4 | AC-67–AC-68 |
| `src/backend/FinancialCopilot.Infrastructure/Authentication/TelegramAssistantResponseRenderer.cs` | Infrastructure; renderer | F129 bounded output | 4 | AC-69–AC-70 |
| `src/frontend/src/lib/chat.functions.ts` | Frontend; chat mapping | F129 DTO/view model | 5 | AC-71–AC-72 |
| `src/frontend/src/components/app/message-list.tsx` | Frontend; message component | F129 states/result | 5 | AC-71–AC-72 |
| `src/frontend/src/components/app/__tests__/message-list.test.tsx` | Frontend tests | State/server-value/accessibility tests | 5 | AC-71–AC-72 |

### 33.2 Existing inspected but intentionally unchanged

`src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/MetricRecalculationProcessor.cs`; shared unrelated RabbitMQ feature consumer behavior; historical migrations; existing old payload meanings; existing Product Revenue Mix and Monthly Trend calculators during shadow period. These are unchanged for F129, except the explicit separate scheduler guard in §33.1.

### 33.3 Proposed new files

In `src/backend/FinancialCopilot.Infrastructure/Financial/Feature129/`: `Feature129Rows.cs`, `Feature129Configurations.cs`, `Feature129RevisionService.cs`, `Feature129ManifestService.cs`, `Feature129AliasService.cs`, `Feature129PolicyService.cs`, `Feature129Calculator.cs`, `Feature129SnapshotWriter.cs`, `Feature129JobRepository.cs`, `Feature129OutboxRepository.cs`, `Feature129Dispatcher.cs`, `Feature129MessageContracts.cs`, `Feature129Publisher.cs`, `Feature129Consumer.cs`, `Feature129CancellationService.cs`; in Application: typed proposal/frame/executor/DTO contracts; in Worker: `Feature129ConsumerWorker.cs`; in migration project: new migration/model snapshot; in tests: files named §29.

### 33.4 Optional later-slice files

R2 history/anomaly/backfill repositories/calculators/coordinator, history frontend components, and Optional-Endpoint controller/DTO/auth tests are added only under their gates.

## 34. Dedicated consumer and scheduler guard

F129 feature metadata has closed `DispatchMode.TransactionalOutbox`. `FeatureRecalculationScheduler.ScheduleAsync` checks feature metadata before `GetByIdempotencyKeyAsync`, `StoreAsync`, or `PublishRequestedAsync`; F129 throws `FeatureRequiresTransactionalOutbox` with no side effect. Existing direct features preserve current behavior. F129 uses dedicated queue `financialcopilot.f129.requested`, routing key `financialcopilot.f129`, durable publisher confirms, manual ACK, prefetch 1, and a new `Feature129Consumer`/`Feature129ConsumerWorker`. Envelope includes Job ID, Outbox ID, Message ID, attempt, source fingerprint, correlation and causation IDs, schema/version, and payload checksum.

Malformed schema is durably dead-lettered when possible; retryable handler failure follows §19; permanent failure commits terminal evidence then ACKs; cancelled delivery follows §18; duplicate terminal delivery records evidence then ACKs. Existing `RabbitMqFeatureBus` and `FeatureComputationConsumerWorker` remain for unrelated features. Consumer shutdown leaves unACKed delivery for broker redelivery.

## 35. Coexistence, cutover, rollback

Shadow mode dual-writes immutable observations and compatibility rows; existing calculators remain active. F129 shadow snapshots compare source sums/effects/policy IDs with current results. Canary companies verify counts, manifest readiness, alias completeness, reconciliation, and SLOs. Public flag enables only after PostgreSQL trigger, concurrent, retry crash, cancellation, and messaging tests pass. Rollback disables public dispatch/read and pauses backfill; it never deletes or mutates immutable history. Existing features remain direct; F129 is rejected from the old scheduler. MAF V1/native/fallback share the frame; payload v1/v2 remain readable.

## 36. Design-v6 finding-resolution matrix

| Finding ID | Previous severity | Concrete resolution | Design-v7 section | Related ACs | Exact tests | Primary slice | Status |
| --- | --- | --- | --- | --- | --- | --- | --- |
| V6-B-01 | BLOCKER | Closed Outbox `Cancelled`, preventable/published race behavior, events, guards, ACK rules | §§17–19 | AC-59, AC-78 | T-AC-59, T-AC-78, T-ORCH-13, T-ORCH-14 | 1 | Resolved |
| V6-B-02 | BLOCKER | AC-78 is runtime cancelled-delivery behavior; traceability moved to checklist/test | §§18, 39 | AC-78 | T-AC-78, T-ORCH-13 | 1 | Resolved |
| V6-B-03 | BLOCKER | Individual registry row for every T-AC and explicit ORCH tests | §29 | All ACs | T-AC-01, T-AC-02, T-AC-03, T-AC-04, T-AC-05, T-AC-06, T-AC-07, T-AC-08, T-AC-09, T-AC-10, T-AC-11, T-AC-12, T-AC-13, T-AC-14, T-AC-15, T-AC-16, T-AC-17, T-AC-18, T-AC-19, T-AC-20, T-AC-21, T-AC-22, T-AC-23, T-AC-24, T-AC-25, T-AC-26, T-AC-27, T-AC-28, T-AC-29, T-AC-30, T-AC-31, T-AC-32, T-AC-33, T-AC-34, T-AC-35, T-AC-36, T-AC-37, T-AC-38, T-AC-39, T-AC-40, T-AC-41, T-AC-42, T-AC-43, T-AC-44, T-AC-45, T-AC-46, T-AC-47, T-AC-48, T-AC-49, T-AC-50, T-AC-51, T-AC-52, T-AC-53, T-AC-54, T-AC-55, T-AC-56, T-AC-57, T-AC-58, T-AC-59, T-AC-60, T-AC-61, T-AC-62, T-AC-63, T-AC-64, T-AC-65, T-AC-66, T-AC-67, T-AC-68, T-AC-69, T-AC-70, T-AC-71, T-AC-72, T-AC-73, T-AC-74, T-AC-75, T-AC-76, T-AC-77, T-AC-78, T-ORCH-13, T-ORCH-14 | 1 | Resolved |
| V6-M-01 | MAJOR | Eight-column matrix with exact cells and status | §36 | All | T-AC-78 | 1 | Resolved |
| V6-M-02 | MAJOR | Column-level typed schema, exact keys/FKs/indexes/triggers | §21 | AC-08, AC-09, AC-17, AC-19, AC-40–AC-47 | T-AC-08, T-AC-09, T-AC-17, T-AC-19, T-AC-40, T-AC-41, T-AC-42, T-AC-43, T-AC-44, T-AC-45, T-AC-46, T-AC-47 | 1 | Resolved |
| V6-M-03 | MAJOR | Total Outbox transitions incl. permanent failure/cancellation/races | §17 | AC-50, AC-51, AC-52, AC-53, AC-54, AC-55, AC-56, AC-57, AC-58, AC-59, AC-78 | T-AC-50, T-AC-51, T-AC-52, T-AC-53, T-AC-54, T-AC-55, T-AC-56, T-AC-57, T-AC-58, T-AC-59, T-AC-78 | 1 | Resolved |
| V6-M-04 | MAJOR | Actual scheduler method guard before all side effects | §§4,34 | AC-48 | T-AC-48 | 1 | Resolved |
| V6-M-05 | MAJOR | Correct verified frontend paths and symbols | §33 | AC-71, AC-72 | T-AC-71, T-AC-72 | 5 | Resolved |
| V6-M-06 | MAJOR | Field-by-field semantic flow through actual contracts | §24 | AC-60, AC-61, AC-62, AC-63, AC-64, AC-65, AC-66, AC-67, AC-68 | T-AC-60, T-AC-61, T-AC-62, T-AC-63, T-AC-64, T-AC-65, T-AC-66, T-AC-67, T-AC-68 | 4 | Resolved |

## 37. Earlier-finding regression audit

| Earlier findings | Status in v7 | Evidence |
| --- | --- | --- |
| Design-review B-01–B-04 | Resolved | §§6–10, 14, 20; immutable observations/revisions/evidence/reconciliation. |
| Design-review M-01–M-06 | Resolved | §§9–10, 17–22; manifest, framework boundary, alias, pointer/schema, classification. |
| Design-review M-07–M-10 | Resolved | §§23–25, 33–35; semantic flow, DTO/replay, slices, explicit AC/tests. |
| Design-review N-01/T-01 | Resolved | §§12, 27; precision and safe release gates. |
| V2-M-01–V2-M-07 | Resolved | §§10, 20–25, 28, 33–34. |
| V3-M-01–V3-M-04 | Resolved | §§17–19, 23–24, 34. |
| V3-N-01–V3-N-02/V3-T-01 | Resolved | §§22, 33, 36 and policy gates. |
| V4-M-01–V4-M-04 | Resolved | §§9, 17–19, 27–29. |
| V4-N-01–V4-N-02 | Resolved | §§17, 22. |
| V5-AC-01/V5-M-01–V5-M-08 | Resolved | §§17–25, 29–31, 36; v7 does not rely on grouped AC/test ranges. |

## 38. Open decisions and safe gates

Provider monetary-unit confirmation, conversion dictionary, R2 activation, Optional-Endpoint approval, and versioned freshness/materiality/anomaly policy values remain gated. Until decided, monetary/physical/R2/endpoint output is blocked or suppressed with a fixed reason. None requires inventing behavior for R1 correctness.

## 39. Final readiness checklist

- [x] Standalone source, revision, manifest, alias, unit, formula, attribution, snapshot, evidence, job, outbox, semantic, API, UX, security, operations, fixture, tests, slices, impact, coexistence, and matrices are present.
- [x] Outbox cancellation covers preventable, published/confirmed, lease, consumer, duplicate, redrive, and unauthorized cases.
- [x] Outbox state machine includes every declared state and permanent failure.
- [x] Retry attempts are exactly 1–6 with deterministic delays and pre-ACK durable future delivery.
- [x] Exact PostgreSQL/EF keys, FKs, types, ranges, triggers, locks, and `xmin` rules are specified.
- [x] AC-78 is runtime cancellation behavior, not a circular document check.
- [x] Every AC has one individually defined test and one primary slice.
- [x] Correct repository paths and actual scheduler/semantic/payload integration points are named.
- [x] R1/R2/Optional-Endpoint counts are 74/3/1.
- [x] Previous documents are unchanged; no implementation, migration, test, configuration, infrastructure, or production-data file is changed by this design task.
- [x] Mechanical validation is required before task decomposition: 78 AC rows/IDs, every test token once, every matrix row eight cells, every section reference resolved, every path verified/proposed, and prior hashes unchanged.

**Final status:** `READY_FOR_DESIGN_REVIEW`
