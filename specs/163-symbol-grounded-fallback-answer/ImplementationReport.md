# Implementation Report: Feature 163 — Symbol-Grounded Fallback Answer

Date: 2026-11-16. Branch `feature/163-symbol-grounded-fallback`, worktree `D:/wt/f163`, from `develop` `778739f`. Uncommitted. Flag `SymbolGroundedFallback:Enabled` is off everywhere.

## Done

- Application `AI/SymbolGroundedFallback/`: contracts, eligibility rule, prompt builder, model adapter, handler.
- Shared with Feature 162: `ReportDiscussionContextBuilder.RenderReport`, `ReportDataSetBuilder.BuildQuarterly`; `ActiveReportResolver` and `ReportDataSetBuilder.Build` read the hidden `GroundingReport`.
- `AssistantMessagePayload`: optional `GroundingReport` and `FallbackOf`.
- V2 workflow: one call in the result-computation step; own billing reservation; persistence of the two new fields.
- DI, options (validated at start), `appsettings*.json`.
- Docs: `specs/README.md`, `docs/architecture.md`, `docs/domain-glossary.md`, `specs/backlog.md`.

## Verification

- New tests: 26 unit, 11 endpoint, 2 architecture — all pass.
- Full suite (`dotnet test src/backend/FinancialCopilot.sln -c Release`) on this branch and on untouched `develop` `778739f`, same machine, same day:

| Suite | develop | this branch |
|---|---|---|
| Architecture | 1 failed / 17 | 1 failed / 19 (same test, BUG-003) |
| Unit | 34 failed / 3671 | 34 failed / 3697 |
| Integration | 194 failed / 1105 | 195 failed / 1116 |

  The failing sets are identical except `TelegramFinancialStatement143EndToEndTests.Gateway_post_with_auto_backfill_disabled_is_claimed_without_provider_requests`, which failed once in the full run on this branch and passes when its class is run alone (the class's other 3 failures are the same on both). The many integration failures also exist on `develop`; they are not listed in `specs/test-baseline.md`.
- T02 check: `AiFacadeController.MapAssistantContent` maps payload fields one by one into the response, so `GroundingReport` is not sent to the web client or the Telegram renderer. `EfCoreAdminChatHistoryQueries` parses the raw payload for administrators.

## Found and fixed during implementation

- The reservation key is the billing operation id, so a reservation with a derived correlation id reached the turn's already settled reservation and the fallback never ran. The fallback now derives its own deterministic billing operation id.

## Deviations from the approved plan

1. Insufficient credit: the turn is left unchanged instead of returning the insufficient-credit response (user-story FR-12).
2. No `ReportDiscussion` marker on the fallback message (FR-10).
3. No new pricing status: the answer settles with `ReportDiscussionAnswered` (2 credits) and execution route `symbol_grounded_fallback`.
4. The "not answerable" chip sends «گزارش فصلی X» (existing text), not «عملکرد فصلی X».
5. Persian replies only.
6. The failure warning logs the exception object (type, message, stack), not only its type.

## Not done

- T16 live local check with a real model (answer quality, valuation/advice wording, typed replies to the closing offer — OQ-097).
- Endpoint tests for AC-12, AC-15, AC-16.
- Feature 155 SQL (`FallbackOf`), `CHANGELOG.md` line.
- Company named by full name or alias (exact ticker only).
