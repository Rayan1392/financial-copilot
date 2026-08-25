# Feature 129 — Design-v6 Final Review

## 1. Review metadata

**Document:** `specs/129-monthly-product-production-sales-intelligence/Design-v6.md`  
**Date:** 2026-08-25  
**Mode:** independent review-only design gate  
**Verdict:** `NEED_CHANGES`

I read Design-v6 completely, used the prior design/review chain as regression evidence, and inspected the repository ingestion, provider, sync, derived-feature, RabbitMQ, worker, semantic, conversation, Telegram, frontend, registration, migration, and test areas referenced by the documents. Repository behavior is authoritative.

## 2. Executive verdict

V6 improves materially over v5: it restores the major architecture sections, separates mutable Job/Outbox projections from immutable orchestration history, defines a semantic manifest fingerprint, includes the complete Persian fixture, and contains 78 individual AC rows.

It is not implementation-ready. There are 3 blockers, 6 majors, 2 minors, and 2 notes. The blocking defects are an undefined Outbox cancellation state, a broken AC-78 section reference, and missing individually defined named tests. Major defects remain in the malformed resolution matrix, PostgreSQL schema detail, total Outbox state machine, direct scheduler guard/impact map, frontend paths, and semantic integration mapping.

## 3. Files and repository areas inspected

Specifications: `Design.md`, `Design-review.md`, `Design-v2.md`, `Design-v2-review.md`, `Design-v3.md`, `Design-v3-review.md`, `Design-v4.md`, `Design-v4-review.md`, `Design-v5.md`, `Design-v5-review.md`, and `Design-v6.md`.

Repository areas: `NadpcoApiDataProviderClient.cs`, `NadpcoApiMonthlyActivityNormalizer.cs`, `FinancialDataSyncProcessor.cs`, `FeatureComputationProcessor.cs`, `RabbitMqFeatureMessaging.cs`, `FeatureComputationConsumerWorker.cs`, `ServiceCollectionExtensions.cs`, `AiModelContracts.cs`, `ConversationContracts.cs`, `ConversationRepositories.cs`, `MessagePersistenceFunction.cs`, `TelegramAssistantResponseRenderer.cs`, frontend chat/message files, EF configurations/migrations, MAF V2 workflow files, and relevant unit/integration/frontend tests.

## 4. Structural validation

| Check | Result |
| --- | --- |
| AC rows | 78 |
| Identifiers | AC-01 through AC-78, each once |
| Missing/duplicates | None |
| AC range in identifier column | None |
| R1-Core | 74 |
| R2-History | 3 |
| Optional-Endpoint | 1 |
| Valid section references | 77; AC-78 references nonexistent `§43` |
| Individually defined test cases | Incomplete; §27 mostly defines ranges |
| Final document section | `§34`; no `§43` |

The row count and gate counts pass mechanically, but several rows are not independently executable because their states, tests, sections, or persistence contracts are undefined.

## 5. Finding summary

| Severity | Count |
| --- | ---: |
| BLOCKER | 3 |
| MAJOR | 6 |
| MINOR | 2 |
| NOTE | 2 |

## 6. Required changes

1. Define Outbox cancellation, event, transition, dispatcher predicate, consumer action, and broker behavior, or remove Outbox cancellation from AC-59.
2. Correct AC-78 to the actual design-gate section and define `T-TRACE-01`.
3. Define every referenced test ID individually; specifically define `T-ORCH-13`, `T-ORCH-14`, and all fixture IDs.
4. Repair every resolution-matrix row to contain all seven columns.
5. Replace the persistence inventory with exact PostgreSQL columns/types/nullability/keys/FKs/index predicates/`xmin`/trigger/delete contracts.
6. Name the actual `FeatureRecalculationScheduler.ScheduleAsync` guard and correct the frontend paths.
7. Add a concrete semantic field-flow map through the existing MAF V2, task-state, payload, and conversation-deserialization paths.

## 7. Detailed findings

### V6-B-01 — Outbox cancellation state is undefined

**Severity:** `BLOCKER`  
**Location:** §17.2–§17.3, AC-59, T-ORCH-12  
**Related:** AC-59, T-ORCH-12, Slice 6

The closed Outbox states are `Pending`, `Leased`, `PublishedAwaitingConfirm`, `Confirmed`, `DeliveryConsumed`, `RetryablePublishFailure`, `PermanentlyFailed`, and `DeadLettered`. Neither the list nor the transition table contains `Cancelled`. AC-59 nevertheless requires both Job and Outbox to transition to a cancellation state. A queued, leased, published, or confirmed delivery requires different cancellation behavior; without a defined state/action a cancelled message may still dispatch or be ACKed inconsistently.

**Correction:** add `Cancelled` to the Outbox contract, event/history, transition table, dispatcher predicate, consumer behavior, and broker action, with allowed cancellation points and race tests; or change AC-59 to an explicit Job-only contract.

### V6-B-02 — AC-78 references nonexistent §43

**Severity:** `BLOCKER`  
**Location:** AC-78, line 503; v6 ends at §34  
**Related:** AC-78, T-TRACE-01

AC-78 cites `§43`, but there is no section 43. This violates the mandatory valid-section-reference rule and makes the design gate itself untestable.

**Correction:** reference the actual readiness section and define the exact `T-TRACE-01` inputs, checks, and result.

### V6-B-03 — Named tests are mostly ranges, not test definitions

**Severity:** `BLOCKER`  
**Location:** §27 line 407, resolution matrix §33, all AC test cells  
**Related:** all range-mapped ACs; especially AC-55–AC-59 and AC-77

§27 says `T-ORCH-01 to T-ORCH-14`, `T-CALC-01 to T-CALC-12`, and `T-FIX-01 to T-FIX-12`, but does not define each case. `T-ORCH-13` and `T-ORCH-14` are referenced by the resolution matrix but have no individual name/assertion. AC-77 maps twelve IDs but only says “T-FIX assertions.” A task author must invent the test boundaries and proof obligations.

**Correction:** define every test ID individually with setup, operation, expected persisted state/output, and test level; mechanically ensure every AC token resolves to one test definition.

### V6-M-01 — Resolution matrix rows have missing Status cells

**Severity:** `MAJOR`  
**Location:** §33 lines 579–589  
**Related:** V5-M-01 through V5-M-08, AC-40–AC-59

The header has seven columns. Rows V5-M-01, V5-M-05, V5-M-06, V5-M-07, and V5-M-08 have only six data cells; `Status` is omitted and `Resolved` shifts into the Slice column. The matrix cannot be consumed as reliable finding-to-section/AC/test/slice/status traceability.

**Correction:** give every row all seven cells and replace range-only test references with exact IDs.

### V6-M-02 — Persistence contract is not migration-ready

**Severity:** `MAJOR`  
**Location:** §20 lines 224–263  
**Related:** AC-08, AC-09, AC-17, AC-19, AC-40–AC-47; T-IMM-*, T-PUB-*

Most rows say only “ID, scope, version,” “event fields,” or “product fields.” They omit PostgreSQL type, nullability, exact PK/business/alternate keys, exact index predicates, exact composite FK pairs, range/exclusion expressions, and per-table `xmin` mappings. The filtered uniqueness for matched/unmatched facts, range column type, deferred trigger predicates, and bounded update predicates are not defined. This is still a descriptive inventory rather than an implementation-ready schema.

**Correction:** provide exact per-table column/type/nullability/key/FK/index/delete/mutability/`xmin`/trigger tables, including `EXCLUDE USING gist`, partial uniqueness, alternate-key pairs, and deferred trigger predicates.

### V6-M-03 — Outbox state machine is not total

**Severity:** `MAJOR`  
**Location:** §17.2–§17.3  
**Related:** AC-50–AC-59; T-ORCH-03–T-ORCH-14

`PermanentlyFailed` is listed as an Outbox state but has no transition row. Cancellation is also missing. The prose says permanent failure ACKs after terminal evidence but does not say whether Outbox becomes `PermanentlyFailed` or `DeadLettered`, which row is authoritative, or which future deliveries are forbidden. Lease-expiry versus consumer-delivery races are not given exact outcomes.

**Correction:** provide a total permitted/forbidden transition table with authority row, event, broker action, `xmin`/lease guard, and idempotency behavior for every state.

### V6-M-04 — Actual direct scheduler is omitted from the guard impact

**Severity:** `MAJOR`  
**Location:** §4, §16, §30.1; repository `src/backend/FinancialCopilot.Infrastructure/Financial/Features/FeatureComputationProcessor.cs:7–29`  
**Related:** AC-48, T-ORCH-01, Slice 1

The repository’s `FeatureRecalculationScheduler.ScheduleAsync` stores a generic job and calls `PublishRequestedAsync`. V6 lists `FeatureComputationProcessor.cs` generally but does not name this scheduler/method or define a pre-persistence dispatch-mode guard. A processor-only change could leave the direct scheduler able to publish Feature 129.

**Correction:** name `FeatureRecalculationScheduler.ScheduleAsync`, its interface/registration, the pre-persistence guard, fixed exception, and test proving no generic job/message is created while unrelated features remain unchanged.

### V6-M-05 — Frontend impact paths are wrong

**Severity:** `MAJOR`  
**Location:** §30.1 lines 549–550  
**Related:** AC-71–AC-72, T-UI-01–T-UI-02

V6 lists `src/frontend/src/functions/chat.functions.ts` and `src/frontend/src/components/chat/message-list.tsx`. Actual repository paths are `src/frontend/src/lib/chat.functions.ts` and `src/frontend/src/components/app/message-list.tsx`; the existing test is under `src/frontend/src/components/app/__tests__/message-list.test.tsx`.

**Correction:** use actual paths and identify DTO/view-model integration points.

### V6-M-06 — Semantic flow is not mapped to actual contracts

**Severity:** `MAJOR`  
**Location:** §§22–23 and §30.1  
**Related:** AC-60–AC-68; T-SEM-01–T-SEM-06, T-API-01–T-API-03

V6 defines a proposed schema and says V1/native V2/fallback V2 share a frame, but does not map proposal slots through the repository’s actual MAF V2 workflow messages, task state/interpretation contracts, executor registry, `AssistantMessagePayload`, and `ConversationRepositories.DeserializePayload`. The repository does have `AiStructuredOutputContract`, `JsonStructuredOutputValidator`, `MessagePersistenceFunction`, and versioned `AssistantMessagePayload`, but v6 does not define the exact adapter fields and migration branches.

**Correction:** add a field-by-field data-flow table from model output through local validation, merge, resolver, frame, workflow, executor, DTO, persistence, decoder, and replay.

## 8. Previous-finding audit

| Finding ID(s) | Previous severity | v6 status | Evidence/evaluation |
| --- | --- | --- | --- |
| B-01–B-04 | BLOCKER | Resolved | Immutable observations, unsafe entire-contribution routing, copied evidence, and accepted revision precedence are in §§7–9. Current normalizer still collapses/replaces rows, so cutover remains future work. |
| M-01 | MAJOR | Resolved | Per-operation manifest and type-0 barrier in §§7, 10. |
| M-02 | MAJOR | Partially resolved | Feature 129 outbox is separated, but actual scheduler guard is omitted. |
| M-03–M-04 | MAJOR | Partially resolved | Versioned aliases and GiST are present; exact schema/range predicates are not. |
| M-05 | MAJOR | Partially resolved | Pointer/trigger concepts exist; concrete EF/PostgreSQL contracts are incomplete. |
| M-06 | MAJOR | Partially resolved | Ordered attribution table exists; full closed reason/threshold/numeric proof contract is incomplete. |
| M-07–M-08 | MAJOR | Partially resolved | Semantic/DTO concepts exist; actual repository flow and paths are not mapped. |
| M-09 | MAJOR | Resolved | R1/R2 boundaries, stable identity before public snapshot, and cutover are explicit. |
| M-10 | MAJOR | Partially resolved | 78 rows exist, but tests/matrix/AC-78 reference remain defective. |
| N-01 | MINOR | Partially resolved | Numeric scale is stated but not fully mapped to each schema column. |
| T-01 | NOTE | Resolved | Advanced work is explicitly R2 or Optional-Endpoint. |
| V2-M-01–V2-M-02 | MAJOR | Resolved | Immutable alias/snapshot history and mutable pointers are separated. |
| V2-M-03–V2-M-06 | MAJOR | Partially resolved | Typed semantic and dispatch concepts exist, but actual task-state/scheduler/payload integration is incomplete. |
| V2-M-07 | MAJOR | Resolved | Persian fixture is complete. |
| V3-M-01–V3-M-04 | MAJOR | Partially resolved | Dedicated path and strict validator are intended; shared-bus isolation, schema, and repository mapping remain incomplete. |
| V3-N-01–V3-N-02 | MINOR | Partially resolved | Four categories and criteria exist; matrix/path defects remain. |
| V3-T-01 | NOTE | Resolved | Monetary/conversion gates are explicit. |
| V4-M-01 | MAJOR | Partially resolved | Durable retry owner and pre-ACK retry creation exist; Outbox cancellation/permanent transitions and test definitions do not. |
| V4-M-02 | MAJOR | Resolved | Canonical vector/fingerprint rebuild is explicit and excludes audit-only retry noise. |
| V4-M-03 | MAJOR | Resolved | Complete fixture is present. |
| V4-M-04 | MINOR | Resolved | Numeric SLO/backfill defaults are explicit. |
| V4-N-01–V4-N-02 | NOTE | Partially resolved | Broker/Job/result distinction and trigger inventory exist; Outbox state and schema details remain incomplete. |
| V5-AC-01, V5-M-01–V5-M-08 | MAJOR | Partially resolved | Full sections and 78 rows were restored, but tests, matrix, persistence, semantic integration, and file-impact defects remain. |

## 9. Acceptance-criteria audit

The mechanical count passes, but the following ACs are not fully acceptable: AC-32 uses an undefined closed reason precedence; AC-39 lacks a complete classification/threshold enum; AC-41 names an undefined repository error; AC-43 omits exact composite columns; AC-48 does not identify the actual scheduler guard; AC-52 omits lease-recovery race outcomes; AC-55 does not define exact attempt/current-state conflict semantics; AC-59 uses undefined Outbox cancellation; AC-64 does not map fields through actual V1/V2 contracts; AC-67 lacks a decoder mapping; AC-77 maps to undefined individual fixture tests; AC-78 has an invalid section.

Every other row must still be rechecked after the missing test definitions and schema are added. A separate row alone does not satisfy atomicity or implementability.

## 10. Release-gate audit

The actual AC counts are 74 R1-Core, 3 R2-History, and 1 Optional-Endpoint. The body correctly says R2 does not gate R1 and the endpoint is disabled by default. Slice ownership is ambiguous: Slice 3 owns AC-28–AC-59 while Slice 6 also owns several R1 durable-dispatch ACs. This should be corrected to one implementation owner per AC, even if release gates remain unchanged.

## 11. Persistence and concurrency audit

The conceptual separation of immutable snapshots/events/history from mutable pointers and operational Job/Outbox projections is sound. Report and manifest locks are deterministic, and alias history uses the correct PostgreSQL mechanism directionally.

Approval remains blocked because §20 does not specify exact per-table PostgreSQL types/nullability/keys/FKs/index predicates, exact GiST range expressions, filtered uniqueness for unmatched facts, deferred trigger predicates, or EF `xmin` mappings. The current repository provides concrete EF/advisory-lock patterns, but v6 does not bind its proposed schema to them.

## 12. Durable execution and crash-point audit

The intended retry transaction is safe in principle: failed attempt evidence, Job event/projection, next Outbox/event, availability, and commit precede ACK. The current shared bus still ACKs before handler invocation at `RabbitMqFeatureMessaging.cs:87`, so Feature 129 must use a genuinely separate handler-before-ACK consumer. The actual generic scheduler still persists and directly publishes at `FeatureComputationProcessor.cs:7–29`.

The unresolved cancellation/permanent-failure state transitions, lease/consumer race, and missing named crash-point tests prevent approval. Duplicate delivery and stable Message ID are directionally correct but not executable without the missing state/test contracts.

## 13. Financial-calculation audit

The symmetric formulas, signed values, residual, unsafe contribution preservation, unit gate, cancellation ratio, company equation, decimal scale, and fixture arithmetic are sound. The ordered attribution table is materially improved but still lacks a complete closed reason enum/order and numeric reconciliation proof for each branch. “Highest-priority reason,” concentration/breadth thresholds, and several negative/partial branches remain under-specified for AC-32, AC-36, AC-39, and AC-77.

## 14. Semantic/API/replay/UX audit

The proposed slot schema, local validation, evidence spans, DTO example, payload version concept, Telegram limits, and web state matrix are useful. The actual repository has `AiStructuredOutputContract` at `AiModelContracts.cs:56`, `AssistantMessagePayload` at `ConversationContracts.cs:36–51`, deserialization at `ConversationRepositories.cs:226–229`, and MAF V2 persistence/workflow functions. V6 does not state the exact new discriminator/field, decoder branch, workflow message, task-state field, or executor registration. Frontend paths are wrong as documented in V6-M-05. This is a material implementation gap.

## 15. Repository and file-impact audit

Correctly identified: NADPCO provider/normalizer, sync processor, generic feature processor, worker, shared root contract, Telegram renderer, and frontend chat architecture. Incorrect/incomplete: frontend paths are wrong; `FeatureRecalculationScheduler` and `IFeatureRecalculationScheduler` are omitted as direct-dispatch guard points; proposed files lack exact project directory/namespace/interface contracts; registration impact is broad rather than concrete.

## 16. Remaining business decisions and safe gates

Provider monetary-unit confirmation, conversion dictionary, materiality/freshness/anomaly policy versions, R2 activation, and Optional-Endpoint approval have safe block/suppress defaults. They do not independently block R1. Outbox cancellation, schema details, scheduler guard, and named tests are not business decisions; they are mandatory design corrections.

## 17. Approval checklist

- [x] Exactly 78 AC rows exist and identifiers are unique.
- [ ] All AC section references resolve; AC-78 fails.
- [ ] Every AC maps to an individually defined test; range-only definitions fail.
- [ ] Outbox state machine is total; cancellation/permanent-failure transitions fail.
- [ ] Persistence schema is migration-ready; exact details fail.
- [ ] Direct Feature 129 publication is enforceably isolated; scheduler guard is omitted.
- [ ] File-impact paths match repository; frontend paths fail.
- [x] Release-gate counts are internally consistent.
- [x] Prior documents remain unchanged during this review.

## 18. Final verdict

Design-v6 is not ready for User Story or implementation-task decomposition. The AC count is correct, but the broken normative reference, undefined cancellation state, incomplete Outbox state machine, malformed resolution matrix, missing individual tests, incomplete PostgreSQL contract, and repository path/dispatch mismatches are material approval blockers.

NEED_CHANGES
