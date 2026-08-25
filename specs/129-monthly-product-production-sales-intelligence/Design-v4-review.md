# Feature 129 — Design-v4 Independent Gate Review

## 1. Review metadata and scope

| Field | Value |
| --- | --- |
| Review subject | `specs/129-monthly-product-production-sales-intelligence/Design-v4.md` |
| Reviewer role | Independent Principal Software Architect, financial-systems, PostgreSQL/EF Core, and adversarial design gate |
| Review date | 2026-08-24 |
| Decision gate | Ready for User Story and implementation-task decomposition |
| Verdict | `NEED_CHANGES` |

This review does not implement code, create migrations, or modify source, tests, configuration, production data, or prior design/review documents. Repository behavior is treated as authoritative; the v4 resolution matrix is not accepted as proof without an implementable mechanism.

## 2. Repository evidence inspected

The review inspected the v4 document and the cited repository implementation, including:

- `src/backend/FinancialCopilot.Infrastructure/Financial/Features/FeatureComputationProcessor.cs`: the current scheduler stores a job and then publishes directly; the current processor has no Feature 129 handler or dispatch-mode guard.
- `src/backend/FinancialCopilot.Infrastructure/Financial/Features/Messaging/RabbitMqFeatureMessaging.cs`: the existing feature consumer ACKs before invoking its handler. A handler exception is logged after ACK and cannot cause broker redelivery.
- `src/backend/FinancialCopilot.Worker/FeatureComputationConsumerWorker.cs`: the existing worker invokes `IFeatureComputationProcessor` through that consumer.
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/FinancialDataSyncProcessor.cs`: raw payload persistence, normalization, and the existing derived-metric publication are separate from the proposed Feature 129 outbox.
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/MetricRecalculationProcessor.cs`: existing metric-registry recalculation path, intentionally outside Feature 129.
- `src/backend/FinancialCopilot.Application/AI/Orchestration/AiOrchestrationContracts.cs` and `ConversationContracts.cs`: current shared/root structured-output and persisted assistant-payload contracts.
- Current EF ingestion rows/configuration/context and existing migrations/model snapshot; no Feature 129 migration, trigger, outbox, dedicated transport, or local semantic validator exists yet.

## 3. Overall assessment

The v4 document substantially improves the alias model, manifest pointer, immutable-history policy, dedicated dispatch design, semantic validation choice, monetary gates, impact map, and slice order. The complete ownership-version model is internally coherent on paper, and the seven-bucket fixture arithmetic is consistent with the earlier verified values.

The design is not yet task-ready. A retryable Feature 129 handler failure is explicitly ACKed after writing `RetryableFailed`, but no durable mechanism is defined that converts that state into a future outbox delivery. This can lose work permanently. The manifest-selection comparator is also not deterministic enough to implement safely, and the standalone v4 document does not contain the normative fixture inputs needed to implement AC-77. Several acceptance criteria remain non-objective. These are material gaps, so the gate is `NEED_CHANGES`.

## 4. Previous-finding audit

| Finding family | Assessment | Evidence |
| --- | --- | --- |
| Original B-01–B-04 | Covered | Immutable observations, complete attribution, copied evidence, and revision precedence are retained. |
| Original M-01–M-10 | PartiallyCovered | Most foundations remain, but deterministic manifest selection and durable retry recovery are not fully implementable. |
| Original N-01 | Covered | Precision, scale, ToEven rounding, fingerprints, and denominator reasons are specified. |
| Original T-01 | Covered | Later history/anomaly/endpoint work is separated from core correctness. |
| Design-v2 findings | PartiallyCovered | V2 concerns are addressed structurally, but the retry/ACK gap remains a direct descendant of V2-M-04. |

## 5. Design-v3 finding audit

| Finding | Assessment | Reason |
| --- | --- | --- |
| V3-B-01 | Covered | Drafts are not inputs; approval materializes a complete immutable version; pointer/projection/snapshot use one version. |
| V3-M-01 | PartiallyCovered | Direct-path guard and dedicated ACK-after-persistence are specified, but retryable ACK recovery is not durable. |
| V3-M-02 | PartiallyCovered | Pointer and exact generation IDs exist, but the candidate ordering/staleness comparator is underspecified. |
| V3-M-03 | Covered | Trigger function, insert-only repositories, composite/deferred checks, Restrict deletes, and soft raw references are selected. |
| V3-M-04 | Covered | Shared root contract remains unchanged and local strict nested validation is authoritative. |
| V3-N-01 | Covered | Four impact categories include migration/model snapshot, dispatcher/consumer, validator, auth/audit, and tests. |
| V3-N-02 | Covered | Documentation-only checks are in AC-78/readiness, not runtime behavior. |
| V3-T-01 | Covered | `MonetaryUnitUnconfirmed` and `UnitConversionUnapproved` are explicit publication gates. |

## 6. Findings requiring correction

| ID | Severity | Section | Finding | Evidence | Required change | Affected ACs/slices |
| --- | --- | --- | --- | --- | --- | --- |
| V4-M-01 | MAJOR | 20 | Retryable handler failure can lose work. The design says to persist `RetryableFailed`/`next attempt` and then ACK because “durable retry owns recovery,” but neither `Feature129ComputationJob` nor `Feature129ComputationOutbox` defines a worker, scheduled query, retry message, or state transition that consumes `RetryableFailed` and creates/requeues a delivery. Once ACKed, RabbitMQ has no message to redeliver. | Design-v4 §20; current consumer ACK-before-handler behavior in `RabbitMqFeatureMessaging.cs` demonstrates why this must be explicit. | Define one durable retry owner: for example, a transactional handler failure writes a retry-ready outbox row (or a durable retry-attempt row consumed by a specified scheduler) with `NextAttemptAt`, lease/fencing semantics, bounded backoff, and exact idempotency. Specify the transaction, crash points, poison handling, and test that a persisted retry is actually delivered. Do not ACK until either terminal persistence or an explicitly persisted retry delivery exists. | AC-55, AC-57; T-ORCH-08/10; Slices 3 and 6 |
| V4-M-02 | MAJOR | 9.3 | Manifest current-generation selection is not deterministic enough for implementation. “Newer or materially changed,” “older/equal,” and “deterministic accepted generation” do not define a total ordering for two valid different fingerprints with the same accepted type-0 revision and optional outcomes. Generation number allocation, provider publication time, operation completion time, and corrected facts are not given precedence rules. | Design-v4 §9.3 steps 5–6 and paragraph after step 8. | Define a total comparator and immutable candidate fields: comparable provider revision, valid provider publication timestamp, normalized payload fingerprint, operation outcome vector, and a deterministic tie-breaker. State when a corrected same-revision payload is admissible, how generation numbers are assigned under the advisory lock, and how stale candidates are rejected/retried. Add concurrent same-revision/different-fingerprint tests. | AC-10–AC-12, AC-43, AC-50; T-MAN-06/07; Slice 1 |
| V4-M-03 | MAJOR | 29–30 | AC-77 is not implementable from the standalone v4 design. It names `غاذر` and `سبزیجات ۴۰ گرمی` and asserts totals, but omits the normative base/current product rows, rates, quantities, units, and expected seven-bucket effects. A task author must consult an earlier document, contrary to the required standalone artifact. | Design-v4 AC-77 and §30 mention the fixture but contain no input table; the prior Design.md contains the actual values. | Reproduce the complete fixture table in v4, including all exact Persian text, base/current quantities and rates, reported revenue, units, expected product effects, company totals, and malformed-variant cases. Keep AC-77 tied to that in-document fixture. | AC-28–AC-35, AC-77; T-FIX-01/T-CALC-*; Slice 3 |
| V4-M-04 | MINOR | 29, 32 | AC-72 and AC-76 are not fully objective. “Optional history policies” and “bounded performance targets” have no numeric limits, dataset sizes, latency percentiles, backfill rate, or accepted policy values. | Design-v4 AC-72/AC-76 and §32 item 3 leave thresholds as future choices. | Add v1 numeric SLO/load/backfill limits and explicit defaults, or move non-normative targets out of runtime ACs. Version any later change and identify the recalculation trigger. | AC-72, AC-76; T-BF-*, T-PERF-01; Slice 6 |
| V4-N-01 | NOTE | 20 | The outbox state machine has both `Confirmed` and handler terminal states but does not explicitly state whether broker confirmation is merely publication bookkeeping or whether it controls consumer deduplication. | Design-v4 §§19–20. | Add a short state-transition table separating publication confirmation from job/result terminality and naming the deduplication key lookup. | AC-52–AC-56 |
| V4-N-02 | NOTE | 22 | The trigger list is normative but does not enumerate every concrete table name for report decision/status event tables and evidence subtypes. | Design-v4 §22.1. | Add the exact table-to-trigger inventory in the migration design task. | AC-38, AC-46 |

## 7. Alias ownership verification

The selected alias model passes the core reproducibility gate. Drafts are administrative only; approval locks the company/provider pointer, loads the prior complete set, applies changes, inserts a new complete header and members, replaces only the mutable projection, updates the pointer, and schedules affected months. The version/member composite keys, GiST exclusion, `btree_gist` prerequisite, event idempotency, `xmin`, Restrict behavior, and deferred projection consistency are suitable PostgreSQL mechanisms.

Concurrency outcomes are coherent: the advisory lock serializes approvals; rollback after projection replacement restores the entire transaction; overlap fails before commit; merge/split/reversal generate another complete version; historical snapshots retain their original version. The only required alias correction is to include the complete normative fixture and to make affected-period job retry behavior use the durable retry correction in V4-M-01.

## 8. Manifest pointer verification

The pointer has the required provider/company/period/family identity, accepted generation, accepted type-0 revision, fingerprint, readiness, `xmin`, Restrict FK, advisory lock, and atomic job/outbox intent. Exact generation IDs are carried into jobs and publisher revalidation.

The acceptance comparator remains a task-blocking gap. Two simultaneous valid responses with the same accepted type-0 revision but different corrected facts can both be “materially changed”; v4 does not say which wins. Identical retries converge, optional late completion is described, and stale pointer writes are serialized, but deterministic selection requires the total ordering specified in V4-M-02.

## 9. Dispatch, ACK, and retry verification

The closed `DirectPublish`/`TransactionalOutbox` policy, pre-persistence direct-scheduler rejection, fixed `FeatureRequiresTransactionalOutbox` code, dedicated routing key/queue, leasing with `SKIP LOCKED`, publisher confirms, fencing, duplicate MessageId, dead-letter/redrive, cancellation, and unchanged unrelated consumer are all well-directed.

The failure path is not safe: “persist `RetryableFailed` then ACK” is only safe if the same transaction creates a durable retry delivery or a separately specified retry scheduler. The v4 document does neither. This is a potential permanent-loss path and blocks approval until V4-M-01 is resolved.

## 10. Immutability and relational-integrity verification

The reusable `prevent_f129_history_mutation()` trigger rejects `UPDATE`/`DELETE` for the listed source, revision, manifest-attempt, alias, snapshot, evidence, event, and policy history. Insert-only repositories and separated mutable pointer/lease/projection repositories are appropriate. Ordinary application bypass is prohibited; raw payload links are correctly described as cross-DbContext verified soft references with checksum failure evidence.

The design correctly selects ordinary FKs for single identities, composite alternate keys/FKs for scope identity, checks for bounded values, GiST exclusion for ranges, and deferred constraint triggers where PostgreSQL regular FKs cannot express pointer-selected-version equality. The migration task still needs the exact table inventory (V4-N-02), but no contradiction was found in the chosen enforcement model.

## 11. Semantic-validator verification

The local `MonthlyProductSemanticProposalValidator` choice is correct. It owns schema versioning, closed root/nested properties, slot/value kinds, one-of shapes, Jalali ranges, enums, limits, confidence, counts/lengths, duplicate conflict handling, UTF-16 spans, fixed rejection codes, fallback, and injection isolation. The proposed path through typed proposal, deterministic merge, canonical resolver, `QueryInterpretation`, `ValidatedQueryFrame`, task state, and shared V1/V2 executor prevents raw model values from reaching calculation.

The impact map names the required existing and new touchpoints while leaving shared provider adapters unchanged. Implementation tasks must preserve the existing root-only `AiStructuredOutputContract` behavior and put nested validation after model response receipt.

## 12. Monetary and unit-gate verification

`MonetaryUnitUnconfirmed` blocks public monetary output; shadow calculations are explicitly non-public; confirmation is an immutable policy version that schedules eligibility. `UnitConversionUnapproved` prevents unsafe physical conversion while preserving monetary contribution and suppressing quantity/rate comparison. AC-73–AC-75 and their policy tests are covered.

## 13. Formula and fixture verification

The earlier fixture arithmetic is independently consistent: base sales 450,000, current sales 570,150, change 120,150, and growth 26.7%. The previously verified product effects sum to 120,150: 91,881.6, −30,000, and 58,268.4; quantity 116,440.8 plus price 3,440.8 plus residual 268.4 also equals 120,150. The exact symbol and product spelling are preserved in v4.

However, v4 does not reproduce those input rows. The arithmetic can be verified only by consulting Design.md or an earlier review, so the v4 artifact itself cannot drive the implementation fixture. This is V4-M-03, not a mathematical defect.

## 14. Acceptance-criteria traceability audit

`AC-78` is correctly a document-readiness gate and not runtime Feature behavior. The following table audits every criterion individually.

| AC | Status | Review note |
| --- | --- | --- |
| AC-01 | Covered | Immutable repeated observations and totals are specified/tested. |
| AC-02 | Covered | Reordering/ordinal semantics are objective. |
| AC-03 | Covered | Replay receipt and no economic revision are defined. |
| AC-04 | Covered | Raw/normalized reconciliation blocks acceptance. |
| AC-05 | Covered | Manifest outcome matrix is explicit. |
| AC-06 | Covered | Type-0 readiness gate is explicit. |
| AC-07 | Covered | Optional late success creates a generation/request. |
| AC-08 | Covered | One pointer business key is unique. |
| AC-09 | PartiallyCovered | Composite consistency is selected, but the candidate ordering/revision comparator remains incomplete. |
| AC-10 | PartiallyCovered | Concurrent selection lacks a total order for same-revision different fingerprints. |
| AC-11 | PartiallyCovered | Stale behavior depends on the unspecified comparator. |
| AC-12 | Covered | Optional completion/readiness behavior is stated. |
| AC-13 | Covered | Corrections are immutable revisions/observations. |
| AC-14 | Covered | Revision precedence is stated. |
| AC-15 | Covered | Late/equal ambiguous payload handling is stated. |
| AC-16 | Covered | Drafts cannot enter calculation/snapshot. |
| AC-17 | Covered | Compilation includes unchanged and changed mappings. |
| AC-18 | Covered | Pointer uniqueness gives one current complete version. |
| AC-19 | Covered | Snapshot stores one immutable version ID. |
| AC-20 | Covered | Projection version equality is trigger-enforced. |
| AC-21 | Covered | Composite consistency and product scope are specified. |
| AC-22 | Covered | GiST overlap and `btree_gist` are specified. |
| AC-23 | Covered | Approval replacement and affected outbox are atomic. |
| AC-24 | Covered | Approval rollback scope is explicit. |
| AC-25 | Covered | Merge/split/reversal/retirement/reactivation preserve history. |
| AC-26 | Covered | Superseded versions cannot re-enter projection. |
| AC-27 | Covered | Historical snapshot version remains stable. |
| AC-28 | Covered | Type-0 reported revenue is authoritative. |
| AC-29 | Covered | Symmetric effects and residual are defined. |
| AC-30 | Covered | Lifecycle/unsafe paths allocate once. |
| AC-31 | Covered | Stored-scale reconciliation blocks publication. |
| AC-32 | Covered | Unit-incompatible physical decomposition is suppressed. |
| AC-33 | Covered | Precision, rounding, tolerance, and reasons are specified. |
| AC-34 | Covered | Cancellation guard is specified. |
| AC-35 | Covered | Public facts carry bounded copied evidence. |
| AC-36 | Covered | Evidence retains old immutable inputs. |
| AC-37 | Covered | Cross-context ID/checksum verification is specified. |
| AC-38 | Covered | History triggers reject UPDATE/DELETE. |
| AC-39 | Covered | Insert-only repository surface is required. |
| AC-40 | Covered | Historical Restrict deletion is normative. |
| AC-41 | Covered | Snapshot pointer composite scope trigger is specified. |
| AC-42 | Covered | Publication event/snapshot/type constraint is specified. |
| AC-43 | PartiallyCovered | Manifest composite scope is present, but stale selection ordering is incomplete. |
| AC-44 | Covered | Alias pointer/projection/member consistency is specified. |
| AC-45 | Covered | Approval/publication/outbox idempotency keys are unique. |
| AC-46 | Covered | Snapshot history is trigger-protected. |
| AC-47 | Covered | Current snapshot business-key uniqueness is specified. |
| AC-48 | Covered | Feature 129 declares outbox and direct scheduling rejects. |
| AC-49 | Covered | Fixed rejection code is named. |
| AC-50 | Covered | Ingestion/alias transaction coupling is stated. |
| AC-51 | Covered | Lease/SKIP LOCKED/token/expiry/xmin are stated. |
| AC-52 | Covered | Confirm and expired-row recovery are stated. |
| AC-53 | Covered | Crash-after-publish idempotency is stated. |
| AC-54 | Covered | Dedicated routing isolation is stated. |
| AC-55 | PartiallyCovered | ACK-after-persistence is stated, but persisted retry is not scheduled/delivered. |
| AC-56 | Covered | Duplicate job/snapshot idempotency is stated. |
| AC-57 | PartiallyCovered | Retry/dead-letter/recovery outcomes lack a durable retry owner. |
| AC-58 | Covered | Shared root contract remains unchanged. |
| AC-59 | Covered | Unknown nested properties are rejected locally. |
| AC-60 | Covered | Closed slots/value shapes/enums/counts are specified. |
| AC-61 | Covered | Period/limit/confidence/version validation is specified. |
| AC-62 | Covered | UTF-16 span handling is specified. |
| AC-63 | Covered | Merge precedence/conflict clarification is specified. |
| AC-64 | Covered | Canonical resolution and bounded ambiguity are specified. |
| AC-65 | Covered | Only validated values enter frame/state/executor. |
| AC-66 | Covered | V1/native/fallback parity is specified. |
| AC-67 | Covered | Payload v3 exact value/ID/evidence mapping is specified. |
| AC-68 | Covered | Semantic replay equality is specified. |
| AC-69 | Covered | Narrative and decoder replay behavior is specified. |
| AC-70 | Covered | Optional endpoint security/ETag behavior is conditional but implementable if enabled. |
| AC-71 | Covered | UI/Telegram use server values without arithmetic. |
| AC-72 | PartiallyCovered | Boundary/restartability are stated; optional history and operational limits are not objective. |
| AC-73 | Covered | Unconfirmed monetary unit blocks public output. |
| AC-74 | Covered | Shadow output is marked non-public. |
| AC-75 | Covered | Unapproved conversion suppresses physical comparison. |
| AC-76 | PartiallyCovered | Security/non-disclosure is objective enough, but performance targets are not quantified. |
| AC-77 | NotCovered | Exact names and totals exist, but the standalone normative fixture inputs are missing. |
| AC-78 | Covered | Readiness checks are correctly outside runtime behavior. |

Summary: 68 Covered, 9 PartiallyCovered, 1 NotCovered, 0 Contradictory. The partial/not-covered criteria include two MAJOR blockers and one bounded operational correction above.

## 15. Vertical-slice assessment

| Slice | Assessment |
| --- | --- |
| Slice 1 | Covered in scope: manifest pointer, dispatch guard, job/outbox, dedicated transport, and trigger foundation are present. Add the total manifest comparator. |
| Slice 2 | Covered: complete ownership approval and atomic projection/pointer replacement are present. |
| Slice 3 | PartiallyCovered: calculator, evidence, immutable publication, and monetary gate are present; retryable handler recovery must be made durable and the fixture must be embedded. |
| Slice 4 | Covered: local validator, typed resolution, V1/V2 parity, persistence, billing, and Telegram are present. |
| Slice 5 | Covered: UI is server-value driven with no client financial calculation. |
| Slice 6 | PartiallyCovered: dead-letter/redrive is required before dispatch, but retryable handler redelivery is not assigned to a concrete worker/scheduler. |

No required recovery mechanism is intentionally deferred, but one is missing from the design and must be assigned before dispatch enablement.

## 16. File-impact assessment

The four categories are present and substantially non-overlapping. They include dispatch contracts/guard/registration, ingestion rows/configuration/context, manifest pointer, complete alias rows, snapshot/event/pointer models, dedicated outbox/dispatcher/publisher/consumer/worker, migration and generated model snapshot, trigger SQL, local validator/schema/prompt, DI, `QueryInterpretation`, task state, V1/V2, conversation persistence, auth/audit, Telegram, frontend, and tests.

The intentionally unchanged category correctly retains `MetricRecalculationProcessor.cs`, shared provider adapters/root contract, unrelated direct-feature consumer behavior, and historical migrations. No prohibited file is listed for modification. The impact map should add the concrete durable retry scheduler/worker introduced by V4-M-01.

## 17. Remaining business decisions

The four listed decisions remain safely gated: provider monetary-unit confirmation, conversion dictionary approval, freshness/materiality thresholds, and future ServiceSales scope. None independently blocks decomposition once the findings above are corrected; monetary and conversion safe defaults are explicit.

## 18. Required changes and implementation cautions

Before re-review:

1. Define and test a durable retry owner. Persisting `RetryableFailed` followed by ACK must atomically create or expose a future delivery with backoff, lease, fencing, idempotency, and poison/dead-letter semantics.
2. Define a total manifest-generation comparator and stale-candidate algorithm for all same-revision/different-fingerprint and optional-outcome races.
3. Copy the complete `غاذر` fixture input/output table into v4 so AC-77 is standalone and task-ready.
4. Quantify AC-72/AC-76 v1 limits or remove their non-objective portions from runtime acceptance criteria.
5. Add exact history table names to the trigger migration inventory and distinguish publication confirmation from handler-result terminality.

## 19. Final verdict

The design has no alias-history contradiction and makes strong progress on the prior review findings, but V4-M-01 and V4-M-02 are architecture/task-decomposition blockers. V4-M-03 prevents the required standalone fixture acceptance test, and V4-M-04 should be corrected before implementation planning.

NEED_CHANGES
