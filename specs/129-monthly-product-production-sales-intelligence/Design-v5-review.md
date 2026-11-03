# Feature 129 — Design-v5 Review

## Verdict

`Design-v5.md` is **not ready** for User Story or implementation-task decomposition. The final verdict is `NEED_CHANGES`.

The principal failure is acceptance-criteria integrity: v5 replaces 78 individually numbered criteria with 17 grouped table rows. That is not a compression of equivalent contracts; it removes the independently testable wording and traceability required to decompose work safely. Several other v5 sections also compress previously explicit contracts into assertions that are not independently implementable.

## Review scope and repository authority

I read the complete design history and review chain:

- `Design.md`
- `Design-review.md`
- `Design-v2.md`
- `Design-v2-review.md`
- `Design-v3.md`
- `Design-v3-review.md`
- `Design-v4.md`
- `Design-v4-review.md`
- `Design-v5.md`

I inspected the repository files and executable behavior referenced by that history, including:

- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiMonthlyActivityNormalizer.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/FinancialDataSyncProcessor.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/NadpcoApiDataProviderClient.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Features/FeatureComputationProcessor.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Features/Messaging/RabbitMqFeatureMessaging.cs`
- `src/backend/FinancialCopilot.Worker/FeatureComputationConsumerWorker.cs`
- `src/backend/FinancialCopilot.Application/AI/ModelProviders/AiModelContracts.cs`
- `src/backend/FinancialCopilot.Application/Conversations/ConversationContracts.cs`
- `src/backend/FinancialCopilot.API/appsettings.Development.json`
- relevant registration, migration, and test files identified by the design history.

The executable repository remains authoritative. The current development configuration sets `AiOrchestration:Mode` to `MicrosoftAgentFrameworkV2`. The current shared feature bus ACKs before invoking its handler, `FeatureComputationProcessor` persists a generic job and publishes completion/failure directly, and the current monthly normalizer still collapses duplicate line-item codes, replaces child rows, and uses an array index in fallback identity. v5 acknowledges these facts but does not provide the full implementable replacement contracts.

## 1. Acceptance-criteria integrity audit

### Counts

| Measure | Result |
| --- | ---: |
| AC identifiers referenced | 78 (`AC-01` through `AC-78`) |
| Table rows in the v5 AC table | 17 |
| Grouped summary rows | 13 |
| Standalone rows containing one identifier | 4 (`AC-72`, `AC-76`, `AC-77`, `AC-78`) |
| Independently written row-level normative statements | 17 |
| Individually specified AC identifiers with their own criterion wording | 4 |
| AC identifiers represented only by a grouped row | 74 |
| Missing identifier in the numeric range | None; all 78 identifiers are referenced |

The distinction between the two “independent” counts matters. There are 17 prose rows, but only four identifiers have a criterion that can be read without splitting a multi-behavior range. The other 74 identifiers are not individually specified.

### Required checks

- `AC-01–04` is one row with one collective sentence, not four separately written criteria. It combines duplicate/repeated observations, ordinal semantics, replay receipts, and reconciliation. Those have different fixtures, preconditions, outcomes, and likely persistence boundaries.
- Every identifier from `AC-01` to `AC-78` is syntactically referenced, but 74 do not have their own normative statement.
- The grouped rows do not each express one unambiguous behavior. Examples include `AC-16–27` combining draft isolation, complete versioning, overlap constraints, approval atomicity, lifecycle transitions, and historical stability; `AC-28–37` combining calculations, quality states, units, rounding, cancellation, and evidence; and `AC-55–57` combining ACK/NACK, retries, exhaustion, dead letters, redrive, cancellation, and duplicate delivery.
- The grouped rows do not map each criterion individually to a named test. For example, `AC-16–27` maps to `T-ALIAS-01–12`, but does not state which test proves which AC, nor whether all 12 tests cover all 12 behaviors. The same defect exists for nearly every range.
- The grouped rows do not map each criterion individually to the correct slice. `AC-16–27` maps to slices `2–4`, while its behaviors have materially different ownership, calculator, publication, and API dependencies. A task author must invent the work breakdown and sequencing.
- Different severities, preconditions, transaction boundaries, and test types are hidden in the same rows. For example, a database exclusion constraint, an approval rollback test, and historical snapshot stability are not one implementation task or one test type.
- A task author must reconstruct missing individual AC wording from earlier documents. That violates the standalone-design gate. Earlier documents may identify regressions, but they cannot supply omitted v5 normative requirements.

This is a `MAJOR` blocker by itself. The v5 completion claim of “17 acceptance criteria” is therefore misleading: it describes 17 summary rows while claiming coverage of 78 criteria.

## 2. Standalone completeness and compression regressions

The shorter v5 text did not preserve all independently implementable contracts.

### Data model and PostgreSQL contracts

V5 names many entities and says that FKs, checks, deferred triggers, `xmin`, exclusion constraints, and unique keys are required, but it does not provide an implementable schema contract for most of them. Missing or insufficiently explicit details include the exact columns, nullability, precision/scale per numeric field, alternate-key definitions, FK column pairs, check-value sets, partial/filtered unique-index predicates, deferrable constraint timing, trigger function behavior, and concurrency-token mapping for the new tables.

The exact immutable-trigger table inventory is useful, but it is not a substitute for the relational model. In particular, v5 does not define the complete schema for the manifest operation vector, report-revision accepted pointer, ownership current projection, job/outbox relationship, attempt/dead-letter evidence, snapshot product-fact uniqueness, or publication-pointer constraints. These are task-authoring decisions, not harmless implementation detail, because they determine whether the stated invariants can be enforced in PostgreSQL.

### Ingestion and revision acceptance

The v5 revision section gives a good precedence outline, but it does not fully specify the provider revision representation, publication timestamp validity/normalization, fingerprint input schema, accepted-pointer columns, receipt-to-revision relationship, manual decision contract, or the exact behavior for a correction with equal provider metadata and a different semantic fingerprint. It says the accepted revision remains current and DataAdmin action is required, but does not define the durable decision states, authorization contract, or acceptance transition table.

The current normalizer still performs `GroupBy(item => item.LineItemCode).Select(group => group.Last())`, deletes existing line items, and inserts new IDs. V5 correctly requires immutable observations, but the compatibility projection and transaction boundary needed to replace this behavior are only described in broad nouns. There is no complete operation-to-source-payload mapping or per-output provider response contract for the existing NADPCO client.

### Ownership, units, and attribution state machine

V5 states the seven formulas and names separate dimensions, but it does not provide the complete decision table that maps every combination of lifecycle, identity, unit, sign, quality, cancellation, and availability to exactly one bucket and fixed reason code. “Fixed reason codes” are asserted without an exhaustive enum/list and without a precedence order when multiple unsafe conditions coexist. “Classification,” “coverage,” “freshness,” and “contributor metadata” are also not given exact schemas or thresholds.

The earlier explicit state-machine material was compressed into prose. The v5 fixture proves only three continuing products; it does not independently prove every lifecycle and unsafe branch that the grouped AC row claims.

### Durable orchestration

V5 materially improves the retry design by naming one owner and describing transactional retry-row creation. However, the contract is still not sufficiently decomposed for implementation: it does not name the dispatcher/worker classes or registration points, define the exact durable lease-recovery operation for expired `Leased` and `PublishedAwaitingConfirm` rows, define publisher-confirm persistence semantics, or specify the broker redelivery/dead-letter policy values and ownership boundary. The current shared bus ACKs before handler invocation and must remain unchanged for unrelated features; v5 says a dedicated consumer is required, but does not provide the concrete message contract, queue declaration contract, worker lifecycle, or DI impact needed to implement that isolation.

The job/outbox state tables also mix logical attempt identity, handler attempt number, broker delivery evidence, and calculation-result uniqueness. V5 describes these concepts but does not define the exact state transition guards and which row is authoritative for each transition. That is especially risky for “ACK only after commit,” redelivery after commit failure, and redrive creating a new attempt number.

### Semantic routing, API, conversation, and UI

Section 6 is an architecture assertion, not a standalone contract. It does not define:

- the actual `MonthlyProductSemanticProposal` JSON shape, closed property lists, slot names, one-of discriminators, enum values, limits, or rejection-code values;
- the exact UTF-16 span validation rules and evidence-span ownership;
- deterministic merge precedence and conflict behavior for every slot/value kind;
- canonical company/product resolver inputs and ambiguity outputs;
- the `ValidatedQueryFrame` fields and versioning;
- the executor request/response DTO, payload v3 discriminators, nullability, warning/limitation enums, and evidence limits;
- API route, HTTP status/error behavior, authorization/entitlement/rate/billing integration, or ETag contract;
- conversation payload migration/backward-decoder rules beyond the phrase “payload v3”; or
- the web/Telegram result-state matrix, accessible table fallback, mobile/RTL behavior, and server-value mapping.

The actual repository has a shared `AiStructuredOutputContract` and `AssistantMessagePayload.Version`; v5 says the shared root remains unchanged but does not specify how the new nested contract is attached, persisted, decoded, and replayed through the active MAF V2 workflow. “V1 and both V2 paths consume the same frame” is a goal, not a task-ready integration contract.

### Evidence and observability

V5 says evidence copies all numeric inputs, IDs, checksums, policies, and reasons, but does not specify the evidence row schema, public versus internal evidence projection, bounded truncation rules, ordering, freshness calculation, or one-to-one mapping from each response numeric field to evidence. The observability section names bounded labels and fixed codes without listing the metrics, cardinality keys, audit event payloads, or alert/runbook acceptance behavior.

## 3. Internal scope and normative contradictions

V5 says first release is ProductSales `OutputType=0` from `1404/01`, and says later work may add YoY, fiscal YTD, averages, longer history, anomalies, and related capabilities. Section 10 nevertheless makes YTD, contiguous 3/12-month averages, anomaly thresholds, and 24-month history normative, and AC-72/its tests include those behaviors. Slice 6 calls them optional history/anomaly/endpoint work. A task author cannot tell whether these are first-release acceptance gates or explicitly deferred work.

The same ambiguity affects the direct endpoint: section 6 says protected endpoint behavior is retained, while section 2 calls a direct read endpoint later work and AC-67–71 includes protected API behavior. The release boundary and the acceptance set must be made consistent before decomposition.

## 4. Repository alignment findings

1. `FinancialDataSyncProcessor` currently saves the normalized projection, checks only whether requested monthly rows exist, marks the run completed, and publishes the existing derived-metric recalculation request. V5 requires the manifest/job/outbox intent to be atomic with authoritative ingestion, but does not define how the existing sync-run transaction and raw-payload transaction are replaced or how the existing recalculation publisher is prevented from being treated as Feature 129 dispatch.
2. `NadpcoApiMonthlyActivityNormalizer` currently performs authoritative replacement and invokes existing revenue-mix/trend calculators. V5 says Feature 129 has its own immutable history and no direct-publish path, but does not specify the compatibility projection's exact cutover and whether current calculators continue, are adapted, or are isolated during migration.
3. `FeatureComputationProcessor` and `RabbitMqFeatureBus` have direct-publish/ACK-before-handler behavior. V5 says the direct scheduler rejects Feature 129 and a dedicated consumer ACKs after commit, but the exact dispatch-mode guard and new worker/queue registration are absent from the impact map's concrete file-level contracts.
4. The active development configuration uses `MicrosoftAgentFrameworkV2`; v5's semantic section does not identify the concrete MAF workflow messages, tool registration, fallback runner, or existing conversation persistence points that must carry the new frame and payload.
5. `AssistantMessagePayload` already has a versioned shared envelope. Saying “payload v3” without a migration table for existing versions is insufficient to preserve backward decoding and semantic replay.

These are not claims that the architecture is impossible. They are evidence that v5 has compressed required implementation choices below the task-decomposition threshold.

## 5. Fixture assessment

The v5 fixture now includes exact Persian names, input rows, outputs, formulas, totals, and negative variants, resolving the specific v4 fixture omission. The arithmetic is consistent:

- base total `450,000`;
- current total `570,150`;
- change `120,150`;
- growth `26.7%`; and
- product effects reconcile at the displayed stored scale.

This part is acceptable as a fixture, subject to the missing global calculation-state decision table described above. The fixture does not rescue the grouped AC problem: `AC-77` is one criterion while the table claims it also covers multiple malformed and quality cases without giving each a separate test contract.

## 6. Required changes before decomposition

1. Expand `AC-01` through `AC-78` into 78 individually written, testable criteria. Each must state one behavior, preconditions, expected state/output, failure/severity where relevant, named test(s), and one or more slices. Preserve AC-78 as a separate design gate if desired, but do not use grouped range labels as its substitute.
2. Add a complete traceability matrix with one row per AC-to-test and AC-to-slice mapping. Split grouped tests where one test does not prove one criterion.
3. Restore standalone normative contracts for the PostgreSQL schema, constraints/indexes/triggers, report and manifest state machines, alias state machine, seven-bucket decision table/reason codes, outbox/lease/confirm transitions, semantic proposal/frame/executor DTOs, API/payload migration, UX states, evidence, security, audit, and observability.
4. Resolve the release contradiction between “later work” and the normative YTD/history/average/anomaly/direct-endpoint criteria. Explicitly mark each as first-release or deferred and remove it from the applicable acceptance set when deferred.
5. Add exact file-level impact for the dedicated Feature 129 dispatcher, publisher-confirm recovery, consumer, worker, queue declarations, registration, migration/model snapshot, semantic workflow integration, and conversation decoder. Identify unchanged shared paths and the tests protecting them.
6. Define the exact compatibility behavior for the current normalizer, sync processor, existing calculators, and existing derived-feature publisher during rollout. Include transaction ownership and no-double-publication behavior.

## 7. Verification record

The pre-review working tree reported the Feature 129 specification directory as pre-existing untracked content. No existing file was edited. Baseline SHA-256 values recorded before creating this review were:

| File | SHA-256 |
| --- | --- |
| `Design.md` | `07B9538F5544C70DF11BB2CEF0B5B2F45CEE4E8D96F45CCB71B68FB3EC184243` |
| `Design-review.md` | `3390E828B44D01FE46CA2F866EAEB04741D66442A30870AC43D9DEF814F8C814` |
| `Design-v2.md` | `9252F9447F4913684A424E8AB3A452D86688D2A7CB5E49107D425E06D40DCDD9` |
| `Design-v2-review.md` | `3A26207F255F938DC917B1B626683B4394D068A5DDA8A9B36E2AF4BB85BFAF8A` |
| `Design-v3.md` | `A38B49033E409B402F4F10A8BEB6B544E8B6A892AC39303DE1DF7355FD0F936D` |
| `Design-v3-review.md` | `8618052B953BA3342735954CBCB47BF7917BB9ADF07373366CA9F53A2AB1803E` |
| `Design-v4.md` | `79F2803C3901795AF4F5529C9D2C96599B33527B7820A7592A1196039B38152F` |
| `Design-v4-review.md` | `A54B895FA52B5D684A016C567204617ADC475357E29C6C61BB026D4D45EFB789` |
| `Design-v5.md` | `F43B51D030862DF46BB59B9875AF3D1F90098EECC239EAEE5547ECF03239CA76` |

The hash table above is a record of the pre-review state; the final verification must recompute and compare every value, confirm this file is non-empty, confirm its final line is exactly the verdict, and run `git status --short`.

NEED_CHANGES
