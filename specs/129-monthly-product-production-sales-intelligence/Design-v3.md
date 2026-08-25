# Feature 129 — Monthly Product Production and Sales Intelligence

## 1. Document status

**Status:** `READY_FOR_DESIGN_REVIEW`  
**Repository discovery date:** 2026-08-24  
**Document role:** Complete standalone technical design replacing neither the original design nor either review document. This design does not authorize application code, migrations, provider calls, or production-data changes.

Feature 129 explains why a company's reported monthly product revenue changed. It reads persisted accepted report revisions, resolves products through an approved immutable alias-set revision, calculates a deterministic seven-bucket decomposition, publishes immutable snapshot facts, and serves bounded structured results to both orchestration paths. The LLM may propose intent and typed slot candidates and may render grounded prose; it cannot supply calculator inputs until every slot has passed deterministic schema validation, normalization, canonical resolution, and capability governance.

## 2. Design-v2 Review Resolution Matrix

| Finding | Resolution | Design-v3 sections | ACs | Tests | Status |
| --- | --- | --- | --- | --- | --- |
| V2-M-01 | Immutable alias revisions remain historical facts; `CurrentApprovedCompanyProductAliasOwnership` is the sole constrained current projection and is atomically replaced during approval. | 11, 20, 21 | AC-16–AC-22 | T-ALIAS-01–07 | Resolved |
| V2-M-02 | Snapshot facts contain no mutable status/current flag; append-only publication events plus an `xmin`-protected current-pointer projection represent publication state. | 19–21 | AC-36–AC-43 | T-PUB-01–08 | Resolved |
| V2-M-03 | Closed typed slot proposals, JSON-schema validation, deterministic merge, canonical resolution, shared validated frames, task-state persistence, and executor isolation are specified. | 25–27 | AC-49–AC-56 | T-SEM-01–08 | Resolved |
| V2-M-04 | Feature 129 bypasses direct-publish scheduling and uses only an ingestion-transaction job plus outbox, leased dispatcher, RabbitMQ, and idempotent consumer. | 17–18 | AC-44–AC-48 | T-ORCH-01–07 | Resolved |
| V2-M-05 | Replay uses canonical semantic equality with exact financial values/immutable IDs; persisted deterministic Persian narrative replays exactly. | 24, 28 | AC-57–AC-60 | T-CONV-01–05 | Resolved |
| V2-M-06 | The impact map has four non-overlapping categories; `MetricRecalculationProcessor.cs` is inspected and intentionally unchanged. | 35 | AC-64 | T-TRACE-01 | Resolved |
| V2-M-07 | All fixture references use `غاذر` and `سبزیجات ۴۰ گرمی`; exact-text validation is normative. | 14.2, 31, 32 | AC-63 | T-FIX-01 | Resolved |

### 2.1 Previous review resolution history

| Previous findings | Retained resolution in this design |
| --- | --- |
| B-01 | Every source row is an immutable observation; repeated product codes and exact duplicate occurrences remain economic facts. |
| B-02 | Mutually exclusive attribution paths allocate every signed contribution to exactly one of seven buckets. |
| B-03 | Evidence references immutable observations and copies every source value required for replay. |
| B-04 | Immutable report revisions, deterministic precedence, status events, and a locked accepted pointer reject late older facts. |
| M-01 | A generation-based company-month manifest records ProductSales 0–4 and ServiceSales outcomes; type 0 is the core barrier. |
| M-02 | The existing feature computation consumer/processor contracts are reused, with a Feature-129-only transactional outbox entry point. |
| M-03–M-04 | Conservative economic identity, immutable alias revisions/memberships, lineage, and the current ownership projection make matching reproducible and enforceable. |
| M-05 | Serializable advisory-lock publication uses immutable snapshot facts/events and one concrete current pointer. |
| M-06 | Lifecycle, match, sign, quality, and attribution availability are independent; driver classification is cancellation-aware. |
| M-07 | Typed semantic proposals and one validated slot frame provide the missing end-to-end slot-value path. |
| M-08 | Result schema ownership, payload v3, backward decoding, bounded evidence, Telegram fallback, and contribution transport are explicit. |
| M-09 | Source, revision, identity, alias, and unit foundations precede the first publication; supported backfill begins at 1404/01. |
| M-10 | Every normative behavior maps to a named test and implementation slice. |
| N-01 | Decimal types, rounding, serialization, tolerance, and zero/immaterial reason codes are fixed. |
| T-01 | First-release correctness is separated from optional historical/visual work. |

## 3. Scope and non-goals

### 3.1 First-release scope

- NADPCO ProductSales `OutputType=0` monthly facts from 1404/01 onward.
- Latest published month versus immediately previous Jalali month, plus an explicit two-period comparison when both periods are publishable.
- Immutable raw-derived observations, report revisions, acceptance history, company-month manifests, canonical products, immutable alias-set revisions, current approved ownership, reviewed unit policy, deterministic calculation, immutable evidence, atomic publication, semantic/API integration, conversation replay, concise Telegram output, and accessible web tables.
- Company/product totals, guarded changes, concentration, seven effect buckets, lifecycle/sign/identity/quality dimensions, contributors, coverage, driver, cancellation ratio, freshness, warnings, and selected-product production-versus-sales visualization when units are compatible.

### 3.2 Optional later slices

- YoY, fiscal YTD, prior-year equivalent YTD, contiguous 3/12-month averages, longer product history, robust anomalies, inferred inventory signals, plotted waterfall, image export, and a direct read endpoint.
- An optional capability is not considered complete until its mapped later-slice ACs pass.

### 3.3 Non-goals

- Forecasts, inventory-balance claims, target prices, buy/sell advice, or technical analysis.
- Cross-company physical-quantity aggregation or unapproved conversion of economic packages.
- Provider access or raw aggregation in an HTTP query.
- LLM arithmetic, LLM-selected raw database identifiers, or execution of unvalidated semantic values.
- A pre-1404 backfill without a separately approved archive-source design.
- Changes to the existing direct-publish scheduler behavior for unrelated features.

## 4. Repository-verified current state

| Area | Repository fact | Design consequence |
| --- | --- | --- |
| Provider | `NadpcoApiDataProviderClient` requests ProductSales output types 0–4 independently; failures can become empty/null slots. | Persist explicit outcomes; distinguish valid empty responses from failures. |
| Raw data | `ProviderRawPayloadPersistence` retains exact text, endpoint, reference, checksum, and receipt time. | Reuse immutable raw IDs/checksums; never expose payload text. |
| Normalizer | It currently groups by line code, takes the last row, mutates logical reports, deletes children, and uses array position in fallback keys. | Revisions/observations become authority; existing rows are compatibility projections only. |
| Backfill boundary | Provider and current coordinators clamp monthly activity to 1404. | Feature 129 begins at 1404/01. |
| Feature scheduling | `FeatureRecalculationScheduler.ScheduleAsync` stores a job and then directly invokes `PublishRequestedAsync`. | Feature 129 cannot call this method; it requires an ingestion-transaction outbox entry point. |
| Feature processing | Feature jobs, RabbitMQ publisher/consumer, consumer worker, and `FeatureComputationProcessor` already exist. | Reuse message and consumer contracts and dispatch to a keyed complex handler. |
| Publication precedent | Industry relative valuation uses a PostgreSQL transaction-scoped advisory lock but mutates current flags. | Reuse lock ordering only; do not copy mutable snapshot facts. |
| Semantic proposal | `QueryInterpretationProposal` currently carries capability codes and metadata, not slot candidates. | Extend the contract with typed values and strict schema validation. |
| Slots/task state | Current resolved/task slots store a string plus optional canonical ID; product/focus/measure are absent. | Add closed types and validated normalized value envelopes shared by frame/state. |
| Conversation | V1/V2 store object JSON and history deserializes it; raw response bytes are not retained. | Define semantic replay equality, not byte equality. |
| Metric recalculation | `MetricRecalculationProcessor` owns metric-registry work. | Inspect but do not modify or route Feature 129 through it. |

## 5. System context and invariants

```text
raw payload
→ operation attempt and manifest
→ immutable report revision and source observations
→ accepted-revision pointer
→ immutable approved alias-set revision + current ownership projection
→ Feature 129 job/outbox
→ dispatcher → RabbitMQ → consumer → Feature 129 handler
→ immutable snapshot facts/items/evidence + publication events
→ current published-snapshot pointer
→ shared query use case and validated semantic frame
→ V1/V2/API/conversation/Telegram/web
```

Invariants:

1. Every accepted raw economic row is retained; no product-code grouping discards a fact.
2. A calculator reads exact immutable report, alias, unit-policy, and calculation-policy revisions.
3. Historical facts never resolve through a current projection.
4. Every product contribution and the company change reconcile exactly at stored scale.
5. No snapshot, child, evidence row, or publication event is updated after insertion.
6. Exactly one current pointer may exist for a complete business key.
7. Feature 129 has exactly one request dispatch path: transactional outbox.
8. A model proposal is data, not authority; only a validated frame reaches an executor.
9. Historical replay reads the embedded immutable structured result and persisted narrative, not latest financial state.

## 6. Functional requirements

1. Resolve company identity through the existing canonical company resolver and retain provider `ExternalCompanyId` as ingestion identity.
2. Default current period to the current published pointer, not the largest raw period.
3. Default comparison to the immediately previous Jalali month; never skip a missing month.
4. Sum all accepted ProductSales type-0 observations, including repeated codes and negatives; never mix types 1–4 or ServiceSales into monthly product revenue.
5. Compare the union of current/base canonical products plus explicit unmatched source groups.
6. Return current/base production, sales quantity, rate, reported revenue, guarded changes, shares, seven effects, states, warnings, stable rank, and bounded evidence.
7. Return company totals, guarded growth, contributors, concentration, effect coverage, driver, cancellation, freshness, and limitations.
8. Emit `null` plus a fixed reason for invalid denominators; never infinity.
9. Preserve the last valid current pointer while newer input is processing, stale, blocked, rejected, or failed.
10. Never query NADPCO or recalculate from raw rows on a user request.

## 7. Non-functional requirements

- **Determinism:** the same immutable input IDs and policy versions yield the same decimals, states, ordering, warnings, fingerprint, and deterministic narrative.
- **Reproducibility:** a snapshot and persisted conversation remain interpretable without current report, alias, pointer, or latest-policy lookup.
- **Atomicity:** approval/current ownership and publication/current pointer each commit or roll back as one transaction.
- **Availability:** failed or stale work cannot displace the current published snapshot.
- **Performance:** repository read p95 ≤300 ms and structured facade retrieval p95 ≤700 ms excluding model latency; ≤100 product items and ≤24 periods.
- **Security:** raw text, credentials, internal errors, and unvalidated slot values never cross public/executor boundaries.
- **Accessibility/localization:** Persian RTL, explicit units, non-color signs, keyboard evidence actions, and table equivalents.
- **Observability:** only low-cardinality fixed labels; no company, product, query, payload, checksum, or exception text in metric labels.

## 8. Source-row preservation and reconciliation

For every successful operation payload, parse every array element into an immutable `MonthlyReportSourceObservation`. Never group by product code.

- `CanonicalRowJson`: fixed property order, invariant numeric representation, raw numeric lexemes retained separately.
- `EconomicSignature`: hash of normalized vendor identity, title, domestic/export, category, package, grade/quality, and unit; excludes array ordinal and measures.
- `SourceFactFingerprint`: hash of the canonical row including measures.
- `DuplicateOccurrence`: 1..N among identical fact fingerprints.
- `SourceRowDiscriminator`: hash of provider, logical report, fact fingerprint, and duplicate occurrence.
- `RawArrayOrdinal`: evidence-only source locator; never identity.

Rows with the same vendor code but distinct signatures remain distinct. Identical duplicates also remain distinct and raise `DuplicateFactObserved`. Payload replay is detected by payload checksum or semantic multiset checksum, not by deleting rows.

`RawRevenueTotal` equals the sum of all authoritative raw values. `NormalizedRevenueTotal` and row count must match exactly at `numeric(28,8)`. A mismatch produces `Blocked(RawNormalizationMismatch)` and cannot advance the accepted pointer.

## 9. Company-month ingestion manifest

`CompanyMonthIngestionManifest` is append-only by provider, company, Jalali year/month, and generation. Its operations are `ProductSales:0` through `ProductSales:4` and `ServiceSales:none` with states `SucceededWithRows`, `SucceededEmpty`, `Failed`, `TimedOut`, `NotRequested`, `Retrying`, or `PermanentlyUnavailable`.

Each attempt stores request/attempt ID, timestamps, safe status/error codes, row count, raw payload ID/checksum, candidate/accepted revision IDs, and retry eligibility. `SucceededEmpty` requires a valid HTTP-success empty collection; an exception converted to an empty array is `Failed`.

- ProductSales 0 is mandatory and must have an accepted reconciled revision.
- ProductSales 1 is optional for the two-period core and mandatory only for YTD.
- ProductSales 2–4 are optional validation/evidence.
- ServiceSales is recorded when requested but excluded from Feature 129 totals.

The manifest is `CoreReady` only after the type-0 revision and acceptance pointer commit. The same transaction invokes the Feature-129-only job/outbox writer described in section 18. A retry creates a new manifest generation/fingerprint when authoritative input changes.

## 10. Immutable report revisions and accepted selection

`MonthlyReportLogicalIdentity` is unique by provider, external company, report kind, output type, and Jalali month. It owns mutable `AcceptedRevisionId`, `xmin`, and updated time.

`MonthlyReportRevision` and its observations are immutable: logical ID, provider revision/publication metadata, raw payload ID/checksum, semantic checksum, parsed period, predecessor, parser schema, row count/totals, and validation result. Receipt and status/decision events are append-only.

Under `Serializable` isolation and `pg_advisory_xact_lock(hashtextextended('f129-report|' || LogicalIdentityId,0))`:

1. Replay checksum records a receipt only.
2. A comparable higher provider revision wins.
3. Otherwise a newer valid provider publication timestamp wins.
4. Receipt time never makes an older publication authoritative.
5. First fully validated metadata-less facts may be accepted only when no pointer exists.
6. Equal-precedence conflicting candidates become `Blocked(AmbiguousRevisionOrder)` and do not move the pointer.
7. DataAdmin resolution appends a decision event with actor/reason; it does not mutate revision facts.
8. `xmin` protects the pointer; serialization/deadlock conflicts retry at most three times.

Existing normalized report/line tables become accepted-current compatibility projections for Features 075–079. Historical evidence never references their replaceable line rows.

## 11. Canonical products and alias ownership

### 11.1 Identity and immutable history

Canonical products are company-scoped. Blank/zero vendor IDs are absent. A positive ID is only a candidate and must have compatible unit dimension, domestic/export status, category, package, grade, and quality. Matching order is approved current ownership, collision-free compatible vendor ID, exact economic signature, previously approved historical signature, then manual-review candidate. Text similarity alone never auto-merges material revenue.

Immutable tables:

- `CompanyCanonicalProduct`: identity/display/economic metadata and immutable creation facts; lifecycle comes from lineage.
- `CompanyProductAliasSetRevision`: company, revision number, parent ID, draft checksum, effective scope, algorithm version, author/time.
- `CompanyProductAliasMembership`: revision ID, provider, alias key/signature, effective `int4range`, canonical product ID, method, confidence, evidence, and override reason. It has no mutable approval flag.
- `CompanyProductAliasApprovalEvent`: append-only `Approved`, `Rejected`, `Superseded`, or `ApprovalReversed` with actor/reason/time and prior event.
- `CanonicalProductLineage`: append-only `Merge`, `Split`, `MergeReversal`, `SplitReversal`, `Retirement`, and `Reactivation` edges with effective periods.

### 11.2 Current approved ownership projection

`CurrentApprovedCompanyProductAliasOwnership` is the only mutable alias ownership projection:

| Field | Contract |
| --- | --- |
| `Id` | Stable projection-row GUID. |
| `ExternalCompanyId` | Required company scope. |
| `ProviderName` | Required bounded provider code. |
| `ProviderAliasKey` | Required collision-checked vendor key or economic signature. |
| `EffectiveMonthRange` | Required non-empty canonical `[from,to)` `int4range`. |
| `CanonicalProductId` | Required FK. |
| `ApprovedAliasSetRevisionId` | Required FK to immutable revision. |
| `MembershipId` | Required FK to a membership in that revision. |
| `CreatedAtUtc`, `UpdatedAtUtc` | Audit timestamps. |
| `xmin` | Npgsql concurrency token. |

`btree_gist` is required. Every referenced field exists on the constrained table:

```sql
ALTER TABLE "CurrentApprovedCompanyProductAliasOwnerships"
ADD CONSTRAINT "EX_CurrentAliasOwnership_NoOverlap"
EXCLUDE USING gist
("ExternalCompanyId" WITH =,
 "ProviderName" WITH =,
 "ProviderAliasKey" WITH =,
 "EffectiveMonthRange" WITH &&);
```

Supporting uniqueness: membership ID unique in the projection; `(ApprovedAliasSetRevisionId, MembershipId)` FK/unique validation; provider/company/alias/range lookup index.

### 11.3 Atomic approval and replacement

Approval uses `Serializable` isolation:

1. Acquire `pg_advisory_xact_lock(hashtextextended('f129-alias|' || ExternalCompanyId,0))` before projection reads.
2. Lock the draft revision and validate immutability, membership ownership, canonical products, non-empty ranges, economic compatibility, lineage, actor permission, and draft checksum.
3. Determine the affected provider/alias keys and month ranges; lock their current projection rows in ordinal key order.
4. Append an `Approved` event and a `Superseded`/`ApprovalReversed` event for the prior decision when applicable.
5. Delete only current projection rows whose keys/ranges are replaced, splitting unaffected range fragments when necessary, then insert rows derived exclusively from the approved revision memberships.
6. Let the GiST exclusion constraint reject any remaining overlap; verify each projection row points to its revision membership.
7. Insert idempotent recalculation jobs/outbox rows for every supported affected month and every comparison snapshot that consumes it.
8. Commit atomically; only after commit can the dispatcher publish.

No outbox is visible if approval rolls back. A constraint, `xmin`, serialization, validation, or audit failure restores the prior projection exactly because all changes share the transaction. Multi-company administrative operations lock company IDs in ascending ordinal order; ordinary approval is one company.

Merge, split, validity edit, and reversal always create a new revision plus lineage/event records. Reversal reconstructs current ownership from the selected immutable prior revision by the same replacement workflow; it never revives or edits old membership rows. Superseded revision memberships do not remain in the current projection and therefore cannot block a later overlap. Historical snapshots continue to reference their immutable `ApprovedAliasSetRevisionId`; they never reference the current projection.

## 12. Unit normalization and policy

Preserve raw unit text; normalize Persian/Arabic characters and digits, whitespace, ZWNJ, punctuation, package count/size, grade, quality, domestic/export, and category without removing economic distinctions. Store governed `UnitCode`, physical `Dimension`, and immutable `ConversionPolicyVersion`.

Version 1 enables no conversion by default. A reviewed policy may permit exact conversions inside one dimension. Count, thousand-count, carton, package, litre, kilogram, and tonne are not assumed interchangeable. An unsupported unit change keeps monetary contribution but routes it to `UnattributedComparableEffect` and suppresses quantity/price and production/sales claims.

Company quantities are unit-bucket maps. A scalar exists only when all included values share one approved convertible dimension.

## 13. Calculation definitions

For product `i`, base `0`, current `1`: sales quantity `Q`, rate `P`, reported revenue `R`, production `G`; `S_t=ΣR_i,t`.

```text
Contribution_i = R_i,1 - R_i,0
CompanyChange D = S_1 - S_0 = ΣContribution_i
PercentChange(x) = 100 × (x1-x0)/x0 only when x0 > 0
RevenueShare_i,t = 100 × R_i,t/S_t only when S_t > 0
```

`ContributionShare` is null with `ZeroCompanyChange` when `D=0`, and null with `ImmaterialCompanyChange` when `abs(D)` is below the policy floor. Otherwise it is `100×Contribution_i/D` and may exceed 100% or be negative.

For safe continuing products:

```text
QuantityEffect = (Q1-Q0) × (P0+P1)/2
PriceEffect = (P1-P0) × (Q0+Q1)/2
ResidualEffect = Contribution - persisted QuantityEffect - persisted PriceEffect
```

Reported revenue remains authoritative. The balancing residual guarantees stored-scale equality; the unrounded `R-Q×P` delta remains audit evidence.

### 13.1 Precision and rounding

Quantities, rates, revenues, effects, and totals use PostgreSQL `numeric(28,8)`/.NET `decimal`; ratios use `numeric(20,10)`; percentages use `numeric(18,6)`. Parsing is checked before acceptance. Use `MidpointRounding.ToEven`; round quantity and price effects once to scale 8, then calculate residual as the exact balancing difference. Reconcile before presentation rounding.

The v1 warning tolerance is `max(1.00000000 million rial, 0.5%×max(|R0|,|R1|))`; it controls warnings, never equality. Fingerprints serialize fixed-scale invariant decimals, explicit nulls, enum codes, immutable IDs, and ordinal item order as UTF-8 before SHA-256.

### 13.2 Representative غاذر fixture

Values are million rial. The text is itself a normative fixture.

| Product | Contribution | Quantity | Price | Residual | Check |
| --- | ---: | ---: | ---: | ---: | ---: |
| سبزیجات ۴۰ گرمی | 91,881.6 | 95,440.8 | -3,559.2 | 0 | 91,881.6 |
| کنسرو مخلوط | -30,000 | -30,000 | 0 | 0 | -30,000 |
| غذای آماده صادراتی | 58,268.4 | 51,000 | 7,000 | 268.4 | 58,268.4 |
| **Total** | **120,150** | **116,440.8** | **3,440.8** | **268.4** | **120,150** |

Base sales are 450,000; current sales are 570,150; change is 120,150; growth is 26.7%. `سبزیجات ۴۰ گرمی` is the largest positive contributor. Numerical values remain unchanged from the verified calculation.

## 14. Exhaustive seven-bucket attribution

Independent dimensions:

- Lifecycle: `New`, `Resumed`, `ContinuouslyActive`, `Inactive`, `Discontinued`, `HistoryInsufficient`.
- Identity: `Matched`, `Unmatched`, `Ambiguous`, `ManualReview`.
- Sign: `PositiveSale`, `ZeroActivity`, `ReturnOrReversal`, `NegativeAdjustment`.
- Quality: `Valid`, `Warning`, `Partial`, `Blocking`.
- Availability: `Decomposed`, `UnattributedComparable`, `Unmatched`, `Unavailable`.

Exactly one ordered path allocates `C=R1-R0`:

| Path | Condition | Allocation |
| --- | --- | --- |
| Blocking | Type 0 incomplete/raw mismatch | No comparison snapshot; prior pointer remains. |
| Unsafe identity | Unmatched/ambiguous/manual review | `UnmatchedEffect=C`. |
| Inactive | `R0=R1=0`, no activity | All zero. |
| Activation | Safe identity, base absent/zero, current positive | `ActivationEffect=C`. |
| Discontinuation | Safe identity, base positive, current absent/zero | `DiscontinuationEffect=C=-R0`. |
| Decomposed | Continuing, compatible unit, valid nonnegative Q, positive P, valid sale semantics | Symmetric quantity + price + residual. |
| Unattributed comparable | Continuing monetary facts but missing/invalid Q/P, unsupported unit conversion, return/reversal, or negative-only adjustment | `UnattributedComparableEffect=C`. |

For every product and company:

```text
Contribution = QuantityEffect + PriceEffect + ActivationEffect
             + DiscontinuationEffect + ResidualEffect
             + UnattributedComparableEffect + UnmatchedEffect
```

No path drops or double-counts a contribution.

## 15. Company classification

Let `G=Σ|effect atom|` and `CancellationRatio=1-|D|/G` when `G>0`. Classification is unreliable when `D=0`, `abs(D)` is immaterial, cancellation exceeds 35%, match coverage is below 90%, decomposable continuing coverage is below 80%, residual ratio exceeds 10%, or unmatched plus unattributed mass exceeds 20% of `G`.

For reliable cases, use aligned signed mass:

- `QuantityDriven`: aligned quantity ≥60%, at least 15 points above aligned price, and opposing quantity ≤25% of quantity gross mass.
- `PriceDriven`: symmetric rule.
- `ActivationDriven`/`DiscontinuationDriven`: aligned category ≥50% and at least 50% of `|D|`.
- `Mixed`: prerequisites pass and no threshold wins.

Quantity `+100/-99` and price `+1` yields `D=2`, `G=200`, and 99% cancellation, so it is `NotReliablyClassifiable`. Revenue-share turnover is a separate non-additive `MixShift`; it never enters the effect equation.

## 16. Quality and publication policy

Publication requires current/base core-ready manifests, exact accepted revision IDs matching the fingerprint, raw reconciliation, approved alias revision, approved unit/calculation policies, exact product/company equations, immutable evidence for every public number, and no blocking identity/period/overflow/source-scale issue.

Quality outcomes are `Blocking`, `Partial`, `Warning`, and `ManualReview`. Read freshness is derived as `Fresh`, `Stale`, `Partial`, `Processing`, `Unavailable`, or `Blocked`. Provider publication time is used only when valid; otherwise receipt time is labeled as receipt. A failed or blocked run writes a run outcome/event but never alters the current pointer.

## 17. Backend architecture and calculation workflow

Application owns formulas, policies, typed contracts, semantic validation, and handler interfaces. Infrastructure owns parsing, EF persistence, PostgreSQL locks, outbox leases, RabbitMQ, caches, and render adapters.

Workflow:

1. Persist operation attempt and raw payload.
2. Parse immutable report revision/observations and reconcile.
3. Select the accepted revision under its report lock.
4. Commit manifest generation, Feature 129 job, and Feature 129 outbox atomically.
5. Dispatcher leases and publishes the outbox message; consumer invokes `FeatureComputationProcessor`.
6. Processor selects `MonthlyProductAnalysisComputationHandler` for `monthly_product_activity_analysis` while preserving the existing scalar default handler for other feature codes.
7. Handler loads exact immutable revisions/alias/policies and calculates outside the publication transaction.
8. Writer revalidates and atomically inserts immutable snapshot facts/events and updates the pointer.
9. Completion/cache invalidation occurs only after commit.

## 18. Feature 129 durable dispatch

The only Feature 129 path is:

```text
Ingestion transaction
→ Feature computation job
→ Feature computation outbox
→ dispatcher
→ RabbitMQ
→ consumer
→ Feature 129 computation handler
```

Feature 129 never calls `IFeatureRecalculationScheduler.ScheduleAsync` because that implementation persists then directly publishes. The existing method and global behavior remain unchanged for unrelated features.

### 18.1 Entry point and transaction

`IFeature129ComputationRequestWriter.EnsureRequestedAsync` (new) is called by `FinancialDataSyncProcessor` inside the same `FinancialIngestionDbContext` transaction that commits the core-ready manifest or by the alias-approval transaction for affected-period recalculation. It creates/reuses:

- `FeatureComputationJob` with unique `JobIdempotencyKey = SHA-256(feature code/version, company, current/comparison periods, manifest IDs, accepted revision IDs, alias revision, unit policy, calculation policy)`.
- `FeatureComputationOutbox` with unique `OutboxIdempotencyKey = 'f129-request|' + JobIdempotencyKey`, job FK, serialized versioned request envelope, state, attempts, lease, confirmation, timestamps, and fixed failure code.

If the job exists, the writer ensures exactly one matching outbox row; it never invokes a publisher. Feature 129 message creation is rejected unless the outbox row references a Feature 129 job and the caller's dispatch mode is `TransactionalOutbox`.

### 18.2 Dispatch states and leasing

States: `Pending`, `Leased`, `PublishedAwaitingConfirm`, `Confirmed`, `RetryableFailed`, `DeadLettered`. Fields include `AttemptCount`, `NextAttemptAtUtc`, `LeaseOwner`, `LeaseToken`, `LeaseExpiresAtUtc`, `PublishedAtUtc`, `ConfirmedAtUtc`, `BrokerMessageId`, `LastFailureCode`, `CreatedAtUtc`, and `UpdatedAtUtc` with `xmin`.

The dispatcher selects eligible rows with `FOR UPDATE SKIP LOCKED`, assigns a unique lease/fencing token, commits, publishes with persistent delivery and `MessageId=OutboxId`, waits for broker confirm, then conditionally marks `Confirmed` using lease token and `xmin`. Expired leases return to eligibility.

Crash handling:

- Before publish: expired lease causes safe retry.
- After publish but before confirm persistence: the row is retried and RabbitMQ may receive a duplicate with the same message/job IDs.
- Consumer deduplicates by job idempotency key and terminal job/snapshot fingerprint; duplicate delivery acknowledges without a second publication.
- Confirmed rows are never republished.
- Transient failures use bounded exponential backoff; after the configured maximum, state becomes `DeadLettered` and an authorized recovery action appends an audit event and requeues by state transition, never by creating an unkeyed second message.
- Startup and periodic scans recover all eligible undispatched/expired rows.

Publisher confirm does not mean computation success. The consumer records running/completed/failed job state; message acknowledgement occurs after idempotent handler persistence. Redelivery after handler commit is a no-op. Tests forbid invoking both the direct scheduler and request writer for Feature 129.

## 19. Persistence model

All proposed rows use `FinancialIngestionDbContext` except existing raw payload storage.

| Model | Core contract |
| --- | --- |
| `MonthlyReportLogicalIdentity` | Unique logical key, accepted revision FK, `xmin`. |
| `MonthlyReportRevision`, observations, receipts, status events | Immutable revision facts and history. |
| Manifest/operation/attempt rows | Immutable generations/attempts and exact revision/raw references. |
| Canonical product, alias-set revision, membership, approval event, lineage | Immutable identity history. |
| `CurrentApprovedCompanyProductAliasOwnership` | Mutable constrained current alias projection, `xmin`. |
| Calculation run | Mutable operational status only; no financial facts. |
| `CompanyMonthlyProductAnalysisSnapshot` and items/evidence | Immutable financial facts; no `Status`, `IsCurrent`, or superseded field. |
| `CompanyMonthlyProductAnalysisPublicationEvent` | Immutable event history. |
| `CompanyMonthlyProductAnalysisCurrentPointer` | Mutable current publication projection, `xmin`. |
| Feature computation job/outbox | Durable request and dispatch state. |

Snapshot item uniqueness is filtered: unique `(SnapshotId, CanonicalProductId)` where canonical ID is non-null, and unique `(SnapshotId, UnmatchedSourceKey)` where canonical ID is null. Snapshot fingerprint is unique.

## 20. Immutable snapshots, publication events, and current pointer

### 20.1 Snapshot facts

`CompanyMonthlyProductAnalysisSnapshot` stores immutable business key, version, input fingerprint, exact current/base report revision IDs, alias-set revision ID, unit/calculation policy IDs, totals, effects, coverage, warnings, evidence bounds, calculated time, and schema version. It has no mutable publication status or `IsCurrent`. Items, signals, and evidence are inserted in the same transaction and are immutable.

Append-only `CompanyMonthlyProductAnalysisPublicationEvent` fields: ID, snapshot ID, type (`Calculated`, `ValidationPassed`, `Published`, `PublicationRejected`, `ReplacedAsCurrent`), reason code, event time, actor/run/correlation IDs, and optional prior-current snapshot ID. Events describe history; they are never edited.

### 20.2 Current pointer schema

`CompanyMonthlyProductAnalysisCurrentPointer`:

| Field | Contract |
| --- | --- |
| `Id` | GUID. |
| `ExternalCompanyId` | Required. |
| `CurrentMonthOrdinal` | Required. |
| `ComparisonMonthOrdinal` | Required for two-period comparison. |
| `ComparisonKind` | Closed enum code. |
| `AnalysisPolicyFamily` | Stable family code; policy revision remains on snapshot. |
| `CurrentSnapshotId` | Required FK to immutable snapshot. |
| `PublicationEventId` | Optional/required-on-published FK to the `Published` event. |
| `UpdatedAtUtc` | Projection update timestamp. |
| `xmin` | Npgsql concurrency token. |

Unique business key: `(ExternalCompanyId, CurrentMonthOrdinal, ComparisonMonthOrdinal, ComparisonKind, AnalysisPolicyFamily)`. The current snapshot must have the identical business key. A non-unique snapshot-key/version index supports history; pointer snapshot FK uses `Restrict`.

### 20.3 Atomic publication protocol

Lock key:

`f129-snapshot|{company}|{currentOrdinal}|{comparisonOrdinal}|{comparisonKind}|{policyFamily}`.

1. Calculate from immutable inputs and record run `Running` outside the publication transaction.
2. Begin `Serializable`; acquire the transaction advisory lock before pointer/input reads.
3. Re-read accepted revision, manifest, current approved alias ownership revision, and policies. If different, roll back, record `StaleInput`, and request the new fingerprint through the outbox.
4. If the identical immutable snapshot exists and is current, return `NoOp`. If it exists but is not current, it may become current only after the same input/current-policy validation.
5. Allocate the immutable business-key version; insert snapshot, children, evidence, `Calculated`, and `ValidationPassed` events.
6. Validate inserted stored-scale equations and required evidence. A structural/transaction failure rolls back financial facts and records the failed run outside; no partial snapshot remains. A complete audit-worthy calculated snapshot that fails only publication policy may instead commit with a `PublicationRejected` event and no pointer change.
7. Append `Published` for a publishable new snapshot and, when replacing a pointer, `ReplacedAsCurrent` for the old snapshot. Do not update either snapshot.
8. Insert the pointer or update `CurrentSnapshotId`, event ID, and timestamp using its original `xmin`.
9. Commit; then emit completion/cache invalidation through the appropriate post-commit mechanism.

SQLSTATE `40001`, `40P01`, `23505`, and concurrency conflicts retry at most three times with bounded jitter. Identical concurrent work results in one snapshot/current pointer and one no-op. Different concurrent work revalidates inputs; only the writer whose immutable inputs are still approved may move the pointer. A stale writer appends no publication event and cannot overwrite a newer pointer. Old snapshots are never mutated after insertion.

Current/superseded is derived: a snapshot is current iff referenced by the pointer; it was published/replaced based on events. Query/caches use pointer snapshot ID and result schema version. Cache invalidation never changes snapshot facts.

## 21. Alias/snapshot historical behavior

Snapshot fingerprints and evidence store the exact `ApprovedAliasSetRevisionId`, membership IDs, product lineage decisions, report revision/observation IDs, unit policy ID, and calculation policy ID. Calculator matching uses the immutable selected revision membership set, not `CurrentApprovedCompanyProductAliasOwnership` after the input is fixed.

An alias merge, split, range edit, or reversal atomically changes the current ownership projection and requests new computations for intersecting periods. New snapshots may become current; old snapshots and conversations remain linked to the old alias revision and retain their product identities and values. A superseded approval cannot block a new approval because its rows no longer exist in the current projection.

## 22. Evidence and audit

Every internal evidence fact includes snapshot/item/insight IDs, report revision/observation IDs, raw payload ID/checksum, source discriminator/signature, numeric lexeme/value, raw/canonical units, company/period/output type, provider publication/receipt/sync times, alias revision/membership, unit/calculation policy, formula/rule, and calculated value. It copies the actual fact even while referencing immutable source rows.

Public evidence is bounded to 5 facts per insight, 4 per displayed product field, 12 report references, and 100 contribution members. It omits raw payload text, credentials, full ordinary-user checksums, stack traces, and rejected semantic text. DataAdmin audit may reveal permitted IDs/checksums, never provider secrets or raw payload bodies through Feature 129.

## 23. API and structured result

Application owns `MonthlyProductAnalysisResult` with schema `monthly-product-analysis/1`. API, V1, V2, conversation, Telegram, and frontend map it without financial arithmetic. It is a discriminated structure containing company/period/comparison, units, exact snapshot metadata, summary, product items, effect totals, quality, warnings, bounded evidence, and contribution view model.

Statuses: `Executed`, `Partial`, `NoData`, `ClarificationRequired`, `TemporarilyUnavailable`. Limits: 100 products, default top 20, 24 history months, and section 22 evidence bounds.

The optional later endpoint, if approved, is `GET /api/v1/companies/{symbol}/monthly-product-analysis?...`; it uses a dedicated authorization/entitlement/rate policy, reads the same DTO, never calls the provider, and has a strong ETag derived from snapshot ID, schema version, and fingerprint.

## 24. Semantic replay equality

`AssistantMessagePayload.Version=3` embeds the complete bounded result, snapshot fingerprint, and final deterministic Persian narrative. Narrative is persisted and replayed exactly; it is not regenerated during history reads.

Historical replay means **semantic schema equality with exact financial values and immutable identifiers**:

- same result schema version;
- exactly equal decimal coefficient/scale-independent numeric values;
- same immutable snapshot, report revision, observation, alias revision/membership, unit policy, and calculation policy IDs;
- same enum/reason codes, units, Jalali periods, comparison, warnings, evidence facts, and stable ordering of products/effects/Other members;
- same persisted deterministic narrative when included in the assistant message.

JSON whitespace, object property order, serializer metadata, escaping choices that decode to the same Unicode string, and transport formatting are excluded. `MonthlyProductAnalysisSemanticComparer` (proposed test/application utility) compares decoded typed contracts field-by-field, decimals numerically and immutable IDs exactly; it does not compare serialized bytes.

History mapping returns the embedded decoded result and narrative without a latest-snapshot query. Payload v1/v2 decoders retain existing behavior. Unknown future result kinds remain bounded opaque JSON and degrade safely.

## 25. Typed semantic slot proposal contracts

### 25.1 Closed contracts

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

record ValidatedSemanticSlot(
  QuerySlotType SlotType, string SlotName, SemanticSlotValueKind ValueKind,
  SemanticSlotValue Value, QueryValueProvenance Provenance,
  decimal Confidence, EvidenceSpan EvidenceSpan,
  SemanticSlotValidationResult Validation);
```

`SemanticSlotValue` is a closed discriminated union; exactly one value member is present. `JalaliPeriodValue` is `{year, month, canonical:"YYYY-MM"}` with year 1300–1600 and month 1–12. Comparison codes are `PreviousMonth`, `SameMonthPreviousYear`, and `ExplicitPeriod`. Focus codes are `Summary`, `Contributors`, `PriceQuantity`, `ProductionSales`, `Mix`, and `PotentialInventory`. Measure codes are `Revenue`, `ProductionQuantity`, `SalesQuantity`, and `Rate`. Presentation codes reuse the governed registry.

Resolver status: `NotApplicable`, `Unresolved`, `Resolved`, `Ambiguous`, `NotFound`. Validation: `Proposed`, `Valid`, `Rejected`, `Ambiguous`, `Unsupported`, `Invalid`. Provenance: `UserExplicit`, `DeterministicExtraction`, `ModelProposal`, `ConversationCarryOver`, `Defaulted`, `CanonicalResolver`.

### 25.2 Proposal JSON schema

The model contract uses `additionalProperties:false`, maximum 16 slots, unique slot names, bounded 256-character raw/normalized values, confidence `[0,1]`, nonnegative spans wholly inside the normalized user message, and closed enums. The essential schema is:

```json
{
  "type": "object",
  "additionalProperties": false,
  "required": ["capabilityCodes", "slots", "confidence", "evidence"],
  "properties": {
    "capabilityCodes": { "type": "array", "maxItems": 8, "items": { "type": "string", "maxLength": 128 } },
    "slots": {
      "type": "array", "maxItems": 16,
      "items": {
        "type": "object", "additionalProperties": false,
        "required": ["slotName", "valueKind", "rawText", "confidence", "evidenceSpan"],
        "properties": {
          "slotName": { "enum": ["symbol", "product", "products", "period", "comparison", "analysisFocus", "measure", "presentation", "limit"] },
          "valueKind": { "enum": ["Text", "CanonicalEntityReference", "JalaliPeriod", "ComparisonKind", "AnalysisFocus", "Measure", "Presentation", "Integer"] },
          "rawText": { "type": "string", "maxLength": 256 },
          "normalizedValue": { "type": ["string", "null"], "maxLength": 256 },
          "canonicalEntityId": { "type": ["string", "null"], "format": "uuid" },
          "jalaliPeriod": { "type": ["object", "null"] },
          "comparisonKind": { "type": ["string", "null"] },
          "analysisFocus": { "type": ["string", "null"] },
          "measure": { "type": ["string", "null"] },
          "presentation": { "type": ["string", "null"] },
          "integerValue": { "type": ["integer", "null"], "minimum": 1, "maximum": 100 },
          "confidence": { "type": "number", "minimum": 0, "maximum": 1 },
          "evidenceSpan": {
            "type": "object", "additionalProperties": false,
            "required": ["start", "length"],
            "properties": { "start": {"type":"integer","minimum":0}, "length": {"type":"integer","minimum":1,"maximum":256} }
          }
        }
      }
    },
    "confidence": { "type": "number", "minimum": 0, "maximum": 1 },
    "evidence": { "type": "array", "maxItems": 16, "items": { "type": "string", "maxLength": 256 } }
  }
}
```

Deserializer additionally enforces the exact value-kind/member matrix; for example `JalaliPeriod` requires `jalaliPeriod` and forbids enum/text payload members beyond raw/normalized evidence. Unknown slot/value kinds, duplicate scalar slots, malformed UUIDs/periods, unsupported enum values, inconsistent normalized values, invalid spans, or unexpected properties reject the entire model proposal with a fixed safe reason and fall back to deterministic interpretation. The wire proposal is deliberately limited to candidate data. During deserialization the server sets provenance to `ModelProposal`, resolver status to `Unresolved`/`NotApplicable`, validation status to `Proposed`, and rejection reason to null; subsequent stages return the full internal contract with resolver/validation outcome and a fixed ambiguity/rejection reason. A model cannot self-declare a candidate resolved or valid.

### 25.3 Example proposal

For a request asking whether `سبزیجات ۴۰ گرمی` at `غاذر` in 1405/05 changed due to price or quantity versus the prior month:

```json
{
  "capabilityCodes": ["monthly_product_activity_analysis"],
  "slots": [
    {"slotName":"symbol","valueKind":"Text","rawText":"غاذر","normalizedValue":"غاذر","canonicalEntityId":null,"confidence":0.99,"evidenceSpan":{"start":0,"length":4}},
    {"slotName":"product","valueKind":"Text","rawText":"سبزیجات ۴۰ گرمی","normalizedValue":"سبزیجات ۴۰ گرمی","canonicalEntityId":null,"confidence":0.98,"evidenceSpan":{"start":5,"length":17}},
    {"slotName":"period","valueKind":"JalaliPeriod","rawText":"۱۴۰۵/۰۵","normalizedValue":"1405-05","jalaliPeriod":{"year":1405,"month":5,"canonical":"1405-05"},"confidence":1.0,"evidenceSpan":{"start":23,"length":7}},
    {"slotName":"comparison","valueKind":"ComparisonKind","rawText":"ماه قبل","normalizedValue":"PreviousMonth","comparisonKind":"PreviousMonth","confidence":0.99,"evidenceSpan":{"start":31,"length":7}},
    {"slotName":"analysisFocus","valueKind":"AnalysisFocus","rawText":"قیمتی یا مقداری","normalizedValue":"PriceQuantity","analysisFocus":"PriceQuantity","confidence":0.99,"evidenceSpan":{"start":39,"length":15}},
    {"slotName":"measure","valueKind":"Measure","rawText":"قیمتی یا مقداری","normalizedValue":"Revenue","measure":"Revenue","confidence":0.91,"evidenceSpan":{"start":39,"length":15}},
    {"slotName":"presentation","valueKind":"Presentation","rawText":"خلاصه","normalizedValue":"Summary","presentation":"Summary","confidence":0.88,"evidenceSpan":{"start":55,"length":5}}
  ],
  "confidence": 0.97,
  "evidence": ["explicit company, product, period, prior-month comparison, and price/quantity focus"]
}
```

Model-provided canonical IDs are never trusted. Even when present, canonical company/product resolution reruns server-side and replaces the candidate ID in the validated value.

## 26. Slot merge, canonical resolution, and clarification

Complete flow:

```text
User message
→ semantic capability/slot proposal
→ JSON schema and value-kind validation
→ deterministic extraction/normalization
→ canonical company/product resolution
→ conflict and confidence governance
→ ValidatedQueryFrame
→ ConversationTaskState
→ Feature 129 executor
```

Precedence for a scalar slot: current-turn user-explicit deterministic extraction; current-turn canonically resolved model candidate when nonconflicting and above threshold; valid conversation carryover; governed default. A model candidate never overrides a conflicting explicit deterministic value. Equal-precedence unequal candidates become `AmbiguousSlotConflict`; no executor call occurs.

Merge rules:

- Combine semantically equal candidates and retain all provenance/evidence with the highest safe confidence.
- Reject slot names unsupported by the selected capability.
- Company resolution occurs before product resolution; product resolver is company- and period-scoped and uses current approved ownership only to resolve the request, then returns canonical ID plus the alias revision used.
- An ambiguous company/product yields bounded candidates and expected slot in task state. An unresolved explicit period or conflicting current/base period asks clarification rather than defaulting.
- `PreviousMonth` is deterministically converted to a concrete Jalali comparison period only after current period resolution.
- Defaults apply only when the user supplied no conflicting evidence: latest current pointer, previous Jalali month, summary focus, revenue measure, summary presentation.

`QueryInterpretation` gains typed proposal candidates and normalized selections. `ResolvedQuerySlot`/`ValidatedSemanticSlot` carry a closed value envelope rather than an executor-parsed arbitrary string. `ValidatedQueryFrame` contains only valid slots and exact canonical IDs/periods/enums. `ConversationTaskState` persists the same validated value envelope, alias-resolution revision, provenance, confidence, originating message/state version, and expiry; it never stores a rejected raw model value as an active slot.

V1, native V2, and V2 fallback call the same dialogue gate, slot merger/validator, canonical resolvers, `ValidatedQueryFrame` builder, and executor. There is no second V2 slot parser.

Prompt-injection defense: proposals are parsed only as closed JSON data; raw text is length-bounded and never interpolated into SQL, routes, feature codes, enum switches, or formulas. Registry membership, entity/period bounds, allowed focus/measure/presentation, authorization, collection limits, and publication availability are checked after model output. Rejected or unsupported values are excluded from state and executor input.

## 27. Capability routing and executor

Deterministic precedence remains: scanner threshold queries; direct metric lookup; statements/disclosures/valuation; existing monthly trend for a time series without product causality; product revenue mix for single-period composition; Feature 129 for explicit comparison/contributor/price-quantity/product production-sales/cause focus; comprehensive-analysis fallback.

`MonthlyProductActivityAnalysisCapabilityExecutor` accepts only `ValidatedQueryFrame`. It maps typed values into `MonthlyProductAnalysisQuery`, reads the current pointer/snapshot repository, and returns the structured result. It cannot accept raw proposal JSON or unresolved slot strings. The calculator is internal to background computation and cannot be invoked by the executor.

The LLM receives bounded calculated facts and may choose section order/connective Persian prose. Numeric consistency validates all output. The persisted narrative is produced by a versioned deterministic renderer in release 1; optional later stylistic model text cannot replace or alter financial statements.

## 28. Conversation persistence and backward compatibility

Payload v3 adds discriminated `StructuredResults`, typed result schema/version, exact snapshot fingerprint, and deterministic narrative. Both V1 and V2 persist v3 for Feature 129. Existing v1/v2 payloads decode as before; unknown kinds degrade safely.

Replay integration tests persist a Feature 129 response, then independently introduce a new report revision, alias merge/split/reversal, calculation-policy revision, new current snapshot, and compatible serializer update. Every replay must pass the semantic comparator and return the original persisted narrative. No history read may query the current pointer or current alias ownership.

## 29. UX, frontend, and Telegram

Release 1 renders summary, limitations, product table, accessible contribution table, selected-product production/sales visualization, evidence, all freshness/result states, RTL, and mobile. Advanced plotted waterfall is optional.

The server supplies contribution item ID, canonical/unmatched identity, Persian label, exact amount, cumulative start/end, type, stable order, evidence key, and inspectable `Other` members. It begins at base subtotal, applies top-N product contributions ordered by absolute amount then stable identity, includes server-summed `Other`, and ends at current total. Client code computes pixels only, never financial values.

Telegram renders a bounded summary, top positive/negative contributors, driver/quality/freshness, explicit unit/period, and operational-analysis limitation. It does not fabricate a chart.

## 30. Security, observability, and operations

AI reads retain current authentication, tenant/actor ownership, billing, rate limits, and entitlements. Alias/revision decisions, backfills, dead-letter recovery, and rebuilds require DataAdmin initially and append actor/reason audit events. Validate all company/product/period/limit/ETag inputs and use parameterized EF queries.

Metrics include fixed operation/revision/manifest/outbox/job/run/publication/coverage/semantic/replay outcomes. Allowed labels are provider, fixed operation/status/reason, policy/schema version, comparison kind, and rollout cohort. Structured logs carry bounded correlation and immutable IDs, not payload/query/product text. Runbooks cover failed operations, ambiguous revisions, alias collisions, outbox recovery, duplicate delivery, stale publication, serialization retries, and 1404 backfill resume.

## 31. Acceptance criteria

AC-01–AC-60 are first-release core requirements; AC-61–AC-62 are optional later-slice requirements; AC-63–AC-69 are cross-cutting release, operations, transport, and traceability gates. Every criterion maps to a named test and slice.

| AC | Objective criterion | Test | Slice |
| --- | --- | --- | --- |
| AC-01 | Distinct source rows sharing one product code persist separately and both enter totals. | T-ING-01 | 1 |
| AC-02 | Payload reorder preserves semantic checksum/discriminator multiset; array ordinal is evidence only. | T-ING-02 | 1 |
| AC-03 | Exact/semantic replay creates no economic revision and appends a receipt. | T-ING-03 | 1 |
| AC-04 | Raw row count/revenue exactly equals normalized observations or acceptance is blocked. | T-ING-04 | 1 |
| AC-05 | Manifest distinguishes every ProductSales 0–4/ServiceSales success-empty/failure/retry state. | T-MAN-01 | 1 |
| AC-06 | No job/outbox is created before type 0 is accepted/core-ready; optional failures are explicit partials. | T-MAN-02 | 1 |
| AC-07 | A later accepted retry creates a new manifest generation and idempotent request. | T-MAN-03 | 1 |
| AC-08 | Corrections append immutable revisions/observations and never delete prior facts. | T-REV-01 | 1 |
| AC-09 | Provider revision/publication precedence selects one deterministic accepted revision. | T-REV-02 | 1 |
| AC-10 | A late older payload cannot replace a newer accepted revision. | T-REV-03 | 1 |
| AC-11 | Equal-precedence conflicts retain/null the pointer and require audited decision. | T-REV-04 | 1 |
| AC-12 | Concurrent candidates yield one deterministic pointer under lock/`xmin`. | T-REV-05 | 1 |
| AC-13 | Manual revision decisions are authorized, reasoned, append-only, and schedule through outbox. | T-REV-06 | 1 |
| AC-14 | Blank/zero vendor IDs are absent; array position never affects lasting identity. | T-ID-01 | 2 |
| AC-15 | Incompatible signatures cannot auto-merge; collision revenue remains unmatched. | T-ID-02 | 2 |
| AC-16 | Alias-set revisions/memberships are immutable and reproduce a historical match set. | T-ALIAS-01 | 2 |
| AC-17 | Approval atomically appends events, replaces affected current ownership, and creates recalculation outbox rows. | T-ALIAS-02 | 2 |
| AC-18 | Concurrent overlapping current ownership is rejected by the projection GiST constraint. | T-ALIAS-03 | 2 |
| AC-19 | Superseded revision memberships are absent from current ownership and do not block a later overlap. | T-ALIAS-04 | 2 |
| AC-20 | Merge, split, reversal, and range edit create new revisions/lineage without rewriting history. | T-ALIAS-05 | 2 |
| AC-21 | Approval failure/rollback leaves prior ownership and outbox state unchanged. | T-ALIAS-06 | 2 |
| AC-22 | Historical snapshots retain original alias revision/memberships after current ownership changes. | T-ALIAS-07 | 2, 3 |
| AC-23 | Monthly revenue sums all accepted type-0 observations including negatives and excludes other types/services. | T-CALC-01 | 3 |
| AC-24 | Product union returns distinct current/base facts, guarded changes, rank, and concentration. | T-CALC-02 | 3 |
| AC-25 | Safe continuing products use symmetric quantity/price and balancing residual. | T-CALC-03 | 3 |
| AC-26 | Activation requires absent/zero base revenue; discontinuation requires positive base and absent/zero current. | T-CALC-04 | 3 |
| AC-27 | Every state routes the entire contribution exactly once across seven buckets. | T-CALC-05 | 3 |
| AC-28 | Product/company equations reconcile exactly at stored scale or publication is blocked. | T-CALC-06 | 3 |
| AC-29 | Unit/rate/quantity/sign/identity unsafe cases retain signed contribution in the specified safe bucket. | T-CALC-07 | 3 |
| AC-30 | Precision, ToEven rounding, serialization, and tolerance boundaries match section 13. | T-DEC-01 | 3 |
| AC-31 | Zero/immaterial company change yields null shares/classification with exact reason codes. | T-DEC-02 | 3 |
| AC-32 | Cancellation guard prevents misleading price/quantity driver classification. | T-CLASS-01 | 3 |
| AC-33 | Every public number has bounded evidence and self-sufficient immutable internal evidence. | T-EVID-01 | 1, 3 |
| AC-34 | Evidence retains exact old report/alias/policy values after correction/reversal. | T-EVID-02 | 3 |
| AC-35 | No snapshot/evidence query resolves through mutable normalized rows or current alias ownership. | T-EVID-03 | 3 |
| AC-36 | Snapshot facts/children/evidence and publication events are never updated after insertion. | T-PUB-01 | 3 |
| AC-37 | Exactly one current pointer exists per complete business key and matches the referenced snapshot key. | T-PUB-02 | 3 |
| AC-38 | Publication appends events and changes only the pointer; the prior snapshot row is byte-unchanged. | T-PUB-03 | 3 |
| AC-39 | Concurrent identical publications yield one snapshot/current pointer plus no-op. | T-PUB-04 | 3 |
| AC-40 | Concurrent different publications allow only still-approved immutable inputs to move the pointer. | T-PUB-05 | 3 |
| AC-41 | `xmin` stale pointer writers fail/retry and cannot overwrite a newer pointer. | T-PUB-06 | 3 |
| AC-42 | Child/validation failure rolls back facts/events/pointer and preserves prior current result. | T-PUB-07 | 3 |
| AC-43 | Cache/query state is derived from pointer/events and never requires mutable snapshot flags. | T-PUB-08 | 3, 4 |
| AC-44 | Manifest/alias transaction commits job and outbox atomically with unique idempotency keys. | T-ORCH-01 | 1, 2 |
| AC-45 | Feature 129 cannot invoke both direct scheduler publication and transactional outbox. | T-ORCH-02 | 1 |
| AC-46 | Dispatcher leasing/confirm/retry recovers all undispatched/expired rows without parallel ownership. | T-ORCH-03 | 1 |
| AC-47 | Crash after broker publish but before acknowledgement produces an idempotent duplicate and one computation/publication. | T-ORCH-04 | 3 |
| AC-48 | Transient/dead-letter recovery, duplicate Rabbit delivery, and terminal handler replay have fixed outcomes. | T-ORCH-05–07 | 3, 6 |
| AC-49 | Typed proposal schema rejects unknown kinds/names, malformed values, invalid spans, extra properties, and oversized collections. | T-SEM-01 | 4 |
| AC-50 | Company/product/period/comparison/focus/measure/presentation candidates deserialize into the closed typed contract. | T-SEM-02 | 4 |
| AC-51 | Explicit deterministic values outrank model/carryover/default values; equal conflicts clarify. | T-SEM-03 | 4 |
| AC-52 | Canonical company/product resolution replaces model raw/canonical values and ambiguity asks bounded clarification. | T-SEM-04 | 4 |
| AC-53 | Invalid product, period, focus, measure, or presentation never reaches an executor. | T-SEM-05 | 4 |
| AC-54 | Follow-up task state carries the same validated company/product/period/comparison/focus/measure envelope. | T-SEM-06 | 4 |
| AC-55 | V1/native V2/fallback V2 consume the same validated slots and produce identical structured numbers/reasons. | T-SEM-07 | 4 |
| AC-56 | Prompt-injection/unsupported slot content cannot select SQL/routes/formulas or bypass capability/authorization validation. | T-SEM-08 | 4 |
| AC-57 | Live and persisted payload v3 carry result schema, exact decimals/IDs/enums/units/ordering/warnings/evidence. | T-CONV-01 | 4 |
| AC-58 | Replay after report, alias, policy, or current-snapshot changes has semantic schema equality with exact financial values and immutable identifiers. | T-CONV-02 | 4 |
| AC-59 | Compatible serializer updates preserve semantic equality despite whitespace/property-order/metadata differences. | T-CONV-03 | 4 |
| AC-60 | Persisted deterministic Persian narrative replays exactly; v1/v2/unknown-kind decoding remains safe. | T-CONV-04–05 | 4 |
| AC-61 | If enabled, YoY/YTD/averages/anomalies meet exact accepted-revision, contiguity, and wording policies. | T-HIST-01–03 | 6 |
| AC-62 | If enabled, direct endpoint enforces auth/rate/entitlement and strong ETag/304 semantics. | T-API-03 | 6 |
| AC-63 | Exact fixture text is `غاذر` and `سبزیجات ۴۰ گرمی`; malformed order/ZWNJ and `۴۰۰` variants are absent. | T-FIX-01 | 1–6 |
| AC-64 | Impact categories are disjoint, every AC maps to a named test/slice, and only documentation is changed by this task. | T-TRACE-01 | 1–6 |
| AC-65 | Backfill rejects periods before 1404/01, resumes from durable progress, is bounded/throttled, and is idempotent after every failure point. | T-BF-01–04 | 6 |
| AC-66 | Result schema v1 maps through live API and persisted payload v3 without financial recomputation. | T-API-01 | 4 |
| AC-67 | Telegram returns a bounded explicit-unit summary/evidence/limitation and never fabricates a chart or investment recommendation. | T-API-02 | 4 |
| AC-68 | Contribution/table/product views preserve server amounts, starts/ends/order/Other members, expose accessible evidence, and remain usable in RTL/mobile states. | T-UI-01–02 | 5 |
| AC-69 | Security tests expose no provider credential/raw payload, and bounded repository/facade performance meets section 7 targets. | T-SEC-01, T-PERF-01 | 4–6 |

## 32. Testing strategy

- `T-ING-01–04`: repeated codes, duplicate occurrences, reordering, replay, raw mismatch, numeric scale.
- `T-MAN-01–03`, `T-REV-01–06`: operation-state matrix, retries, revision precedence, late/equal/concurrent/manual decisions.
- `T-ID-01–02`, `T-ALIAS-01–07`: zero/reused IDs, signatures, real PostgreSQL GiST overlap, atomic replacement under concurrency, superseded ownership, rollback, merge/split/reversal, historical alias retention.
- `T-CALC-01–07`, `T-DEC-01–02`, `T-CLASS-01`: representative `غاذر` fixture, property-based seven-bucket identity, precision and cancellation boundaries.
- `T-EVID-01–03`: immutable copied facts, correction/reversal replay, prohibition on current projections.
- `T-PUB-01–08`: database triggers/change tracking proving immutable rows, pointer uniqueness/FK, old-row hash before/after replacement, identical/different concurrency, stale `xmin`, rollback, cache derivation.
- `T-ORCH-01–07`: transaction rollback, direct/outbox mutual exclusion, `SKIP LOCKED` lease competition, broker confirms, crash after publish, duplicate delivery, retry/dead-letter/recovery.
- `T-SEM-01–08`: JSON-schema/value-kind rejection, all typed values, deterministic/model conflict, product/period/focus errors, clarification, task carryover, V1/V2 equality, injection cases.
- `T-CONV-01–05`: typed payload, semantic comparator after report/alias/policy/pointer changes, serializer options update, exact narrative, v1/v2/unknown-kind decoding.
- `T-API-01–03`, `T-UI-01–02`: live mapping, Telegram, optional endpoint, contribution totals/Other, accessible RTL/mobile states.
- `T-BF-01–04`, `T-HIST-01–03`: restartable bounded 1404 backfill and optional history rules.
- `T-SEC-01`, `T-PERF-01`: credential/raw-payload non-disclosure and bounded repository/facade load targets.
- `T-FIX-01`: ordinal Unicode assertions plus whole-document scan for wrong product size and symbol character order/ZWNJ variants.
- `T-TRACE-01`: mechanically verifies findings, AC/test/slice mappings, disjoint impact categories, required files, and documentation-only diff.

## 33. Vertical-slice implementation plan

### Slice 1 — Immutable source, revisions, manifest, and durable request

No user-facing analysis. Add observations, revisions/events/pointers, manifest, raw reconciliation, Feature 129 job/outbox writer and dispatcher. Keep direct scheduler unchanged. Done when accepted facts are deterministic and every core-ready request is durably recoverable.

### Slice 2 — Canonical product, alias history, and current ownership

Add canonical products, immutable revisions/memberships/approval events/lineage, current ownership projection, GiST constraint, atomic approval/reversal, unit policy, and affected-period outbox requests. No snapshot publication until identity is stable.

### Slice 3 — Calculator and immutable publication

Add keyed Feature 129 handler, seven-bucket calculator/classifier, immutable snapshot/items/evidence/events, current pointer, query repository, and cache invalidation. Shadow calculations precede internal publication.

### Slice 4 — Typed semantic/API/conversation end to end

Add typed proposal schema/deserializer/merge, product resolver, shared validated frame/task-state values, executor, V1/V2 mapping, payload v3, semantic comparator, deterministic narrative, Telegram, and frontend transport. Roll out proposal use shadow → allowlist → gradual; deterministic fallback remains.

### Slice 5 — Investor-facing web experience

Add summary, limitations, contribution/product/evidence tables, selected-product chart, RTL/mobile/accessibility, and optional waterfall visualization. Client performs layout only.

### Slice 6 — History and operational hardening

Add approved optional YoY/YTD/averages/anomalies/inferred signals, restartable 1404 backfill, optional endpoint, SLO/load tests, dead-letter operations, dashboards, and runbooks.

## 34. Dependencies, decisions, and unresolved business decisions

Decisions: reported revenue is authoritative; symmetric decomposition plus balancing residual is used only for safe continuing products; alias/snapshot facts are immutable; mutable current state exists only in explicit projections; Feature 129 uses transactional outbox exclusively; semantic values use a closed validated contract; replay equality is semantic; backfill starts at 1404/01.

Dependencies: NADPCO/raw store, Financial ingestion DbContext, canonical company resolver, existing feature job/RabbitMQ consumer infrastructure, semantic registry/task state, AI facade/conversation/billing/auth, frontend structured chat, and Telegram renderer.

Genuinely unresolved business decisions:

1. Confirm contractual NADPCO monetary unit per tenant using a provider fixture and reconciled sample; repository convention currently indicates million rial.
2. Approve the initial unit-conversion dictionary; safe default is none.
3. Choose business freshness SLA and economic materiality floor if different from v1 defaults.
4. Decide whether ServiceSales belongs in a separate future service-analysis feature; it is excluded here.

These do not change architecture; safe defaults block/suppress the affected claim.

## 35. File-by-file implementation impact map

### 35.1 Existing files to modify

| Path | Planned impact |
| --- | --- |
| `NadpcoApiDataProviderClient.cs`, `ProviderRawPayloadPersistence.cs`, `NadpcoApiMonthlyActivityNormalizer.cs`, `MonthlyActivityBackfillCoordinator.cs` | Explicit operation outcomes, raw linkage, immutable revision/observation population, compatibility projection, 1404 boundary. |
| `FinancialIngestionContracts.cs`, `FinancialDataSyncProcessor.cs` | Carry manifest/revision IDs and call only the transactional Feature 129 request writer inside the ingestion transaction. |
| `FinancialIngestionRows.cs`, `FinancialIngestionConfigurations.cs`, `FinancialIngestionDbContext.cs` | Register proposed revisions/manifests/alias ownership/outbox/snapshot events/current pointer models, precision, `xmin`, filtered indexes, and GiST constraint. |
| `FeatureModels.cs`, `DerivedFeatureContracts.cs`, `FeatureComputationProcessor.cs`, `PersistedFeatureServices.cs` | Add dispatch mode/keyed handler contracts and route Feature 129 to its specialized handler while retaining existing scalar behavior. |
| `RabbitMqFeatureMessaging.cs`, `FeatureComputationConsumerWorker.cs`, Worker `Program.cs` | Preserve at-least-once consumption and register Feature 129 outbox dispatcher/recovery. |
| `ServiceCollectionExtensions.cs` | Register new repositories, policies, handlers, dispatcher, typed proposal provider/validator, resolvers, executor, and renderer. |
| `CanonicalQueryEntityContracts.cs`, `CapabilityInterpretationGovernance.cs`, `ConversationalCapabilityContracts.cs`, `ConversationTaskStateContracts.cs`, `SemanticCapabilityExecutionContracts.cs`, `SemanticCapabilityExecutors.cs` | Add closed typed slot proposal/value/validation contracts, merge/canonical resolution, state persistence, capability, and executor. |
| `AiOrchestrationContracts.cs`, `AiQueryOrchestrationService.cs`, V2 workflow messages/definition/runner, `MessagePersistenceFunction.cs` | Shared validated frame/result mapping, deterministic narrative, payload v3 persistence, V1/V2 parity. |
| `ConversationContracts.cs`, `ConversationRepositories.cs`, `AiFacadeContracts.cs`, `AiFacadeController.cs` | Typed result, backward decoder, semantic replay mapper/comparator integration, live/history HTTP mapping. |
| `TelegramAssistantResponseRenderer.cs`, `chat.functions.ts`, `message-list.tsx` | Bounded Telegram fallback, discriminated frontend schema, and result rendering. |

### 35.2 Existing files inspected but intentionally unchanged

| Path | Reason |
| --- | --- |
| `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/MetricRecalculationProcessor.cs` | Feature 129 is not a metric-registry branch and must not route through it. |
| `IndustryRelativeValuationCalculationSnapshotWriter.cs` | Advisory-lock ordering was inspected as precedent; its mutable current flags are not reused. |
| Existing `FeatureRecalculationScheduler.ScheduleAsync` implementation | Global direct-publish behavior remains unchanged for unrelated features; Feature 129 bypasses it. |
| Existing migrations | Historical migrations are never edited; any implementation migration requires separate review. |

### 35.3 Proposed new files

- Application: `MonthlyProductAnalysisContracts.cs`, `MonthlyProductAnalysisPolicies.cs`, `MonthlyProductAnalysisCalculator.cs`, `CanonicalProductContracts.cs`, `ReportRevisionContracts.cs`, `IngestionManifestContracts.cs`, `SemanticSlotProposalContracts.cs`, `MonthlyProductAnalysisSemanticComparer.cs`, and `IFeature129ComputationRequestWriter.cs`.
- Infrastructure: revision ingestor/acceptance service, canonicalizer/alias approval service, Feature 129 request writer, outbox dispatcher, keyed computation handler, snapshot writer/query repository, and row/configuration files.
- Frontend: `monthly-product-analysis.tsx`, view model, contribution/table/evidence/selected-product components, and colocated tests.
- Tests: named `T-*` families in section 32 within existing backend/frontend test projects.

### 35.4 Optional later-slice files

- `MonthlyProductAnalysisController.cs` and dedicated authorization/permission entries for the optional direct endpoint.
- Historical/anomaly/inferred-inventory services and advanced waterfall/image-export components.
- No optional file is required for first-release correctness.

## 36. Final readiness checklist

- [x] V2-M-01 through V2-M-07 have concrete schema, workflow, AC, test, and slice resolutions.
- [x] Alias overlap is constrained only on fields present in current ownership.
- [x] Alias approval atomically replaces current ownership and preserves immutable history.
- [x] Snapshot facts contain no mutable current/status field; events and a concrete pointer own publication state.
- [x] Typed slot values flow from proposal through validation, canonical resolution, frame, state, and shared executor.
- [x] Feature 129 has one durable dispatch path and cannot use direct publication.
- [x] Replay equality is semantic with exact decimals/immutable IDs and exact persisted narrative.
- [x] The fixture uses `غاذر` and `سبزیجات ۴۰ گرمی` consistently.
- [x] Every AC maps to a named test and slice.
- [x] `MetricRecalculationProcessor.cs` appears only as inspected and intentionally unchanged.
- [x] No implementation, migration, provider call, or production-data change is authorized by this design.

**Final status:** `READY_FOR_DESIGN_REVIEW`
