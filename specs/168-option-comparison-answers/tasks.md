# Tasks: Option Comparison Answers

Status: Gate A: Approved by the owner on 2026-11-28 (amended scope) — Gate B: Approved by the owner on 2026-11-28 («با گیت B موافقم», with the risk-free rate for OQ-178) — implemented 2026-11-28 and pushed to `develop` (`024e420f`), not released, flag off; open items marked `[ ]` and `[~]` below

## Feature Folder

`168-option-comparison-answers`

## Before Starting

- [ ] This file's `Status:` line records Gate B approval by the owner (see `specs/FEATURE_DEVELOPMENT_WORKFLOW.md`, "Recording Approval").
- [ ] The features in `Depends On` are done: Feature 167 is implemented; its AC-13 and rollout are open. Steps 1 to 4 do not need them; AC-23 does.
- [ ] Read `user-story.md`, the references it lists, and the relevant `docs/` files.
- [ ] No open question in `specs/open-questions.md` blocks this spec (OQ-177 blocks task 1.2, OQ-178 the value in task 6.1, OQ-179 task 7.2).

## Implementation Tasks

Each numbered group ends in a state that builds, passes its tests and can be released with the
flag off. Groups 1 to 4 are delivery step 1.

### 1. Measures (application/domain, no I/O)

- [x] 1.1 `OptionAnswers` options (flag, three fee rates, annualization floor, maximum expiries) with start-up validation.
- [x] 1.2 Underlying class (share or fund unit): rule from OQ-177, checked against the underlyings of a real `Option` response; unknown → share, flagged.
- [x] 1.3 Order-book fill: volume-weighted price for a quantity over levels 1 to 5, with fillable / not fillable / no order.
- [x] 1.4 Call measures and the two call highlights (AC-01 to AC-04).
- [x] 1.5 Freshness rule shared with Feature 167's ranking (AC-05).
- [x] 1.6 `EfCoreOptionChainReader`: return all five levels for the selected expiries and several expiries in one call; keep the Feature 167 endpoints' output unchanged.
- [x] 1.7 `OptionComparison` contract (versioned) and the use case that builds the call view for an underlying.

### 2. Assistant capability (V2)

- [x] 2.1 Intent rules for the call view, with reject words, placed in the V2 dialogue gate (AC-06).
- [x] 2.2 Route constant, registry definition, data requirements, executor, DI, source-map entry, availability (AC-06, AC-22).
- [x] 2.3 Text composer in Persian with the wording table as the single owner of all labels and notes (AC-10, AC-11).
- [x] 2.4 Attach `OptionComparison` to the response, facade contract, mapper and message persistence (AC-08).
- [x] 2.5 Billing: one credit per answered turn with rows; free outcomes (AC-07).
- [x] 2.6 `GET /api/v1/market/options/comparison`.

### 3. Web

- [x] 3.1 Types and block mapping for `optionComparison`.
- [x] 3.2 Card component: view tabs, expiry tabs, rows, highlights, collapsed empty rows, notes, not-current state; desktop and `/m`; both themes (AC-09).
- [x] 3.3 Starter card and symbol actions menu item (desktop menu and phone sheet) with their visibility rules are done (AC-12). The follow-up button was taken out of scope by the owner on 2026-11-28 (OQ-185): follow-up providers are a first-match chain with four buttons per answer, so an option button would displace an existing one.
- [x] 3.4 Browser check of the card on desktop and phone widths in both themes, with contrast measured.

### 4. Telegram

- [x] 4.1 Render the text composer's output in `TelegramAssistantResponseRenderer` (AC-10).

### 5. Put and covered-call views (delivery step 2)

- [x] 5.1 Put measures and highlight (AC-13).
- [x] 5.2 Covered-call measures and highlight (AC-14).
- [x] 5.3 Intent wording, view tabs, per-view notes, text renderings (AC-15).

### 6. Question parameters and theoretical price (delivery steps 3 and 4)

- [x] 6.1 Quantity and expiry parsing with Jalali month names and dates (AC-16).
- [x] 6.2 Historical volatility of an underlying from daily closes; Black–Scholes price; implied-volatility solver with bounded iterations (AC-17, AC-18).
- [x] 6.3 Columns in the contract, card and text, with inputs stated.

### 7. Discount alert (delivery step 5)

- [x] 7.1 Read the conditional tracker model end to end (rule definition storage, governance, evaluation trigger, notification, limits). Report to the owner before coding if a schema change or a new evaluation trigger is needed.
- [~] 7.2 Rule type `OptionDiscount` (code `OPTION_CALL_DISCOUNT_PCT`, unit percent), governance entry and evaluation in the existing tracker processor are done; no migration was needed. Not as planned: evaluation runs on the tracker worker's 60-second timer, not right after the order-book sync, and repeats are limited by the rule's cooldown and re-arm policy, not once per contract per session. No automated test of the evaluation path (governance is unit-tested).
- [x] 7.3 Closed without code by the owner's decision of 2026-11-28 (OQ-186): the rule is created and managed through the tracker API only until trackers get a web surface; creation from the card or a question, a tracker list entry and a dedicated notification text are in `specs/backlog.md`. Found in task 7.1: the web app has no tracker list or creation screen, the assistant has no chat path that creates trackers, tracker notifications go to Telegram only and use one generic text for every rule type. A rule can be created today through `POST /api/v1/trackers/me` with `ruleType = OptionDiscount`.

### 8. Documentation

- [x] 8.1 `docs/domain-glossary.md`: fill price, time value, intrinsic value, implied annual rate, leverage, effective sale price, covered call, premium income, downside cushion, discount alert.
- [x] 8.2 A short `docs/` page for the capability: questions, views, measures with formulas, fees and their source, flags, freshness rule, alert.
- [~] 8.3 `specs/README.md` entry done; no `CHANGELOG.md` line while the flag is off; `CHANGELOG.md` line under `[Unreleased]` when the flag is turned on for users; deferred items to `specs/backlog.md`.

## Tests

- [ ] Unit: fill, each view's measures with the acceptance-criteria figures, highlights and ties, freshness, annualization floor, fee configuration validation, underlying class.
- [ ] Unit: intent rules (positive, negative, flag off), quantity and expiry parsing, text composer, banned-word check over the wording table.
- [ ] Unit: Black–Scholes reference values, implied-volatility round trip, non-convergence.
- [ ] Unit: alert rule evaluation (fires, does not fire, once per session).
- [ ] Integration: the use case and endpoint on PostgreSQL with stored contracts and five-level books; payload persistence round trip; billing statuses; existing routing regression suites.
- [ ] Frontend (Vitest): card states, tabs, collapsed rows, starter card and menu visibility.
- [ ] No live provider call and no live model call in any test.

## Verification Record (2026-11-28)

- Build of `FinancialCopilot.sln`: 0 errors, 0 warnings.
- Unit tests `OptionComparison168Tests` (44) and the capability, follow-up, gauge and tracker groups (290 together): passed.
- Full unit suite (Release), run before the tracker rule and the catalog-count fix: 4,379 total, 33 failed; against the Feature 167 run the only new failing method was `ConversationalCapabilityRegistryTests.InitialCatalog_ContainsOnlyExecutableBusinessCapabilities` (a fixed capability count), fixed afterwards. Not rerun in full after that fix.
- End-to-end on the V2 path, `OptionComparison168E2ETests` and `OptionComparison168FlagOffE2ETests` (14): routing, the three views, quantity and expiry from the question, a contract symbol, a symbol without options, an unknown symbol, advice wording, one credit, persistence replay, availability, flag off. Passed.
- Integration groups touched by the wiring (option, TSETMC instrument sync, conditional trackers, gauge billing and the Noavaran-mapped gauge E2E): 33 of 33 passed.
- Frontend (Vitest): `option-comparison-card.test.tsx` (12) and the starter, symbol-prompt, phone and message-list tests it touches: passed. ESLint clean on the touched files.
- Browser check `npm run test:option-comparison-browser`: 16 combinations (current and not-current book × dark and light × 1280, 390, 360, 320 px): no page overflow, tabs and collapsed rows work, minimum text contrast 4.80:1 dark and 6.59:1 light. Evidence in `evidence/browser/`.
- Not verified: the full integration suite and the full frontend suite after these changes; Telegram MarkdownV2 rendering of an option answer; the discount alert end to end; AC-23 (real environment).

## Definition Of Done

- [ ] `dotnet test src/backend/FinancialCopilot.sln --configuration Release` green: no failure outside `specs/test-baseline.md`; integration results compared with clean `HEAD`, as recorded for Feature 167.
- [ ] `dotnet ef migrations has-pending-model-changes --context <DbContext>` reports no pending model changes for every touched DbContext.
- [ ] In `src/frontend`: `npm run lint`, `npm run test` and `npm run build` green, and `npm run typecheck` adds no type error beyond `specs/test-baseline.md`.
- [ ] Acceptance criteria in `user-story.md` all checked (AC-23 by the owner).
- [ ] Reference docs updated: `specs/README.md` index entry, affected `docs/*.md` and `README.md` sections.
- [ ] User-visible change: a Persian, user-facing line under `## [Unreleased]` in `CHANGELOG.md`.
- [ ] New questions → `specs/open-questions.md`; deferred work → `specs/backlog.md`; defects found → `specs/bugs.md`.
