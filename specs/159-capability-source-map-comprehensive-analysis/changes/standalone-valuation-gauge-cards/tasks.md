# Tasks — Standalone valuation gauge cards

Status: Gate B: Approved by the owner on 2026-11-25

Approval record: Gate A «**Gate A**  مورد تایید است» and Gate B «گیت بی را تایید میکنم» on 2026-11-25. Gate B approves this plan with the settings in OQ-130 through OQ-132.

[Approved design](./design-proposal.md) · [Acceptance criteria](./user-story.md)

## Before starting

- [x] Record explicit Gate B approval of this plan and the OQ-130 through OQ-132 decisions.
- [x] Check the current worktree for changes to shared routing and persistence files before editing.
- [~] Verify Feature 148 read dependencies in the target environment. Review DataAdmin sync timestamps and coverage. Sample one fresh ticker per gauge kind, a missing vendor match, an ambiguous match where available, a null close and signed equilibrium bounds. Use fixtures for edge cases unavailable in staging and identify them as such. Capture read-only evidence and record the existing entitlement and tariff behavior and regression baseline. *Local development database only (`evidence/feature148-local-sample*`, 2026-11-22 sync); edge cases covered by fixtures. Staging not reachable from this session.*
- [x] Use current V2 orchestration and provider-composition conventions and the existing frontend chat rendering and action flows.

## Implementation

### T1 — Application and Infrastructure data path

- [x] Define requested kinds, a canonical-symbol query and typed results per kind under the current Application financial-data structure. Represent fetch time, state and source per kind, and quote metadata separately. Consume latest-state `GaugeAvailability`, `GaugeDetails` and `EquilibriumRanges` through a scoped read-only Infrastructure adapter. Use a unique active identity or ISIN match, with coverage-miss outcomes for missing or ambiguous mappings.
- [x] Reuse the canonical quote interface used by `CyclicalWavesV2RelativeValuationInputReader` for the equilibrium needle. Select the quote for the same instrument, preserving market timestamp, source and nominal units. Match existing market-session rules and test closed-market and timeout cases.
- [x] Project ratios and equilibrium geometry deterministically. Reuse the largest-remainder calculation from `PsGaugeCalculator` where safe and keep the existing P/S defaults for old callers. Compute 30° / 90° / 150° ratio anchors from provider facts and 0° / 90° / 180° equilibrium anchors. Preserve exact wire values and keep angular calculations bounded. Specify clamp and textual fallback semantics with fixtures.
- [x] Bind and validate fetch-age settings and use the existing clock. Retain per-kind partial availability, distinguish expected timeout from caller cancellation, and add low-cardinality metrics for coverage, age, outcomes and latency. Initial implementation adds no tables, migrations, provider jobs or external HTTP dependencies.

### T2 — V2 classification, availability and accounting

- [x] Attach proposed route `valuation_gauges` to the V2 capability registry/interpreter and execute via the existing deterministic result path in `FinancialCopilotWorkflowDefinition` and the applicable semantic executor. Classify only explicit single-purpose requests. Ensure the accepted capability, entity, requested kinds and operation agree before reservation. Preserve Feature 148 fallback behavior for the old mode when the flag is off and narrowly prioritize the new route when eligible. Leave V1 registration and behavior on the existing baseline.
- [x] Extend `ChatCapabilitySourceMap`, `ChatCapabilityAvailability` and `CyclicalWavesV2ServiceCollectionExtensions`. Allow the new route to map to CyclicalWaves only. Set `ValuationGaugeCards:Enabled` false by default in API config and expose it through existing deployment configuration conventions. Verify absent override, invalid value, and both base modes.
- [~] Use the operation-based billing framework and existing zero-charge outcomes. Review `AiQueryBillingOperationSelector`, reservation-before-dispatch and shared finalization. Reuse a verified existing minimum-credit policy if suitable. If the selected tariff requires a new billing seed or migration, return for scope review before adding it. Cover no-data, failure, cancellation, partial results and duplicate request settlement. *Verified: one `AiQuery.Scanner` minimum for usable single, all-gauges and range-only turns; free clarification, no-data, stale and temporarily-unavailable turns; a retried request settles once. Not exercised end to end: settlement of a turn cancelled by the caller (the use case rethrows caller cancellation into the existing finalization).*

### T3 — API and persisted assistant message

- [x] Carry the optional collection from the V2 execution result through `AiFacadeContracts`, response mapping, `MessagePersistenceFunction` and authorized history reads. Confirm payload-size and serialization compatibility with the existing result envelope. Keep exact numbers as strings on the wire, preserve timestamps, and verify legacy payloads and feature rollback.
- [x] Build localized text from the typed result. Connect the summary to `TelegramAssistantResponseRenderer` through the existing response path. Show the same resolved symbol, facts, source and missing-data disposition as the web cards. Reuse channel length and formatting rules.

### T4 — Web cards and symbol menu

- [x] Extend existing chat types and `assistant-block-mapper.ts` for live and persisted messages. Render independent metric cards from `message-list.tsx`, reusing compatible dial primitives and Sapio theme tokens. Implement compact fact lists, signed values, labels for unranked or out-of-range values, and localized source/fetch metadata.
- [x] Extend `symbol-actions-menu.tsx` and its existing desktop/mobile callers. Derive visibility from server-evaluated feature/source availability through the existing client configuration or availability boundary. Treat unknown availability as disabled. Submit the same text handled by direct chat and preserve normal confirmation or billing behavior.
- [x] Verify representative screenshots for light/dark, narrow/mobile and desktop with sidebar, plus RTL, zoom, text equivalents and keyboard interaction for any controls. Collect browser evidence for a single ratio card, all cards, range-only, stale and missing data.

### T5 — Release readiness and rollback

- [~] Run targeted verification before the full suite. Compare the V2 route, data-source reads, outcome and ledger against the existing behavior for point lookup, scanner, analysis/fallback, monthly sales and PNG, quarterly reports and Feature 125. Smoke-test standalone Feature 115 in base CyclicalWaves with the flag off. *2026-11-26: point lookup, monthly sales, financial statements and Feature 158 rollback classes fail identically on clean HEAD and this tree (16/58 both); full integration suite 199 failures on both (HEAD 874/1231 passed, this tree 892/1249 with the 18 new tests passing). Flag-off phrases keep their existing route (integration). Not done: a live Feature 115 smoke in a running environment and PNG output by eye.*
- [ ] Prepare staging config with the base provider unchanged. Check effective route source, 48-hour age behavior with actual sync timing, quote coverage, charge behavior, text parity and reopened conversations. Verify rolling back the enable flag stops new cards and menu actions while leaving stored results readable.
- [~] Document the activation and rollback steps, health signals, source-coverage baseline and any deviations. Use the repository's release process for changelog and deployment coordination after verification. *Documented in `docs/providers/cyclicalwaves-v2/README.md` and ADR 0001. Changelog and deployment coordination are left to the release process (not authorized here).*

## Verification matrix

| Suite | Required coverage |
|---|---|
| Backend unit | Phrase recognition and collisions, both provider-mode combinations, grouping by requested kind, status/freshness, six counts and rounding, asymmetric average anchors, exact/signed values, out-of-axis clamps, zero/degenerate ranges, quote timeout and cancellation. |
| Backend integration | Auth and ownership, fresh/partial/empty/unavailable responses, V2 persistence and replay, feature rollback, tariff selection, idempotency, and assertions that accidental source reads and model or vendor requests are absent. |
| Frontend | Message mapper and render tests for old, live and replayed replies, plus accessibility and responsive browser verification in T4. |
| Telegram | Equivalent text facts, graceful missing data and current channel formatting limits. |

## Definition of done

- [~] AC-01 through AC-11 verified with automated tests or recorded manual evidence. Review test commands and known failures against `specs/test-baseline.md`. *All ACs covered by automated tests or browser evidence on fixtures and the local database; staging checks and the cross-host rollback replay (AC-06) are not verified.*
- [x] Run `dotnet test src/backend/FinancialCopilot.sln --configuration Release`. In `src/frontend`, run `npm run lint`, `npm run test`, `npm run build` and `npm run typecheck`. Investigate new failures and report existing baseline deviations. *2026-11-26: unit 28 failures, all in the baseline except `AnalyticalAnswerAndReportScoreTests.FallbackPrompt_IsAnalytical_KeepsKeyFigures_AndScores` (fails on clean HEAD too); architecture 24/24; integration has no baseline and matches clean HEAD. Frontend: typecheck 37 (baseline), lint 1211 errors / 14 warnings identical to HEAD, build green, tests 3 failures that also fail on clean HEAD (fa-terminology, markdown-message, monthly-product-trend-chart) plus the `scripts/*.browser.test.mjs` suites that vitest picks up but which run under node.*
- [ ] Verify staging and rollback evidence. Before any production enablement, confirm authorization through the existing release process.
