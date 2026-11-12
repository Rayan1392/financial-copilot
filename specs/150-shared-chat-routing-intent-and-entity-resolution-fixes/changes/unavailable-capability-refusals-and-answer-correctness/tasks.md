# Tasks: Clear refusal for unavailable topics and correct lookup answers

Status: Gate B: Approved by the owner on 2026-11-11

Gate B conditions (owner, 2026-11-11):

- C1: «تحلیل بنیادی فولاد», «تحلیل فولاد», «تحلیل جامع فولاد» keep today's comprehensive-analysis route and answer (CyclicalWaves source). Do not break it.
- C2: a bare ticker keeps today's route and answer.
- C3 (OQ-072): a message with an analysis word and a topic word (for example «تحلیل تکنیکال فولاد») keeps the analysis card when comprehensive analysis is available. Rule: the topic rule does not fire when the deterministic interpreter's first candidate is `comprehensive_analysis` and `IChatCapabilityAvailability.Evaluate(comprehensive_analysis)` is available.
- R5 stays in scope.

## Feature Folder

`150-shared-chat-routing-intent-and-entity-resolution-fixes/changes/unavailable-capability-refusals-and-answer-correctness`

Branch: `track/a-routing-answer-correctness` in worktree `D:\wt\a`, fast-forwarded to `develop` `da623ed` on 2026-11-11.

## Before Starting

- [x] This file's `Status:` line records Gate B approval by the owner (see `specs/FEATURE_DEVELOPMENT_WORKFLOW.md`, "Recording Approval").
- [x] The features in `Depends On` are done (none).
- [x] Read `user-story.md`, the references it lists, and the relevant `docs/` files.
- [x] No open question in `specs/open-questions.md` blocks this spec (OQ-066 to OQ-071 answered at Gate A).

## Implementation Tasks

Order: the two independent fixes first (smallest, no shared files with R0), then R0 bottom-up. Each bug gets a failing regression test before the fix.

### 1. R4 — unit label (BUG-006)

- [x] 1.1 Failing tests in `tests/FinancialCopilot.UnitTests` (`AnswerConsistencyTests` style): requested `LATEST_PRICE` with a `MONTHLY_SALES` context column → no `واحد: میلیون ریال`, sentence contains `ریال`; requested `MONTHLY_SALES` → note kept; table without `RequestedMetricCodes` → text unchanged (V1).
- [x] 1.2 `Scanner/AnswerConsistencyServices.cs`: `WithUnitNote` decides from `RequestedMetricCodes` when set; `ValueSentence` appends ` ریال` for `LATEST_PRICE` only.

### 2. R5 — no value is NoData (BUG-007)

- [x] 2.1 Failing tests: `SymbolMetricLookupCapabilityExecutor` with every requested cell Missing → `NoData` with table payload; one requested value present → `Executed`; billing completion `NoData` → zero charge.
- [x] 2.2 `AI/Orchestration/SemanticCapabilityExecutors.cs`: executor returns `NoData(frame, table)` when no row has a value for any requested metric.
- [x] 2.3 Failing test, then fix: the web text for that turn is the deterministic Persian sentence, not the generic NoData text, in `FinancialCopilotWorkflowDefinition` and `FinancialCopilotAgentWorkflowRunner` (summary for a NoData `SymbolLookupTableResult`).
- [x] 2.4 Failing test, then fix: `TelegramAssistantResponseRenderer` prints `TextAnswer` above the single-symbol card when `Outcome` is `NoData`.

### 3. R0 — topic catalog and match rule

- [x] 3.1 Failing tests: theory table for every phrase in R1–R3, the five reported questions, ZWNJ and Arabic-letter variants, the R3 pair rule, and the negative cases (`قیمت فولاد`, `آخرین قیمت فولاد`, `نسبت قیمت به سود فولاد`, `ارزش بازار فولاد`, `تحلیل بنیادی فولاد`, `چارت فروش فولاد`).
- [x] 3.2 New `Application/AI/Orchestration/UnavailableTopics/UnavailableTopicCatalog.cs`: one row per topic (topic, phrases, reason code), whole-word match on `QueryNormalization.Normalize`. No framework dependency.

### 4. R0 — capability, executor, text

- [x] 4.1 `ConversationalCapabilityContracts.cs`: route `unavailable_topic`, definition `GateRoutedOnly = true`, optional `symbol` slot, data requirement `unavailable_topic_copy`.
- [x] 4.2 New `UnavailableTopics/UnavailableTopicCapabilityExecutor.cs`: no data read, returns `Unsupported` with the topic reason code (read from the frame's interpretation evidence).
- [x] 4.3 `ChatCapabilityAvailability.cs`: three reason-code constants, Persian (approved copy) and English messages, `MessageForReasonCode` cases. `Evaluate` and route sets unchanged; `CyclicalWavesSupportedRoutes` gets `unavailable_topic` only if a test shows it is needed.
- [x] 4.4 `FinancialCopilot.Infrastructure/ServiceCollectionExtensions.cs`: register the executor and pass the availability service to the gate.
- [x] 4.5 Test: every reason code has Persian and English text; registry validation accepts the new definition; the capability is not in `GetEnabled()` (not offered to the model or guidance).

### 5. R0 — dialogue gate

- [x] 5.1 Failing gate tests (style of `ComprehensiveAnalysisSemanticRoutingRegressionTests`): stub model returns `product_revenue_mix`; assert frame capability `unavailable_topic`, reason code, zero model calls; symbol slot filled only for a single resolved company; ambiguous/unknown → empty slot, no clarification; pending question → today's route; Symbol Snapshot on + bare ticker → snapshot; V1 scope, CyclicalWaves mode, rollout Rollback → today's route.
- [x] 5.1a Guard tests for C1–C3, run before and after the change with identical results: «تحلیل بنیادی فولاد», «تحلیل فولاد», «تحلیل جامع فولاد», «تحلیل تکنیکال فولاد» (comprehensive analysis available) → frame `comprehensive_analysis`; a bare ticker with Symbol Snapshot on → `symbol_snapshot`. «تحلیل تکنیکال فولاد» with comprehensive analysis unavailable → technical refusal. Existing Feature 156 and Feature 159 test classes stay green unchanged.
- [x] 5.2 `ConversationTaskStateContracts.cs` (`ConversationDialogueGate`): new optional constructor arguments (catalog, `IChatCapabilityAvailability`), default `null` = rule off; `TryPrepareUnavailableTopicAsync` after the snapshot check and before assistant help, same frame shape as assistant help.

### 6. Workflow behaviour (both V2 runners)

- [x] 6.1 Tests: refusal turn → Outcome Unsupported, approved Persian text, follow-ups `روند فروش {symbol}` / `گزارش فصلی {symbol}`, billing `ValidationFailed` (zero), Feature 141 fallback not run, `SemanticCapabilityCode` = `unavailable_topic`. Fix only what a failing test shows.

### 7. Documentation and records

- [x] 7.1 `specs/open-questions.md`: OQ-066 to OQ-071 with the Gate A answers (done at Gate A; verify).
- [x] 7.2 `specs/README.md`: index line for this change.
- [x] 7.3 `docs/architecture.md`: gate order and the `unavailable_topic` capability, one short paragraph.
- [x] 7.4 `CHANGELOG.md` `## [Unreleased]`: one Persian user-facing line.
- [x] 7.5 `specs/backlog.md`: prompt "return no capability when nothing fits"; default refusal text still offers "تحلیل‌های ثبت‌شده"; wider phrase list after false-positive data.
- [x] 7.6 Bug status for BUG-004 to BUG-007: they live on the unmerged `track/lead-unanswered-coverage` branch. Update them there, not on `develop`, until the owner approves the lead-branch merge.

## Tests

- [ ] Unit tests for tasks 1–6 (`tests/FinancialCopilot.UnitTests`).
- [ ] Integration tests where the existing workflow harness covers the runners (`tests/FinancialCopilot.IntegrationTests`).
- [ ] Architecture tests (`tests/FinancialCopilot.ArchitectureTests`): the new folder has no Infrastructure or framework dependency; only BUG-003 may fail.
- [ ] Live check on local Development with the disposable PostgreSQL (memory: Feature 141 runner): `تکنیکال کگهر`, `پیوت های کاما را نشان بده`, `قیمت تعادلی فپنتا`, `ارزش ذاتی رمپنا چقدر است؟`, `فاذر ارزش خرید داره؟`, `قیمت فولاد`, `P/E فولاد`.

## Expected files

| File | Change |
|---|---|
| `src/backend/FinancialCopilot.Application/AI/Orchestration/UnavailableTopics/UnavailableTopicCatalog.cs` | new |
| `src/backend/FinancialCopilot.Application/AI/Orchestration/UnavailableTopics/UnavailableTopicCapabilityExecutor.cs` | new |
| `src/backend/FinancialCopilot.Application/AI/Orchestration/ConversationTaskStateContracts.cs` | gate rule |
| `src/backend/FinancialCopilot.Application/AI/Orchestration/ConversationalCapabilityContracts.cs` | route and definition |
| `src/backend/FinancialCopilot.Application/AI/Orchestration/ChatCapabilityAvailability.cs` | reason codes and text |
| `src/backend/FinancialCopilot.Application/AI/Orchestration/SemanticCapabilityExecutors.cs` | R5 outcome |
| `src/backend/FinancialCopilot.Application/Scanner/AnswerConsistencyServices.cs` | R4 |
| `src/backend/FinancialCopilot.Infrastructure/ServiceCollectionExtensions.cs` | wiring |
| `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs`, `FinancialCopilotAgentWorkflowRunner.cs` | R5 text, only if task 2.3 fails |
| `src/backend/FinancialCopilot.Infrastructure/Authentication/TelegramAssistantResponseRenderer.cs` | R5 Telegram |
| tests (new and existing test classes), docs listed in task 7 | |

No database migration, no API contract change, no configuration key, no frontend change.

## Risks

| Risk | Mitigation |
|---|---|
| False refusals | Whole-word match, short list, negative theory table, kill switch `SemanticRouting__Capabilities__unavailable_topic=Rollback` |
| R5 turns other single-metric answers into NoData | Only when no requested value exists in any row; tested both ways |
| Merge contention with tracks B and C (gate, workflow step 3) | Track C merged; B (`e79979f`, Feature 157) is proposal only. Rebase on `develop` before merge |
| Revenue: these turns become free | Existing "no answer, no charge" rule; stated at Gate A (design §6) |

## Definition Of Done

- [ ] `dotnet test src/backend/FinancialCopilot.sln --configuration Release` green (builds the solution and runs unit, integration and architecture tests): no failure outside `specs/test-baseline.md`.
- [ ] Acceptance criteria in `user-story.md` all checked.
- [ ] Reference docs updated: `specs/README.md` index entry, `docs/architecture.md`.
- [ ] User-visible change: a Persian, user-facing line under `## [Unreleased]` in `CHANGELOG.md`.
- [ ] New questions → `specs/open-questions.md`; deferred work → `specs/backlog.md`; defects found → `specs/bugs.md`.
- [ ] No commit, merge, push, or release without the owner's request.

## Results (2026-11-11)

- Full solution run on this branch and on `develop` `da623ed` (same machine, same session): identical failure sets after the catalog-count update. Unit 32 (= `specs/test-baseline.md`), architecture 1 (BUG-003), integration 186 — all 186 also fail on `develop`; not in the baseline file (see the Feature 159 backlog note).
- New tests, all green: `Feature150UnavailableTopicTests` (59), `Feature150LookupAnswerCorrectnessTests` (10), `Feature150UnavailableTopicE2ETests` + `…MappedAnalysisE2ETests` + `…CyclicalWavesE2ETests` (20, through `POST /api/ai/v1/query`).
- Existing test changed on purpose: `ConversationalCapabilityRegistryTests.InitialCatalog_ContainsOnlyExecutableBusinessCapabilities` 22 → 23 capabilities.
- Gate B C1–C3 verified end to end on the production shape (NoavaranAmin + comprehensive analysis from CyclicalWaves); «تحلیل تکنیکال فولاد» reply checked identical on `develop` (BUG-020 registered on the lead branch).
- [~] AC-12 live check on local Development: not run. A real local HTTP turn needs a test identity, JWT signing key, and billing account (see memory of Feature 141). Owner to run the seven questions, or approve setting that up.
- Build: 0 warnings. Nothing committed, merged, or pushed.
