# Feature 129 Design Review

## 1. Review scope and repository evidence

I reviewed the complete Feature 129 design, repository implementation, migrations, and relevant specifications.

Key repository evidence inspected:

- Ingestion/provider:
  - `NadpcoApiDataProviderClient.cs`
  - `NadpcoApiMonthlyActivityNormalizer.cs`
  - `FinancialDataSyncProcessor.cs`
  - `MetricRecalculationProcessor.cs`
  - `ProviderRawPayloadPersistence.cs`
- Persistence and migrations:
  - `FinancialIngestionRows.cs`
  - `FinancialIngestionConfigurations.cs`
  - `20260527045316_InitialFinancialIngestion.cs`
  - `20260527151735_AddDerivedFeatureFoundation.cs`
  - Product-mix, monthly-trend, report-uniqueness, task-state, and durable-backfill migrations.
- Existing derived calculations:
  - Product revenue mix and monthly trend calculators, repositories, backfills, and tests.
  - `FeatureComputationProcessor.cs`
  - `IndustryRelativeValuationCalculationSnapshotWriter.cs`
- Semantic orchestration:
  - `DeterministicCapabilityInterpreter.cs`
  - `CapabilityInterpretationGovernance.cs`
  - `CanonicalQueryEntityContracts.cs`
  - `ConversationTaskStateContracts.cs`
  - V1, native V2, V2 fallback, registry, executors, facade, and response-consistency paths.
- API, persistence, and frontend:
  - `ConversationContracts.cs`
  - `MessagePersistenceFunction.cs`
  - `AiFacadeController.cs`
  - `chat.functions.ts`
  - `message-list.tsx`
  - `monthly-activity-trend-chart.tsx`
- Specifications:
  - Features 042, 057, 059, 075–079, 118–123, and 128, including implementation evidence and stale-status caveats.

Verification executed:

- 120 targeted backend unit tests passed.
- 8 targeted frontend tests passed.
- No repository source files were modified during the review.

## 2. Overall assessment

The design is unusually thorough and gets the central symmetric decomposition, reported-value residual, unit caution, partial-window semantics, and investor-facing limitations mostly right.

It is not implementation-ready. Four issues can produce financially wrong or historically unreproducible results:

1. The normalized source currently discards duplicate product-code rows.
2. The activation/unsafe-product rules do not always satisfy the stated reconciliation identity.
3. Evidence can point to normalized line-item IDs that are deleted during corrections.
4. No deterministic accepted-report revision model prevents an older or concurrent payload from replacing newer facts.

There are also material repository-alignment, semantic-routing, alias-versioning, concurrency, transport, and slice-sequencing gaps.

## 3. Findings

| ID | Severity | Design section | Finding | Evidence | Required change | Affected AC/slices |
|---|---|---|---|---|---|---|
| B-01 | BLOCKER | §§3.2, 7.1, 13.4, 14.2 | “Sum all reported rows” is impossible from the current normalized source because duplicate line-item codes are collapsed using last-write-wins. | The normalizer groups by `LineItemCode` and selects `Last()` in `NadpcoApiMonthlyActivityNormalizer.cs:174`. Positive vendor codes become the line key; the DB then enforces unique report/product code in `FinancialIngestionConfigurations.cs:240`. The existing test explicitly expects collapse in `NadpcoApiMonthlyActivityNormalizerTests.cs:289`. Two legitimate rows of 100 and 80 can therefore become 80 rather than 180. | Define and persist a stable source-row discriminator. Distinguish exact replay/revision duplicates from distinct domestic/export/category/package rows. Preserve all economic line facts and aggregate only in the Feature 129 calculator. Add raw-to-normalized reconciliation tests. | AC 1, 3, 7, 15, 23; Slices 1–3, 6 |
| B-02 | BLOCKER | §§8.3–8.4 | Edge-case effects are not collectively exhaustive and can violate company reconciliation. | The design says missing base sales **or rate** assigns current revenue to activation. Counterexample: `R0=100`, `P0=missing`, `R1=120`. Contribution/company change is 20, but activation becomes 120, leaving −100 unassigned. Unit-change, invalid-rate, and missing-quantity contributions likewise have no unambiguous bucket in the equation. | Introduce a mutually exclusive effect taxonomy, such as `UnattributedComparableEffect`, or define an unsafe product’s entire signed contribution as unmatched. Activation must apply only when the authoritative base revenue is absent/zero. Provide an equation and test case for every edge state. | AC 7, 11, 13; Slices 1, 3 |
| B-03 | BLOCKER | §§13.5, 18, 22.3–22.6 | Historical evidence cannot remain valid after corrected reports replace normalized line items. | The normalizer deletes all old line items and creates new GUIDs in `NadpcoApiMonthlyActivityNormalizer.cs:68`. The initial schema does not create a line-item/report FK in `20260527045316_InitialFinancialIngestion.cs:77`. Design §13.5 nevertheless “links” evidence to mutable report/line IDs and does not require copying the observed values. | Evidence must reference an immutable source-observation/report-revision record, or copy the complete source fact used—field, value, unit, source-row key, payload ID/checksum, revision ID, and timestamps—into append-only snapshot evidence. Old replay must not require resolving a current normalized row. | AC 23, 25, 29, 30; Slices 1, 3, 4 |
| B-04 | BLOCKER | §§14.2, 18 | “Newer accepted payload” and “deterministic revision winner” are asserted but not defined or supported by the current ingestion model. | The normalizer finds the current logical report, blindly overwrites its checksum and fields, deletes children, and never compares provider publication/revision order. Although `PublishedAt` exists, it is not populated here. An older payload arriving later can therefore become authoritative. | Add immutable report revisions and a locked accepted-current pointer. Specify provider revision/publication/receipt precedence, ties, missing dates, late older payload rejection, concurrent arrival behavior, and how source fingerprints identify accepted revisions. | AC 23–25, 32; Slices 1, 3, 6 |
| M-01 | MAJOR | §§3.4, 12.2, 14.2, 18 | Feature 129 cannot know that required output types have completed successfully. | The provider independently catches failures for types 0–4 and serializes failed slots as null in `NadpcoApiDataProviderClient.cs:111`. Sync completion checks only whether **any** report exists for the month in `FinancialDataSyncProcessor.cs:311`. The recalculation event carries only dataset, company reference, checksum, and time. | Persist a company-month ingestion manifest with per-operation/type outcome, accepted revision IDs, completeness, and retry state. Emit the calculation event only after the manifest is committed; type 0 success must be mandatory. Optional type failures must be distinguishable from legitimate empty responses. | AC 1, 2, 18, 23–25; Slices 1, 6 |
| M-02 | MAJOR | §§3.4, 12, 24–25 | The repository discovery overlooks the existing derived-feature calculation framework and instead plans to put a domain feature into the metric recalculation processor. | Registered infrastructure already includes feature definitions, snapshots, computation jobs, scheduler, RabbitMQ publisher/consumer, and `FeatureComputationProcessor` in `ServiceCollectionExtensions.cs:1325`. The schema was created by `20260527151735_AddDerivedFeatureFoundation.cs`. | Evaluate and explicitly adopt the feature scheduler/job/event framework for orchestration, while retaining domain-specific child tables if needed. Otherwise document a concrete architectural reason for extending `MetricRecalculationProcessor` and avoid mixing metric-registry processing with Feature 129 branching. | AC 23–25, 35; Slices 1, 6 |
| M-03 | MAJOR | §§10, 13.1–13.2 | Vendor identity and automatic matching can false-merge economically different products. Alias overlap is not database-enforceable as described. | Current fallback identity includes an array index in `NadpcoApiMonthlyActivityNormalizer.cs:401`, so reordering can false-split. The design assigns confidence 1.00 to a vendor ID before requiring compatible unit/package/grade/domestic-export evidence. A normal unique index cannot prohibit overlapping validity ranges. | Treat zero/blank IDs as absent; detect reuse/collisions; gate vendor matches by compatible economic signatures; eliminate array indexes from lasting identity; require effective ranges and an explicit PostgreSQL exclusion constraint or serialized equivalent for approved-alias overlap. | AC 3, 13–17, 23; Slices 2, 3 |
| M-04 | MAJOR | §§10.3, 13.1–13.3 | `AliasRevision` is a scalar, but the proposed alias rows are mutable and cannot reconstruct an old alias set or merge/split decision. | Alias fields include updated timestamps and override history, but no immutable alias-set version, version membership, or canonical-product lineage. `Merged/Retired` status has no successor/split relationship. | Add append-only alias-set revisions and memberships, canonical merge/split lineage, effective ranges, and snapshot FK to the exact alias revision. Define affected-period recalculation for merge, split, reversal, and validity-range edits. | AC 17, 23, 25, 29, 30; Slices 2–4 |
| M-05 | MAJOR | §§13, 18 | Several PostgreSQL constraints and the publication transaction remain under-specified. | A unique `(SnapshotId, CanonicalProductId)` permits multiple null canonical IDs. `RowVersion` does not identify whether the implementation uses `xmin` or an explicit concurrency token. Publication offers a choice between optimistic concurrency, advisory lock, or row lock instead of one contract. The repository already has a concrete advisory-lock/current-pointer pattern in `IndustryRelativeValuationCalculationSnapshotWriter.cs:39`. | Specify filtered unique constraints for canonical and unmatched items, alias exclusion constraints, concrete concurrency-token behavior, advisory-lock key/acquisition order, transaction isolation, pointer update/insert sequence, retry semantics, and rollback behavior. | AC 17, 23–25, 35; Slices 1–3 |
| M-06 | MAJOR | §§8.3, 9, 11 | Lifecycle, sign, identity, and quality states are conflated; company classification can contradict net drivers. | `ReturnsOrReversal` and `Unmatched` are treated alongside lifecycle states. A product can be both continuously active and a return, but the model does not show separate axes. With quantity effects `+100` and `−99` and price effect `+1`, absolute quantity share is 99.5%, so classification is `QuantityDriven` even though signed quantity and price each explain 1 of the net 2. `MixDriven` uses a non-additive share-turnover metric and an undefined “explain the change” condition. | Separate lifecycle, identity/match, sign, and quality dimensions. Add `FirstObserved/HistoryInsufficient`. Classify from aligned signed effects plus a gross-cancellation guard. Guard signed-change denominators. Keep `MixShift` as a composition signal unless a separately defined, testable non-additive label is desired. | AC 11, 12, 16, 20–22; Slices 2, 3, 6 |
| M-07 | MAJOR | §§16, 25 | The proposed semantic route is not wired to the repository’s active interpretation or slot model. | DI registers `ICapabilityInterpreter` as the deterministic interpreter and registers a no-op model proposal provider in `ServiceCollectionExtensions.cs:680` and `:725`. The dialogue gate consumes that interface directly in `ConversationTaskStateContracts.cs:101`. The interpreter contains fixed keyword arrays in `DeterministicCapabilityInterpreter.cs:14`. `QuerySlotType` has no product or analysis-focus slot in `CanonicalQueryEntityContracts.cs:69`, and this file is absent from the impact map. | Specify the active interpreter path: enable a validated model/semantic proposal or define another governed paraphrase mechanism. Add product resolution, product/task-state slots, period extraction/defaulting, ambiguity flow, and exact precedence integration across V1/V2. Update the impact map. | AC 26, 27, 29; Slices 4, 6 |
| M-08 | MAJOR | §§15–17, 25 | Structured transport, historical replay, and the waterfall implementation are insufficiently specified. | `AssistantMessagePayload` already has an overall version in `ConversationContracts.cs:37`, but the design does not say whether to increment it or how older payloads decode. The frontend block is an optional-field interface, not a discriminated union. Recharts 2.15 is present, but there is no native Feature 129 waterfall contract; cumulative starts/ends, totals, “Other” membership, and accessible fallback must be shaped explicitly. Telegram rendering is omitted from the impact map. | Define immutable DTO ownership, payload-version increment/backward decoder, bounded embedded evidence, live/history mapping, schema compatibility tests, and Telegram fallback. Define a waterfall view model with server amounts and client-only layout offsets, cumulative totals, inspectable “Other,” table equivalent, and no financial recomputation. | AC 29–33, 35; Slices 4, 5 |
| M-09 | MAJOR | §§22–23 | The vertical slices publish unstable results before product identity and complete schema exist; the proposed 1403 backfill contradicts the current provider boundary. | Slice 1 publishes product rows using vendor ID/unmatched state; Slice 2 changes identity; Slice 3 may “finalize” effect columns. Slice 4 claims structured completion while temporarily rendering Markdown. Slice 6 defers YTD, YoY, averages, anomalies, and inventory although global ACs already require them. The current provider clamps monthly activity to 1404 in `NadpcoApiDataProviderClient.cs:89`, while §22.3 requires backfill from 1403 without naming an archive source. | Put source revisions, canonical identity, alias revisioning, unit policy, and stable schema before the first published snapshot. Make Slice 1 internal if identity is not ready. Move core reconciliation into the first publishable slice. Either define a compatible archive source for 1403 or change the boundary. Separate MVP ACs from later-release ACs. | AC 1, 3, 7, 13, 17–25, 30–35; all slices |
| M-10 | MAJOR | §§21–22 | Material behaviors appear only as prose/test bullets and are not normative acceptance criteria. | There is no objective AC for per-output ingestion completeness, evidence surviving authoritative replacement, late older revision rejection, alias merge/split lineage, overlapping alias rejection, conversation schema migration, direct-endpoint authorization/ETag behavior, or the 1403 archive source. | Add explicit, measurable ACs for each behavior, including exact concurrency outcomes and historical replay after report, alias, and policy changes. Tie every new AC to an integration/E2E test and slice. | New ACs; Slices 1–6 |
| N-01 | MINOR | §§7.2, 8.2, 13, 15 | Decimal precision, rounding, and the zero/immaterial contribution-share denominator are not fully defined. | Reconciliation is required at “stored decimal precision,” but no column precision/scale or rounding boundary is specified. `ContributionShare` has no rule for `S1−S0=0` or an immaterial denominator. | Define database precision/scale, calculation rounding policy, fingerprint serialization, presentation-only rounding, and nullable contribution share with `ZeroCompanyChange`/`ImmaterialCompanyChange` reason codes. | AC 4, 5, 7, 29; Slice 1 |
| T-01 | NOTE | §23 | Several advanced elements are safe to defer if the release contract is adjusted. | Robust anomalies, investor waterfall, direct endpoint, LLM prose polishing, image export, and extended history are not prerequisites for a correct two-period backend result. | Split MVP correctness from later UX/history enhancements; do not mark the entire feature complete until whichever AC set remains in scope passes. | Release planning |

## 4. Formula and reconciliation verification

The representative fixture is mathematically correct under the symmetric method:

| Product | Reported change | Quantity effect | Price effect | Residual effect | Reconciled |
|---|---:|---:|---:|---:|---:|
| سبزیجات ۴۰ گرمی | 91,881.6 | 95,440.8 | −3,559.2 | 0 | 91,881.6 |
| کنسرو مخلوط | −30,000 | −30,000 | 0 | 0 | −30,000 |
| غذای آماده صادراتی | 58,268.4 | 51,000 | 7,000 | 268.4 | 58,268.4 |
| **Total** | **120,150** | **116,440.8** | **3,440.8** | **268.4** | **120,150** |

Company verification:

```text
Base sales     = 100,000 + 200,000 + 150,000 = 450,000
Current sales  = 191,881.6 + 170,000 + 208,268.4 = 570,150
Company change = 120,150
Growth         = 120,150 / 450,000 × 100 = 26.7%
```

The largest positive contributor is correctly the first product.

Other formula conclusions:

- Monthly net sales, positive-revenue concentration, HHI, YTD source selection, complete/partial averages, and symmetric decomposition are sound.
- Reported revenue correctly remains authoritative.
- MAD anomaly detection is conventional, subject to defining zero-MAD relative denominators and materiality parameters.
- Production-versus-sales wording is appropriately limited to “potential” or “inferred” inventory signals.
- Negative reported values are correctly retained in company totals, but need separate sign and lifecycle dimensions.
- The edge-case attribution equation fails for positive base revenue with missing/invalid base rate, as demonstrated in B-02.
- Absolute effect mass measures gross activity, not necessarily the signed driver of net change.
- Revenue-share turnover is a valid composition metric but is not additive revenue attribution.

## 5. Repository and architecture consistency

Confirmed claims:

- NADPCO ProductSales requests output types 0–4 independently.
- Output type 0 is the current monthly source; types 1–4 are separately persisted.
- Normalization authoritatively replaces current line items.
- Current trend calculations use available rather than necessarily contiguous history, which Feature 129 correctly proposes to fix.
- Current product mix is latest-period oriented and excludes non-positive rows.
- `PublishedAt` exists on normalized reports but is not currently populated by the NADPCO normalizer.
- V1 and V2 have semantic-frame execution paths, structured persistence, billing boundaries, and specialized frontend results.
- Existing trend chart view-model, RTL formatting, palette, responsive, and export conventions are reusable for trend presentation.

Incorrect or incomplete claims:

- The metric recalculation outbox is not the only or closest reusable calculation framework; the derived-feature scheduler, job, event, worker, definition, and snapshot infrastructure was overlooked.
- Active interpretation is deterministic and keyword-heavy; the existing hybrid interpreter/model proposal is not the registered dialogue-gate path.
- The existing recalculation request does not contain period, output-type manifest, accepted revision IDs, or completeness.
- Logical report uniqueness is current-state uniqueness, not immutable report-revision lineage.
- The file-impact map omits the slot schema, feature-computation framework, product resolver/task-state integration, Telegram renderer, complete conversation-version handling, and concrete direct-endpoint controller/auth work.
- The existing normalized rows cannot serve as durable evidence after replacement without immutable copied observations.

## 6. Acceptance-criteria traceability

“Covered” below means the design supplies an explicit, implementable mechanism and test approach—not that Feature 129 is already implemented.

| AC | Status | Requirement/design/test trace |
|---:|---|---|
| 1 | Contradictory | Formula is explicit, but current duplicate collapse prevents summing all reported rows. |
| 2 | Covered | Output-type isolation is explicit and has existing/new test coverage. |
| 3 | PartiallyCovered | Union is defined; stable canonical identity is not yet guaranteed. |
| 4 | Covered | Product facts, changes, nullable percentages, and reason codes are specified. |
| 5 | Covered | Positive-baseline guard and reason codes are explicit. |
| 6 | Covered | Stable ranking tie-breakers and boundary tests are defined. |
| 7 | Contradictory | Main formula reconciles, but unsafe/activation edge cases do not. |
| 8 | Covered | Symmetric identity is mathematically correct and explicitly tested. |
| 9 | Covered | Reported-value authority and residual are explicit. |
| 10 | Covered | Versioned tolerance, warning, and classification suppression are defined. |
| 11 | Contradictory | Activation is valid for absent/zero base revenue, not merely a missing base rate. |
| 12 | Covered | `−R0` discontinuation effect is implementable and testable. |
| 13 | PartiallyCovered | Suppression is specified, but the monetary contribution’s reconciliation bucket is ambiguous. |
| 14 | Covered | Unit-bucket aggregation and scalar suppression are explicit. |
| 15 | PartiallyCovered | Normalization rules exist, but lasting identity and package distinctions are not safely constrained. |
| 16 | PartiallyCovered | Ambiguity policy exists, but vendor-ID confidence can bypass it. |
| 17 | PartiallyCovered | Audit fields exist; immutable alias-set versioning and merge/split lineage do not. |
| 18 | PartiallyCovered | Definitions are mostly explicit; ingestion completeness and the 1403 source are unresolved. |
| 19 | Covered | Contiguous-window and partial-window behavior is explicit and tested. |
| 20 | Covered | Measure labels are explicitly separated. |
| 21 | Covered | Inventory wording and limitations are safe. |
| 22 | Contradictory | Determinism is intended, but signed-driver and mix definitions can contradict the numbers. |
| 23 | NotCovered | Snapshot immutability is asserted, but revision lineage and durable evidence cannot guarantee it. |
| 24 | PartiallyCovered | Retaining the prior publication is required; the exact transaction/lock protocol is not. |
| 25 | PartiallyCovered | Fingerprinting/no-op logic exists, but source and alias inputs are not reproducibly versioned. |
| 26 | NotCovered | Current active interpreter is deterministic; no concrete paraphrase interpretation wiring is provided. |
| 27 | PartiallyCovered | Precedence examples and negative tests exist, but slot/interpreter integration is incomplete. |
| 28 | Covered | Bounded calculated facts and numeric-consistency enforcement are explicit. |
| 29 | PartiallyCovered | Evidence fields are described, but durable source values and revision lineage are missing. |
| 30 | PartiallyCovered | Structured persistence is planned; schema-version migration and immutable replay are incomplete. |
| 31 | PartiallyCovered | Sections are specified; waterfall and transport view models are incomplete. |
| 32 | Covered | All required states have localized UI/test definitions. |
| 33 | PartiallyCovered | Responsive/unit rules exist; complete long-name/large-set/waterfall behavior needs a concrete model. |
| 34 | Covered | Credential/raw-payload prohibitions and security tests are explicit. |
| 35 | PartiallyCovered | Targets and load testing exist, but the data volumes, indexes, and publication contention model are incomplete. |
| 36 | Covered | Operational-analysis/non-recommendation language is explicit. |

Missing normative ACs should be added for:

- Per-output ingestion completeness.
- Late older revision rejection.
- Evidence survival after authoritative replacement.
- Alias merge/split and overlap concurrency.
- Old-payload schema decoding.
- Backfill source compatibility and restart behavior.
- Direct endpoint authorization and ETag semantics, if retained.

## 7. Vertical-slice review

| Slice | Feasible as written? | Required sequencing change |
|---|---|---|
| 1 — Reconciled foundation | No | First establish source-row preservation, immutable report revisions/evidence, stable schema, publication transaction, and at least safe exact canonical identity. Do not publish snapshots whose identity will be redefined in Slice 2. |
| 2 — Canonical products | No, as a post-publication addition | Move canonical products, append-only alias revisioning, unit/package compatibility, and overlap constraints before the first externally published comparison. |
| 3 — Attribution and quality | No | Core reconciliation and complete effect taxonomy belong in the first publishable calculator. Avoid “finalizing” columns through another migration after Slice 1. |
| 4 — Semantic/API | Conditional | Proceed only after product/period slots, interpreter wiring, structured result versioning, immutable conversation copy, and route precedence are resolved. Markdown may be a visual fallback, but structured transport must already be authoritative. |
| 5 — Visual experience | Conditional | Feasible after a waterfall/table/evidence view model and complete state contract exist. The existing trend chart is reusable only for trend data. |
| 6 — History and hardening | Partially | Anomalies and extended UX can remain late. Revision safety, concurrency, restartable backfill, and SLO validation cannot be deferred beyond production enablement. Define an archive source or begin at the supported 1404 boundary. |

Recommended sequence:

1. Source/revision/evidence and canonical-product foundation.
2. Correct two-period calculator, effect taxonomy, publication transaction, and immutable snapshot.
3. Semantic/API/persistence end-to-end using the structured result.
4. Investor UI and accessibility.
5. YoY/YTD/history, anomalies, inventory signals, backfill, and operational hardening.

## 8. Required design changes

1. Update §§3.2, 7.1, 12.2, and 14.2 to preserve distinct source line facts and reconcile normalized totals to raw payloads.
2. Replace §8.3’s edge table with a collectively exhaustive effect-state machine and prove the company equation for every state.
3. Add immutable report-revision and source-observation schemas to §13; prohibit historical evidence from depending on replaceable line-item IDs.
4. Define revision acceptance, late-arrival rejection, and concurrent-winner rules in §§14 and 18.
5. Add a committed per-company-month ingestion manifest and completeness barrier to §§12 and 14.
6. Rework §§10 and 13 around zero/reused vendor IDs, compatibility gates, stable fallback identity, exclusion-constrained validity ranges, immutable alias revisions, and merge/split lineage.
7. Select one concrete PostgreSQL publication protocol in §18, including lock key, transaction order, exact filtered indexes, retry behavior, and concurrency token.
8. Amend §§3.4, 12, and 25 to use or explicitly reject the existing derived-feature computation framework.
9. Separate lifecycle, identity, sign, and quality states; replace gross-effect driver rules with signed/aligned and cancellation-aware rules; keep mix non-additive.
10. Update §16 and the impact map with actual interpreter wiring, product slots/resolution, task-state propagation, ambiguity handling, and V1/V2 precedence.
11. Define assistant payload versioning, backward decoding, immutable embedded result/evidence, API/history mappings, Telegram behavior, and the waterfall view model.
12. Add the missing normative acceptance criteria and tie each to an integration/E2E test.
13. Reorder §23 so no externally published snapshot precedes stable identity, evidence, revision, and publication foundations.
14. Resolve the 1403/1404 backfill contradiction and define decimal precision, rounding, and zero/immaterial denominator semantics.

For the first production release, correctness requires source preservation, accepted revisions, canonical identity, safe units, two-period reconciliation, immutable evidence, atomic publication, structured transport, and deterministic query routing.

Safe deferrals include extended history, anomalies, inventory signals, the direct endpoint, LLM prose polishing, image export, and the visual waterfall—provided their ACs are moved to a later release.

## 9. Final verdict

NEED_CHANGES
