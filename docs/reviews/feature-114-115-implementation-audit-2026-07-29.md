# Feature 114/115 implementation audit

Date: 2026-07-29  
Scope: Feature 114, CyclicalWaves P/S visualization data sync; Feature 115, AI P/S gauge and chart experience  
Verdict: **Not ready for production**

## 1. Executive summary

The repository contains a substantial but incomplete Feature 114 data-pipeline implementation and an early Feature 115 backend projection/calculation layer. The feature is not end-to-end usable.

The most important blocker is a production provider-contract defect: `/api/ps-data/{companyIsin}` returns a `{ "data": { ... } }` envelope, while the production client deserializes the response as a flat object. Every real current-P/S response therefore becomes an invalid-contract result, preventing a combined gauge/current snapshot from being synchronized. The green fixture test does not catch this because it uses a separate, test-only envelope DTO.

Feature 115 is not connected to the AI system. There is no `PsGaugeVisualization` intent, V1/V2 branch, additive AI/API response member, conversation artifact, monthly-sales enrichment, frontend gauge, P/S history chart, accessibility implementation, or PNG export. The registered read use case has no caller outside dependency injection.

Other release blockers include:

- a Git-tracked Bearer token in `docs/cw_api.txt` and committed non-empty CyclicalWaves credentials in API and Worker configuration;
- an EF migration that can generate SQL but is inconsistent with the model snapshot (`has-pending-model-changes` fails);
- no retained 1,124-point fixture and no persistence/concurrency/worker integration tests;
- sync/run observability that is not persisted at the required run/dataset/company granularity;
- incomplete freshness, fallback, lease-loss, bounded-concurrency, and history-read behavior;
- contradictory gauge assertions between the designated primary specifications/screenshots and the audit request;
- all new feature implementation/specification files are currently untracked, so a normal commit or deployment could omit them.

The solution build, 1,087 unit tests, and 7 architecture tests pass. These results do not prove either feature. The integration suite fails 114 of 394 tests, the frontend suite fails 1 of 28 tests, and no Feature 114/115 integration, API, AI-routing, frontend, visual, export, or end-to-end tests were found.

## 2. Completion assessment

Completion is scored from the task matrices only, to avoid double-counting story criteria:

- `Implemented and verified` = 1.00
- `Implemented but insufficiently tested` = 0.75
- `Partially implemented` = 0.50
- `Missing`, `Incorrect implementation`, or `Blocked by unresolved specification ambiguity` = 0

| Feature | Score | Assessment |
| --- | ---: | --- |
| Feature 114 | **41%** | A useful schema/client/sync skeleton exists, but the live current-value contract is broken, migration metadata is inconsistent, and required integration/operational proof is absent. |
| Feature 115 | **19%** | Options, provider-neutral models, a persisted read use case, and deterministic calculator exist; AI/API/frontend/export delivery is missing. |
| Overall | **30%** | Not an end-to-end implemented feature. |

This score measures workspace implementation, not deliverability. The implementation files are untracked, which independently blocks a reliable release.

## 3. Status legend

- **IV** — Implemented and verified
- **IT** — Implemented but insufficiently tested
- **PI** — Partially implemented
- **M** — Missing
- **II** — Incorrect implementation
- **BA** — Blocked by unresolved specification ambiguity
- **NA** — Not applicable, with justification

## 4. Feature 114 story-criterion traceability

| Ref | Criterion | Implementation evidence | Test evidence | Status | Gap |
| --- | --- | --- | --- | --- | --- |
| 114-US:8-9 | Preserve `a`–`f` as bucket populations. | `CyclicalWavesPsPayloadModels.cs:5-17`; `CompanyPsVisualizationRows.cs:3-37`; `CyclicalWavesPsVisualizationSyncService.cs:197-200` | Fixture assertions at `CyclicalWavesPsProviderFixtureTests.cs:15-39` | IT | Production DTO missing-field presence is not enforced; no real client/persistence test. |
| 114-US:9 | Retain `start/end`, use `min/max` as gauge axis, retain `avg`. | `CyclicalWavesPsVisualizationContracts.cs:41-44`; `PsVisualizationExperienceContracts.cs:48-52`; calculator call at `PsVisualizationExperienceUseCase.cs:44-47` | Two calculator examples at `PsGaugeCalculatorTests.cs:7-40` | BA | Primary specs/screenshots support this; the audit request instead demands `start/end` needle mapping. Governance conflict must be resolved. |
| 114-US:11 | Do not calculate local quantiles. | Calculator uses fixed sixths at `PsGaugeCalculator.cs:31-42`. | Equal 30-degree assertion at `PsGaugeCalculatorTests.cs:17-20` | BA | Matches primary specs/screenshots, contradicts the audit request's proportional arcs. |
| 114-US:15 | Total is `a+b+c+d+e+f`. | Checked sum at `CyclicalWavesPsVisualizationSyncService.cs:185-189`; calculator sum at `PsGaugeCalculator.cs:13-15` | Zero-total test at `PsGaugeCalculatorTests.cs:42-49` | IT | Negative and overflow paths are not tested end-to-end. |
| 114-US:17 | Low-to-high order is `a,b,c,d,e,f`. | Contract mapping at `CyclicalWavesDataProviderClient.cs:74-80`; calculator preserves input order at `PsGaugeCalculator.cs:21-42`. | Label/order examples at `PsGaugeCalculatorTests.cs:7-40` | BA | The audit request asserts reversed `f,e,d,c,b,a`; screenshots and primary docs show `a..f`. |
| 114-US:19 | Percentage is bucket count divided by total. | `PsGaugeCalculator.cs:21-29,33-42` | Shiraz/Ggolpa percentages at `PsGaugeCalculatorTests.cs:7-40` | IT | No culture/high-precision/property tests. |
| 114-US:21-23 | Each segment is 30 degrees; population affects label only; raw facts persist. | `PsGaugeCalculator.cs:39-42`; persistence mapping at `CompanyPsVisualizationConfigurations.cs:11-19` | Equal-width assertion at `PsGaugeCalculatorTests.cs:17-20` | BA | Same specification conflict; no rendered-pixel comparison exists. |

## 5. Feature 114 task traceability

The task file itself is inconsistent: it states that all tasks remain unimplemented at `tasks.md:6`, contains both a completed replacement Task 2 at `tasks.md:29-35` and an unchecked older Task 2 at `tasks.md:38-60`, and leaves implemented work unchecked.

| Task | Implementation files | Tests/evidence | Status | Gap/findings |
| --- | --- | --- | --- | --- |
| 1. Sanitize/freeze fixtures | `tests/.../Fixtures/CyclicalWaves/Ps/*`; runbook `operations-runbook.md:3-5` | Hash/security tests at `CyclicalWavesPsProviderFixtureTests.cs:80-117` | PI | Fixtures are token-free, but only six history points are retained; a tracked token still exists elsewhere. F-02, F-05. |
| 2. Document verified gauge contract (replacement) | `provider-contract.md:25-31`; `REVERSE-ENGINEERING-NOTES.md:3-16` | Two calculator examples | BA | Primary evidence is internally aligned, but the audit request asserts incompatible order, arc, and axis behavior. F-13. |
| 2. Verify boundary/arc/needle semantics (older task) | Same documents and two supplied symbol captures | `PsGaugeCalculatorTests.cs:7-40` | PI | Fewer than three complete same-symbol API/UI sets; no agreed pixel tolerance or visual reference test. |
| 3. Provider bounds/error semantics | `provider-contract.md:6-23`; result enum `CyclicalWavesPsVisualizationContracts.cs:4-17`; bounded reader `CyclicalWavesDataProviderClient.cs:109-154` | Fixture tests only | PI | Required-field presence, truncated payload, timeout, 401-refresh, 429, and 5xx behavior are not proven; resilience exceptions escape normalization. F-03, F-16. |
| 4. Provider-neutral application contracts | `CyclicalWavesPsVisualizationContracts.cs:3-119`; `PsVisualizationExperienceContracts.cs:3-65` | Architecture suite passes 7/7 | IT | Contracts are neutral, but warning/correlation/run-detail/freshness facts are incomplete and there are no contract-mapping tests. |
| 5. Typed provider client | `CyclicalWavesDataProviderClient.cs:74-154`; auth/resilience registration `ServiceCollectionExtensions.cs:744-769` | No production-client tests | II | `/ps-data` envelope is wrong; provider-foundation exceptions are not normalized. F-01, F-16. |
| 6. Eligible scope normalization | `CyclicalWavesPsVisualizationSyncService.cs:33-68`; dry run `CyclicalWavesPsVisualizationAdminController.cs:22-29` | No tests found | PI | Trim/case/dedup/conflict/order exist; validation is only `[A-Z0-9]{12}` and no scope behavior is tested. |
| 7. Persistence/constraints/migration | Rows/configuration/migration in `Financial/Ingestion/Persistence` | EF SQL generation succeeds | PI | Required tables/indexes exist, but model snapshot is stale and clean-database replay is unverified. F-04. |
| 8. Canonical hashing | `CyclicalWavesPsVisualizationSyncService.cs:242-247` | No hashing tests found | IT | Invariant ordering/culture is implemented, but restart/culture/array-order equivalence is unproved. |
| 9. Validation/quality classification | `CyclicalWavesPsVisualizationSyncService.cs:164-189,206-234` | Only calculator invalid-total test | PI | Missing fields can become zero; observation/identity/metadata/component states are incomplete; metadata mismatch is recorded as success. F-03, F-10, F-11. |
| 10. Atomic gauge/current upsert | `CyclicalWavesPsVisualizationSyncService.cs:155-203` | No persistence/concurrency test | II | Parallel logical fetch and last-good preservation exist, but real current payload never maps; invalid boundaries are rejected instead of persisted non-renderable; no attempt detail. F-01, F-11. |
| 11. Full-history upsert/reconciliation | `CyclicalWavesPsVisualizationSyncService.cs:206-234`; unique/index config `CompanyPsVisualizationConfigurations.cs:23-32` | Six-point fixture only | PI | Stable ordering, ID identity, soft inactivation, and conflict quarantine exist; 1,124-point proof, correction policy, atomic failure proof, and metadata-mismatch semantics are absent/incorrect. F-05, F-10. |
| 12. Bounded coordinator | `CyclicalWavesPsVisualizationSyncService.cs:82-130` | No coordinator tests | PI | Count, duration, delay, and isolation exist; `MaxConcurrency` is unused and execution is sequential; no cancellation/resume proof. F-09. |
| 13. Renewable lease | `CyclicalWavesPsVisualizationSyncService.cs:249-275` | No lease tests | II | Lease exists and renews per company, but `LeaseRenewalMinutes` is unused and lease ownership is not rechecked before writes/commit. A long request can commit after lease expiry/loss. F-09. |
| 14. Gated worker | `CyclicalWavesPsVisualizationSyncWorker.cs:7-33`; registration `Program.cs:127` | No worker tests | PI | Disabled gate, cadence, and cancellation exist; it immediately runs on restart and has no explicit missed-run/stampede policy. |
| 15. DataAdmin operations | `CyclicalWavesPsVisualizationAdminController.cs:11-60` | No authorization/API tests | PI | Protected, rate-limited, bounded endpoints exist; no durable run/failure inspection, resume cursor, or distinct recurring operation. |
| 16. Telemetry/health/freshness inputs | Sync state rows and admin read | No telemetry/health tests | PI | Snapshot/history timestamps exist, but required health dimensions, metrics, durable per-run detail, and lease/provider telemetry are missing. F-12. |
| 17. Unit/contract tests | `CyclicalWavesPsProviderFixtureTests.cs`; `PsGaugeCalculatorTests.cs` | 8 feature-focused tests; full unit suite 1,087/1,087 | PI | Test-only DTO hides production defect; most malformed/failure/hash/identity/history scenarios are absent. F-03, F-14. |
| 18. Persistence/concurrency/worker integration tests | None found | Integration test discovery found zero P/S visualization tests | M | No migration, idempotency, 1,124-point, atomicity, lease, worker, DataAdmin, or no-HTTP integration proof. F-05, F-14. |
| 19. Operations/security runbooks | `operations-runbook.md:1-20`; `provider-contract.md` | Manual audit only | PI | Basic rollout/rotation/backfill guidance exists, but committed credentials/token violate the gate and several recovery/readiness procedures are incomplete. F-02. |

## 6. Feature 115 acceptance-criterion traceability

| Ref | Acceptance criterion | Implementation/test evidence | Status | Gap |
| --- | --- | --- | --- | --- |
| 115-AC:420 | Explicit gauge aliases route only when enabled/resolved. | No `PsGaugeVisualization` enum/branch; `AiOrchestrationContracts.cs:8-22` | M | F-06. |
| 115-AC:422 | V1/V2 use the same use case and facts. | Use case is registered at `ServiceCollectionExtensions.cs:1069` but has no AI caller. | M | F-06. |
| 115-AC:424 | Point lookup/scanner/comprehensive/general analysis do not regress. | Existing paths remain untouched. Full integration suite has broad failures. | IT | No dedicated negative-precedence tests; regression baseline is red. |
| 115-AC:426 | AI execution performs zero vendor calls. | No AI branch exists; read use case depends on persisted reader at `PsVisualizationExperienceUseCase.cs:22-34`. | NA | Vacuously true for a missing feature; must be proven when integrated. |
| 115-AC:428 | LLM cannot calculate bands/query storage/provider. | Calculator is deterministic application code; no LLM tool exists. | NA | There is no LLM feature branch to assess. |
| 115-AC:432 | Select newest renderable complete snapshot. | Reader filters renderability/date only at `CyclicalWavesPsVisualizationSyncService.cs:132-152`. | PI | Completeness/quality/provider selection and explicit fallback are missing. F-08. |
| 115-AC:434 | Explicit fallback/stale/partial/invalid/unavailable/not-requested states. | Status enum exists at `PsVisualizationExperienceContracts.cs:3`; limited branches at `PsVisualizationExperienceUseCase.cs:35-48`. | PI | Most states/fallback/in-progress/last-failure distinctions are not produced. F-07, F-08. |
| 115-AC:436 | Dual freshness: sync age plus market-calendar observation lag. | Only sync age is evaluated at `PsVisualizationExperienceUseCase.cs:38-41`; option at line 15 is unused. | M | F-07. |
| 115-AC:438 | Read latest successful active history. | Active filter/order at `CyclicalWavesPsVisualizationSyncService.cs:136`. | IT | Correct shape, but full collection is unbounded and latest-success quality is inferred from a boolean. |
| 115-AC:440 | Return all 1,124 points/eight duplicate-date groups. | Six-point sample and `TakeLast` projection only. | M | F-05. |
| 115-AC:444 | Six bands/percentages/boundaries/needle/current values/dates match source. | Models/calculator/use case at `PsVisualizationExperienceContracts.cs:14-59`, `PsGaugeCalculator.cs:6-47`, `PsVisualizationExperienceUseCase.cs:44-48` | IT | Backend-only; no production-client, persistence, API, UI, or visual proof. |
| 115-AC:446 | Display percentages deterministically total 100. | Largest-remainder code `PsGaugeCalculator.cs:19-29`; Shiraz assertion `PsGaugeCalculatorTests.cs:17-20` | IT | Sparse examples; presentation rounding is performed in application code despite the audit request saying UI only. |
| 115-AC:448 | Honest zero/missing/stale/invalid/clamped/truncated display. | Value state/model and clamp exist. | PI | No renderer; current values are non-null persisted decimals, and most states are not exercised. |
| 115-AC:450 | Browser does not recompute/discard same-date points. | No Feature 115 browser code exists. | M | F-06, F-18. |
| 115-AC:452 | No recommendation/invented valuation. | No Feature 115 narrative exists. | NA | Must be tested when narrative is added. |
| 115-AC:456 | Overall/monthly flags default off. | API config `appsettings.json:139-147`; Worker config `appsettings.json:243-251`; validation `ServiceCollectionExtensions.cs:1062-1068` | IT | Defaults/validation exist; no disabled-behavior tests. |
| 115-AC:458 | Monthly flag off means no read/block/layout change. | Option exists but is never consumed outside its class/config. | NA | The enrichment is entirely missing; no behavior to verify. |
| 115-AC:460 | Enabled monthly inset appears identically in UI/export. | No implementation. | M | F-06, F-18. |
| 115-AC:462 | P/S failure never prevents monthly result/export. | No implementation. | M | F-06. |
| 115-AC:464 | Compact projection contains no history. | No compact projection exists. | M | F-06. |
| 115-AC:466 | Existing monthly visual facts remain readable/unchanged. | Existing component remains at `monthly-activity-trend-chart.tsx:105-243`. | NA | No inset was implemented, so compatibility under enabled state is unverified. |
| 115-AC:470 | Conversation reload reproduces exact facts. | AI response/persistence contains monthly result only; `AiOrchestrationContracts.cs:62-88`; `MessagePersistenceFunction.cs:41-79` | M | F-06. |
| 115-AC:472 | Versioned/bounded/safe persisted payload. | Standalone result has `ContractVersion`; it is not persisted. | M | F-06. |
| 115-AC:474 | Interactive and PNG output match. | No P/S renderer/export. | M | F-18. |
| 115-AC:476 | Actor/tenant isolation unchanged. | No new public contract/path. | NA | Must be tested when conversation/API delivery is added. |
| 115-AC:478 | No public vendor proxy. | Only protected DataAdmin endpoints exist at `CyclicalWavesPsVisualizationAdminController.cs:11-20`. | IV | No public CyclicalWaves proxy was introduced. |
| 115-AC:482-488 | Text equivalents, accessibility, RTL/mobile/themes, complete download. | No Feature 115 frontend/export code. | M | F-18. |

## 7. Feature 115 task traceability

The task file is also inconsistent: it marks a short “Task 5 - Implement Gauge Rendering” complete at `tasks.md:3-12`, separately declares “Task 6 - Integrate AI Responses” at `tasks.md:14-17`, then says all tasks remain unimplemented at line 22 and repeats Tasks 1–20.

| Task | Implementation files | Tests/evidence | Status | Gap/findings |
| --- | --- | --- | --- | --- |
| Short Task 5. Gauge rendering | `PsGaugeCalculator.cs`; provider-neutral bands/needle contracts | Three calculator tests | PI | Calculation exists; actual rendering does not. Gauge semantics conflict remains. F-13, F-18. |
| Short Task 6. Integrate AI responses | None | None | M | No direct intent or monthly inset. F-06. |
| 1. Options | `PsVisualizationExperienceUseCase.cs:6-17`; configs; `ServiceCollectionExtensions.cs:1062-1068` | No option tests | IT | Core options/defaults/validation exist; observation option is unused. |
| 2. Versioned provider-neutral read models | `PsVisualizationExperienceContracts.cs:3-65` | No serialization/compatibility tests | PI | Full model exists; compact projection, fetch timestamps, history range/count facts, localized warnings, and persistence compatibility are missing. |
| 3. Persisted snapshot/history selection | `CyclicalWavesPsVisualizationSyncService.cs:132-152`; `PsVisualizationExperienceUseCase.cs:27-48` | No read-policy tests | PI | Persisted-only and canonical resolution exist; complete/quality selection and explicit older fallback do not. F-08. |
| 4. Dual freshness | Sync-age check only | None | M | Market-calendar observation freshness and independent history freshness are absent. F-07. |
| 5. Normalize bands/percentages | `PsGaugeCalculator.cs:13-42` | `PsGaugeCalculatorTests.cs:7-49` | IT | Deterministic decimal math exists; reference/edge coverage is insufficient. |
| 6. Deterministic needle | `PsGaugeCalculator.cs:31-47` | Below-min and in-range examples | IT | Above-max, zero, negative, missing, malformed, and invalid-boundary contracts are not fully covered; no renderer/export consumer. |
| 7. Alias/normalization registry | None | None | M | F-06. |
| 8. Align V1/V2 | None | None | M | F-06. |
| 9. Live API/conversation contracts | None | None | M | F-06. |
| 10. Governed history projection | `PsVisualizationExperienceUseCase.cs:42-48` | None | PI | Deterministic latest-point cap exists, but DB read is unbounded, policy is undocumented in payload, and 1,124-point behavior is unproved. F-05, F-08. |
| 11. Monthly trend enrichment | None | None | M | F-06. |
| 12. Standalone responsive gauge | None | None | M | F-18. |
| 13. Historical P/S panel | None | None | M | F-18. |
| 14. Compact monthly composition | None | None | M | F-18. |
| 15. PNG export | None | None | M | F-18. |
| 16. Localized narrative/states | None | None | M | No deterministic Persian state messages. |
| 17. Routing/orchestration tests | None | Zero matching integration tests | M | F-14. |
| 18. Backend contract/freshness/persistence tests | Calculator tests only | Three calculator tests | M | Almost all enumerated cases are absent. F-14. |
| 19. Frontend/accessibility/export tests | None | None | M | F-18. |
| 20. Performance/security/rollout verification | Config defaults and runbook only | Security scan and build performed by this audit | M | Release gates fail due F-01, F-02, F-04, F-05, F-06, and F-14. |

## 8. Detailed findings

| ID | Severity | Spec/task reference | Affected source | Evidence and impact | Recommended remediation |
| --- | --- | --- | --- | --- | --- |
| F-01 | **Critical** | 114 Tasks 5, 9, 10; provider contract `/ps-data` | `CyclicalWavesPsPayloadModels.cs:19-24`; `CyclicalWavesDataProviderClient.cs:82-89`; fixture `ps-data.json:1-9`; test `CyclicalWavesPsProviderFixtureTests.cs:42-51,145-153` | The provider response is enveloped under `data`; production deserializes a flat DTO. All properties remain null and the client returns `RequiredCurrentValueFieldMissing`, so no live combined snapshot can succeed. The passing fixture test uses a separate correct DTO and never invokes production deserialization. | Model the production envelope, test the production client/DTO against the frozen fixture, and add missing/null/zero/ticker-mismatch cases. |
| F-02 | **Critical** | 114 Tasks 1 and 19; 115 Task 20; security assertions | `docs/cw_api.txt:82`; `FinancialCopilot.API/appsettings.json:112-120`; `FinancialCopilot.Worker/appsettings.json:216-224`; runbook `operations-runbook.md:3-5` | `git grep` finds a Bearer JWT in a tracked file. Both committed appsettings contain non-empty CyclicalWaves username/password values. This violates the explicit no-credential gate and creates credential-reuse/exposure risk even if the token is expired. | Revoke/rotate all disclosed credentials, purge them from current files and Git history under the security process, use environment/secret-store references, and add repository secret scanning in CI. |
| F-03 | **High** | 114 Tasks 3, 9, 17 | `CyclicalWavesPsPayloadModels.cs:5-17`; `CyclicalWavesDataProviderClient.cs:74-80`; `CyclicalWavesPsProviderFixtureTests.cs:15-51,131-153` | Gauge required fields are non-nullable value parameters without required-member presence enforcement. Omitted JSON fields can become zero and may be accepted as explicit zero. Tests deserialize private fixture DTOs rather than production DTOs. Contract failures are therefore not reliably controlled. | Use presence-aware DTOs/required JSON members and explicit validation; exercise the real client DTOs with missing, null, malformed, additive, zero, oversized, and truncated fixtures. |
| F-04 | **High** | 114 Task 7; migration verification | Migration `20260729120000_AddCyclicalWavesPsVisualization.cs:9-41`; `FinancialIngestionDbContext.cs:112-118,153-154`; `FinancialIngestionDbContextModelSnapshot.cs` (no P/S entities) | EF lists the migration and can generate its SQL, but `dotnet ef migrations has-pending-model-changes` exits 1. There is no migration designer and the model snapshot does not contain the new entities. Future migrations can duplicate or drop schema, and clean-database consistency is unproved. | Regenerate the migration through EF so migration metadata/designer/snapshot are coherent, then replay up/down/upgrade paths against disposable PostgreSQL and assert indexes/FKs/precision. |
| F-05 | **High** | 114 Tasks 1, 11, 18; 115 Tasks 10 and 18 | `fixture-manifest.json:20-33`; `ps-history.sample.json:1-36`; `CyclicalWavesPsProviderFixtureTests.cs:80-100` | The manifest states 1,124-point facts but explicitly says the full payload is not retained; the executable fixture has six points. Tests merely assert manifest metadata and cannot prove exact 1,124-row persistence, all eight duplicate-date groups, idempotency, or cap behavior. | Retain a secure deterministic full contract fixture or generate a cryptographically tied complete fixture from approved data, then add real PostgreSQL sync/read tests for all stated counts and reconciliation cases. |
| F-06 | **Critical** | 115 Tasks 6-9, 11, 17; routing/API/conversation criteria | `AiOrchestrationContracts.cs:8-22,62-88`; `ServiceCollectionExtensions.cs:1069`; `message-list.tsx:125-126`; repository search for `IPsVisualizationExperienceUseCase` | The use case is only registered. There is no dedicated intent, alias registry, V1/V2 orchestration branch, AI response member, persisted assistant payload, reload mapping, monthly enrichment, or frontend consumer. Users cannot request or see Feature 115. | Implement the shared deterministic intent policy, V1/V2 branch, additive versioned response/persistence contract, monthly optional projection, and frontend consumers with negative-precedence and reload tests. |
| F-07 | **High** | 115 Task 4; freshness AC | `PsVisualizationExperienceUseCase.cs:14-16,38-48` | Freshness uses only `LastSyncedAtUtc`. `MaxObservationLagTradingDays` is configured but unused, history is labeled fresh based on snapshot freshness, and no governed market calendar is consulted. Old observations can be mislabeled current. | Add/reuse the market-calendar/latest-trading-date abstraction; compute component-specific sync and observation freshness; return facts/reasons and test weekends/holidays/old observations. |
| F-08 | **High** | 115 Tasks 3 and 10; data selection AC | `CyclicalWavesPsVisualizationSyncService.cs:132-152`; `PsVisualizationExperienceUseCase.cs:35-48` | Snapshot selection checks only renderability and date, not completeness/quality/provider. Older fallback is silent. All active history rows are materialized before the application cap, causing an unbounded DB read. Provider outage fallback exists only incidentally and failure/in-progress state is not disclosed. | Encode and test the selection/fallback policy, query only the governed ordered range while retaining the latest point, return source/returned counts and explicit fallback/partial/failure/in-progress warnings. |
| F-09 | **High** | 114 Tasks 12-13 | `CyclicalWavesPsVisualizationSyncService.cs:20-27,82-130,249-275` | `MaxConcurrency` and `LeaseRenewalMinutes` are validated but unused. Work is sequential. Lease renewal occurs only before each company and ownership is not checked immediately before snapshot save/history commit, so a request longer than the lease can let the former owner commit after loss. | Implement bounded parallel scheduling or remove the misleading option; renew on the configured interval; use fencing/lease-owner checks inside write transactions and test contention, expiry, loss, cancellation, and no-overlap. |
| F-10 | **High** | 114 Tasks 9 and 11 | `CyclicalWavesPsVisualizationSyncService.cs:216-234` | Metadata mismatch leaves old active rows intact, but new rows may still be inserted inactive and `LastHistorySuccessAtUtc` is updated; the method returns changed/success. This conflates an incomplete attempt with a successful history refresh. | Reject/quarantine the whole mismatched attempt without accepted-row writes, persist a failed/partial run detail, preserve the previous active series, and update success timestamps only after a metadata-consistent commit. |
| F-11 | **Medium** | 114 Task 10; 115 non-renderable policy | `CyclicalWavesPsVisualizationSyncService.cs:180-203` | Structurally valid snapshots with invalid `min/max` are rejected outright, contrary to Task 10's requirement to preserve facts with explicit non-renderable status. Zero-total snapshots are persisted as `QualityStatus="Valid"`, which is semantically inconsistent. | Separate structural validity from renderability/quality; persist evidence when allowed, set correct component/quality state, never select it as a normal gauge, and test older-renderable fallback disclosure. |
| F-12 | **High** | 114 Tasks 15-16; worker observability assertions | `CompanyPsVisualizationRows.cs:55-86`; `CyclicalWavesPsVisualizationAdminController.cs:38-60`; `CyclicalWavesPsVisualizationSyncService.cs:82-130` | State is per company, but there is no durable run record containing provider, dataset, company, start/end, result, counts, error, and correlation. Admin returns only ephemeral aggregate counts and cannot inspect individual run failures. Required health and telemetry dimensions are absent. | Add bounded run/run-detail persistence and operational queries; emit low-cardinality provider/dataset/outcome metrics and health states without symbol/ISIN labels. |
| F-13 | **High** | 114 gauge story/Task 2; 115 Tasks 5-6; audit gauge assertions | Primary specs `114/user-story.md:15-21`, `provider-contract.md:25-31`, `115/user-story.md:7-13`; audit request lines 80-112; calculator `PsGaugeCalculator.cs:31-47` | Primary specs, reverse-engineering notes, tests, and supplied screenshots support `a..f`, equal 30-degree arcs, and a `min/max` linear needle. The audit request asserts reversed `f..a`, proportional arcs, and `start/end` edge cases. Both cannot be approved simultaneously. The code follows the primary specs. | Product/data owners must issue one authoritative contract amendment with approved captures and numeric/pixel tolerances. Keep rendering disabled until signed off; update all duplicated task text and independent tests together. |
| F-14 | **High** | 114 Tasks 17-18; 115 Tasks 17-20 | `CyclicalWavesPsProviderFixtureTests.cs`; `PsGaugeCalculatorTests.cs`; no matching integration/frontend tests | Unit totals are green, but only eight focused tests exist. Integration test discovery found zero P/S visualization tests. No API, AI alias/precedence, persistence, lease, worker, freshness, frontend, visual, accessibility, export, or E2E proof exists. Full integration suite is already red. | Build an independent test pyramid around real production DTO/client code, disposable PostgreSQL, V1/V2/API/reload, frontend states, RTL/mobile/accessibility, and interactive/PNG numeric parity. |
| F-15 | **High** | Delivery governance; both task files | `114/tasks.md:6,29-38`; `115/tasks.md:3-24`; repository index state for all new Feature 114/115 files | Checklists contradict themselves and most implemented tasks remain unchecked. More importantly, all feature implementation/spec files are untracked. A build sees SDK-globbed files, but a commit/deployment can omit them. | Resolve checklist duplication after semantic approval, add the intended files to source control through the normal review process, and require CI to build the exact committed tree. |
| F-16 | **High** | 114 Tasks 3 and 5 | `FinancialProviderResilienceHandler.cs:24-69`; `CyclicalWavesDataProviderClient.cs:109-154`; registration `ServiceCollectionExtensions.cs:744-769` | The resilience handler converts exhausted timeout/network/circuit cases to `FinancialProviderException`, but `GetPsAsync` catches only caller cancellation, `HttpRequestException`, and `JsonException`. Required normalized timeout/network/server outcomes can escape, and sync state may not record the endpoint classification. | Catch and map provider-foundation exceptions without exposing secrets; preserve caller cancellation; add deterministic tests for timeout, circuit-open, 429/Retry-After, persistent 401, 5xx, and network failure. |
| F-17 | **Medium** | 114 Task 6 | `CyclicalWavesPsVisualizationSyncService.cs:33-68` | Scope handling correctly selects only `CompanyId`/`CompanyIsin`, trims/cases, deduplicates, rejects conflicts, and orders deterministically. “Malformed” validation is only a 12-character alphanumeric regex, so invalid ISIN structure/check digits can reach the provider. | Reuse a governed ISIN validator if available; otherwise document the accepted provider-key grammar and add scope normalization/dry-run tests. |
| F-18 | **High** | 115 Tasks 12-15 and 19 | Existing monthly-only paths `message-list.tsx:125-126`, `monthly-activity-trend-chart.tsx:105-243`, `monthly-activity-trend-chart-image.ts:9-84` | There is no P/S gauge/history component, compact inset, accessible text/table, RTL/mobile state rendering, or P/S export. Screenshot comparison cannot be performed. | Implement a single structured view model consumed by interactive and image renderers, textual equivalents, keyboard/screen-reader behavior, responsive RTL layouts, and deterministic visual tests. |

## 9. Confirmed correct implementations

The following are materially correct within their current scope:

- Provider HTTP DTOs remain in Infrastructure and do not leak into Domain, Application, API, or frontend contracts.
- P/S requests reuse the authenticated/resilient server-side HTTP pipeline and add no browser-only request headers.
- Response bytes are bounded while streaming at `CyclicalWavesDataProviderClient.cs:125-138`; content is not stored as raw P/S payload.
- Application contracts preserve TTM, Forward, GaugeClose, `start`, `end`, `min`, `max`, and `avg` as separate facts.
- Persistence uses fixed `numeric(28,14)`, 64-bit bucket counts, restrictive foreign keys, provider point-ID history uniqueness, and a non-unique observation date.
- Duplicate dates are representable and reads order by observation date then provider point ID.
- Snapshot/current endpoints are fetched as one logical attempt and snapshot persistence happens only after both results are successful.
- History reconciliation uses a transaction, soft-inactivates omitted IDs only for metadata-consistent responses, and does not truncate/delete the table.
- Worker configuration defaults to disabled; DataAdmin endpoints use the existing policy and authenticated rate limit.
- The deterministic calculator uses decimal arithmetic, largest-remainder display reconciliation, preserves the source needle value, and exposes clamp flags.
- The standalone read use case depends on persisted data, not on a provider HTTP client.
- No public vendor-proxy endpoint was added.

These correct pieces do not offset F-01 or the missing Feature 115 delivery path.

## 10. Missing, partial, and incorrect areas

### Missing

- Feature 115 intent, aliases, precedence, V1/V2 orchestration, API field, message persistence, reload, and narrative.
- Compact monthly-sales projection and feature-gated enrichment.
- Frontend standalone gauge, historical chart, compact inset, accessibility, responsive/RTL states, and export.
- Observation/trading-date freshness.
- Durable sync run/run-detail records and full health/telemetry surface.
- Full 1,124-point executable fixture and all required integration/visual/E2E tests.

### Incorrect

- Production `/ps-data` deserialization.
- Normalized provider exception behavior after resilience exhaustion.
- EF migration/model-snapshot coherence.
- History metadata-mismatch success bookkeeping.
- Lease-loss protection before commit.
- Invalid-boundary persistence/quality behavior against Task 10.

### Partial

- Scope normalization, contract validation, canonical hashing proof, coordinator bounds, worker restart policy, DataAdmin operations, selection/fallback, history projection, status modeling, and configuration rollout proof.

## 11. Security and operational findings

Release security gates fail:

1. `docs/cw_api.txt:82` contains a tracked Bearer token.
2. CyclicalWaves username/password values are committed in both API and Worker appsettings.
3. The runbook correctly requires rotation, but documentation is not remediation.
4. The feature fixtures themselves contain no authorization values or browser-only headers, and their hashes are tested.
5. No full P/S payload is logged or returned by the new client/Admin paths based on inspected code.
6. No proof exists that configuration diagnostics cannot expose the committed credentials.

Operationally, the feature is disabled by default, which limits immediate runtime exposure. It does not make the release ready: enabling sync currently cannot create valid snapshots because of F-01.

## 12. Data integrity findings

- Schema key design is largely correct: snapshot uniqueness is provider/company/date; history uniqueness is provider/company/provider-point-ID; date is non-unique.
- Same-date points are preserved and secondarily ordered by opaque provider ID.
- The six-point fixture demonstrates representability, not the required 1,124-point persistence outcome.
- Same provider ID with conflicting accepted content is quarantined, which matches Feature 114 Task 11 but conflicts with the audit request's assertion that corrected records must be updated deterministically. The primary task is authoritative unless amended.
- The full history read is unbounded before truncation.
- Metadata-mismatched refreshes are not activated, but success timestamps/bookkeeping are incorrect.
- Snapshot selection can silently fall back to an older renderable row without stating that a newer attempt failed or was invalid.
- Date parsing uses `DateOnly`/`System.Text.Json` and invariant hashing, avoiding culture-dependent date strings. No trading-date/market-calendar interpretation exists in Feature 115.

## 13. Gauge algorithm verification

### What the code does

`PsGaugeCalculator.cs:13-47`:

1. requires six non-negative counts and a positive total;
2. computes exact percentages with decimal arithmetic;
3. reconciles display percentages to exactly 100 using largest remainder;
4. creates six equal numeric intervals over `min..max`;
5. creates six equal 30-degree arcs in `a..f` order;
6. maps current TTM `ps_ratio` linearly over `min..max`;
7. preserves the original source value and clamps only normalized render position.

### Evidence

- Shiraz example verifies six 30-degree arcs, provider-derived percentages totaling 100, and an in-range needle.
- Ggolpa verifies below-min clamping.
- Zero total and reversed min/max are rejected.
- Supplied screenshots visually agree with `a..f` percentage labels in equal-width green-to-red bins and `min..max` labels.

### Unverified cases

- above-max current value;
- zero and negative current ratio;
- missing current ratio before persistence;
- missing individual boundary/bucket fields;
- negative bucket and sum overflow tests;
- culture/high-precision/property tests;
- exact visual/pixel parity;
- UI/PNG agreement.

`decimal` cannot represent NaN or Infinity; JSON containing those tokens should be rejected as malformed JSON, but that behavior is not tested through the production client.

### Specification conflict

The designated primary specifications and reverse-engineering notes agree with the implementation. The attached audit instructions assert a different algorithm. This audit does not silently choose between conflicting governance documents. Rendering must remain gated until one authoritative specification supersedes the other and independent reference tests are approved.

## 14. AI routing, API, frontend, monthly-sales, and export findings

Repository search found:

- no `PsGaugeVisualization` value in `DetectedIntent`;
- no P/S visualization branch in V1 or Microsoft Agent Framework V2;
- no optional `PsVisualizationResult` on `AiQueryResponse`;
- no P/S visualization field in persisted assistant messages or frontend chat contracts;
- no use of `IPsVisualizationExperienceUseCase` beyond DI registration and its implementation;
- no compact P/S field on the monthly activity result;
- no P/S component or renderer;
- no P/S image-export path.

Consequently, every requested Persian alias currently follows existing metric/analysis behavior, not Feature 115. Conversation reload, disabled/enabled monthly behavior, static fallback, Telegram implications, and interactive/export parity cannot be verified.

## 15. Migration and clean-database verification

Observed:

- EF recognizes `20260729120000_AddCyclicalWavesPsVisualization` as pending.
- Idempotent SQL generation from the previous migration succeeds and contains the gauge, history, state, and lease tables.
- `dotnet ef migrations has-pending-model-changes` fails with: `Changes have been made to the model since the last migration. Add a new migration.`
- The model snapshot contains no Feature 114 entities.

A destructive `database update` was not run against the configured database. The repository did not expose a disposable clean PostgreSQL target for this audit, and the full integration suite was already using a failing external/test infrastructure state. Clean-database apply/rollback/upgrade remains **unverified**, not passed.

## 16. Commands and tests executed

| Command | Result |
| --- | --- |
| `dotnet build FinancialCopilot.sln --configuration Release --no-restore --verbosity minimal` | Passed; 0 warnings, 0 errors. |
| `dotnet test tests/FinancialCopilot.UnitTests/... --configuration Release --no-build` | Passed: 1,087/1,087. |
| `dotnet test tests/FinancialCopilot.ArchitectureTests/... --configuration Release --no-build` | Passed: 7/7. |
| `dotnet test tests/FinancialCopilot.IntegrationTests/... --configuration Release --no-build` | Failed: 114; passed: 280; total: 394. |
| Integration `--list-tests` filtered for P/S visualization/gauge names | Zero matching tests. |
| `npm test` in `src/frontend` | Failed: 1; passed: 27; total: 28. |
| `npm run build` in `src/frontend` | Passed; client and SSR builds completed; chunk-size/unused-import warnings. |
| `dotnet ef migrations list ...` | Feature migration recognized as pending. |
| `dotnet ef migrations has-pending-model-changes ...` | Failed, exit 1; model changed since last coherent snapshot. |
| `dotnet ef migrations script <previous> <feature> --idempotent ...` | Passed; generated SQL contains all four P/S tables. |
| `git grep` token/header scan plus redacted appsettings scan | Found tracked Bearer token and committed non-empty CyclicalWaves credentials; no `sec-ch-*`, Referer, or User-Agent fixture artifacts. |
| `git status --short` | All new Feature 114/115 implementation/spec files are untracked; existing unrelated workspace changes were preserved. |

## 17. Exact failures and skipped verification

### Backend integration

Summary:

```text
Failed! - Failed: 114, Passed: 280, Skipped: 0, Total: 394
```

Failures span existing AI facade, monthly activity trend, direct-period lookup, explainable answer, market insight, memory, and schema tests. Representative failures include:

- `V2MonthlyActivityTrendEndpointTests.V2AiQuery_MonthlyActivityTrendQueries_ReturnChartPayloadWithoutToolLoop`
- `AiFacadeEndpointTests.Messages_ReloadsStructuredAssistantPayload_AndPromotesTitle`
- `CyclicalWavesDirectPeriodMetricLookupTests.AiQuery_PeLookup_StillReturnsValuationMetric`
- `ExplainableAnswerEndpointTests.AiQuery_ExplainableAnswer_PresentWhenScannerSucceeds`

These failures were not modified during the audit. They make the repository regression baseline red and prevent a “ready” verdict, even though no Feature 114/115 integration tests exist.

### Frontend

```text
FAIL disclosures.test.ts
uses an explicit unknown value for missing publication and never substitutes receipt time
Expected receipt date formatting to contain "(تهران)";
received "۱۴۰۵/۰۴/۱۱, ۰۰:۰۰".
```

This appears unrelated to Features 114/115 but keeps the frontend test baseline red.

### Skipped/unverified

- Clean disposable PostgreSQL migration apply/down/upgrade replay: no approved disposable target was provided.
- Provider live calls: intentionally not made; audit relied on frozen contracts and supplied evidence.
- Visual/screenshot comparison: no Feature 115 renderer exists.
- Accessibility, mobile, RTL, light/dark, PNG parity, and E2E: no corresponding implementation/tests exist.
- Real 1,124-point persistence replay: full payload is not retained.

## 18. Prioritized remediation plan

### Critical

1. Rotate/revoke the tracked token and committed provider credentials; remove/purge secrets and enable CI secret scanning.
2. Correct the production `/ps-data` envelope and add production-client contract tests before any sync enablement.
3. Resolve the gauge contract conflict with one authoritative amendment and approved API/UI reference tolerances.
4. Implement the Feature 115 AI/API/conversation/frontend delivery path; until then, do not describe Feature 115 as implemented.

### High

1. Regenerate/cohere the EF migration metadata and replay it on disposable PostgreSQL.
2. Add the full 1,124-point fixture/proof and persistence, idempotency, duplicate-date, reconciliation, and cap tests.
3. Implement dual freshness and explicit newest/older-fallback/component-state selection.
4. Fix lease fencing/renewal and enforce or remove configured concurrency.
5. Normalize resilience failures and required-field validation.
6. Add durable run/run-detail observability and DataAdmin failure inspection.
7. Add V1/V2 routing, API/reload, disabled/enabled monthly enrichment, and no-live-provider integration tests.
8. Add frontend gauge/history/inset/export/accessibility and visual regression coverage.
9. Bring the existing integration and frontend regression baselines back to green.

### Medium

1. Correct metadata-mismatch success bookkeeping and invalid-boundary/quality persistence semantics.
2. Bound history reads at the database while preserving deterministic latest-point behavior.
3. Strengthen/document ISIN validation.
4. Add explicit worker restart/missed-run policy and tests.
5. Complete provider health, low-cardinality metrics, and operational troubleshooting procedures.
6. Consolidate duplicate task entries and update checklist state only after evidence passes.

### Low

1. Add property/culture/high-precision tests for percentage reconciliation and hashing.
2. Add explicit above-max, zero, negative, malformed-number, overflow, and warning-localization cases.
3. Address frontend bundle-size warnings independently of feature correctness.

## 19. Final verdict

**Not ready for production.**

Feature 114 has useful foundations but cannot currently ingest the real current-P/S payload, has unresolved migration/security/lease/observability/test gaps, and lacks full-history proof. Feature 115 is not connected to any user-facing path and has no renderer or export. Production enablement must remain disabled until the Critical and High remediation items are implemented and independently verified against the authoritative gauge contract.
