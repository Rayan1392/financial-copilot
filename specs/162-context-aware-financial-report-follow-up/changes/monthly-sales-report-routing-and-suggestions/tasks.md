# Tasks: Monthly sales report routing and product suggestions

Status: Gate B: Approved by the project owner on 2026-11-25

## Implementation

- [x] T1. In `FinancialCopilotWorkflowDefinition` and the V2 dialogue gate, give supported, explicit sales report requests a deterministic route before Report Discussion. Use the existing capability rules and deterministic interpretation so that hybrid interpretation does not call a model for these requests. Preserve normal capability validation and execution. Keep interpretation suggestions, explicit interpretation requests, pending clarification, and ambiguous requests subject to their existing handling. Add a `ReportDiscussion162EndpointTests` regression for the screenshot sequence with Deep and web selected, with model and web fakes asserting zero calls and the result and billing asserting the existing report path.
- [x] T2. Update the V2 product trend extraction and suggestion round-trip checks so canonical sales-rate requests retain meaningful words in product titles, including the leading «محصول». Retain company-only and comparative query checks. Keep aliases and uniqueness tied to the existing product identity matcher and provider scope. Check the canonical payload for «روند نرخ فروش محصول گالوانیزه فجر».
- [x] T3. Update `MonthlySalesProductFollowUpSuggestionService` and `ProductRevenueMixFollowUpSuggestionService` for at most two rate suggestions below each report. Use the trend product catalog and the mix's period rows, checking the corresponding persisted trend observations for usable rates where the mix rows alone do not establish eligibility. Use the existing rate calculator's amount and quantity conditions and bounded reads, with no per-product provider calls. Sort by revenue descending with a stable tie-breaker. Use `SuggestedAction.LocalizedLabel` for «روند فروش محصول گالوانیزه» and `Message` for the company-scoped rate request. Extend the 075 and 137 unit tests for rank, the exact label and canonical request, ambiguity, and missing rate data.
- [x] T4. Give monthly sales report analysis hints the Simple/no-web defaults in both live and saved history. Resolve the same defaults in `ReportDiscussionHandler` for explicit interpretation with no options. In `use-chat-thread` and the shared analysis choice logic, scope manual overrides to the current monthly sales report so stored preferences cannot override the starting choice. Keep the user's chosen settings on an explicit interpretation request. Extend the shared chat hook and analysis tests for reopened conversations, saved Deep/web, manual overrides, and ordinary report defaults.
- [ ] T5. Run affected backend unit, integration, and architecture tests, plus frontend Vitest, lint, build, and typecheck. Run the Release backend suite and compare failures with `specs/test-baseline.md`. Verify the screenshot flow with representative data, including the Fajr case against live data if authorized access is available. Record verification and update BUG-048 and the relevant specs. Coordinate the Persian Unreleased entry with the repository's release owner for merge.

## Verification

- The focused backend run passed all 89 tests in the 075, 121, 137, 138, and 162 unit test classes.
- The two focused V2 endpoint tests passed. The flow displayed a seeded monthly sales report, reopened it with Simple and web off, submitted the product mix request with Deep and web on, and opened a product trend with a valid rate. The routing and analysis model fakes recorded zero calls. The Fajr label and product identity were verified in the unit test fixture.
- All 24 architecture tests and 34 focused frontend tests passed. The frontend production build succeeded, and lint passed for the four changed frontend files with the Windows line ending option. The full lint and typecheck runs reported failures. Typecheck reported none in the changed frontend files.
- The full Vitest run reported failures in browser scripts, UI terminology, and existing component suites. The rerun was stopped after those failures had been recorded. The focused run completed successfully.
- The full backend Release run reported 33 unit test failures and 191 integration test failures. The failures in the adjacent V2 sales-routing and comparison tests, and the 14 monthly chart average assertions, also reproduced on clean `64b8b8a1`. The existing product mix suggestion round-trip test passed with this change.
- Live Fajr data has not been exercised. The change remains in the local workspace.

## Definition of Done

- [x] Gate B approval recorded before implementation.
- [x] AC1–AC5 verified with test fixtures and live-data and baseline limitations recorded.
- [ ] No new regression or warning beyond the recorded baseline.
