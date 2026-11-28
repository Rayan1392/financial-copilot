# Tasks: Option Comparison Answers

Status: Gate A: Approved by the owner on 2026-11-28 (amended scope) — plan proposed, Gate B not approved

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

- [ ] 1.1 `OptionAnswers` options (flag, three fee rates, annualization floor, maximum expiries) with start-up validation.
- [ ] 1.2 Underlying class (share or fund unit): rule from OQ-177, checked against the underlyings of a real `Option` response; unknown → share, flagged.
- [ ] 1.3 Order-book fill: volume-weighted price for a quantity over levels 1 to 5, with fillable / not fillable / no order.
- [ ] 1.4 Call measures and the two call highlights (AC-01 to AC-04).
- [ ] 1.5 Freshness rule shared with Feature 167's ranking (AC-05).
- [ ] 1.6 `EfCoreOptionChainReader`: return all five levels for the selected expiries and several expiries in one call; keep the Feature 167 endpoints' output unchanged.
- [ ] 1.7 `OptionComparison` contract (versioned) and the use case that builds the call view for an underlying.

### 2. Assistant capability (V2)

- [ ] 2.1 Intent rules for the call view, with reject words, placed in the V2 dialogue gate (AC-06).
- [ ] 2.2 Route constant, registry definition, data requirements, executor, DI, source-map entry, availability (AC-06, AC-22).
- [ ] 2.3 Text composer in Persian with the wording table as the single owner of all labels and notes (AC-10, AC-11).
- [ ] 2.4 Attach `OptionComparison` to the response, facade contract, mapper and message persistence (AC-08).
- [ ] 2.5 Billing: one credit per answered turn with rows; free outcomes (AC-07).
- [ ] 2.6 `GET /api/v1/market/options/comparison`.

### 3. Web

- [ ] 3.1 Types and block mapping for `optionComparison`.
- [ ] 3.2 Card component: view tabs, expiry tabs, rows, highlights, collapsed empty rows, notes, not-current state; desktop and `/m`; both themes (AC-09).
- [ ] 3.3 Starter card, symbol actions menu item, follow-up button; visibility rules (AC-12).
- [ ] 3.4 Browser check of the card on desktop and phone widths in both themes, with contrast measured.

### 4. Telegram

- [ ] 4.1 Render the text composer's output in `TelegramAssistantResponseRenderer` (AC-10).

### 5. Put and covered-call views (delivery step 2)

- [ ] 5.1 Put measures and highlight (AC-13).
- [ ] 5.2 Covered-call measures and highlight (AC-14).
- [ ] 5.3 Intent wording, view tabs, per-view notes, text renderings (AC-15).

### 6. Question parameters and theoretical price (delivery steps 3 and 4)

- [ ] 6.1 Quantity and expiry parsing with Jalali month names and dates (AC-16).
- [ ] 6.2 Historical volatility of an underlying from daily closes; Black–Scholes price; implied-volatility solver with bounded iterations (AC-17, AC-18).
- [ ] 6.3 Columns in the contract, card and text, with inputs stated.

### 7. Discount alert (delivery step 5)

- [ ] 7.1 Read the conditional tracker model end to end (rule definition storage, governance, evaluation trigger, notification, limits). Report to the owner before coding if a schema change or a new evaluation trigger is needed.
- [ ] 7.2 Rule type, governance entry, evaluation after a successful order-book sync, once per contract per session (AC-19, AC-20).
- [ ] 7.3 Notification text; creation from the card and from a question; tracker list entry (AC-21).

### 8. Documentation

- [ ] 8.1 `docs/domain-glossary.md`: fill price, time value, intrinsic value, implied annual rate, leverage, effective sale price, covered call, premium income, downside cushion, discount alert.
- [ ] 8.2 A short `docs/` page for the capability: questions, views, measures with formulas, fees and their source, flags, freshness rule, alert.
- [ ] 8.3 `specs/README.md` entry; `CHANGELOG.md` line under `[Unreleased]` when the flag is turned on for users; deferred items to `specs/backlog.md`.

## Tests

- [ ] Unit: fill, each view's measures with the acceptance-criteria figures, highlights and ties, freshness, annualization floor, fee configuration validation, underlying class.
- [ ] Unit: intent rules (positive, negative, flag off), quantity and expiry parsing, text composer, banned-word check over the wording table.
- [ ] Unit: Black–Scholes reference values, implied-volatility round trip, non-convergence.
- [ ] Unit: alert rule evaluation (fires, does not fire, once per session).
- [ ] Integration: the use case and endpoint on PostgreSQL with stored contracts and five-level books; payload persistence round trip; billing statuses; existing routing regression suites.
- [ ] Frontend (Vitest): card states, tabs, collapsed rows, starter card and menu visibility.
- [ ] No live provider call and no live model call in any test.

## Definition Of Done

- [ ] `dotnet test src/backend/FinancialCopilot.sln --configuration Release` green: no failure outside `specs/test-baseline.md`; integration results compared with clean `HEAD`, as recorded for Feature 167.
- [ ] `dotnet ef migrations has-pending-model-changes --context <DbContext>` reports no pending model changes for every touched DbContext.
- [ ] In `src/frontend`: `npm run lint`, `npm run test` and `npm run build` green, and `npm run typecheck` adds no type error beyond `specs/test-baseline.md`.
- [ ] Acceptance criteria in `user-story.md` all checked (AC-23 by the owner).
- [ ] Reference docs updated: `specs/README.md` index entry, affected `docs/*.md` and `README.md` sections.
- [ ] User-visible change: a Persian, user-facing line under `## [Unreleased]` in `CHANGELOG.md`.
- [ ] New questions → `specs/open-questions.md`; deferred work → `specs/backlog.md`; defects found → `specs/bugs.md`.
