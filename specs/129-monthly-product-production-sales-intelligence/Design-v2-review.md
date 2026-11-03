# Feature 129 Design-v2 Review

## 1. Review scope

This is the second design review of Feature 129, Monthly Product Production and Sales Intelligence. The review covers the complete `Design-v2.md`, the original `Design.md`, the first `Design-review.md`, and the repository implementation referenced by those documents. It assesses data integrity, historical reproducibility, database enforceability, concurrency, durable messaging, semantic routing, conversation replay, testability, slice ordering, and file-impact accuracy.

This review is documentation-only. It does not authorize application code, migrations, provider calls, or production-data changes.

## 2. Overall assessment

`Design-v2.md` correctly resolves most of the first review: immutable source observations, raw-to-normalized reconciliation, report revisions and deterministic accepted-current selection, company-month manifests, a collectively exhaustive seven-bucket attribution model, immutable evidence, cancellation-aware classification, the 1404/01 boundary, fixed decimal rules, and corrected slice ordering are all materially stronger.

The design is still not implementation-ready. Seven major issues remain. Three are internal model contradictions that make stated constraints or immutability impossible to implement as written; one leaves Feature 129 exposed to job/message loss or dual publication; one leaves semantic proposals without slot values; one defines an untestable byte-replay requirement; and two documentation defects make the impact map and fixture unreliable.

## 3. Required changes

### V2-M-01 — Alias approval and ownership constraint is not enforceable

Approval belongs to `CompanyProductAliasSetRevision`, while the proposed GiST exclusion constraint filters `CompanyProductAliasMembership` rows using `ApprovalState`, a field not defined on the membership model. Copying approval onto immutable memberships would not solve the lifecycle problem: memberships from superseded approved revisions would remain approved and block a later overlapping revision.

Required resolution:

- Preserve immutable alias-set revisions and memberships.
- Add a mutable current approved-ownership projection, such as `CurrentApprovedCompanyProductAliasOwnership`.
- Put the overlap/exclusion constraint on that projection, where every constrained field exists.
- Define approval as an atomic replacement of only the affected current-ownership rows.
- Define company-scoped locking, transaction and lock order, supersession, rollback, merge/split/reversal, and affected-period scheduling.
- Keep historical snapshots linked to immutable alias-set revisions and memberships, never to the mutable projection.

This means previous findings M-03 and M-04 were only partially resolved.

### V2-M-02 — Snapshot immutability contradicts publication mutation

The design labels snapshots append-only and immutable, but the persistence model contains `Status` and `IsCurrent`, and publication changes both the old and new snapshot rows. It also mentions an `xmin`-protected pointer without defining a pointer table.

Required resolution:

- Keep snapshot header, items, signals, and evidence immutable after insertion.
- Represent lifecycle through immutable publication-status events.
- Add a concrete mutable `CompanyMonthlyProductAnalysisCurrentPointer` projection.
- Remove `IsCurrent` from snapshot facts; derive current/superseded state from pointer plus events.
- Define pointer key, current snapshot FK, optional publication event FK, `xmin`, indexes, lock, transaction sequence, retry, rollback, and stale-writer behavior.
- Never update an old snapshot after it is inserted.

This means previous finding M-05 was not resolved.

### V2-M-03 — Semantic routing lacks typed slot-value transport

`QueryInterpretationProposal` carries capability codes, missing-slot names, presentation, confidence, and evidence, but no proposed slot values. Adding `QuerySlotType` members and a product resolver therefore cannot transport an extracted company, product, period, comparison, measure, or focus from the model into `QueryInterpretation` or `ValidatedQueryFrame`.

Required resolution:

- Define `SemanticSlotProposal`, `SemanticSlotValueKind`, provenance, validation result, and `ValidatedSemanticSlot` contracts.
- Include slot name/type, value kind, raw text, normalized value, canonical ID where applicable, Jalali period, comparison kind, analysis focus, measure, presentation, confidence, provenance, evidence span, resolver status, validation status, and rejection/ambiguity reason.
- Publish a closed JSON schema and reject unknown slot names/kinds, malformed values, out-of-span evidence, unsupported enum codes, excessive values, and unexpected properties.
- Define deterministic/model merge precedence, explicit conflicts, canonical replacement of raw values, mapping into `QueryInterpretation`, validation into one shared frame, task-state persistence, and V1/V2 parity.
- Ensure no unvalidated model-proposed value reaches an executor or calculator.

This means previous finding M-07 was not resolved.

### V2-M-04 — Durable job transition is ambiguous

The existing `FeatureRecalculationScheduler.ScheduleAsync` stores a job and then calls `PublishRequestedAsync` directly. Those operations do not share a database/message transaction. `Design-v2.md` asks the manifest transaction to create a durable outbox-backed job but also says to reuse the scheduler, leaving it unclear whether Feature 129 uses direct publication, outbox dispatch, or both.

Required resolution:

- Feature 129 must bypass the existing direct-publish scheduling method.
- In the ingestion transaction, create/reuse the Feature 129 computation job and a Feature 129 outbox row.
- Dispatch only undispatched outbox rows through a dedicated leased dispatcher.
- Keep the global scheduler unchanged for unrelated features unless separately approved.
- Define exact entry point, transaction, idempotency keys, dispatch states, leases, publish confirmation, crash-after-publish recovery, duplicate RabbitMQ delivery, retry/dead-letter behavior, recovery, class changes, and mutual-exclusion tests.

Feature 129 must never use direct publication and outbox dispatch for the same job.

### V2-M-05 — Payload replay acceptance criterion is ambiguous

AC-36 requires byte-equivalent numeric structured content, but the repository persists a serialized object and later deserializes it with `JsonSerializer.Deserialize<AssistantMessagePayload>`. Raw response bytes are not retained. JSON property order, whitespace, and serializer metadata are therefore not stable or meaningful replay guarantees.

Required resolution:

- Replace byte equality with canonical semantic equality.
- Persist the immutable result schema version, exact decimal values, enum/reason codes, immutable snapshot/revision/observation/alias/policy IDs, units, periods, warnings, evidence, and ordered collections.
- Decode history from the embedded payload and return semantically equivalent structured content.
- Require exact equality for decimals and immutable identifiers; exclude JSON whitespace, property order, and serializer metadata.
- Persist the final deterministic Persian narrative and replay it exactly if narrative is part of the assistant message.
- Define one canonical semantic comparator and update AC-36 and tests accordingly.

### V2-M-06 — File-impact map contradicts itself

`MetricRecalculationProcessor.cs` appears in the table of existing files to modify, but the same section says it is intentionally not modified for Feature 129 routing.

Required resolution:

- Split the impact map into existing files to modify, existing files inspected but intentionally unchanged, proposed new files, and optional later-slice files.
- Place `MetricRecalculationProcessor.cs` only in the intentionally unchanged category unless an actual approved modification is specified.
- Keep implementation and documentation-only impacts unambiguous.

### V2-M-07 — Fixture names are incorrect

The representative symbol contains a hidden ZWNJ/character-order defect, and the product package size is incorrectly written as 400 grams.

Required resolution:

- Use the symbol exactly as `غاذر`.
- Use the product exactly as `سبزیجات ۴۰ گرمی`.
- Search headings, prose, examples, acceptance criteria, and test names for ZWNJ and character-order variants.
- Preserve the numerical fixture values unless arithmetic verification identifies an independent error.

## 4. Previous-finding audit

| Previous finding | Second-review status | Audit result |
| --- | --- | --- |
| B-01 | Resolved | Immutable observations preserve repeated rows and raw/normalized reconciliation is objective. |
| B-02 | Resolved | The seven mutually exclusive effect paths preserve every signed contribution. |
| B-03 | Resolved | Evidence references immutable observations and copies the exact facts needed for replay. |
| B-04 | Resolved | Immutable revisions, deterministic precedence, locked accepted pointers, and ambiguity events are defined. |
| M-01 | Resolved | The per-company-month manifest separates success-empty from failure and gates on ProductSales type 0. |
| M-02 | Partially resolved | The derived-feature framework is selected, but V2-M-04 requires one unambiguous Feature 129 dispatch path. |
| M-03 | Partially resolved | Identity compatibility is improved; V2-M-01 shows the proposed ownership constraint is not enforceable. |
| M-04 | Partially resolved | Alias history exists; V2-M-01 requires a separate current ownership projection and atomic replacement. |
| M-05 | Not resolved | V2-M-02 shows snapshots are still mutated and the pointer is undefined. |
| M-06 | Resolved | Lifecycle, identity, sign, quality, attribution, and cancellation-aware classification are separated. |
| M-07 | Not resolved | V2-M-03 shows the model proposal cannot transport typed slot values. |
| M-08 | Partially resolved | Structured payloads and backward decoding are designed; V2-M-05 corrects replay equality. |
| M-09 | Resolved | Identity and revision foundations precede publication, and backfill starts at 1404/01. |
| M-10 | Partially resolved | Normative criteria exist, but the seven V2 findings require new/updated criteria and mappings. |
| N-01 | Resolved | Precision, rounding, fingerprints, tolerances, and denominator reason codes are fixed. |
| T-01 | Resolved | First-release correctness and later enhancements are separated. |

## 5. Files and repository evidence inspected

Documents read completely:

- `specs/129-monthly-product-production-sales-intelligence/Design.md`
- `specs/129-monthly-product-production-sales-intelligence/Design-review.md`
- `specs/129-monthly-product-production-sales-intelligence/Design-v2.md`
- `README.md`

Repository evidence inspected:

- `FeatureComputationProcessor.cs`: `FeatureRecalculationScheduler.ScheduleAsync` persists through `IFeatureComputationJobRepository` and then directly publishes through `IFeatureRecalculationPublisher`; `FeatureComputationProcessor` is at-least-once/idempotency oriented but does not supply an ingestion-transaction outbox.
- `PersistedFeatureServices.cs`, `FinancialIngestionRows.cs`, and `FinancialIngestionConfigurations.cs`: current computation-job persistence and unique idempotency-key shape.
- `DerivedFeatureContracts.cs`: current scheduler, job, request, publisher, and processor abstractions.
- `CanonicalQueryEntityContracts.cs`: current closed `QuerySlotType`, name mapping, string-valued `ResolvedQuerySlot`, and validator.
- `CapabilityInterpretationGovernance.cs`: current model proposal contains no slot candidates or typed values.
- `ConversationalCapabilityContracts.cs`: current `QueryInterpretation` carries entities, metrics, one period/comparison/presentation, but no typed arbitrary slot proposals.
- `SemanticCapabilityExecutionContracts.cs`: `ValidatedQueryFrame` currently wraps `ResolvedQuerySlot` plus `QueryInterpretation`.
- `ConversationTaskStateContracts.cs`: task state persists one string value and optional canonical entity ID per slot; the dialogue gate currently calls the synchronous deterministic interpreter.
- `ConversationContracts.cs`, `ConversationRepositories.cs`, `MessagePersistenceFunction.cs`, `AiQueryOrchestrationService.cs`, and `AiFacadeController.cs`: payload versions 1/2 are serialized objects, decoded to objects, and mapped for history; raw response bytes are not retained.
- `IndustryRelativeValuationCalculationSnapshotWriter.cs`: repository advisory-lock precedent currently mutates latest/current flags and therefore is a locking reference, not an immutable-snapshot model to copy literally.
- `MetricRecalculationProcessor.cs`: verified as the metric-registry recalculation path and intentionally excluded from Feature 129.
- `ServiceCollectionExtensions.cs`: current deterministic interpreter/no-op proposal registration and derived-feature registrations.

## 6. Final verdict

The mathematical and source-revision foundations are strong, but alias ownership, snapshot publication, typed semantic transport, durable dispatch, replay semantics, impact mapping, and fixture identity must be corrected before implementation review. `Design-v3.md` must apply these findings throughout its schemas, workflows, acceptance criteria, tests, slices, and impact map.

NEED_CHANGES
