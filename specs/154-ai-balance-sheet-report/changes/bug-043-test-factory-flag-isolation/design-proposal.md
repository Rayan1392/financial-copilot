# Design proposal: BUG-043 — routing endpoint tests inherit product flags from `appsettings.json`

Status: Gate A and Gate B: Approved by the owner on 2026-11-23, C3 option (a)

## Feature Folder   `154-ai-balance-sheet-report/changes/bug-043-test-factory-flag-isolation`
## Layers / Area    backend (tests only) · cross-cutting
## Priority         P2
## Complexity       S
## Depends On       None
## References       `specs/bugs.md` (BUG-043), `specs/FEATURE_DEVELOPMENT_WORKFLOW.md` (Bug Fixes), `tests/FinancialCopilot.IntegrationTests/BalanceSheetReport154EndpointTests.cs` (`BalanceSheetReport154ApiFactory`), `tests/FinancialCopilot.IntegrationTests/CashFlowReport161RoutingBaselineEndpointTests.cs`, `tests/FinancialCopilot.IntegrationTests/FollowUpSuggestionPipeline121EndpointTests.cs`, `src/backend/FinancialCopilot.API/appsettings.json`

No production code and no approved behavior change. The correction restores what these tests were written to
measure. No material design change is proposed.

## 1. Discovery (2026-11-22, proved by runs, no code changed)

The test factories read the API's `appsettings.json` and set only some flags themselves. Three later, approved
commits changed flag values in `appsettings.json`. Each one silently changed what these tests measure.

| Cause | Commit | Flag in `appsettings.json` | Failing tests | Proof |
|---|---|---|---|---|
| C1 | d8c75f63 (162 `sol-deep-analysis`, 2026-11-20) | `ReportDiscussion:Enabled` and `SymbolGroundedFallback:Enabled` false → true | 42 `BalanceSheetReport154FollowUpEndpointTests`, 1 `CashFlowReport161RoutingBaselineEndpointTests.ExistingBalanceSheetFollowUp…`, 2 `FollowUpSuggestionPipeline121EndpointTests` | With `ReportDiscussion__Enabled=false SymbolGroundedFallback__Enabled=false` in the environment, 50 of the 51 154/161 tests pass. |
| C2 | 33dbd926 (081 period-analysis switch, 2026-11-20) | `FinancialStatementPeriodAnalysis:Enabled` added as false | `CashFlowReport161RoutingBaselineEndpointTests.EndpointRoutingAndSequentialConversation…` | The snapshot differs in exactly 3 cases (`fresh-24`, `fresh-53`, `fresh-54`: «تحلیل ترازنامه شگل», «تحلیل صورت مالی فولاد», «تحلیل ترازنامه و جریان وجوه نقد فولاد»). Intent was `FinancialStatementPeriodAnalysis` and is now `Unknown`, as the 081 change intends. |
| C3 | Feature 161 enablement | `CashFlowReport:Enabled` true | 4 `FollowUpSuggestionPipeline121EndpointTests` | The follow-ups of a balance sheet and the greeting samples now include «جریان وجوه نقد X». The expected lists predate 161. |

How C1 reaches the response:

- With Report Discussion on, a message inside an active balance-sheet task first goes to the 162 routing step.
- In these factories the AI client is a routing fake (`V2MonthlySalesRoutingFakeAiModelClient`) that cannot answer the
  162 disposition prompt.
- The handler therefore returns `ProviderUnavailable`, and the turn ends as `ReportDiscussion` ("temporarily
  unavailable") instead of the capability the test expects.

In production the real model hands these messages back, so this is a test-isolation defect, not a product defect.
The interaction with 162 is covered by the 162 and 163 endpoint tests, which turn the flags on explicitly with scripted
models.

## 2. Proposed correction

Each factory states the flags its test measures, so a later change in `appsettings.json` cannot change the result.

1. **`BalanceSheetReport154ApiFactory`** sets `ReportDiscussion:Enabled=false` and `SymbolGroundedFallback:Enabled=false`.
   - Derived factories that need either flag (162, 163, 159, period-analysis) already set it, or will set it, in their
     own in-memory configuration. That configuration is added after the base one, so it wins.
   - `AnalysisRecencyWindow159ApiFactory` sets `SymbolGroundedFallback` but not `ReportDiscussion`, so it today relies on
     `appsettings.json`. It gets `ReportDiscussion:Enabled=true` explicitly, keeping its current behavior.
2. **`CashFlow161BaselineFactory`** and **`CashFlow161V1BaselineFactory`** pin the flags the 161 snapshot was
   captured under: `ReportDiscussion=false`, `SymbolGroundedFallback=false`, `FinancialStatementPeriodAnalysis=true`.
   - The snapshot then proves again what it was written for: that Feature 161 changes no other route.
   - The snapshot file is **not** regenerated.
3. **C3 (`FollowUpSuggestionPipeline121EndpointTests`).** Option (a), recommended: no factory pin. Update the expected
   follow-up lists to include «جریان وجوه نقد X». Cash flow is live in production (owner, 2026-11-22), so these lists
   describe what users see. Option (b): pin `CashFlowReport:Enabled=false` in that test and keep the lists.
4. Update `specs/bugs.md` (BUG-043 → Fixed) and record nothing in `specs/test-baseline.md`.

### Alternatives not proposed

- **Regenerating the 161 snapshot.** It would pass, but the test would then record today's flag set instead of the
  161 routing contract, and the next flag change would break it again.
- **Teaching the routing fake to answer the 162 disposition prompt.** This couples every routing test to 162's prompt
  format. 162 already has its own endpoint tests.

## 3. Acceptance criteria

- AC-1: `BalanceSheetReport154FollowUpEndpointTests` (42) and both `CashFlowReport161RoutingBaselineEndpointTests` pass
  with the committed `appsettings.json`, unchanged, and with no environment overrides.
- AC-2: `endpoint-routing-baseline.json` is unchanged.
- AC-3: `FollowUpSuggestionPipeline121EndpointTests` pass (with option a or b, as approved).
- AC-4: The 162, 163, 159, period-analysis and 154 endpoint test classes have no new failure.
- AC-5: A full integration run has no failure that is not also failing on a clean `HEAD`, other than the ones fixed here.

## 4. Risks

- A derived factory that silently depended on `ReportDiscussion` being on from `appsettings.json` would change
  behavior. Mitigation: AC-4, plus a grep of every `: BalanceSheetReport154ApiFactory`. Only 159 lacks an explicit value
  today.
- Production code and configuration are untouched, so there is no production risk.

## 5. Plan (for Gate B)

- [x] T1: Pin the flags in `BalanceSheetReport154ApiFactory`, and add `ReportDiscussion:Enabled=true` to
  `AnalysisRecencyWindow159ApiFactory`.
- [x] T2: Pin the flags in both 161 baseline factories.
- [x] T3: Apply C3 option (a) (approved) in `FollowUpSuggestionPipeline121EndpointTests`.
- [x] T4: Run the affected classes (AC-1…AC-4), then a full integration run compared with `HEAD` (AC-5).
- [x] T5: Update `specs/bugs.md` (BUG-043 → Fixed).

## 6. Result (2026-11-23)

The changes are in test factories and test expectations only. No production code or configuration changed, and
`endpoint-routing-baseline.json` is unchanged.

- **AC-1 to AC-4:** 145 passed, 3 skipped and 0 failed across the 154, 161, 121, 159, 162, 163 and period-analysis
  classes, with the committed `appsettings.json` and no environment overrides.
- **AC-5:** a full integration run had 205 failures, down from 248 before the fix. All 50 failures in scope are gone:
  42 in `BalanceSheetReport154FollowUp`, 2 in `CashFlowReport161RoutingBaseline` and 6 in `FollowUpSuggestionPipeline121`.
- **Seven failures not present before:** they are in `MarketViewEndpointTests`, `TahlilAppTopUpEndpointTests`,
  `V2SymbolLookupEndpointTests` and `TelegramFinancialStatement143EndToEndTests`. All seven also fail on a clean
  `HEAD` b0aff268 run on the same day.
  - They depend on the date: their "today" fixtures stopped matching when the date moved to 2026-11-23.
  - None of these classes uses a factory changed here.
  - They are recorded as BUG-044.
