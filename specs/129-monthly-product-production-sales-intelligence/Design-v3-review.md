# Feature 129 Design-v3 Review

## 1. Review metadata and scope

**Review role:** Independent Principal Software Architect, Financial Systems Reviewer, and adversarial design gate  
**Review date:** 2026-08-24  
**Reviewed design:** `specs/129-monthly-product-production-sales-intelligence/Design-v3.md`  
**Purpose:** Determine readiness for User Story and implementation-task decomposition  
**Review type:** Documentation and repository inspection only; no application code, migration, configuration, test, or production-data change is authorized.

The following documents were read completely:

- `Design.md`
- `Design-review.md`
- `Design-v2.md`
- `Design-v2-review.md`
- `Design-v3.md`
- repository `README.md` and governing `AGENTS.md`

The review does not accept a resolution because a matrix marks it `Resolved`. Each correction was checked against the proposed schema/workflow and current executable repository state.

## 2. Repository evidence inspected

### Ingestion, source facts, and migrations

- `NadpcoApiDataProviderClient.cs`, provider payload models, and the 1404 monthly-activity boundary.
- `ProviderRawPayloadPersistence.cs` and `FinancialProviderDbContext`: raw payloads live in a separate provider DbContext and are therefore immutable ID/checksum references, not Financial-ingestion database FKs.
- `NadpcoApiMonthlyActivityNormalizer.cs` and its tests: current line-code grouping/last-write-wins, replaceable children, fallback identity, output-type handling, and compatibility implications.
- `FinancialDataSyncProcessor.cs`: current flow saves the sync run, invokes the normalizer, marks completion, then directly publishes metric recalculation; there is no current Feature 129 ingestion transaction or manifest.
- `FinancialIngestionRows.cs`, `FinancialIngestionConfigurations.cs`, `FinancialIngestionDbContext.cs`, the current model snapshot, the initial ingestion migration, derived-feature foundation migration, recalculation-outbox migration, monthly backfill migration, and later monthly/report migrations.
- Existing schema evidence confirms company identity is provider-scoped, line-item uniqueness is currently `(MonthlyReportId, ProductCode)`, and no `btree_gist` extension/exclusion constraint currently exists.

### Feature job, RabbitMQ, and workers

- `DerivedFeatureContracts.cs`: current `FeatureRecalculationRequested` contains job, feature/version, optional external company, closed period, idempotency key, and request time.
- `FeatureComputationProcessor.cs`: `FeatureRecalculationScheduler.ScheduleAsync` persists the job and then calls `PublishRequestedAsync` directly; it contains no dispatch-mode or Feature 129 rejection guard.
- `PersistedFeatureServices.cs`, `FeatureComputationJobRow`, and its configuration: one unique job idempotency key exists, but no feature-computation outbox currently exists.
- `RabbitMqFeatureMessaging.cs`: the current feature consumer acknowledges a valid message **before** invoking the handler; publish uses `BasicPublishAsync` without a persisted publisher-confirm state machine.
- `RabbitMqConsumerAcknowledgement.cs` and `RabbitMqDataSyncMessaging.cs`: the data-sync consumer demonstrates the repository's handler-before-ACK pattern, while the feature bus currently does the opposite.
- `FeatureComputationConsumerWorker.cs`, Worker `Program.cs`, and dependency registration in `ServiceCollectionExtensions.cs`.
- Existing derived-feature persistence tests cover basic schedule/idempotency/completion, but not transactional outbox leasing, publisher confirms, crash-after-publish, or consumer redelivery.

### AI orchestration and semantic state

- `CanonicalQueryEntityContracts.cs`: current closed `QuerySlotType`, string-valued `ResolvedQuerySlot`, and required/optional slot validator.
- `CapabilityInterpretationGovernance.cs`: current `QueryInterpretationProposal` transports capability codes, missing slots, presentation, confidence, and evidence, but no slot values.
- `ConversationalCapabilityContracts.cs`: current `QueryInterpretation` has entity mentions, metrics, one period/comparison/presentation, and no typed arbitrary slot collection.
- `ConversationTaskStateContracts.cs`: current state persists a slot string, optional canonical entity ID, provenance, confidence, and origin; `ConversationDialogueGate` calls the synchronous registered `ICapabilityInterpreter`.
- `ServiceCollectionExtensions.cs`: the deterministic interpreter and no-op proposal provider are active; the hybrid interpreter exists but is not the gate's registered interpreter.
- `AiModelContracts.cs` and `AiModelProviderServices.cs`: `AiStructuredOutputContract` carries only a schema name plus required **root property names**; `JsonStructuredOutputValidator` validates only JSON object shape and presence of those roots. It cannot carry or enforce the nested JSON Schema written in Design-v3.
- V1/V2 workflow, semantic executor, response-consistency, billing, and facade mapping paths cited by the design.

### Conversation and frontend

- `ConversationContracts.cs`, `ConversationPersistenceModels.cs`, `ConversationRepositories.cs`, `MessagePersistenceFunction.cs`, `AiQueryOrchestrationService.cs`, and `AiFacadeController.cs`: assistant payloads are serialized objects; history deserializes an `AssistantMessagePayload`; raw response bytes are not retained.
- `chat.functions.ts`: current frontend transport is TypeScript-interface based and has no Feature 129 result/Zod discriminated response parser.
- `message-list.tsx`, monthly trend view-model/chart tests, and `TelegramAssistantResponseRenderer.cs` as the existing rendering touchpoints.

## 3. Overall assessment

Design-v3 materially improves the design. It preserves the original financial-correctness work, replaces mutable snapshot flags with an explicit pointer/event model, defines a strong typed slot-value flow, selects semantic replay equality, corrects the fixture, and separates Feature 129 request dispatch from the metric recalculation processor.

It is not ready for User Story/task decomposition. One blocker and four major findings remain:

1. Alias approval is internally inconsistent about whether an alias-set revision is a complete immutable set or a partial change set. The current workflow can leave multiple revisions active while a snapshot stores one revision ID.
2. Direct-publish/outbox mutual exclusion is a convention, not an enforceable runtime contract, and the current shared feature consumer ACKs before handler persistence.
3. Append-only manifest generations have no concrete current-generation pointer/selection protocol.
4. Historical immutability and several cross-table integrity rules are asserted/tested without selecting an enforceable database/application mechanism.
5. The full nested semantic JSON Schema cannot flow through the repository's current structured-output contract, and the design does not choose local-only validation versus a shared model-provider contract extension.

These gaps require architectural decisions and affect ACs/slice boundaries. The verdict is therefore `NEED_CHANGES`.

## 4. Audit of original findings

| Original finding | Status in Design-v3 | Audit |
| --- | --- | --- |
| B-01 | Covered | Immutable observations, fact fingerprints, duplicate occurrences, and exact raw/normalized counts preserve repeated product-code rows. |
| B-02 | Covered | The ordered seven-bucket state machine allocates each contribution exactly once. |
| B-03 | Covered | Evidence references immutable observations and copies the exact values required for replay. |
| B-04 | Covered | Immutable revisions, deterministic provider revision/publication precedence, ambiguity events, advisory lock, and accepted pointer reject late older payloads. |
| M-01 | PartiallyCovered | Operation outcomes/type-0 barrier are defined, but the current manifest generation is not selected by a concrete pointer/lock contract (V3-M-02). |
| M-02 | PartiallyCovered | The derived-feature framework and specialized handler are selected, but request-path mutual exclusion and consumer ACK behavior remain unresolved (V3-M-01). |
| M-03 | PartiallyCovered | Economic compatibility and current GiST ownership are strong, but the revision/projection lifecycle contradiction prevents one reproducible current alias set (V3-B-01). |
| M-04 | NotCovered | Immutable alias history exists, but a snapshot's singular alias revision cannot reproduce a current projection assembled from multiple revisions (V3-B-01). |
| M-05 | PartiallyCovered | Pointer/event publication is conceptually correct; immutable-row enforcement and relational integrity remain under-specified (V3-M-03). |
| M-06 | Covered | Lifecycle, match, sign, quality, availability, aligned effects, and cancellation guard remain separate and deterministic. |
| M-07 | PartiallyCovered | Typed values now have an end-to-end conceptual flow; repository structured-output integration remains undecided (V3-M-04). |
| M-08 | Covered | Payload v3, semantic replay, bounded evidence, Telegram fallback, and server-supplied contribution transport are specified. |
| M-09 | Covered | Revisions/identity precede publication; core reconciliation is in Slice 3; 1404/01 is the boundary. |
| M-10 | PartiallyCovered | 69 mapped ACs exist, but AC-64 is a design-task condition rather than a Feature 129 acceptance criterion (V3-N-02). |
| N-01 | Covered | Decimal types, ToEven rounding, balancing residual, canonical serialization, and denominator reasons are explicit. |
| T-01 | Covered | Correctness is separated from safely optional history/visual/direct-endpoint work. |

## 5. Audit of V2-M-01 through V2-M-07

| Finding | Status | Review conclusion |
| --- | --- | --- |
| V2-M-01 | NotCovered | The exclusion constraint is feasible on current ownership, but partial replacement, whole-revision supersession, AC-19, and singular snapshot revision conflict. See V3-B-01. |
| V2-M-02 | PartiallyCovered | Snapshot flags/status mutations are removed and the pointer key/lock/`xmin` flow is strong; append-only enforcement and pointer/event composite integrity need a selected mechanism. See V3-M-03. |
| V2-M-03 | PartiallyCovered | DTO/value kinds/provenance/evidence/merge/state/executor isolation are substantively defined; actual nested-schema transport/validation ownership is unresolved. See V3-M-04. |
| V2-M-04 | NotCovered | The desired outbox state machine is detailed, but no runtime guard prevents the direct scheduler path and current consumer ACK order contradicts recovery semantics. See V3-M-01. |
| V2-M-05 | Covered | Replay is explicitly semantic, exact for financial values/immutable IDs, order-aware, and excludes serializer formatting. Persisted narrative is replayed exactly. |
| V2-M-06 | PartiallyCovered | Four categories are present and MetricRecalculationProcessor is correctly unchanged; required migration/schema/transport touchpoints are omitted or ambiguous. See V3-N-01. |
| V2-M-07 | Covered | `غاذر` and `سبزیجات ۴۰ گرمی` are correct; wrong size/order/ZWNJ variants are absent from Design-v3. |

## 6. New findings by severity

| ID | Severity | Section | Finding | Repository/design evidence | Required change | Affected ACs/slices |
| --- | --- | --- | --- | --- | --- | --- |
| V3-B-01 | BLOCKER | §§11.1–11.3, 21 | Alias revision scope contradicts partial current-projection replacement. Approval deletes only affected rows and may split/retain unaffected fragments from the old revision, while it appends a whole-revision `Superseded` event and states superseded revision memberships cannot remain current. Each snapshot nevertheless stores one `ApprovedAliasSetRevisionId`. A partial update can therefore make current ownership a composition of multiple revisions that no single snapshot revision reproduces. | Design-v3 §11.3 step 5 retains unaffected fragments; the same section says prior decisions are superseded and superseded memberships do not remain. §§20–21 fingerprint one alias-set revision. AC-19 requires no superseded membership in current ownership. | Choose one coherent version model. Either make each approved alias-set revision a complete company ownership set and atomically rebuild all current rows from it, or add an immutable composed `CompanyProductAliasOwnershipVersion` containing the complete membership set and have snapshots reference that version/set. Define partial supersession semantics, projection-row revision/version FKs, merge/split/reversal behavior, fingerprinting, and ACs accordingly. | AC-16–22; Slices 2–3 |
| V3-M-01 | MAJOR | §§17–18, 35 | Feature 129's exclusive outbox path is not enforceable, and consumer acknowledgement semantics are not reconciled with the existing bus. The design leaves `FeatureRecalculationScheduler.ScheduleAsync` unchanged, so any caller can still submit the Feature 129 code and directly publish. It also requires ACK after handler persistence, whereas the current `RabbitMqFeatureBus` ACKs before invoking the handler. Changing the shared bus would alter unrelated features, but a dedicated consumer/queue is not selected. | Current scheduler persists then publishes without feature dispatch policy. Current feature bus calls `TryAckAsync` before `handler`. Design-v3 relies on tests/convention and lists the scheduler implementation as intentionally unchanged. | Add an enforceable dispatch-mode policy checked by the scheduler/publisher (Feature 129 must be rejected from direct scheduling), or isolate Feature 129 behind a distinct request contract/queue inaccessible to the direct scheduler. Explicitly choose whether the shared feature bus changes to handler-before-ACK or a dedicated Feature 129 consumer is added; define NACK/requeue/dead-letter behavior and impacts for unrelated features. | AC-44–48; Slices 1, 3, 6 |
| V3-M-02 | MAJOR | §§9, 19–20 | Append-only manifest generations have no concrete accepted/current-generation projection. The design uses “the manifest” for stale-input revalidation and job fingerprints, but does not define how concurrent/retry generations are selected, locked, superseded, or prevented from both becoming core-ready/current. | Repository has only mutable sync runs and an “any report exists” completion check; there is no manifest precedent. Design-v3 defines manifest uniqueness by generation but no pointer table, unique current constraint, `xmin`, advisory key, or deterministic winner. | Define a `CompanyMonthIngestionManifestCurrentPointer` (or an equally concrete deterministic accepted-generation model) with complete provider/company/period key, generation FK, `xmin`, lock/transaction order, idempotent retry, stale writer behavior, and job/outbox coupling. Fingerprints and publication revalidation must use its exact generation ID. | AC-05–07, AC-36–48; Slices 1, 3 |
| V3-M-03 | MAJOR | §§11, 19–22, 32 | Append-only and cross-table integrity are not enforceably specified. AC-36 promises immutable snapshot/events/evidence, and testing mentions “database triggers/change tracking,” but the design does not select DB triggers, EF interception, restricted repositories/permissions, or another enforcement contract. It also does not fully constrain pointer publication event ownership/type or current-alias membership/company/product/range consistency, and delete behaviors are mostly absent. | EF Core entity/configuration conventions alone allow updates. `PublicationEventId` is described as “optional/required-on-published” even though a current pointer is inherently published. A simple FK cannot prove the event belongs to `CurrentSnapshotId` and has type `Published`. Alias `(RevisionId, MembershipId)` does not by itself prove denormalized provider/company/alias/range/product fields match the membership. | Select and document immutable-row enforcement. Define PKs, composite alternate keys/FKs, `Restrict` delete behavior, event idempotency uniqueness, pointer `(PublicationEventId, CurrentSnapshotId)` integrity and published-event validation, alias projection composite consistency, evidence retention, and raw cross-DbContext soft-reference rules. Specify which constraints require migration SQL/triggers versus EF configuration/application validation. | AC-16–22, AC-33–43; Slices 1–3 |
| V3-M-04 | MAJOR | §§25–27, 35 | The nested semantic JSON Schema is not connected to the current structured-output infrastructure. `AiStructuredOutputContract` supports only required root property names and its validator checks only those roots. Design-v3 does not decide whether the full schema is local post-response validation or a new provider-facing schema contract, and the impact map omits the shared model contract/validator/adapters if they must change. | Current `AiStructuredOutputContract(string SchemaName, IReadOnlyCollection<string> RequiredRootProperties)` and `JsonStructuredOutputValidator` cannot represent `additionalProperties:false`, `oneOf`, nested period properties, enum closure, sizes, or evidence-span bounds. | Explicitly choose: (a) keep provider contract root-only and define a versioned local strict parser/validator as the authoritative security boundary, or (b) extend the common structured-output contract and all applicable provider adapters/validators to carry JSON Schema. Define schema versioning, normalization index units for evidence spans, error/fallback behavior, and update the impact map/tests. | AC-49–56; Slice 4 |
| V3-N-01 | MINOR | §35 | The four impact categories are structurally separate and MetricRecalculationProcessor is correctly placed, but implementation touchpoints are incomplete/ambiguous. | A reviewed implementation requires a new EF migration and updates the existing Financial-ingestion model snapshot. V3-M-01 may require scheduler/consumer acknowledgement changes. V3-M-04 may require `AiModelContracts.cs`, `AiModelProviderServices.cs`, and provider adapters. Authorization/audit touchpoints for alias/revision administration are not named. | Update the map after resolving V3-M-01/V3-M-04. Add proposed migration/model-snapshot impact, exact dispatcher/job/outbox repository/configuration paths, semantic schema validator ownership, acknowledgement/consumer path, and authorization/audit files. Keep existing historical migrations unchanged. | AC-64; Slices 1–4, 6 |
| V3-N-02 | MINOR | §31 | AC-64 mixes a one-time design-authoring verification (“only documentation is changed by this task”) into Feature 129 implementation acceptance. It becomes false as soon as implementation work begins and cannot be assigned to a feature implementation slice. | The criterion is mapped to Slices 1–6 even though those slices necessarily modify code/schema. | Move the documentation-only diff check to the design review checklist. Retain the impact-category and AC/test/slice trace checks as design-quality gates, not runtime feature ACs. | AC-64; traceability only |
| V3-T-01 | NOTE | §§16, 34 | The four business decisions can remain open only while the stated safe defaults are enforced as explicit publication gates. | “No conversion” is safe; ServiceSales is excluded; v1 thresholds exist. Monetary-unit confirmation is described as pre-public enablement but not named as a fixed blocking reason code/AC. | During the next revision, add fixed `MonetaryUnitUnconfirmed` and unapproved-conversion publication outcomes and map them to an AC/test; business choices themselves need not block architecture review afterward. | AC-29, AC-69; Slices 2–4 |

Finding counts: **1 BLOCKER, 4 MAJOR, 2 MINOR, 1 NOTE**.

## 7. Formula and fixture verification

The fixture was recalculated independently using decimal arithmetic.

| Product | Quantity effect | Price effect | Residual | Contribution | Verified |
| --- | ---: | ---: | ---: | ---: | --- |
| سبزیجات ۴۰ گرمی | `(1966-1000)×(100+97.6)/2 = 95,440.8` | `(97.6-100)×(1000+1966)/2 = -3,559.2` | `0` | `91,881.6` | Yes |
| کنسرو مخلوط | `(1700-2000)×100 = -30,000` | `0` | `0` | `-30,000` | Yes |
| غذای آماده صادراتی | `(2000-1500)×102 = 51,000` | `(104-100)×1750 = 7,000` | `268.4` | `58,268.4` | Yes |

```text
Base total       = 450,000
Current total    = 570,150
Company change   = 120,150
Effect total     = 116,440.8 + 3,440.8 + 268.4 = 120,150
Growth           = 120,150 / 450,000 × 100 = 26.7%
```

The seven-bucket equation, authoritative reported revenue, balancing residual, unit-unsafe treatment, negative preservation, cancellation guard, `numeric(28,8)`, and ToEven rules are mathematically coherent.

The symbol is exactly `غاذر`. The product is exactly `سبزیجات ۴۰ گرمی`. Design-v3 contains neither malformed `غذا‌ر` nor `سبزیجات ۴۰۰ گرمی`, and contains no hidden ZWNJ character.

## 8. Persistence and concurrency verification

| Model/area | Assessment | Verification |
| --- | --- | --- |
| Source observations | Feasible | GUID PK, revision FK, unique revision/discriminator, exact decimals, immutable values, and raw soft references are conceptually sufficient. |
| Report revisions/accepted pointer | Feasible | Logical-key uniqueness, revision checksum uniqueness, advisory lock, `FOR UPDATE`, `xmin`, deterministic precedence, and retry are coherent. Explicit `Restrict` delete behavior should be retained in implementation. |
| Manifest generations | Incomplete | Generation rows are append-only, but current/accepted selection is missing (V3-M-02). |
| Alias revisions/memberships/events | Contradictory | Immutable tables are appropriate, but full-set versus change-set semantics are unresolved (V3-B-01). |
| Current alias ownership | SQL constraint feasible, aggregate lifecycle not feasible as written | With `btree_gist`, equality on bounded text plus overlap on nonempty canonical `int4range` is implementable. Projection PK and `xmin` are specified. Composite consistency and supersession composition need correction. |
| Job/outbox | Feasible after path correction | Unique job/outbox keys, lease token, `FOR UPDATE SKIP LOCKED`, expiry, confirms, and at-least-once duplicate handling are feasible in PostgreSQL/RabbitMQ. Direct-path and ACK issues remain V3-M-01. |
| Snapshot facts/items/evidence/events | Conceptually correct, enforcement incomplete | No mutable current/status field remains. Precision, filtered item uniqueness, immutable evidence, and event history are sound. Enforcement/delete/FK details remain V3-M-03. |
| Current snapshot pointer | Mostly feasible | Business key, snapshot FK, advisory key, `xmin`, stale revalidation, and retries are defined. Provider/canonical-company scope must be made explicit; event-to-snapshot/type integrity and requiredness need constraints. |
| Conversation payload | Feasible | JSON object persistence and typed decoding support semantic equality; payload size/evidence bounds remain necessary. |

PostgreSQL notes:

- The proposed exclusion expression is valid only after migration SQL creates `btree_gist`; EF Core does not natively express all exclusion/conditional event constraints, so raw migration SQL is expected.
- `xmin` is feasible through Npgsql concurrency-token mapping, but every mutable pointer/projection update must use tracked original values or explicit `WHERE xmin = ...` semantics.
- `FOR UPDATE SKIP LOCKED` leasing is feasible, but the lease claim must commit before broker I/O and every confirm update must fence on lease token plus `xmin`.
- Cross-DbContext raw payload references cannot be enforced by an ordinary FK unless the databases/schema ownership are consolidated; the design should explicitly call them immutable soft references verified by ID/checksum.

## 9. Semantic-routing verification

The proposed conceptual path is complete:

```text
message → typed proposal → strict validation → deterministic merge/normalization
→ company resolution → company/period-scoped product resolution
→ conflict/confidence governance → ValidatedQueryFrame
→ validated task state → shared V1/V2 executor
```

Design-v3 defines closed slot names/value kinds, raw/normalized values, canonical IDs, Jalali period, comparison, focus, measure, presentation, integer limit, confidence, provenance, evidence span, resolver/validation states, and rejection reasons. It defines explicit-current-turn precedence, semantic-equality merge, ambiguity clarification, canonical ID replacement, validated state carryover, shared V1/V2 consumption, and executor isolation. It does not require a rigid Persian word order; deterministic parsing remains a safe fallback while model proposals can cover paraphrases.

Security posture is sound at the conceptual boundary: raw model strings do not become routes, SQL, formulas, or calculator inputs; registry membership, authorization, entity, period, enum, and limit validation occurs after output.

The remaining issue is implementation ownership. The repository's current structured-output contract cannot transport the nested schema. Until V3-M-04 chooses provider-facing schema extension or a versioned local strict parser, AC-49 cannot be decomposed without a new architecture decision.

## 10. Job/outbox verification

The proposed outbox row states, job/outbox idempotency keys, lease fields, `SKIP LOCKED`, persistent message ID, confirm fencing, expired-lease recovery, crash-after-publish duplicate, consumer idempotency, retry/dead-letter, and authorized recovery are individually sound.

The repository gap is concrete:

```text
Current scheduler: StoreAsync(job) → PublishRequestedAsync(message)
Current feature consumer: deserialize → ACK → handler
Required Feature 129: DB job+outbox commit → leased publish/confirm → handler persistence → ACK
```

Simply “not calling” the scheduler does not make dual publication impossible. The selected feature code needs an enforceable dispatch-mode guard. Likewise, changing ACK order in the shared bus may affect every current derived feature; the design must deliberately select a shared change with regression tests or a dedicated Feature 129 transport/consumer.

## 11. Conversation-replay verification

The replay contract is substantively correct:

- result schema version is included;
- decimals compare numerically and exactly;
- snapshot, report revision, observation, alias membership/revision, unit policy, and calculation policy IDs compare exactly;
- enums, reasons, units, periods, warnings, evidence, and ordered products/effects/Other members compare exactly;
- deterministic Persian narrative is persisted and replayed exactly;
- whitespace, object-property order, serializer metadata, and equivalent Unicode escaping are excluded.

History reads the embedded result rather than the current pointer/current alias projection. The proposed tests cover report correction, merge/split/reversal, policy update, current-pointer replacement, compatible serializer changes, v1/v2 decoding, and unknown future result kinds. V2-M-05 is resolved.

Implementation caution: the semantic comparator should compare every discriminated result version explicitly and must never normalize immutable IDs or reorder collections as a convenience.

## 12. Acceptance-criteria traceability

All **69** criteria were audited. Every row currently names a test and slice, but naming alone does not make a criterion covered.

| AC range/item | Status | Design mechanism | Test | Slice | Finding |
| --- | --- | --- | --- | --- | --- | --- |
| AC-01–04 | Covered | Immutable observations, semantic replay receipt, exact raw reconciliation | T-ING-01–04 | 1 | — |
| AC-05–07 | PartiallyCovered | Operation manifest/type-0 gate exists; accepted/current generation selection does not | T-MAN-01–03 | 1 | V3-M-02 |
| AC-08–13 | Covered | Immutable revisions/events, deterministic precedence, lock/`xmin`, audited manual decision | T-REV-01–06 | 1 | — |
| AC-14–15 | Covered | Missing/zero ID and economic-signature compatibility/collision handling | T-ID-01–02 | 2 | — |
| AC-16 | Covered | Immutable revision/membership history can reproduce a revision | T-ALIAS-01 | 2 | — |
| AC-17–22 | Contradictory | Current projection/GiST exists, but partial replacement and singular snapshot revision conflict | T-ALIAS-02–07 | 2–3 | V3-B-01, V3-M-03 |
| AC-23–32 | Covered | Type-0 totals, union, symmetric calculation, seven buckets, rounding, cancellation guard | T-CALC-01–07, T-DEC-01–02, T-CLASS-01 | 3 | — |
| AC-33–35 | Covered | Bounded copied immutable evidence and prohibition on mutable source projections | T-EVID-01–03 | 1, 3 | — |
| AC-36–43 | PartiallyCovered | Immutable snapshot/event plus pointer protocol is sound; enforcement/composite integrity is incomplete | T-PUB-01–08 | 3–4 | V3-M-03 |
| AC-44 | PartiallyCovered | Atomic job/outbox writer is designed; manifest current generation is unresolved | T-ORCH-01 | 1–2 | V3-M-02 |
| AC-45 | NotCovered | No runtime scheduler guard prevents direct Feature 129 publication | T-ORCH-02 | 1 | V3-M-01 |
| AC-46–48 | PartiallyCovered | Lease/confirm/duplicate/dead-letter design is good; current ACK-before-handler path and shared-impact choice are unresolved | T-ORCH-03–07 | 1, 3, 6 | V3-M-01 |
| AC-49 | PartiallyCovered | Written JSON Schema exists but current structured-output contract cannot carry/enforce it | T-SEM-01 | 4 | V3-M-04 |
| AC-50–56 | Covered | Typed values, precedence, canonical replacement, clarification, task state, V1/V2 frame, injection isolation | T-SEM-02–08 | 4 | — |
| AC-57–60 | Covered | Payload v3 and semantic equality/exact narrative/backward decoding | T-CONV-01–05 | 4 | — |
| AC-61–62 | Covered as optional | Exact-revision history rules and optional direct endpoint contract | T-HIST-01–03, T-API-03 | 6 | — |
| AC-63 | Covered | Exact Unicode fixture scan | T-FIX-01 | 1–6 | — |
| AC-64 | Contradictory | Documentation-only diff condition cannot be a feature implementation AC across code-changing slices | T-TRACE-01 | 1–6 | V3-N-02 |
| AC-65–69 | Covered with caution | 1404 backfill, typed transport, Telegram, server-owned UI, security/performance | T-BF-01–04, T-API-01–02, T-UI-01–02, T-SEC-01, T-PERF-01 | 4–6 | V3-T-01 for unit gate |

Summary: 45 criteria are fully covered, 16 are partially covered, and 8 are contradictory/not covered because of the findings above. These totals cover all 69 criteria; status aggregation treats each individual AC, not each table row.

## 13. Vertical-slice assessment

| Slice | Assessment | Dependency/required correction |
| --- | --- | --- |
| 1 — Source/revisions/manifest/durable request | Not implementable as written | Add accepted/current manifest-generation protocol and enforce Feature 129 dispatch mode; choose consumer ACK/transport scope. |
| 2 — Canonical product/alias/current ownership | Blocked | Resolve complete alias-set versus partial change-set/composed ownership version before schema/tasks. |
| 3 — Calculator/immutable publication | Conditional | Financial calculator is sound, but it depends on corrected alias version and manifest pointer; select immutability/composite constraint enforcement. |
| 4 — Semantic/API/conversation | Conditional | Replay and shared-frame design are sound; choose nested-schema validation ownership and update shared contracts/impact map. Billing/Telegram remain in the correct slice. |
| 5 — Investor web experience | Feasible after Slice 4 | Server supplies all financial values/start/end/Other; client performs layout only. No financial recomputation is introduced. |
| 6 — History/operations | Feasible after prior corrections | YoY/YTD/history/endpoint are safely deferred. Dead-letter recovery is operationally necessary for the core outbox and should not be deferred if production dispatch is enabled earlier. |

Slice ordering remains conceptually correct: source and durable request precede identity; identity precedes publication; publication precedes semantic exposure; UI follows structured transport. The blocked items are missing contracts inside early slices, not a need to reorder the six slices.

## 14. File-impact assessment

The four categories are present and mostly non-overlapping. `MetricRecalculationProcessor.cs` appears in the impact map only under **existing files inspected but intentionally unchanged**, which is correct.

Verified included touchpoints:

- ingestion provider/normalizer/sync contracts and processor;
- Financial ingestion rows/configuration/DbContext;
- feature models/contracts/processor/repository/RabbitMQ/worker registration;
- semantic proposal/interpretation/slot/task-state/executor and V1/V2 mappings;
- conversation contracts/repository/facade;
- Telegram and frontend transport/rendering.

Required corrections:

- Add the future EF migration and generated `FinancialIngestionDbContextModelSnapshot` impact; historical migrations remain unchanged.
- Name the exact job/outbox repository/configuration/dispatcher files after final schema selection.
- If the scheduler receives a dispatch-mode guard, do not describe its implementation as intentionally unchanged.
- If shared nested JSON Schema is chosen, add `AiModelContracts.cs`, `AiModelProviderServices.cs`, provider adapters, and their tests. If local strict parsing is chosen, name that validator and state common adapters remain unchanged.
- If shared feature ACK order changes, record the regression impact on `RabbitMqFeatureMessaging.cs` and all feature consumers/tests; if dedicated, add its new transport/worker files.
- Name authorization/audit touchpoints for alias/revision decisions and dead-letter recovery.

## 15. Remaining business decisions

| Decision | May remain open? | Safe default/gate |
| --- | --- | --- |
| NADPCO monetary unit confirmation | Yes, before public monetary output | Block public monetary publication/response with a fixed reason until tenant/provider unit is confirmed. Repository convention alone is not sufficient. |
| Initial unit-conversion dictionary | Yes | No conversions. Monetary contribution remains; quantity/rate comparison is suppressed. |
| Freshness/materiality thresholds | Yes | Use immutable policy v1 defaults; a later choice creates a new policy revision and recalculation. |
| Future ServiceSales scope | Yes | Exclude it from Feature 129; a future service feature/policy is separate. |

None requires a core architectural change once the blocking reason for unconfirmed monetary units is made normative.

## 16. Required changes and implementation cautions

Required before User Story/task decomposition:

1. Resolve alias revision granularity and make one immutable ownership version/set reproduce every current and historical mapping.
2. Add a concrete accepted/current manifest-generation pointer or deterministic equivalent.
3. Enforce Feature 129 dispatch mode at the direct scheduler boundary and choose shared versus dedicated handler-before-ACK consumption.
4. Select immutable-row enforcement and complete PK/FK/alternate-key/delete/retention constraints for alias, snapshot, event, pointer, and evidence tables.
5. Choose local strict semantic proposal validation or extend the shared structured-output schema contract; update impact and tests.
6. Correct the impact map and remove the documentation-only clause from Feature AC-64.

Implementation cautions after those changes:

- Create `btree_gist` in the reviewed migration before adding the exclusion constraint.
- Keep provider raw payload links as explicit soft references unless both contexts share an enforceable schema/database FK.
- Fence every outbox confirm/update with lease token and `xmin`; never hold a database transaction open during broker I/O.
- Use composite constraints or triggers for pointer-event and projection-membership consistency where ordinary single-column FKs are insufficient.
- Preserve exact decimal values and ordered collections in the semantic comparator; never compare serialized bytes.
- Do not enable public monetary results until the provider-unit gate passes.

## 17. Final verdict

Design-v3 retains the correct financial formulas and most historical/semantic improvements, but the alias-version blocker and four major persistence/orchestration/schema gaps prevent safe decomposition into implementation tasks. A further complete design revision is required.

NEED_CHANGES
